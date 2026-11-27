using Borealis.Backend;
using Borealis.Gpu;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Borealis.Source2.Shaders;

namespace Sandbox;

/// <summary>
/// Draws from engine buffers and models on Borealis.Gpu: vertex buffers of any vertex type, 16- or 32-bit indices, counts
/// from indirect arguments, and models drawn with transforms or arguments, each as native's render context draws them.
/// </summary>
internal sealed partial class BorealisGpuRenderBackend
{
	//
	// Vertex layouts
	//

	static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, VertexStreamLayout> vertexLayouts = new();

	/// <summary>
	/// The stream layout of a vertex type, as <see cref="VertexLayout"/> describes it to native: its fields in declaration
	/// order, each by its layout attribute's semantic and index, packed one after another; the type's marshalled size apart.
	/// </summary>
	internal static VertexStreamLayout LayoutOf( Type type ) => type == typeof( Vertex ) ? VertexStreamLayout.EngineVertex : vertexLayouts.GetOrAdd( type, MakeLayout );

	/// <summary>Forget the layouts made from vertex types: a hotload can change a type's fields and keep the type.</summary>
	internal static void ForgetVertexLayouts() => vertexLayouts.Clear();

	static VertexStreamLayout MakeLayout( Type type )
	{
		var fields = new List<VertexField>();
		var slots = new HashSet<(VertexSemantic, int)>();
		var offset = 0;
		foreach ( var field in type.GetFields( BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic ) )
		{
			var attribute = field.GetCustomAttribute<VertexLayout.BaseAttribute>()
				?? throw new NotImplementedException( $"Vertex struct '{type.FullName}' is missing layout attributes on '{field.Name}'" );

			var semantic = attribute.Semantic switch
			{
				"position" => VertexSemantic.Position,
				"normal" => VertexSemantic.Normal,
				"color" => VertexSemantic.Color,
				"texcoord" => VertexSemantic.TexCoord,
				"tangent" => VertexSemantic.Tangent,
				"blendweight" => VertexSemantic.BlendWeight,
				"blendindices" => VertexSemantic.BlendIndices,
				_ => throw new NotImplementedException( $"No case implemented for semantic {attribute.Semantic}" ),
			};

			var fieldType = field.FieldType;
			var format = fieldType switch
			{
				_ when fieldType == typeof( float ) => VertexFormat.Float,
				_ when fieldType == typeof( Vector2 ) => VertexFormat.Float2,
				_ when fieldType == typeof( Vector3 ) => VertexFormat.Float3,
				_ when fieldType == typeof( Vector4 ) || fieldType == typeof( Color ) => VertexFormat.Float4,
				_ when fieldType == typeof( uint ) => VertexFormat.UInt,
				_ when fieldType == typeof( int ) => VertexFormat.Int,
				_ when fieldType == typeof( char ) => VertexFormat.Byte,
				_ when fieldType == typeof( byte ) => VertexFormat.UByte,
				_ when fieldType == typeof( Color32 ) => VertexFormat.UByte4Norm,
				_ => throw new NotImplementedException( $"No case implemented for type {fieldType}" ),
			};

			// Unnumbered fields take the first free index of their semantic, in order
			var index = attribute.Index;
			if ( index == -1 )
			{
				for ( index = 0; index < 32 && slots.Contains( (semantic, index) ); index++ ) { }
			}

			if ( !slots.Add( (semantic, index) ) )
				throw new NotImplementedException( $"Vertex struct '{type.FullName}' contains '{attribute.Semantic}{index}' multiple times" );

			fields.Add( new VertexField( semantic, index, format, offset ) );
			offset += fieldType.GetManagedSize();
		}

		return new VertexStreamLayout( Marshal.SizeOf( type ), [.. fields] );
	}

	//
	// Draws from buffers, after SetRenderState
	//

	// SetRenderState's vertex layout, for the draw after it: null for none
	VertexStreamLayout pendingLayout;

	static IndexFormat IndexFormatOf( GpuBuffer indices ) => indices.ElementSize == sizeof( ushort ) ? IndexFormat.UInt16 : IndexFormat.UInt32;

	/// <summary>Draw <paramref name="draw"/> with SetRenderState's material and attributes.</summary>
	void DrawPending( in GeometryDraw draw )
	{
		_ = Recording;
		var (shader, surface) = PrepareDraw( pendingMaterial );

		// Vertices from buffers hold plain normals and tangents, as a native view tells its draws
		var lowered = LoweredForPending( pendingAttributes );

		// Indirect draws index g_TransformBuffer by instance ids counting from the command's first instance, as a model's do, not by the custom object's transform slot
		var lent = drawer.InstanceIds;
		if ( draw.Arguments.IsValid && TransformBufferLength() is > 0 and var transformCount )
			drawer.InstanceIds = InstanceIds( 0, transformCount );

		try
		{
			if ( surface is not null ) context.DrawSurface( surface, lowered, draw );
			else context.Draw( shader, lowered, draw );
		}
		finally
		{
			drawer.InstanceIds = lent;
		}
	}

	int TransformBufferLength()
	{
		var value = pendingAttributes?.Managed?.Get( "g_TransformBuffer" ) ?? blockAttributes?.Managed?.Get( "g_TransformBuffer" );
		return value is GpuBuffer buffer ? buffer.ElementCount : 0;
	}

	//
	// Models
	//

	/// <summary>
	/// A model's draw call at one LOD on this device: its material, what it draws (its buffers bound so that native's first
	/// index and base vertex find its triangles), and <see cref="Slot"/>, its place in its mesh, which picks its indirect
	/// arguments; <see cref="Uncompressed"/> when its normals and tangents are plain.
	/// </summary>
	internal readonly record struct ModelDraw( Material Material, int Slot, GeometryDraw Geometry, bool Uncompressed );

	/// <summary>
	/// A model's geometry at one LOD on this device. A model with a compiled file draws from the mesh Borealis's scene loads
	/// for it (<see cref="BackendDevice.LoadModelAsync"/>), in its own vertex layout. One made at run time is read back from
	/// Source 2 once (<see cref="Model.TryReadDrawCall"/>, as the engine's <see cref="Vertex"/> and 32-bit indices), each
	/// draw's indices placed where binding them from an offset puts native's first index at its own, so indirect arguments
	/// written with native's ranges (<see cref="Model.GetLodDrawCallRange"/>) draw the same triangles. Read-back buffers are
	/// freed after the model is.
	/// </summary>
	sealed class ModelGeometry
	{
		public BufferHandle Vertices, Indices;
		public ModelDraw[] Draws = [];
		public System.Collections.Concurrent.ConcurrentQueue<BufferHandle> FreeQueue;

		~ModelGeometry()
		{
			if ( Vertices.IsValid ) FreeQueue?.Enqueue( Vertices );
			if ( Indices.IsValid ) FreeQueue?.Enqueue( Indices );
		}
	}

	sealed class ModelLods
	{
		public readonly ModelGeometry[] Lods = new ModelGeometry[32];
	}

	static readonly ModelGeometry NoGeometry = new();

	readonly ConditionalWeakTable<Model, ModelLods> models = new();
	readonly System.Collections.Concurrent.ConcurrentQueue<BufferHandle> freedModelBuffers = new();

	/// <summary>Where a draw's indices go in a model's index buffer: native's first index or later, after the last draw's.</summary>
	internal static int PlaceIndices( ref int end, int firstIndex, int count )
	{
		var at = Math.Max( end, firstIndex );
		end = at + count;
		return at;
	}

	ModelGeometry GeometryOf( Model model, int lod )
	{
		lod = Math.Clamp( lod, 0, 31 );
		var lods = models.GetOrCreateValue( model );
		if ( lods.Lods[lod] is { } known ) return known;

		// A file's mesh, unless it's still loading (nothing drawn until it has); else what Source 2 reads back
		var pending = false;
		var geometry = !model.IsProcedural && !string.IsNullOrEmpty( model.ResourcePath ) ? FileGeometry( model, lod, out pending ) : null;
		if ( geometry is null && pending ) return NoGeometry;
		geometry ??= ReadBackGeometry( model, lod );
		lods.Lods[lod] = geometry;
		return geometry;
	}

	readonly Dictionary<string, Borealis.RenderMesh> loadedModels = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// A compiled model's draws at a LOD from the mesh Borealis loads for its file, every mesh group, as native draws them all;
	/// null when it has none here, or it's loading (<paramref name="pending"/>).
	/// </summary>
	ModelGeometry FileGeometry( Model model, int lod, out bool pending )
	{
		pending = false;
		var path = model.ResourcePath;
		Borealis.RenderMesh mesh;
		if ( services.AsyncModels )
		{
			mesh = services.LoadModelAsync( path, ulong.MaxValue, out pending );
		}
		else if ( !loadedModels.TryGetValue( path, out mesh ) )
		{
			loadedModels[path] = mesh = services.LoadModel( path, ulong.MaxValue );
		}

		if ( mesh?.Geometry is not BackendModel file ) return null;
		if ( lod >= mesh.LodCount ) return NoGeometry;

		var meshes = model.MeshInfo.Meshes;
		var draws = new List<ModelDraw>();
		foreach ( var draw in mesh.DrawsForLod( lod ) )
		{
			if ( draw.IndexCount < 3 || draw.Mesh >= meshes.Length || draw.DrawCall >= meshes[draw.Mesh].DrawCalls.Length ) continue;
			var geometry = ((IBackendGeometry)file).For( draw.Mesh, draw.DrawCall );

			// A skinned mesh, one skinned into the vertex cache too, draws its own streams here: its bind pose, as native's does

			// Native's base vertex is where its arguments start: the buffer is bound that far before Borealis's
			var native = meshes[draw.Mesh].DrawCalls[draw.DrawCall];
			var layout = UnrolledLayout( geometry.Layout, false );
			var vertexOffset = geometry.VertexOffset + (long)(geometry.BaseVertex - native.BaseVertex) * layout.Stride;
			if ( vertexOffset < 0 ) continue;

			draws.Add( new ModelDraw( native.Material, draw.DrawCall, new GeometryDraw
			{
				Vertices = geometry.Vertices,
				VertexOffset = vertexOffset,
				Layout = layout,
				Indices = geometry.Indices,
				IndexOffset = geometry.IndexOffset,
				IndexFormat = geometry.IndexFormat,
				Topology = Topology( (Graphics.PrimitiveType)draw.PrimitiveType ),
				First = draw.StartIndex,
				Count = draw.IndexCount,
				BaseVertex = native.BaseVertex,
			}, geometry.UncompressedTangentFrame ) );
		}

		return new ModelGeometry { Draws = [.. draws] };
	}

	/// <summary>A model made at run time, its draws at a LOD read back from Source 2's buffers.</summary>
	ModelGeometry ReadBackGeometry( Model model, int lod )
	{
		var vertices = new List<Vertex>();
		var indices = new List<uint>();
		var draws = new List<(Material Material, int Slot, Graphics.PrimitiveType Primitive, int First, int Count, long IndexOffset, long VertexOffset)>();
		var meshes = model.MeshInfo.Meshes;
		for ( int mesh = 0; mesh < meshes.Length; mesh++ )
		{
			if ( (meshes[mesh].LodMask & (1 << lod)) == 0 ) continue;
			for ( int call = 0; call < meshes[mesh].DrawCalls.Length; call++ )
			{
				var info = meshes[mesh].DrawCalls[call];
				if ( info.Indices < 3 || !model.TryReadDrawCall( mesh, call, out var v, out var i, out _ ) ) continue;

				var end = indices.Count;
				var at = PlaceIndices( ref end, info.StartIndex, i.Length );
				while ( indices.Count < at ) indices.Add( 0 );
				draws.Add( (info.Material, call, (Graphics.PrimitiveType)info.PrimitiveType, info.StartIndex, i.Length, (long)(at - info.StartIndex) * sizeof( uint ), (long)vertices.Count * Unsafe.SizeOf<Vertex>()) );
				vertices.AddRange( v );
				indices.AddRange( i );
			}
		}

		var geometry = new ModelGeometry { FreeQueue = freedModelBuffers };
		if ( indices.Count == 0 ) return geometry;

		geometry.Vertices = gpu.CreateBuffer( new BufferInfo { Size = (long)vertices.Count * Unsafe.SizeOf<Vertex>(), Usage = BufferUsage.Vertex | BufferUsage.CopyDestination, Name = $"Model {model.Name}" } );
		geometry.Indices = gpu.CreateBuffer( new BufferInfo { Size = (long)indices.Count * sizeof( uint ), Usage = BufferUsage.Index | BufferUsage.CopyDestination, Name = $"Model {model.Name} indices" } );
		gpu.WriteBuffer( geometry.Vertices, MemoryMarshal.AsBytes( CollectionsMarshal.AsSpan( vertices ) ) );
		gpu.WriteBuffer<uint>( geometry.Indices, CollectionsMarshal.AsSpan( indices ) );

		// Read back with plain normals and tangents
		var drawn = new ModelDraw[draws.Count];
		for ( int d = 0; d < drawn.Length; d++ )
		{
			var (material, slot, primitive, first, count, indexOffset, vertexOffset) = draws[d];
			drawn[d] = new ModelDraw( material, slot, new GeometryDraw
			{
				Vertices = geometry.Vertices,
				VertexOffset = vertexOffset,
				Layout = VertexStreamLayout.EngineVertex,
				Indices = geometry.Indices,
				IndexOffset = indexOffset,
				IndexFormat = IndexFormat.UInt32,
				Topology = Topology( primitive ),
				First = first,
				Count = count,
			}, true );
		}

		geometry.Draws = drawn;
		return geometry;
	}

	/// <summary>Free the geometry of models that have gone (the GPU is done with it before it's destroyed).</summary>
	void FreeModelGeometry()
	{
		while ( freedModelBuffers.TryDequeue( out var handle ) ) gpu.Destroy( handle );
	}

	void DestroyModelGeometry()
	{
		foreach ( var (_, lods) in models )
		{
			foreach ( var geometry in lods.Lods )
			{
				if ( geometry is null ) continue;
				if ( geometry.Vertices.IsValid ) gpu.Destroy( geometry.Vertices );
				if ( geometry.Indices.IsValid ) gpu.Destroy( geometry.Indices );
				geometry.Vertices = geometry.Indices = default;
			}
		}

		models.Clear();
		loadedModels.Clear();
		FreeModelGeometry();
		if ( instanceIds.IsValid ) gpu.Destroy( instanceIds );
		instanceIds = default;
		instanceIdCount = 0;
	}

	/// <summary>
	/// Draw each of a model's draw calls at a LOD with its material, as native's <c>RenderTools::DrawModel</c> does: those
	/// with no material, or none for the mode being drawn, are skipped. <paramref name="arguments"/> (indexed, five 32-bit
	/// values each) gives each draw's counts from <paramref name="argumentsOffset"/> plus 20 bytes a place in its mesh;
	/// otherwise each draws <paramref name="instances"/> instances of its own range. <paramref name="transforms"/> and
	/// <paramref name="ids"/> stand for <c>g_TransformBuffer</c> and the instance ids its shaders read through
	/// <c>TEXCOORD13</c>, where given.
	/// </summary>
	void DrawModelGeometry( ModelGeometry geometry, RenderAttributes attributes, int instances, BufferHandle arguments = default, long argumentsOffset = 0, BufferHandle transforms = default, (BufferHandle Buffer, long Offset) ids = default )
	{
		if ( geometry.Draws.Length == 0 ) return;

		var lent = drawer.InstanceIds;
		if ( ids.Buffer.IsValid ) drawer.InstanceIds = ids;
		try
		{
			foreach ( var draw in geometry.Draws )
			{
				if ( draw.Material is null ) continue;

				var (shader, surface) = PrepareDraw( draw.Material );
				var lowered = LoweredForModel( attributes, transforms, draw.Uncompressed );
				var drawn = draw.Geometry with
				{
					Instances = instances,
					Arguments = arguments,
					ArgumentsOffset = argumentsOffset + draw.Slot * 20L,
					DrawCount = 1,
				};
				if ( surface is not null ) context.DrawSurface( surface, lowered, drawn );
				else context.Draw( shader, lowered, drawn );
			}
		}
		finally
		{
			drawer.InstanceIds = lent;
		}
	}

	static readonly int TransformBufferId = AttributeIds.Of( "g_TransformBuffer" );
	static readonly int UncompressedTangentFrameId = AttributeIds.Of( UncompressedTangentFrame );

	/// <summary>
	/// A draw's or dispatch's attributes lowered, kept on the engine set they came from (<see cref="ManagedRenderAttributes.Lowered"/>,
	/// a few ways of lowering it in a row) and used again, in any frame, while the set's values, the tangent frame and lent
	/// transforms it was lowered with, whether it has transforms of its own (set over it each time) and the backings are
	/// unchanged. The block's lowered set shows through under it (<see cref="ShaderAttributes.Under"/>), not copied: a draw from
	/// buffers only needs the block to have or lack the same transform and tangent frame. Values set again since are lowered into it alone, unless something was
	/// cleared meanwhile (<see cref="ManagedRenderAttributes.RemovedAt"/>), which lowers them all. Its textures are lowered again
	/// in a new device frame or after a view drew into a texture: a texture's backing can change under it (streaming, a view's drawing).
	/// </summary>
	sealed class LoweredSet
	{
		public readonly ShaderAttributes Set = new();
		public ManagedRenderAttributes Owner;
		public BorealisGpuRenderBackend Maker;
		public LoweredSet Next;
		public int Version, Backings, Built, ViewDraws;
		public ulong Frame;
		public BufferHandle Lent;
		public bool Transforms, Uncompressed, Pending, Dispatch, Block, Textures, BlockTangent, BlockTransform;
	}

	// Ways of lowering one engine set kept on it; more lower again over the last
	const int LoweredPerSet = 4;

	// Every lowered set kept on an engine set, and those let go, to reuse: a set not drawn with for a couple of frames is taken back,
	// so sets made and dropped every frame (a fresh RenderAttributes per draw) reuse the same few
	readonly List<LoweredSet> loweredSets = new();
	readonly Stack<LoweredSet> spareLowered = new();
	ulong loweredSwept;

	// Textures lowered by Lower since this was last zeroed
	int loweredTextures;

	ShaderAttributes LoweredForModel( RenderAttributes attributes, BufferHandle transforms, bool uncompressed ) => LoweredFor( attributes, transforms, uncompressed, pending: false );

	/// <summary>The same for draws from buffers or CPU vertices; the identity transform is added here so drawing doesn't change the kept set.</summary>
	ShaderAttributes LoweredForPending( RenderAttributes attributes ) => LoweredFor( attributes, default, true, pending: true );

	ShaderAttributes LoweredFor( RenderAttributes attributes, BufferHandle transforms, bool uncompressed, bool pending, bool dispatch = false )
	{
		if ( attributes?.Managed is null && attributes?.ShadowSet is { } copy ) attributes = copy;
		if ( attributes?.Managed is null )
			throw new NotSupportedException( "Native render attributes can't be drawn with on Borealis.Gpu" );

		var managed = attributes.Managed;
		var frame = gpu.FrameNumber;
		if ( frame != loweredSwept ) SweepLowered( frame );

		var block = blockAttributes?.Managed is { } b && !ReferenceEquals( b, managed ) ? LoweredBlock( b ) : null;
		var hasBlock = block is not null;

		// Under a block, the lent transforms are in the block's lowered set
		var lent = hasBlock ? default : lentTransforms;
		var version = managed.Version;
		// Lowered draws hold the sampled backing, and dispatches copy from it when taking a texture back from a view.
		var viewDraws = Texture.ViewDraws;

		// This way of lowering the set if it's kept, else a new one, else the last kept
		LoweredSet made = null, last = null;
		var count = 0;
		for ( var kept = managed.Lowered as LoweredSet; kept is not null; kept = kept.Next )
		{
			if ( !ReferenceEquals( kept.Maker, this ) ) continue;
			if ( kept.Block == hasBlock && kept.Lent == lent && kept.Transforms == transforms.IsValid && kept.Uncompressed == uncompressed && kept.Pending == pending && kept.Dispatch == dispatch )
			{
				made = kept;
				break;
			}

			last = kept;
			count++;
		}

		// Only forward from it: a store read on another thread can be older (RenderAttributes' held copy), with nothing set since.
		// A draw from buffers adds the tangent frame and transform the block lacks: a block that has other of them lowers it again
		var blockTangent = block?.Get( UncompressedTangentFrameId ) is not null;
		var blockTransform = block?.Get( TransformBufferId ) is not null;
		var full = made is null || made.Backings != backingsVersion || made.Built != made.Set.OwnVersion
			|| version < made.Version || (version > made.Version && managed.RemovedAt > made.Version)
			|| (pending && (made.BlockTangent != blockTangent || made.BlockTransform != blockTransform));
		if ( made is null )
		{
			if ( count < LoweredPerSet )
			{
				made = spareLowered.TryPop( out var spare ) ? spare : new LoweredSet();
				(made.Owner, made.Maker) = (managed, this);
				made.Next = managed.Lowered as LoweredSet;
				managed.Lowered = made;
				loweredSets.Add( made );
			}
			else made = last;
		}

		var set = made.Set;

		// A dispatch's textures are the engine's own copies (EngineWrites)
		loweringDispatch = dispatch;
		loweredTextures = 0;
		try
		{
			if ( full )
			{
				set.Clear();
				if ( lent.IsValid ) set.SetById( TransformBufferId, lent );
				Lower( managed, set );
				made.Textures = false;
			}
			else
			{
				if ( version != made.Version ) Lower( managed, set, made.Version );
				if ( made.Textures && (made.Frame != frame || made.ViewDraws != viewDraws) ) LowerTextures( managed, set );
			}
		}
		finally
		{
			loweringDispatch = false;
		}

		made.Textures |= loweredTextures > 0;
		set.Under = block;

		// Over what the attributes set, as when made: set again, a changed value can have replaced them
		if ( transforms.IsValid ) set.SetById( TransformBufferId, transforms );
		if ( pending )
		{
			if ( set.Get( UncompressedTangentFrameId ) is null ) set.SetById( UncompressedTangentFrameId, 1.0f );
			context.EnsureTransform( set );
		}
		else if ( !dispatch ) set.SetById( UncompressedTangentFrameId, uncompressed ? 1.0f : 0.0f );

		(made.Version, made.Block, made.BlockTangent, made.BlockTransform, made.Frame, made.Lent, made.Transforms, made.Uncompressed, made.Pending, made.Dispatch, made.ViewDraws, made.Backings, made.Built)
			= (version, hasBlock, blockTangent, blockTransform, frame, lent, transforms.IsValid, uncompressed, pending, dispatch, viewDraws, backingsVersion, set.OwnVersion);
		return set;
	}

	/// <summary>Take back the lowered sets nothing drew with in the last two device frames, for other engine sets to reuse.</summary>
	void SweepLowered( ulong frame )
	{
		loweredSwept = frame;
		for ( int i = loweredSets.Count - 1; i >= 0; i-- )
		{
			var kept = loweredSets[i];
			if ( kept.Frame + 2 > frame ) continue;
			Forget( kept );
			loweredSets[i] = loweredSets[^1];
			loweredSets.RemoveAt( loweredSets.Count - 1 );
			spareLowered.Push( kept );
		}
	}

	// Unhook a lowered set from its engine set and empty it
	static void Forget( LoweredSet kept )
	{
		var owner = kept.Owner;
		if ( ReferenceEquals( owner.Lowered, kept ) ) owner.Lowered = kept.Next;
		else
		{
			for ( var before = owner.Lowered as LoweredSet; before is not null; before = before.Next )
			{
				if ( !ReferenceEquals( before.Next, kept ) ) continue;
				before.Next = kept.Next;
				break;
			}
		}

		kept.Set.Clear();
		(kept.Owner, kept.Maker, kept.Next) = (null, null, null);
	}

	/// <summary>Forget the kept lowered attributes, which hold the engine sets they came from.</summary>
	void ReleaseModelAttributes()
	{
		foreach ( var kept in loweredSets ) Forget( kept );
		loweredSets.Clear();
		spareLowered.Clear();
	}

	//
	// Instance transforms
	//

	/// <summary>Bytes of one <c>TransformBufferData_t</c> (<see cref="Rendering.GpuInstanceTransform"/>).</summary>
	const int TransformSize = 64;

	/// <summary>
	/// Write <paramref name="transforms"/> as <c>TransformBufferData_t</c>s, white and opaque, into this frame's upload memory
	/// in the block's list, as native writes a draw's transforms into the frame's transform buffer. Returns the buffer they're
	/// in, to bind as <c>g_TransformBuffer</c>, and the instance ids that read them, in order.
	/// </summary>
	(BufferHandle Transforms, (BufferHandle Buffer, long Offset) Ids) UploadTransforms( ReadOnlySpan<Transform> transforms )
	{
		var upload = Recording.AllocateUpload( transforms.Length * TransformSize, TransformSize );
		var entries = MemoryMarshal.Cast<byte, Rendering.GpuInstanceTransform>( upload.Data );
		for ( int i = 0; i < transforms.Length; i++ ) entries[i] = InstanceTransform( transforms[i] );

		var first = (int)(upload.Offset / TransformSize);
		return (upload.Buffer, InstanceIds( first, transforms.Length ));
	}

	/// <summary>
	/// The view of the pass a custom object draws in (<see cref="BorealisGpuCustomDraw.Draw"/>), which its instanced model draws are
	/// culled against (<see cref="DrawCulled"/>); null outside one, where nothing is culled.
	/// </summary>
	internal Borealis.RenderView CullView { get; set; }

	/// <summary>
	/// Whether instanced model draws in a custom object's depth pass leave out, on the GPU, the instances its view doesn't see.
	/// Off by default (an experiment); <c>-instance-cull</c> turns it on. On the A10, Mow the Lawn's grass draws a fifth of its
	/// triangles with it, in the same time: it costs its fragments, and each draw's cull breaks the pass.
	/// </summary>
	internal static bool CullInstances = Utility.CommandLine.HasSwitch( "-instance-cull" );

	/// <summary>The fewest instances worth culling: a draw of fewer draws them all.</summary>
	const int MinCulledInstances = 64;

	ShaderAttributes cullAttributes;

	/// <summary>
	/// Draw <paramref name="count"/> instances of a model, their transforms in <paramref name="transforms"/> from entry
	/// <paramref name="first"/>, without those outside <paramref name="view"/>'s sides: one group of
	/// <c>borealis_instance_cull_cs</c> writes the instances whose transformed bounds it sees after one another, in order, and
	/// each draw's instance count into indirect arguments, and every draw of the model reads them. The same pixels: an
	/// instance outside a side draws none (whether the view clips or clamps depth, which the near and far planes would care
	/// about), and the kept ones draw in their order. False, drawing nothing, where it doesn't cull: too few instances, a draw
	/// that isn't indexed, or one whose shader reads its instance's index, which leaving instances out would change.
	/// </summary>
	bool DrawCulled( Model model, ModelGeometry geometry, RenderAttributes attributes, Borealis.RenderView view, BufferHandle transforms, int first, int count )
	{
		if ( !CullInstances || count < MinCulledInstances || view.Orthographic && view.OrthoSize.x <= 0 ) return false;

		var commands = 0;
		foreach ( var draw in geometry.Draws )
		{
			if ( draw.Material is null ) continue;
			if ( !draw.Geometry.Indices.IsValid ) return false;

			var (shader, surface) = PrepareDraw( draw.Material );
			if ( context.ReadsInstanceIndex( shader, surface, LoweredForModel( attributes, transforms, draw.Uncompressed ) ) ) return false;
			commands = Math.Max( commands, draw.Slot + 1 );
		}

		if ( commands == 0 ) return false;

		// Each draw's indexed arguments, as it draws directly; the cull writes their instance counts
		var list = Recording;
		var arguments = list.AllocateUpload( commands * 20, 16 );
		var words = MemoryMarshal.Cast<byte, uint>( arguments.Data );
		words.Clear();
		foreach ( var draw in geometry.Draws )
		{
			if ( draw.Material is null ) continue;
			var at = draw.Slot * 5;
			words[at] = (uint)draw.Geometry.Count;
			words[at + 2] = (uint)draw.Geometry.First;
			words[at + 3] = (uint)draw.Geometry.BaseVertex;
		}

		var kept = list.AllocateUpload( count * TransformSize, TransformSize );
		var keptFirst = (int)(kept.Offset / TransformSize);

		var bounds = model.Bounds;
		var frustum = view.Frustum;
		var cull = cullAttributes ??= new ShaderAttributes();
		cull.Set( "CullTransforms", transforms );
		cull.Set( "CullKept", kept.Buffer );
		cull.Set( "CullArguments", arguments.Buffer );
		cull.Set( "CullFirst", first );
		cull.Set( "CullCount", count );
		cull.Set( "CullKeptFirst", keptFirst );
		cull.Set( "CullArgumentsOffset", arguments.Offset );
		cull.Set( "CullCommands", commands );
		cull.Set( "CullBoundsCenter", bounds.Center.x, bounds.Center.y, bounds.Center.z );
		var extents = bounds.Size * 0.5f;
		cull.Set( "CullBoundsExtents", extents.x, extents.y, extents.z );
		for ( int i = 0; i < 4; i++ )
		{
			var plane = frustum.Plane( i );
			cull.Set( CullPlaneNames[i], plane.x, plane.y, plane.z, plane.w );
		}

		drawer.Material = null;
		if ( !context.Dispatch( Shader( "shaders/borealis_instance_cull_cs.shader" ), cull, 1, 1, 1 ) ) return false;

		// Only the draws that read what it wrote wait for it, not the work before it
		list.Barrier( PipelineStage.ComputeShader, MemoryAccess.ShaderWrite, PipelineStage.DrawIndirect | PipelineStage.VertexShader, MemoryAccess.IndirectRead | MemoryAccess.ShaderRead );

		DrawModelGeometry( geometry, attributes, 1, arguments.Buffer, arguments.Offset, kept.Buffer, InstanceIds( keptFirst, count ) );
		return true;
	}

	static readonly string[] CullPlaneNames = ["CullPlane0", "CullPlane1", "CullPlane2", "CullPlane3"];

	/// <summary>
	/// <see cref="Rendering.GpuInstanceTransform.From"/>'s entry, made straight from the transform's rotation, scale and position:
	/// the rows of its 3x4 matrix, alpha 1 and a white tint.
	/// </summary>
	internal static Rendering.GpuInstanceTransform InstanceTransform( in Transform transform )
	{
		var q = transform.Rotation;
		var s = transform.Scale;
		var p = transform.Position;
		float xx = q.x * q.x, yy = q.y * q.y, zz = q.z * q.z;
		float xy = q.x * q.y, xz = q.x * q.z, yz = q.y * q.z;
		float wx = q.w * q.x, wy = q.w * q.y, wz = q.w * q.z;

		return new Rendering.GpuInstanceTransform
		{
			Row0 = new Vector4( (1 - 2 * (yy + zz)) * s.x, 2 * (xy - wz) * s.y, 2 * (xz + wy) * s.z, p.x ),
			Row1 = new Vector4( 2 * (xy + wz) * s.x, (1 - 2 * (xx + zz)) * s.y, 2 * (yz - wx) * s.z, p.y ),
			Row2 = new Vector4( 2 * (xz - wy) * s.x, 2 * (yz + wx) * s.y, (1 - 2 * (xx + yy)) * s.z, p.z ),
			Alpha = 1.0f,
			Tint = 0xFFFFFF,
		};
	}

	// 0, 1, 2 ... as 32-bit values: the instance ids of transforms written in a row, read from the first's
	BufferHandle instanceIds;
	int instanceIdCount;

	/// <summary>Instance ids <paramref name="first"/> to <paramref name="first"/> + <paramref name="count"/> - 1, as a stream.</summary>
	(BufferHandle Buffer, long Offset) InstanceIds( int first, int count )
	{
		var needed = first + count;
		if ( needed > instanceIdCount )
		{
			if ( instanceIds.IsValid ) gpu.Destroy( instanceIds );
			instanceIdCount = (int)Math.Max( 65536, System.Numerics.BitOperations.RoundUpToPowerOf2( (uint)needed ) );
			instanceIds = gpu.CreateBuffer( new BufferInfo { Size = (long)instanceIdCount * sizeof( uint ), Usage = BufferUsage.Vertex | BufferUsage.CopyDestination, Name = "Instance ids" } );
			var values = new uint[instanceIdCount];
			for ( int i = 0; i < values.Length; i++ ) values[i] = (uint)i;
			gpu.WriteBuffer<uint>( instanceIds, values );
		}

		return (instanceIds, (long)first * sizeof( uint ));
	}
}

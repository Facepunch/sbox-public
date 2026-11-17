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
		var shader = Prepare( pendingMaterial );
		var lowered = Lower( pendingAttributes );

		// Vertices from buffers hold plain normals and tangents, as a native view tells its draws
		if ( lowered.Get( UncompressedTangentFrame ) is null ) lowered.Set( UncompressedTangentFrame, 1.0f );
		context.Draw( shader, lowered, draw );
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

				var shader = Prepare( draw.Material );
				var lowered = Lower( attributes );
				if ( transforms.IsValid ) lowered.Set( "g_TransformBuffer", transforms );
				lowered.Set( UncompressedTangentFrame, draw.Uncompressed ? 1.0f : 0.0f );
				context.Draw( shader, lowered, draw.Geometry with
				{
					Instances = instances,
					Arguments = arguments,
					ArgumentsOffset = argumentsOffset + draw.Slot * 20L,
					DrawCount = 1,
				} );
			}
		}
		finally
		{
			drawer.InstanceIds = lent;
		}
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

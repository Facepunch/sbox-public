using Sandbox.Rendering;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sandbox.SceneRenderer.Features;

/// <summary>
/// Sorts meshes and custom objects into layer run lists, instancing compatible neighbours.
/// Membership and draw rules live in <c>MeshRenderFeature.Lists.cs</c>; native instancing is described in <c>docs/native/instancing.md</c>.
/// </summary>
internal sealed partial class MeshRenderFeature : RenderFeature
{
	/// <summary>
	/// The shader draws use when a mesh draw has no material of its own.
	/// </summary>
	public const string DefaultShader = "shaders/scene_renderer_mesh.shader";

	/// <summary>
	/// Fast-path depth material (<c>CBaseSceneObjectDesc::GetMaterialForDraw</c>).
	/// </summary>
	public const string DepthOnlyMaterial = "materials/dev/depth_only.vmat";

	/// <summary>
	/// The combo native's Fade layers set: dithered opacity, or alpha to coverage with MSAA.
	/// </summary>
	internal static readonly StringToken OpaqueFade = new( "D_OPAQUE_FADE" );

	/// <summary>
	/// Per-draw vertex-cache skinning combo (<c>CAnimatableSceneObjectDesc::BindMaterial</c>).
	/// </summary>
	static readonly StringToken CsVertexAnimation = new( "D_CS_VERTEX_ANIMATION" );

	/// <summary>
	/// Filters mixed objects' draws by blend type (<c>WantAlphaBlended</c>, sceneobjectdescs.cpp).
	/// </summary>
	internal enum DrawFilter : byte
	{
		All,
		Opaque,
		Translucent,
	}

	/// <summary>
	/// Compatible instances sharing mesh, LOD and draw state. Skinned IDs are grouped per model mesh;
	/// custom objects occupy individual runs. Lighting and frame-copy inputs apply in forward passes.
	/// </summary>
	internal readonly record struct Run( RenderMesh Mesh, int Lod, Material Override, bool OverrideBlends, DrawFilter Filter, int FirstInstance, int Count, bool Skinned = false, RenderAttributes Attributes = null, CustomObject Custom = null, RenderAttributes Lightmap = null, bool ReadsFrameBuffer = false, RenderAttributes Probe = null, bool Tinted = false, bool Deformed = false, bool Morphable = false );

	/// <summary>
	/// A view's runs, each list in draw order: what native's opaque, fade and translucent layers would hold.
	/// </summary>
	internal sealed class RunLists
	{
		/// <summary>
		/// Every list, by the bit of <see cref="MeshRuns"/> it's drawn for - in draw order.
		/// </summary>
		public readonly List<Run>[] ByBit = NewLists();

		static List<Run>[] NewLists()
		{
			var lists = new List<Run>[Lists.Length];
			for ( int i = 0; i < lists.Length; i++ )
				lists[i] = new List<Run>();
			return lists;
		}

		public List<Run> Opaque => ByBit[0];
		public List<Run> OpaqueNoPrepass => ByBit[1];
		public List<Run> Faded => ByBit[2];
		public List<Run> Translucent => ByBit[3];
		public List<Run> StaticOverlay => ByBit[4];

		/// <summary>
		/// Unfaded game-overlay runs, split by blend type.
		/// </summary>
		public List<Run> OverlayOpaque => ByBit[5];
		public List<Run> OverlayTranslucent => ByBit[6];

		/// <summary>
		/// Unfaded bloom runs in opaque batch order.
		/// </summary>
		public List<Run> Bloom => ByBit[7];

		/// <summary>
		/// Fully sorted output overlays, including faded objects.
		/// </summary>
		public List<Run> OverlayWithDepth => ByBit[8];
		public List<Run> OverlayWithoutDepth => ByBit[9];
		public List<Run> AfterUI => ByBit[10];

		/// <summary>
		/// Unfaded translucent frame-copy readers for refraction depth.
		/// </summary>
		public List<Run> Refraction => ByBit[11];

		/// <summary>
		/// Separate overlay prepass runs, redrawn after opaque in forward.
		/// </summary>
		public List<Run> OverlayPrepass => ByBit[12];

		/// <summary>
		/// Fully sorted decal geometry, including faded objects.
		/// </summary>
		public List<Run> Decal => ByBit[13];

		/// <summary>
		/// Lists requiring a frame copy in translucent layers.
		/// </summary>
		public MeshRuns FrameBufferReads;

		/// <summary>
		/// Lists containing custom objects, which require main-thread recording.
		/// </summary>
		public MeshRuns Custom;

		public void Clear()
		{
			foreach ( var list in ByBit )
				list.Clear();
			Custom = 0;
			FrameBufferReads = 0;
		}
	}

	/// <summary>
	/// Reusable world-index and sort-key arrays. LODs are stored separately.
	/// </summary>
	sealed class Bucket
	{
		public ulong[] Keys = new ulong[64];
		public int[] Items = new int[64];
		public int Count;

		public void Add( ulong key, int index )
		{
			if ( Count == Keys.Length )
			{
				Array.Resize( ref Keys, Count * 2 );
				Array.Resize( ref Items, Count * 2 );
			}

			Keys[Count] = key;
			Items[Count] = index;
			Count++;
		}
	}

	/// <summary>
	/// Each run list's bucket, by its bit (<see cref="Lists"/>).
	/// </summary>
	readonly Bucket[] buckets = NewBuckets();

	static Bucket[] NewBuckets()
	{
		var buckets = new Bucket[Lists.Length];
		for ( int i = 0; i < buckets.Length; i++ )
			buckets[i] = new Bucket();
		return buckets;
	}

	/// <summary>
	/// Each visible object's LOD this view, by world index.
	/// </summary>
	byte[] lods = new byte[256];

	/// <summary>
	/// Probe attributes by world index, selected only for the main view's non-lightmapped draws.
	/// </summary>
	RenderAttributes[] probes = new RenderAttributes[256];

	Material defaultMaterial;
	Material depthOnlyMaterial;

	/// <summary>
	/// <see cref="MeshObject"/>s.
	/// </summary>
	public override Type ObjectType => typeof( MeshObject );

	/// <summary>
	/// Meshes and custom objects share sorting lists.
	/// </summary>
	internal override bool Accepts( Type type ) => typeof( MeshObject ).IsAssignableFrom( type ) || typeof( CustomObject ).IsAssignableFrom( type );

	/// <summary>
	/// Draw with each mesh draw's own material. When off, everything uses <see cref="DefaultShader"/>.
	/// </summary>
	public bool UseMeshMaterials { get; set; } = true;

	internal override bool DrawsShadows => true;

	/// <summary>
	/// A view's runs, after <see cref="Prepare"/> - for the tests.
	/// </summary>
	internal RunLists RunsFor( ViewPass pass ) => pass.State<RunLists>( this );

	internal override void Prepare( RenderWorld world, ViewPass pass, TransformBuffer transforms )
	{
		using var zone = Zones.MeshPrepare.Start();
		var layers = pass.State<RunLists>( this );
		layers.Clear();

		var visible = pass.Visible( this );
		if ( visible.Count == 0 ) return;

		var objects = world.Objects;
		var centers = world.BoundsCenter;
		var extents = world.BoundsExtents;

		// LOD uses a radius-0.5 sphere's pixel width. Shadows use the main view to match visible LODs.
		var root = pass.Root;
		var camera = root.Position;
		var tanHalfFov = MathF.Tan( root.FieldOfView.DegreeToRadian() * 0.5f );
		var screenWidth = root.Viewport.Width;

		// Translucency sorts from the camera drawing it - a shadow view's own
		var sortCamera = pass.View.Position;

		foreach ( var bucket in buckets )
			bucket.Count = 0;

		if ( lods.Length < objects.Length ) Array.Resize( ref lods, Math.Max( objects.Length, lods.Length * 2 ) );
		if ( probes.Length < lods.Length ) Array.Resize( ref probes, lods.Length );
		var volumes = world.LightProbeVolumes;
		var volumesKey = volumes.Count > 0 && !pass.IsShadow ? LightProbeVolume.Key( volumes ) : 0;

		foreach ( var index in CollectionsMarshal.AsSpan( visible ) )
		{
			// Membership comes from MeshRenderFeature.Lists.cs.
			var renderObject = objects[index];
			if ( renderObject is CustomObject custom )
			{
				var customRuns = ListsFor( custom, pass.IsShadow );
				if ( customRuns == 0 ) continue;

				var customDistance = (customRuns & DistanceSortedLists) != 0 ? TranslucentSortDistance( centers[index], extents[index], sortCamera ) : 0;
				AddToLists( customRuns, index, (ulong)(uint)RuntimeHelpers.GetHashCode( custom ) << 8, customDistance, !custom.IsOpaque, 0 );
				layers.Custom |= customRuns;
				continue;
			}

			var obj = (MeshObject)renderObject;
			if ( obj.Mesh is null ) continue;

			var lod = ChooseLod( obj, centers[index], camera, tanHalfFov, screenWidth );
			var blend = obj.BlendForLod( lod );
			lods[index] = (byte)lod;
			if ( !pass.IsShadow ) obj.DrawnLod = lod;

			// Probe lighting is forward-only (CBaseSceneObjectDesc::GeneratePrimitives).
			var probe = -1;
			if ( obj.NeedsLightProbe && !pass.IsShadow && volumes.Count > 0 ) probe = ProbeFor( obj, volumes, volumesKey, centers[index], extents[index] );
			probes[index] = probe >= 0 ? volumes[probe].Attributes : null;

			var runs = ListsFor( obj, blend, pass.IsShadow );
			if ( runs == 0 ) continue;

			// A translucent layer copies the frame for what reads it
			if ( obj.WantsFrameBufferCopy && !pass.IsShadow ) layers.FrameBufferReads |= runs & FrameBufferCopyingLists;

			// Only the sort keys its lists use
			var opaqueKey = (runs & BatchedLists) != 0 ? OpaqueKey( obj, lod ) : 0;
			var distance = (runs & DistanceSortedLists) != 0 ? TranslucentSortDistance( centers[index], extents[index], sortCamera ) : 0;
			AddToLists( runs, index, opaqueKey, distance, blend == MeshBlend.Translucent, obj.RenderOrder );
		}

		for ( int bit = 0; bit < buckets.Length; bit++ )
			Emit( buckets[bit], Lists[bit].Draws, layers.ByBit[bit], world, transforms, lods );
	}

	/// <summary>
	/// Add an object to selected buckets using each list's sort rule.
	/// </summary>
	void AddToLists( MeshRuns runs, int index, ulong opaqueKey, uint distance, bool onlyTranslucent, int renderOrder )
	{
		for ( var bits = (uint)runs; bits != 0; bits &= bits - 1 )
		{
			var bit = System.Numerics.BitOperations.TrailingZeroCount( bits );
			var key = Lists[bit].Sort switch
			{
				ListSort.Batched => opaqueKey,
				ListSort.BackToFront => (ulong)(uint.MaxValue - distance) << 32 | (uint)index,
				ListSort.Full => FullSortKey( distance, onlyTranslucent, index ),
				ListSort.NearestFirst => FullSortKey( distance, false, index ),
				_ => (ulong)(uint)(renderOrder ^ int.MinValue) << 32 | (uint)index,
			};

			buckets[bit].Add( key, index );
		}
	}

	/// <summary>
	/// Reuse the cached probe choice until selection inputs change.
	/// </summary>
	static int ProbeFor( MeshObject obj, List<LightProbeVolume> volumes, long volumesKey, Vector3 center, Vector3 extents )
	{
		// From its lighting origin, where it has one (CBaseSceneObjectDesc::GeneratePrimitives, GetLightingOrigin)
		var origin = obj.LightingOrigin ?? center;
		if ( obj.ProbeVolumesKey == volumesKey && obj.ProbeCenter.Equals( center ) && obj.ProbeExtents.Equals( extents ) && obj.ProbeOrigin.Equals( origin ) && obj.ProbeLightGroup == obj.LightGroup )
			return obj.ProbeChoice;

		obj.ProbeChoice = LightProbeVolume.Choose( volumes, origin, center, extents, obj.LightGroup );
		obj.ProbeOrigin = origin;
		obj.ProbeCenter = center;
		obj.ProbeExtents = extents;
		obj.ProbeLightGroup = obj.LightGroup;
		obj.ProbeVolumesKey = volumesKey;
		return obj.ProbeChoice;
	}

	/// <summary>
	/// The light probe volume attributes an object drew with in the last <see cref="Prepare"/>, for the tests.
	/// </summary>
	internal RenderAttributes ProbeFor( RenderObject obj ) => probes[obj.Index];

	/// <summary>
	/// The LOD an object drew at in the last <see cref="Prepare"/>, for the tests.
	/// </summary>
	internal int LodFor( RenderObject obj ) => lods[obj.Index];

	/// <summary>
	/// Nearest-first key, reversed for fully translucent objects; ties use world index (<c>CSceneSystem</c> layer sorting).
	/// </summary>
	static ulong FullSortKey( uint distance, bool translucent, int index )
	{
		return (ulong)(translucent ? uint.MaxValue - distance : distance) << 32 | (uint)index;
	}

	/// <summary>
	/// Batch key: similarity (24 bits), material (16), object-state identity (16), LOD (8)
	/// (<c>CBaseSceneObjectDesc::BindMaterial</c>). Collisions only cost batching efficiency.
	/// </summary>
	ulong OpaqueKey( MeshObject obj, int lod )
	{
		var identity = (uint)RuntimeHelpers.GetHashCode( obj.Mesh ) ^ (uint)RuntimeHelpers.GetHashCode( obj.MaterialOverride ) * 0x9E3779B1u
			^ (uint)RuntimeHelpers.GetHashCode( obj.Attributes ) * 0x85EBCA77u ^ (uint)RuntimeHelpers.GetHashCode( obj.LightmapAttributes ) * 0xC2B2AE3Du
			^ (uint)RuntimeHelpers.GetHashCode( probes[obj.Index] ) * 0x27D4EB2Fu;
		var similarity = obj.Mesh.SimilarityForLod( lod );
		similarity ^= similarity >> 24 ^ similarity >> 48;

		// Group identical materials within each similarity group.
		var draws = obj.Mesh.DrawsForLod( lod );
		var material = obj.MaterialOverride ?? (draws.Length > 0 ? draws[0].Material : null);
		var materialBits = (uint)RuntimeHelpers.GetHashCode( material );
		materialBits = (materialBits ^ materialBits >> 16) & 0xFFFF;
		identity = (identity ^ identity >> 16) & 0xFFFF;

		return (similarity & 0xFFFFFF) << 40 | (ulong)materialBits << 24 | (ulong)identity << 8 | (ulong)(uint)lod;
	}

	/// <summary>
	/// Camera-to-AABB distance in truncated hundredths (<c>CSceneSystem</c> layer sort).
	/// Subtract from UINT32_MAX for back-to-front order; containing bounds draw last.
	/// </summary>
	internal static uint TranslucentSortDistance( Vector3 center, Vector3 extents, Vector3 camera )
	{
		// CalcSqrDistanceToAABB
		var offset = Vector3.Max( (camera - center).Abs() - extents, Vector3.Zero );
		var distance = MathF.Sqrt( offset.LengthSquared );
		return (uint)Math.Clamp( 100.0f * distance, 0, 4294967040.0f );
	}

	static bool IsSorted( ReadOnlySpan<ulong> keys )
	{
		for ( int i = 1; i < keys.Length; i++ )
		{
			if ( keys[i] < keys[i - 1] ) return false;
		}

		return true;
	}

	/// <summary>
	/// Sort, write instance IDs and merge compatible neighbours into runs.
	/// </summary>
	void Emit( Bucket bucket, DrawFilter draws, List<Run> runs, RenderWorld world, TransformBuffer transforms, byte[] lods )
	{
		var count = bucket.Count;
		if ( count == 0 ) return;

		// Skip sorting already ordered buckets, common in heavily instanced scenes.
		var items = bucket.Items.AsSpan( 0, count );
		var keys = bucket.Keys.AsSpan( 0, count );
		if ( !IsSorted( keys ) ) keys.Sort( items );

		var objects = world.Objects;
		var matrices = world.LocalToWorld;

		// Rigid IDs append directly; skinned IDs are grouped per mesh when the run ends.
		RenderMesh mesh = null;
		Material material = null;
		var lod = 0;
		var filter = DrawFilter.All;
		var overrideBlends = false;
		RenderAttributes attributes = null;
		RenderAttributes lightmap = null;
		RenderAttributes probe = null;
		var reads = false;
		var start = 0;
		var length = 0;
		var skinnedRun = false;
		var deformedRun = false;
		var morphableRun = false;
		skinMembers.Clear();
		tintedSlots.Clear();

		foreach ( var index in items )
		{
			if ( objects[index] is CustomObject custom )
			{
				Flush();
				var instance = transforms.AddInstance( transforms.SlotFor( custom, matrices[index], custom.Tint ) );
				runs.Add( new Run( null, 0, null, false, DrawFilter.All, instance, 1, Custom: custom ) );
				continue;
			}

			var obj = (MeshObject)objects[index];
			var objLod = lods[index];
			// A partially blended object draws only the list's part of its draws (ListDefinition.Draws)
			var objFilter = obj.BlendForLod( objLod ) != MeshBlend.Partial ? DrawFilter.All : draws;

			var skinned = obj.IsSkinned;
			var deformed = obj.IsDeformed;
			var morphable = obj.MorphSource is not null;
			if ( skinned ) WriteBones( obj, objLod, matrices[index], transforms );

			var joins = length > 0 && skinned == skinnedRun && deformed == deformedRun && morphable == morphableRun && obj.Mesh == mesh && objLod == lod && obj.MaterialOverride == material && objFilter == filter && obj.Attributes == attributes && obj.LightmapAttributes == lightmap && obj.WantsFrameBufferCopy == reads && probes[index] == probe;
			if ( !joins )
			{
				Flush();
				mesh = obj.Mesh;
				material = obj.MaterialOverride;
				attributes = obj.Attributes;
				lightmap = obj.LightmapAttributes;
				reads = obj.WantsFrameBufferCopy;
				probe = probes[index];
				lod = objLod;
				filter = objFilter;
				overrideBlends = material is not null && (obj.OverrideTranslucent || obj.OverrideAlphaTest);
				skinnedRun = skinned;
				deformedRun = deformed;
				morphableRun = morphable;
			}

			if ( skinned )
			{
				skinMembers.Add( obj );
			}
			else if ( mesh.HasDrawTints )
			{
				// Its entries now; the ids are written a block per draw when the run ends
				tintedSlots.Add( transforms.TintedSlotsFor( obj, matrices[index], obj.Tint, mesh ) );
			}
			else
			{
				var instance = transforms.AddInstance( transforms.SlotFor( obj, matrices[index], obj.Tint ) );
				if ( length == 0 ) start = instance;
			}

			length++;
		}

		Flush();

		void Flush()
		{
			if ( length == 0 ) return;

			if ( skinnedRun )
			{
				start = WriteSkinnedInstances( mesh, lod, transforms );
				runs.Add( new Run( mesh, lod, material, overrideBlends, filter, start, length, Skinned: true, Attributes: attributes, Lightmap: lightmap, ReadsFrameBuffer: reads, Probe: probe, Deformed: deformedRun, Morphable: morphableRun ) );
				skinMembers.Clear();
			}
			else if ( mesh.HasDrawTints )
			{
				// Group tinted instance IDs per draw.
				start = transforms.InstanceCount;
				var offset = mesh.TintOffsetForLod( lod );
				var draws = mesh.DrawsForLod( lod ).Length;
				for ( int d = 0; d < draws; d++ )
				{
					foreach ( var slot in tintedSlots )
						transforms.AddInstance( slot + offset + d );
				}

				runs.Add( new Run( mesh, lod, material, overrideBlends, filter, start, length, Attributes: attributes, Lightmap: lightmap, ReadsFrameBuffer: reads, Probe: probe, Tinted: true ) );
				tintedSlots.Clear();
			}
			else
			{
				runs.Add( new Run( mesh, lod, material, overrideBlends, filter, start, length, Attributes: attributes, Lightmap: lightmap, ReadsFrameBuffer: reads, Probe: probe ) );
			}

			length = 0;
		}
	}

	/// <summary>
	/// Make the materials draws fall back to, then skin this frame's vertex cache meshes (<see cref="DispatchSkinning"/>).
	/// </summary>
	internal override void Setup( RenderContext context, RenderWorld world, ViewPass view )
	{
		// On the main thread: draws may record on worker threads, where materials can't be made
		defaultMaterial ??= Material.Create( "scene_renderer_default", DefaultShader );
		depthOnlyMaterial ??= Material.Load( DepthOnlyMaterial );

		DispatchSkinning( context );
	}

	/// <summary>
	/// Native <c>CalculateLODLevel</c>: radius-0.5 sphere pixel width and maximum object scale.
	/// </summary>
	static int ChooseLod( MeshObject obj, Vector3 center, Vector3 camera, float tanHalfFov, float screenWidth )
	{
		var mesh = obj.Mesh;
		if ( mesh.LodCount == 1 ) return 0;

		// Fixed LOD takes precedence (RootLODLevel).
		if ( obj.LodOverride >= 0 ) return Math.Min( obj.LodOverride, mesh.LodCount - 1 );

		var distance = center.Distance( camera );
		var screenSize = distance < 0.5f ? 1.0f : Math.Clamp( 0.5f / (distance * tanHalfFov), 0, 1 );

		var scale = obj.Transform.Scale;
		var largest = MathF.Max( MathF.Abs( scale.x ), MathF.Max( MathF.Abs( scale.y ), MathF.Abs( scale.z ) ) );

		return mesh.LodForScreenSize( screenSize * screenWidth, largest );
	}

	internal override void Dispose()
	{
		defaultMaterial?.Destroy();
		defaultMaterial = null;
		volumeRing.Dispose();
	}

	/// <summary>
	/// Worker-safe when no custom objects will draw.
	/// </summary>
	internal override bool CanRecordOffMainThread( ViewPass view, MeshLayer layer )
	{
		return !layer.DrawsCustomObjectsIn( view ) || (view.State<RunLists>( this ).Custom & layer.Runs) == 0;
	}

	internal override bool ReadsFrameBuffer( ViewPass view, MeshRuns runs ) => (view.State<RunLists>( this ).FrameBufferReads & runs) != 0;

	/// <summary>
	/// Count runs in the layer's selected lists, in list order.
	/// </summary>
	internal override int RunCount( ViewPass view, MeshLayer layer )
	{
		var lists = view.State<RunLists>( this );
		var runs = 0;
		for ( int bit = 0; bit < lists.ByBit.Length; bit++ )
		{
			if ( ((int)layer.Runs & (1 << bit)) != 0 ) runs += lists.ByBit[bit].Count;
		}

		return runs;
	}

	/// <summary>
	/// A run costs a draw for each of its LOD's draws: one mesh of a map's world geometry can be hundreds.
	/// </summary>
	internal override void RunWeights( ViewPass view, MeshLayer layer, Span<int> weights )
	{
		var lists = view.State<RunLists>( this );
		var i = 0;
		for ( int bit = 0; bit < lists.ByBit.Length; bit++ )
		{
			if ( ((int)layer.Runs & (1 << bit)) == 0 ) continue;

			foreach ( ref readonly var run in CollectionsMarshal.AsSpan( lists.ByBit[bit] ) )
				weights[i++] = run.Custom is not null ? 1 : Math.Max( 1, run.Mesh.DrawsForLod( run.Lod ).Length );
		}
	}

	/// <summary>
	/// Draw a run range, applying each list's fade combo.
	/// </summary>
	internal override void Draw( RenderContext context, ViewPass view, MeshLayer layer, int first, int count, ref RenderStats stats )
	{
		var lists = view.State<RunLists>( this );
		var end = first + count;

		// Each list's part of [first, end), in order
		var start = 0;
		for ( int bit = 0; bit < lists.ByBit.Length; bit++ )
		{
			if ( (layer.Runs & Lists[bit].Runs) == 0 ) continue;
			DrawListRange( context, view, CollectionsMarshal.AsSpan( lists.ByBit[bit] ), layer, first, end, ref start, Lists[bit].Fade, ref stats );
		}
	}

	void DrawListRange( RenderContext context, ViewPass view, ReadOnlySpan<Run> runs, MeshLayer layer, int first, int end, ref int start, bool fade, ref RenderStats stats )
	{
		var from = Math.Max( first - start, 0 );
		var to = Math.Min( end - start, runs.Length );
		start += runs.Length;
		if ( from >= to ) return;

		if ( fade ) context.SetCombo( OpaqueFade, 1 );
		DrawRuns( context, view, runs[from..to], layer, ref stats );
		if ( fade ) context.SetCombo( OpaqueFade, 0 );
	}

	void DrawRuns( RenderContext context, ViewPass view, ReadOnlySpan<Run> runs, MeshLayer layer, ref RenderStats stats )
	{
		var depth = layer.Depth;

		// The depth-normals prepass runs materials' own depth pixel shaders into the G-buffer (DepthNormalPrepassLayer)
		var normals = depth && layer.WritesNormals && view.View.DepthNormals && !view.IsShadow;
		var mode = layer.ShaderMode;
		var shadow = layer.LayerType == SceneLayerType.Shadow;
		var translucentLayer = layer.LayerType == SceneLayerType.Translucent;
		var depthBias = shadow ? view.DepthBias : 0;
		var slopeBias = shadow ? view.SlopeScaledDepthBias : 0;

		foreach ( var run in runs )
		{
			// Bind the custom transform, draw, then restore frame targets.
			if ( run.Custom is { } custom )
			{
				if ( !layer.DrawsCustomObjectsIn( view ) ) continue;
				if ( layer.WritesNormals && !custom.DepthPrepass ) continue;

				context.BindInstances( 1, run.FirstInstance );
				custom.Render( context, view, layer );
				context.RestoreBound();
				stats.CustomDraws++;
				continue;
			}

			// Copy on the first reader per layer (MakeFrameBufferCopy).
			if ( run.ReadsFrameBuffer && !depth ) context.CopyFrameBuffer();

			var mesh = run.Mesh;

			// Forward draws use lightmaps or probes (CBaseSceneObjectDesc::DrawArray, CalculateBatchFlags).
			// Shared attributes preserve material binds between runs.
			var lightmap = depth ? null : run.Lightmap;
			var probe = depth ? null : run.Probe;

			var lodDraws = mesh.DrawsForLod( run.Lod );
			for ( int drawIndex = 0; drawIndex < lodDraws.Length; drawIndex++ )
			{
				var draw = lodDraws[drawIndex];
				if ( run.Filter == DrawFilter.Opaque && draw.Translucent ) continue;
				if ( run.Filter == DrawFilter.Translucent && !draw.Translucent ) continue;

				context.SetObjectAttributes( run.Attributes, draw.Lightmapped ? lightmap : probe );

				// Skinned draws select per-mesh instance blocks and optional cached vertices.
				var first = run.FirstInstance;
				var vertexCache = false;

				// Tinted draws select per-draw instance blocks.
				if ( run.Tinted ) first += drawIndex * run.Count;

				if ( run.Skinned )
				{
					first += mesh.BlockForLod( run.Lod, draw.Mesh ) * run.Count;
					// Deformation and morphs force vertex-cache skinning.
					vertexCache = mesh.SkinFor( draw.Mesh ) is { } drawSkin && (drawSkin.UsesVertexCache || run.Deformed || (run.Morphable && drawSkin.Morphs));
					context.SetCombo( CsVertexAnimation, vertexCache ? 1 : 0 );
				}

				var material = MaterialFor( run, draw, depth && !normals, layer.ForceDepthFastPath, out var stripPixelShader );
				if ( vertexCache )
				{
					if ( !context.BindGeometry( mesh, draw ) ) continue;
					if ( !context.SetRenderState( material, mode, mesh, draw, depthBias, slopeBias, stripPixelShader, vertexCache ) ) continue;

					// Override model stream 1 with instance IDs before binding cache IDs.
					context.BindInstances( 1, first );
					context.BindVertexCacheIds( mesh, draw );
					context.DrawIndexedInstanced( draw, run.Count );
				}
				else if ( !context.DrawModel( material, mode, mesh, draw, first, run.Count, depthBias, slopeBias, stripPixelShader ) )
				{
					continue;
				}

				if ( shadow )
				{
					stats.ShadowDraws++;
					continue;
				}

				if ( depth )
				{
					stats.DepthDraws++;
					continue;
				}

				if ( translucentLayer ) stats.TranslucentDraws++;

				stats.Draws++;
				stats.Instances += run.Count;
				stats.Triangles += (long)(draw.IndexCount / 3) * run.Count;
			}

			if ( run.Skinned )
			{
				context.SetCombo( CsVertexAnimation, 0 );
			}
		}

		context.SetObjectAttributes( null );
	}

	/// <summary>
	/// Select depth overrides or strip opaque non-alpha-tested pixel shaders (<c>CSceneSystem::ShouldOverrideDepthMaterial</c>).
	/// Only fast-path depth draws dither when faded.
	/// </summary>
	Material MaterialFor( in Run run, in RenderMesh.Draw draw, bool depth, bool forceDepthOnly, out bool stripPixelShader )
	{
		stripPixelShader = false;
		if ( !UseMeshMaterials ) return defaultMaterial;

		// A layer on the forced fast path draws everything with it, overrides too (CSceneSystem::ShouldOverrideDepthMaterial)
		if ( depth && forceDepthOnly && depthOnlyMaterial is not null ) return depthOnlyMaterial;

		if ( run.Override is { } material )
		{
			stripPixelShader = depth && !run.OverrideBlends;
			return material;
		}

		if ( depth && draw.ShadowFastPath && depthOnlyMaterial is not null ) return depthOnlyMaterial;

		stripPixelShader = depth && !draw.Translucent && !draw.AlphaTest;
		return draw.Material ?? defaultMaterial;
	}
}

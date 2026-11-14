using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Borealis.Features;

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
	/// Compatible instances sharing mesh, LOD and draw state: a <see cref="Bin"/>'s objects a view drew, as a range of its
	/// instance ids. Skinned IDs are grouped per model mesh; custom objects occupy individual runs. Lighting and frame-copy
	/// inputs apply in forward passes. <see cref="TextureScale"/> and <see cref="TextureFallback"/> are the largest of its
	/// objects' texture sizes (<see cref="TextureSizes.ForObject"/>), which its draws ask texture streaming for: all mips by
	/// default.
	/// </summary>
	internal readonly record struct Run( Bin Bin, int FirstInstance, int Count, int CullDraw = -1, float TextureScale = -1, float TextureFallback = ushort.MaxValue )
	{
		public RenderMesh Mesh => Bin.Mesh;
		public int Lod => Bin.Lod;
		public GpuMaterial Override => Bin.Override;
		public bool OverrideBlends => Bin.OverrideBlends;
		public DrawFilter Filter => Bin.Filter;
		public bool Skinned => Bin.Skinned;
		public ShaderParameters Attributes => Bin.Attributes;
		public CustomObject Custom => Bin.Custom;
		public ShaderParameters Lightmap => Bin.Lightmap;
		public bool ReadsFrameBuffer => Bin.Custom is { } custom ? custom.WantsFrameBufferCopy : Bin.ReadsFrameBuffer;
		public ShaderParameters Probe => Bin.Probe;
		public bool Tinted => Bin.Tinted;
		public bool Deformed => Bin.Deformed;
		public bool Morphable => Bin.Morphable;
		public DrawCallbacks Callbacks => Bin.Callbacks;
	}

	/// <summary>
	/// One draw of a run: the unit a layer records. A run groups instances; its draws can go out apart from each other, as
	/// native's batch lists sort single draws (<c>CMeshDrawPrimitive_t</c>), so draws of one material follow each other.
	/// <see cref="Draw"/> indexes the run's LOD draws, and is 0 for a custom object.
	/// </summary>
	internal readonly record struct DrawItem( int Run, int Draw );

	/// <summary>
	/// A view's runs, each list in draw order: what native's opaque, fade and translucent layers would hold.
	/// </summary>
	internal sealed class RunLists
	{
		/// <summary>
		/// Every list, by the bit of <see cref="MeshRuns"/> it's drawn for - its instance groups.
		/// </summary>
		public readonly List<Run>[] ByBit = NewLists<Run>();

		/// <summary>
		/// Every list's draws, by the same bit, in draw order (<see cref="BuildItems"/>).
		/// </summary>
		public readonly List<DrawItem>[] ItemsByBit = NewLists<DrawItem>();

		static List<T>[] NewLists<T>()
		{
			var lists = new List<T>[Lists.Length];
			for ( int i = 0; i < lists.Length; i++ )
				lists[i] = new List<T>();
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

		/// <summary>
		/// Lists containing objects with <see cref="DrawCallbacks"/>, which colour layers record on the main thread.
		/// </summary>
		public MeshRuns Callbacks;

		/// <summary>
		/// The opaque list's draws the device draws GPU-driven, taken out of <see cref="ItemsByBit"/>: the aggregates' feature
		/// draws them with its batches, so they share buckets (<see cref="TakeGpuDriven"/>).
		/// </summary>
		public readonly List<DrawItem> GpuDriven = new();

		/// <summary>Each static overlay run's render order, by run (<see cref="StaticOverlay"/>).</summary>
		public readonly List<int> OverlayOrders = new();

		public void Clear()
		{
			foreach ( var list in ByBit )
				list.Clear();
			foreach ( var list in ItemsByBit )
				list.Clear();
			GpuDriven.Clear();
			OverlayOrders.Clear();
			Custom = 0;
			Callbacks = 0;
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

		[MethodImpl( MethodImplOptions.AggressiveInlining )]
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
	ShaderParameters[] probes = new ShaderParameters[256];

	/// <summary>
	/// What <see cref="Classify"/> made of each visible object, by its place in the visible list.
	/// </summary>
	Classified[] classified = new Classified[256];

	/// <summary>
	/// The view <see cref="Classify"/> is working on while it runs.
	/// </summary>
	(RenderWorld World, ViewPass Pass, List<int> Visible, Vector3 Camera, float TanHalfFov, float ScreenWidth, Vector3 SortCamera, List<LightProbeVolume> Volumes, long VolumesKey, int Generation, TextureSizes Textures) classifying;

	/// <summary>
	/// The frame (<see cref="TransformBuffer.Generation"/>) each object's LOD in <see cref="lods"/> was chosen in, by world index:
	/// every view of a frame takes the LOD the first chose, as all of them choose it from the main view's camera.
	/// </summary>
	int[] lodFrames = new int[256];

	/// <summary>
	/// What main views made of each object, by world index, kept between frames (<see cref="Kept"/>).
	/// </summary>
	Kept[] keptMain = new Kept[256];

	/// <summary>
	/// What shadow views made of each object, by world index, kept between frames. Every shadow view makes the same of an
	/// object: its LOD comes from the main view's camera and shadows take no probe. Only the sort distance is each view's own.
	/// </summary>
	Kept[] keptShadow = new Kept[256];

	/// <summary>
	/// What shadow views that pick LODs by their own texels (<see cref="ViewPass.LodTexelsPerUnit"/>, the sun's last cascade)
	/// made of each object. Kept apart from <see cref="keptShadow"/>, so an object in a near cascade and the last one isn't
	/// made again twice a frame for its two LODs.
	/// </summary>
	Kept[] keptTexel = [];

	Kept[] KeptFor( ViewPass pass ) => pass.LodTexelsPerUnit > 0 ? keptTexel : pass.IsShadow ? keptShadow : keptMain;

	/// <summary>
	/// What an object is classified as, kept between frames while its draw version (<see cref="RenderWorld.DrawVersions"/>)
	/// is the one it was made at and its LOD and probe are the ones it was made for. Whatever else decides its lists and
	/// batch key gives it a new draw version when it changes.
	/// </summary>
	struct Kept
	{
		public int Version;
		public KeptFlags Flags;

		/// <summary>
		/// The LOD <see cref="Class"/> is for, or -1 while it isn't made.
		/// </summary>
		public int Lod;

		/// <summary>
		/// The light probe volume attributes <see cref="Class"/> is for.
		/// </summary>
		public ShaderParameters Probe;

		/// <summary>
		/// Its lists and keys, without a sort distance, which is each view's own.
		/// </summary>
		public Classified Class;
	}

	/// <summary>
	/// What kind of object a <see cref="Kept"/> is, which only a change alters.
	/// </summary>
	[Flags]
	enum KeptFlags : byte
	{
		Custom = 1,
		NoMesh = 2,
		Lods = 4,
		NeedsProbe = 8,
	}

	struct Classified
	{
		public MeshRuns Runs;
		public MeshRuns FrameBufferReads;

		/// <summary>
		/// The bin it draws in, found on the main thread as it's put in its lists; a custom object's own.
		/// </summary>
		public Bin Bin;
		public uint Distance;
		public int RenderOrder;
		public bool OnlyTranslucent;

		/// <summary>
		/// <see cref="MeshObject.OcclusionCulling"/>, so emitting a kept object doesn't read it.
		/// </summary>
		public bool Occludes;

		/// <summary>
		/// A custom object, left for the main thread: it isn't classified like a mesh.
		/// </summary>
		public bool Custom;

		/// <summary>
		/// A mesh with <see cref="DrawCallbacks"/>, in a view that runs them.
		/// </summary>
		public bool Callbacks;

		/// <summary>
		/// Its texture sizes in this view (<see cref="TextureSizes.ForObject"/>), each view's own as the sort distance is.
		/// </summary>
		public float TextureScale;
		public float TextureFallback;
	}

	/// <summary>
	/// Whether <see cref="Prepare"/> may classify a big view's objects across the job dispatcher's threads
	/// (<see cref="RenderSystem.ParallelRecording"/>).
	/// </summary>
	internal bool Parallel { get; set; } = true;

	/// <summary>
	/// Visible objects a view needs before classifying them is split across threads. Classifying on one thread leaves
	/// the objects in its cache for emitting them after; split, emitting fetches them again, which costs as much as the
	/// split saves until there are too many objects to stay in cache anyway: even at 10-20k objects, 15% faster from 35k.
	/// </summary>
	internal static int MinParallelClassify { get; set; } = 32_000;

	GpuMaterial defaultMaterial;
	GpuMaterial depthOnlyMaterial;

	// One default material for every render system, made by the first: a child view's system starts in the middle of its
	// parent's frame, where the engine makes no materials. Freed with the last system using it
	static GpuMaterial s_defaultMaterial;
	static int s_defaultMaterialUsers;

	/// <summary>
	/// Collects the main view's rigid opaque instances for occlusion culling. Null when not culling, and never set for shadow views.
	/// </summary>
	internal OcclusionCuller Occlusion { get; set; }

	/// <summary>
	/// Borealis's debug views the frame draws, set before the main view prepares: LOD colours and cull reasons tint meshes
	/// through their transform entries (<see cref="TintFor"/>).
	/// </summary>
	internal DebugViews DebugViews { get; set; }

	/// <summary>
	/// The view being prepared's cull reasons (<see cref="ViewPass.CullReasons"/>), while <see cref="Prepare"/> runs.
	/// </summary>
	byte[] tintReasons;

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
		if ( !pass.IsShadow ) DropUnusedBins();

		var visible = pass.Visible( this );
		if ( visible.Count == 0 ) return;
		tintReasons = pass.CullReasons;

		var objects = world.Objects;
		var centers = world.BoundsCenter;
		var extents = world.BoundsExtents;

		// Attributes read as the objects draw, here on the main thread, before classifying reads them on several. A world with
		// no attribute sources (the lab's own) skips the pass
		if ( world.AttributeSources > 0 )
		{
			foreach ( var index in CollectionsMarshal.AsSpan( visible ) )
			{
				if ( objects[index] is MeshObject { AttributesFrom: not null } mesh ) mesh.RefreshAttributes();
			}
		}

		// LOD uses a radius-0.5 sphere's pixel width. Shadows use the main view to match visible LODs.
		var root = pass.Root;
		var camera = root.Position;
		var tanHalfFov = MathF.Tan( root.FieldOfView.DegreeToRadian() * 0.5f );
		var screenWidth = root.Viewport.Width;

		// Translucency sorts from the camera drawing it - a shadow view's own
		var sortCamera = pass.View.Position;

		// What texture sizes the view's draws ask streaming for
		var textureSizes = new TextureSizes( pass.View, root );

		foreach ( var bucket in buckets )
			bucket.Count = 0;

		if ( lods.Length < objects.Length ) Array.Resize( ref lods, Math.Max( objects.Length, lods.Length * 2 ) );
		if ( probes.Length < lods.Length ) Array.Resize( ref probes, lods.Length );
		if ( lodFrames.Length < lods.Length ) Array.Resize( ref lodFrames, lods.Length );
		if ( keptMain.Length < lods.Length ) Array.Resize( ref keptMain, lods.Length );
		if ( keptShadow.Length < lods.Length ) Array.Resize( ref keptShadow, lods.Length );
		if ( pass.LodTexelsPerUnit > 0 && keptTexel.Length < lods.Length ) Array.Resize( ref keptTexel, lods.Length );
		if ( classified.Length < visible.Count ) Array.Resize( ref classified, Math.Max( visible.Count, classified.Length * 2 ) );
		var volumes = world.LightProbeVolumes;
		var volumesKey = volumes.Count > 0 && !pass.IsShadow ? LightProbeVolume.Key( volumes ) : 0;

		// Each object's LOD, probe, lists and sort keys are its own, so a big view classifies across threads
		classifying = (world, pass, visible, camera, tanHalfFov, screenWidth, sortCamera, volumes, volumesKey, transforms.Generation, textureSizes);
		if ( Parallel && visible.Count >= MinParallelClassify && JobDispatcher.MaxDegreeOfParallelism > 1 )
			JobDispatcher.ForBatched( visible.Count, this, static ( feature, start, end ) => feature.Classify( start, end ) );
		else
			Classify( 0, visible.Count );
		classifying = default;

		// Then into the buckets in visible order, whichever thread classified what. Buckets hold places in the visible list
		var visibleSpan = CollectionsMarshal.AsSpan( visible );
		var kept = KeptFor( pass );
		var opaque = buckets[0];
		MeshRuns frameBufferReads = 0;
		MeshRuns callbacks = 0;
		for ( int k = 0; k < visibleSpan.Length; k++ )
		{
			var index = visibleSpan[k];
			ref var c = ref classified[k];

			// Membership comes from MeshRenderFeature.Lists.cs.
			if ( c.Custom )
			{
				var custom = (CustomObject)objects[index];
				var customRuns = ListsFor( custom, pass.IsShadow );
				if ( customRuns == 0 ) continue;

				// Its own bin, kept with it
				c.Bin ??= kept[index].Class.Bin ??= new Bin( custom );

				var customDistance = (customRuns & DistanceSortedLists) != 0 ? TranslucentSortDistance( centers[index], extents[index], sortCamera ) : 0;
				AddToLists( customRuns, k, index, c.Bin.Key, customDistance, !custom.IsOpaque, 0 );
				layers.Custom |= customRuns;
				if ( custom.WantsFrameBufferCopy && !pass.IsShadow ) layers.FrameBufferReads |= customRuns & FrameBufferCopyingLists;
				continue;
			}

			if ( c.Runs == 0 ) continue;

			// Its bin, found here on the main thread the first time it's drawn as it is now, or if its bin went unused and was dropped
			if ( c.Bin is not { Dropped: false } )
			{
				ref var keep = ref kept[index];
				c.Bin = keep.Class.Bin = BinFor( (MeshObject)objects[index], keep.Lod, keep.Probe );
			}

			frameBufferReads |= c.FrameBufferReads;
			if ( c.Callbacks ) callbacks |= c.Runs;

			// Most objects go in the opaque list alone
			if ( c.Runs == MeshRuns.Opaque ) opaque.Add( 0, k );
			else AddToLists( c.Runs, k, index, c.Bin.Key, c.Distance, c.OnlyTranslucent, c.RenderOrder );
		}

		layers.FrameBufferReads |= frameBufferReads;
		layers.Callbacks |= callbacks;

		for ( int bit = 0; bit < buckets.Length; bit++ )
		{
			// A batched list groups its objects by bin; the others sort theirs
			if ( Lists[bit].Sort == ListSort.Batched ) GroupByBin( buckets[bit], Lists[bit].Draws );
			else SortBucket( buckets[bit] );

			Emit( buckets[bit], Lists[bit].Draws, layers.ByBit[bit], world, visibleSpan, transforms, bit == 0 && !pass.IsShadow ? Occlusion : null );
			BuildItems( layers.ByBit[bit], layers.ItemsByBit[bit], Lists[bit].Sort == ListSort.Batched );
			if ( Lists[bit].Runs == MeshRuns.StaticOverlay ) RenderOrders( buckets[bit], layers.ByBit[bit], layers.OverlayOrders );
		}

		overlayTransforms = transforms;

		// Static opaque draws go with the aggregates when the device draws them GPU-driven, as one run (so not while
		// occlusion culls, which the aggregates' feature then draws batch by batch)
		if ( Occlusion is null && RenderHost.Current?.Device is { DrawsMeshesGpuDriven: true } ) TakeGpuDriven( layers );
	}

	/// <summary>
	/// Move the opaque list's draws the GPU-driven path can take into <see cref="RunLists.GpuDriven"/>: static meshes'
	/// instances with nothing per object but their lighting (no attributes, callbacks, skinning, frame copy or culled
	/// arguments), which the aggregates' feature hands the device with its own batches.
	/// </summary>
	static void TakeGpuDriven( RunLists layers )
	{
		var runs = CollectionsMarshal.AsSpan( layers.Opaque );
		var items = layers.ItemsByBit[0];
		var kept = 0;
		for ( int i = 0; i < items.Count; i++ )
		{
			var item = items[i];
			ref readonly var run = ref runs[item.Run];
			if ( run.Custom is null && run.Callbacks is null && !run.Skinned && !run.Deformed && !run.Morphable && !run.ReadsFrameBuffer && run.CullDraw < 0 && run.Attributes is null )
			{
				layers.GpuDriven.Add( item );
				continue;
			}

			items[kept++] = item;
		}

		CollectionsMarshal.SetCount( items, kept );
	}

	/// <summary>
	/// For the aggregates' feature: a GPU-driven draw of <see cref="RunLists.GpuDriven"/> as its batch would be, with the
	/// material, lighting and first instance <see cref="DrawItems"/> would use; null when it has no material.
	/// </summary>
	internal GpuContext.GpuDrivenBatch? GpuDrivenBatch( ViewPass view, DrawItem item, bool depth, bool forceDepthOnly )
	{
		var lists = view.State<RunLists>( this );
		ref readonly var run = ref CollectionsMarshal.AsSpan( lists.Opaque )[item.Run];
		ref readonly var draw = ref run.Mesh.DrawsForLod( run.Lod )[item.Draw];
		var material = MaterialFor( run, draw, depth, forceDepthOnly, out var strip );
		if ( material is null ) return null;

		var probe = depth || run.Probe is null || !RenderMesh.NeedsLightProbe( material ) ? null : run.Probe;
		var lighting = draw.Lightmapped ? (depth ? null : run.Lightmap) : probe;
		var first = run.FirstInstance + (run.Tinted ? item.Draw * run.Count : 0);
		var textureSize = TextureSizes.ForDraw( draw.UvDensity, run.TextureScale, run.TextureFallback );
		return new GpuContext.GpuDrivenBatch( material, run.Mesh, draw, lighting, strip, -1, run.Count, first, (long)(draw.IndexCount / 3) * run.Count, null, Instances: true, TextureSize: textureSize );
	}

	/// <summary>The view's GPU-driven draws the device left, drawn here as <see cref="DrawItems"/> draws any.</summary>
	internal void DrawGpuDrivenLeft( GpuContext context, ViewPass view, ReadOnlySpan<DrawItem> items, MeshLayer layer, ref RenderStats stats )
	{
		if ( items.IsEmpty ) return;
		DrawItems( context, view, CollectionsMarshal.AsSpan( view.State<RunLists>( this ).Opaque ), items, layer, ref stats, MeshRuns.Opaque, left: true );
	}

	/// <summary>A GPU-driven draw's triangles (over its instances) and instances, for the stats.</summary>
	internal (long Triangles, int Instances) GpuDrivenCounts( ViewPass view, DrawItem item )
	{
		ref readonly var run = ref CollectionsMarshal.AsSpan( view.State<RunLists>( this ).Opaque )[item.Run];
		return ((long)(run.Mesh.DrawsForLod( run.Lod )[item.Draw].IndexCount / 3) * run.Count, run.Count);
	}

	/// <summary>
	/// For a draw census (<see cref="GpuContext.Census"/>): what a run's draws are and why they aren't GPU-driven, by
	/// <see cref="TakeGpuDriven"/>'s rules.
	/// </summary>
	static string CensusNote( in Run run, MeshRuns list, bool left )
	{
		var kind = run.Custom is not null ? $"custom object ({run.Custom.GetType().Name})"
			: run.Skinned ? (run.Morphable ? "skinned mesh, morphs" : run.Deformed ? "skinned mesh, deformed" : "skinned mesh")
			: run.Morphable ? "mesh with morphs" : "static mesh";
		var why = left ? "the device's GPU-driven path left it"
			: list != MeshRuns.Opaque ? $"{list} list (only the opaque list goes GPU-driven)"
			: run.Custom is not null ? "custom object"
			: run.Callbacks is not null ? "draw callbacks"
			: run.Skinned ? "skinned"
			: run.Deformed ? "deformed"
			: run.Morphable ? "morphs"
			: run.ReadsFrameBuffer ? "reads the frame buffer"
			: run.CullDraw >= 0 ? "occlusion culled"
			: run.Attributes is not null ? "object attributes"
			: RenderHost.Current?.Device is { DrawsMeshesGpuDriven: true } ? "occlusion culling on" : "GPU-driven meshes off";
		return $"{kind}: {why}";
	}

	/// <summary>The view's draws <see cref="TakeGpuDriven"/> took.</summary>
	internal List<DrawItem> GpuDrivenItems( ViewPass view ) => view.State<RunLists>( this ).GpuDriven;

	/// <summary>
	/// Classify visible objects [<paramref name="start"/>, <paramref name="end"/>) of the view in <see cref="classifying"/>:
	/// each mesh's LOD, probe, lists and sort keys. Writes only what's the object's own, so ranges can run on any thread.
	/// Lists and keys come from what was kept of the object (<see cref="Kept"/>), and are made again only when it changed.
	/// </summary>
	void Classify( int start, int end )
	{
		var (world, pass, visible, camera, tanHalfFov, screenWidth, sortCamera, volumes, volumesKey, generation, textureSizes) = classifying;
		var objects = world.Objects;
		var versions = world.DrawVersions;
		var centers = world.BoundsCenter;
		var extents = world.BoundsExtents;
		var shadow = pass.IsShadow;
		var kept = KeptFor( pass );
		var texelsPerUnit = pass.LodTexelsPerUnit;

		var made = 0;

		for ( int k = start; k < end; k++ )
		{
			var index = visible[k];
			ref var c = ref classified[k];
			ref var keep = ref kept[index];

			// Changed since it was kept, or never kept
			if ( keep.Version != versions[index] ) keep = Rebase( objects[index], versions[index], shadow );

			if ( (keep.Flags & (KeptFlags.Custom | KeptFlags.NoMesh)) != 0 )
			{
				c = default;
				c.Custom = (keep.Flags & KeptFlags.Custom) != 0;
				c.Bin = keep.Class.Bin;
				continue;
			}

			// One LOD a frame, whichever view chooses it. Only a mesh with LODs needs its object for that. A view picking by its
			// own texels has its own, which the others don't take
			int lod;
			if ( texelsPerUnit > 0 ) lod = (keep.Flags & KeptFlags.Lods) != 0 ? ChooseLodByTexels( (MeshObject)objects[index], texelsPerUnit ) : 0;
			else if ( lodFrames[index] == generation ) lod = lods[index];
			else
			{
				lod = 0;
				if ( (keep.Flags & KeptFlags.Lods) != 0 )
				{
					var obj = (MeshObject)objects[index];
					lod = ChooseLod( obj, centers[index], camera, tanHalfFov, screenWidth );

					// Written only when it changes: a write from a worker would leave the object's memory on that core, for the
					// main thread to fetch back when it emits the object
					if ( !shadow && obj.DrawnLod != lod ) obj.DrawnLod = lod;
				}

				lods[index] = (byte)lod;
				lodFrames[index] = generation;
			}

			// Probe lighting is forward-only (CBaseSceneObjectDesc::GeneratePrimitives).
			ShaderParameters probeAttributes = null;
			if ( !shadow && (keep.Flags & KeptFlags.NeedsProbe) != 0 && volumes.Count > 0 )
			{
				var probe = ProbeFor( (MeshObject)objects[index], volumes, volumesKey, centers[index], extents[index] );
				probeAttributes = probe >= 0 ? volumes[probe].Attributes : null;
			}
			if ( probeAttributes is not null || probes[index] is not null ) probes[index] = probeAttributes;

			if ( keep.Lod != lod || keep.Probe != probeAttributes )
			{
				made++;
				keep.Lod = lod;
				keep.Probe = probeAttributes;
				keep.Class = ClassOf( (MeshObject)objects[index], lod, shadow );
			}

			c = keep.Class;
			if ( (c.Runs & DistanceSortedLists) != 0 ) c.Distance = TranslucentSortDistance( centers[index], extents[index], sortCamera );
			(c.TextureScale, c.TextureFallback) = textureSizes.ForObject( centers[index], extents[index] );
		}

		if ( made > 0 ) Interlocked.Add( ref objectsClassified, made );
	}

	/// <summary>
	/// Keep an object again from its new draw version: what kind it is. Its lists are made when it's next classified.
	/// </summary>
	static Kept Rebase( RenderObject obj, int version, bool shadow )
	{
		var keep = new Kept { Version = version, Lod = -1 };

		// Anything else this feature takes is a custom object
		if ( obj is not MeshObject mesh )
		{
			keep.Flags = KeptFlags.Custom;
			return keep;
		}

		if ( mesh.Mesh is not { } model )
		{
			keep.Flags = KeptFlags.NoMesh;
			return keep;
		}

		// A mesh without LODs draws its only one
		if ( model.LodCount > 1 ) keep.Flags |= KeptFlags.Lods;
		else if ( !shadow && mesh.DrawnLod != 0 ) mesh.DrawnLod = 0;

		if ( mesh.NeedsLightProbe ) keep.Flags |= KeptFlags.NeedsProbe;
		return keep;
	}

	/// <summary>
	/// A mesh's lists at a LOD, without a sort distance. Its bin is found on the main thread, as it's put in its lists.
	/// </summary>
	static Classified ClassOf( MeshObject obj, int lod, bool shadow )
	{
		var c = default( Classified );
		var blend = obj.BlendForLod( lod );
		var runs = ListsFor( obj, blend, shadow );
		if ( runs == 0 ) return c;

		c.Runs = runs;

		// A translucent layer copies the frame for what reads it
		if ( obj.WantsFrameBufferCopy && !shadow ) c.FrameBufferReads = runs & FrameBufferCopyingLists;

		c.OnlyTranslucent = blend == MeshBlend.Translucent;
		c.Occludes = obj.OcclusionCulling;
		c.Callbacks = obj.Callbacks is not null && !shadow;
		c.RenderOrder = obj.RenderOrder;
		return c;
	}

	int keysMade;
	int objectsClassified;

	/// <summary>
	/// Meshes classified, batch keys hashed (<see cref="OpaqueKey"/>) and buckets sorted since the feature was made - for the
	/// tests, which count work rather than time it.
	/// </summary>
	internal int ObjectsClassified => Volatile.Read( ref objectsClassified );
	internal int KeysMade => Volatile.Read( ref keysMade );
	internal int BucketsSorted { get; private set; }

	ulong[] itemKeys = new ulong[256];
	DrawItem[] itemScratch = new DrawItem[256];

	/// <summary>
	/// A list's draws: each run's, in run order, or for a batched list sorted so draws that bind the same state follow each
	/// other (<see cref="ItemKey"/>), however their runs were grouped for instancing. Each run's bin has its draws and their
	/// sort keys.
	/// </summary>
	void BuildItems( List<Run> runs, List<DrawItem> items, bool sorted )
	{
		var runSpan = CollectionsMarshal.AsSpan( runs );
		var count = 0;
		foreach ( ref readonly var run in runSpan )
			count += run.Bin.Custom is null ? run.Bin.Draws.Length : 1;

		if ( count == 0 ) return;
		CollectionsMarshal.SetCount( items, count );
		var itemSpan = CollectionsMarshal.AsSpan( items );

		sorted &= count > 1;
		if ( sorted && itemKeys.Length < count )
		{
			itemKeys = new ulong[Math.Max( count, itemKeys.Length * 2 )];
			itemScratch = new DrawItem[itemKeys.Length];
		}

		// The low 24 bits are the item's index, so sorting the keys orders the items
		var keys = sorted ? itemKeys.AsSpan( 0, count ) : default;
		var i = 0;
		for ( int r = 0; r < runSpan.Length; r++ )
		{
			var bin = runSpan[r].Bin;
			if ( bin.Custom is not null )
			{
				if ( sorted ) keys[i] = (uint)i;
				itemSpan[i++] = new DrawItem( r, 0 );
				continue;
			}

			var draws = bin.Draws;
			var drawKeys = bin.DrawKeys;
			for ( int d = 0; d < draws.Length; d++ )
			{
				if ( sorted ) keys[i] = drawKeys[d] | (uint)i;
				itemSpan[i++] = new DrawItem( r, draws[d] );
			}
		}

		if ( !sorted || IsSorted( keys ) ) return;
		keys.Sort();

		var scratch = itemScratch.AsSpan( 0, count );
		itemSpan.CopyTo( scratch );
		for ( i = 0; i < count; i++ )
			itemSpan[i] = scratch[(int)(keys[i] & 0xFFFFFF)];
	}

	/// <summary>
	/// A batched draw's sort key, above its 24 index bits, as native's batch lists sort single draws by what they bind
	/// (<c>CBaseSceneObjectDesc::BindMaterial</c>): depth-only draws first, as the prepass draws them all with one material
	/// (<see cref="MaterialFor"/>), then skinned apart from rigid, then similarity (13 bits), material (16) and the bin's
	/// attributes, lightmap and probe (8), which a change of rebinds. Custom objects go first, where their runs always sorted.
	/// Collisions only cost binds. Made once a bin.
	/// </summary>
	static ulong ItemKey( Bin bin, int drawIndex, uint state, ref (GpuMaterial Material, uint Bits) hashed )
	{
		ref readonly var draw = ref bin.Mesh.DrawsForLod( bin.Lod )[drawIndex];
		var material = bin.Override ?? draw.Material;
		var depthOnly = bin.Override is null && draw.ShadowFastPath;

		var similarity = bin.Override is null ? bin.Mesh.SimilarityForDraw( bin.Lod, drawIndex ) : 0;
		similarity ^= similarity >> 24 ^ similarity >> 48;

		if ( material != hashed.Material )
		{
			var bits = (uint)RuntimeHelpers.GetHashCode( material );
			hashed = (material, (bits ^ bits >> 16) & 0xFFFF);
		}

		return (depthOnly ? 0 : 1UL << 62) | (bin.Skinned ? 1UL << 61 : 0) | (similarity & 0x1FFF) << 48 | (ulong)hashed.Bits << 32 | (ulong)state << 24;
	}

	/// <summary>
	/// A bin's attributes, lightmap and probe, hashed to <see cref="ItemKey"/>'s 8 bits.
	/// </summary>
	static uint StateBits( ShaderParameters attributes, ShaderParameters lightmap, ShaderParameters probe )
	{
		var state = (uint)RuntimeHelpers.GetHashCode( attributes ) ^ (uint)RuntimeHelpers.GetHashCode( lightmap ) * 0x9E3779B1u
			^ (uint)RuntimeHelpers.GetHashCode( probe ) * 0x85EBCA77u;
		return (state ^ state >> 16 ^ state >> 8) & 0xFF;
	}

	/// <summary>
	/// Add an object, by its place <paramref name="k"/> in the visible list, to selected buckets using each list's sort rule.
	/// Batched lists group by bin instead (<see cref="GroupByBin"/>).
	/// </summary>
	void AddToLists( MeshRuns runs, int k, int index, ulong batchKey, uint distance, bool onlyTranslucent, int renderOrder )
	{
		for ( var bits = (uint)runs; bits != 0; bits &= bits - 1 )
		{
			var bit = System.Numerics.BitOperations.TrailingZeroCount( bits );
			var key = Lists[bit].Sort switch
			{
				ListSort.Batched => 0UL,
				ListSort.BackToFront => (ulong)(uint.MaxValue - distance) << 32 | (uint)index,
				ListSort.Full => FullSortKey( distance, onlyTranslucent, index ),
				ListSort.NearestFirst => FullSortKey( distance, false, index ),
				// Then by material, as native breaks sort key ties (SortLessFunc), and world index
				_ => (ulong)(uint)(renderOrder ^ int.MinValue) << 32 | (batchKey >> 24 & 0xFFFF) << 16 | (uint)index & 0xFFFF,
			};

			buckets[bit].Add( key, k );
		}
	}

	/// <summary>
	/// Reuse the cached probe choice until selection inputs change.
	/// </summary>
	static int ProbeFor( MeshObject obj, List<LightProbeVolume> volumes, long volumesKey, Vector3 center, Vector3 extents )
	{
		// From its lighting origin, where it has one (CBaseSceneObjectDesc::GeneratePrimitives, GetLightingOrigin)
		var origin = obj.LightingOrigin ?? center;
		if ( obj.ProbeVolumesKey == volumesKey && obj.ProbeCenter.Equals( center ) && obj.ProbeExtents.Equals( extents ) && obj.ProbeOrigin.Equals( origin ) && obj.ProbeLightGroup == obj.LightGroup && obj.ProbeHandshake == obj.LightProbeHandshake )
			return obj.ProbeChoice;

		obj.ProbeChoice = LightProbeVolume.Choose( volumes, origin, center, extents, obj.LightGroup, obj.LightProbeHandshake );
		obj.ProbeOrigin = origin;
		obj.ProbeCenter = center;
		obj.ProbeExtents = extents;
		obj.ProbeLightGroup = obj.LightGroup;
		obj.ProbeHandshake = obj.LightProbeHandshake;
		obj.ProbeVolumesKey = volumesKey;
		return obj.ProbeChoice;
	}

	/// <summary>
	/// The light probe volume attributes an object drew with in the last <see cref="Prepare"/>, for the tests.
	/// </summary>
	internal ShaderParameters ProbeFor( RenderObject obj ) => probes[obj.Index];

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
	/// (<c>CBaseSceneObjectDesc::BindMaterial</c>). Collisions only cost batching efficiency. Made once a bin.
	/// </summary>
	static ulong OpaqueKey( RenderMesh mesh, int lod, GpuMaterial materialOverride, ShaderParameters attributes, ShaderParameters lightmap, ShaderParameters probe )
	{
		var identity = (uint)RuntimeHelpers.GetHashCode( mesh ) ^ (uint)RuntimeHelpers.GetHashCode( materialOverride ) * 0x9E3779B1u
			^ (uint)RuntimeHelpers.GetHashCode( attributes ) * 0x85EBCA77u ^ (uint)RuntimeHelpers.GetHashCode( lightmap ) * 0xC2B2AE3Du
			^ (uint)RuntimeHelpers.GetHashCode( probe ) * 0x27D4EB2Fu;
		var similarity = mesh.SimilarityForLod( lod );
		similarity ^= similarity >> 24 ^ similarity >> 48;

		// Group identical materials within each similarity group.
		var draws = mesh.DrawsForLod( lod );
		var material = materialOverride ?? (draws.Length > 0 ? draws[0].Material : null);
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
	/// Sort a list that isn't batched by its keys, skipping a bucket already in order.
	/// </summary>
	void SortBucket( Bucket bucket )
	{
		var keys = bucket.Keys.AsSpan( 0, bucket.Count );
		if ( IsSorted( keys ) ) return;

		keys.Sort( bucket.Items.AsSpan( 0, bucket.Count ) );
		BucketsSorted++;
	}

	/// <summary>
	/// Write a list's instance IDs in its order and make runs of neighbours in one bin. The bucket holds places in the visible
	/// list, in draw order.
	/// </summary>
	void Emit( Bucket bucket, DrawFilter draws, List<Run> runs, RenderWorld world, ReadOnlySpan<int> visible, TransformBuffer transforms, OcclusionCuller occlusion )
	{
		var count = bucket.Count;
		if ( count == 0 ) return;

		var items = bucket.Items.AsSpan( 0, count );
		var objects = world.Objects;
		var matrices = world.LocalToWorld;
		var centers = world.BoundsCenter;
		var extents = world.BoundsExtents;
		var used = mainPrepares;

		// Rigid meshes keep their entries between frames, but for debug tints, which are the frame's
		var keep = DebugViews == DebugViews.None;
		var versions = world.EntryVersions;

		// Rigid IDs append directly; skinned IDs are grouped per mesh when the run ends.
		Bin bin = null;
		var start = 0;
		var length = 0;
		var textureScale = -1.0f;
		var textureFallback = 0.0f;
		skinMembers.Clear();
		tintedSlots.Clear();

		foreach ( var k in items )
		{
			var index = visible[k];
			var objBin = classified[k].Bin.ForFilter( draws, this );
			if ( objBin.Custom is { } custom )
			{
				Flush();
				var instance = transforms.AddInstance( transforms.SlotFor( custom, matrices[index], custom.Tint ) );
				runs.Add( new Run( objBin, instance, 1 ) );
				continue;
			}

			if ( objBin != bin || !bin.Joins )
			{
				Flush();
				bin = objBin;
				bin.Used = used;
			}

			// Its bin says it's a mesh; a kept entry that's current reads nothing of the object itself
			var obj = Unsafe.As<MeshObject>( objects[index] );
			if ( bin.Skinned )
			{
				WriteBones( obj, bin.Lod, matrices[index], keep ? obj.Tint : TintFor( obj, bin.Lod ), transforms );
				skinMembers.Add( obj );
			}
			else if ( bin.Tinted )
			{
				// Its entries now; the ids are written a block per draw when the run ends
				tintedSlots.Add( transforms.TintedSlotsFor( obj, matrices[index], keep ? obj.Tint : TintFor( obj, bin.Lod ), bin.Mesh ) );
			}
			else
			{
				var slot = keep ? transforms.KeptSlotFor( index, obj, matrices[index], versions[index] ) : transforms.SlotFor( obj, matrices[index], TintFor( obj, bin.Lod ) );
				var instance = transforms.AddInstance( slot );
				occlusion?.AddInstance( slot, centers[index], classified[k].Occludes ? extents[index] : new Vector3( -1 ) );
				if ( length == 0 ) start = instance;
			}

			// The run asks for what its largest object needs, as a native batch asks for its largest primitive's
			textureScale = MathF.Max( textureScale, classified[k].TextureScale );
			textureFallback = MathF.Max( textureFallback, classified[k].TextureFallback );
			length++;
		}

		Flush();

		void Flush()
		{
			if ( length == 0 ) return;

			var mesh = bin.Mesh;
			var lod = bin.Lod;
			if ( bin.Skinned )
			{
				start = WriteSkinnedInstances( mesh, lod, transforms );
				runs.Add( new Run( bin, start, length, -1, textureScale, textureFallback ) );
				skinMembers.Clear();
			}
			else if ( bin.Tinted )
			{
				// Group tinted instance IDs per draw.
				start = transforms.InstanceCount;
				var offset = mesh.TintOffsetForLod( lod );
				var lodDraws = mesh.DrawsForLod( lod ).Length;
				for ( int d = 0; d < lodDraws; d++ )
				{
					foreach ( var slot in tintedSlots )
						transforms.AddInstance( slot + offset + d );
				}

				runs.Add( new Run( bin, start, length, -1, textureScale, textureFallback ) );
				tintedSlots.Clear();
			}
			else
			{
				var cull = occlusion?.CommitRun( start, length, mesh.DrawsForLod( lod ) ) ?? -1;
				runs.Add( new Run( bin, start, length, cull, textureScale, textureFallback ) );
			}

			length = 0;
			textureScale = -1.0f;
			textureFallback = 0.0f;
		}
	}

	/// <summary>
	/// An object's tint, or the debug views' (<see cref="DebugViews"/>): its cull reason's colour if the main view culled it, else
	/// its LOD's colour if it has LODs. Alpha stays the object's, so it fades and blends as it would. A frame writes an object's
	/// entry once, for the first view, which is the main view.
	/// </summary>
	Color TintFor( MeshObject obj, int lod )
	{
		var views = DebugViews;
		if ( views == DebugViews.None ) return obj.Tint;

		var reasons = tintReasons;
		if ( reasons is not null && (uint)obj.Index < (uint)reasons.Length && reasons[obj.Index] != 0 )
			return DebugViewReport.CullColor( (CullReason)reasons[obj.Index] ).WithAlpha( obj.Tint.a );

		if ( (views & DebugViews.LodColors) != 0 && obj.Mesh.LodCount > 1 )
			return DebugViewReport.LodColor( lod ).WithAlpha( obj.Tint.a );

		return obj.Tint;
	}

	/// <summary>
	/// The LOD a visible object drew at in the last view prepared.
	/// </summary>
	internal int LodOf( int index ) => lods[index];

	/// <summary>
	/// Make the materials draws fall back to, then skin this frame's vertex cache meshes (<see cref="DispatchSkinning"/>).
	/// </summary>
	internal override void Setup( GpuContext context, RenderWorld world, ViewPass view )
	{
		// On the main thread: draws may record on worker threads, where materials can't be made
		if ( defaultMaterial is null )
		{
			defaultMaterial = s_defaultMaterial ??= RenderHost.Current.Device.CreateMaterial( "scene_renderer_default", DefaultShader );
			s_defaultMaterialUsers++;
		}
		depthOnlyMaterial ??= RenderHost.Current.Device.LoadMaterial( DepthOnlyMaterial );

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

	/// <summary>
	/// <see cref="ChooseLod"/> for an orthographic view, by how many of its texels a world unit covers: the same anywhere in
	/// it, so wherever the camera is.
	/// </summary>
	static int ChooseLodByTexels( MeshObject obj, float texelsPerUnit )
	{
		var mesh = obj.Mesh;
		if ( mesh.LodCount == 1 ) return 0;
		if ( obj.LodOverride >= 0 ) return Math.Min( obj.LodOverride, mesh.LodCount - 1 );

		var scale = obj.Transform.Scale;
		var largest = MathF.Max( MathF.Abs( scale.x ), MathF.Max( MathF.Abs( scale.y ), MathF.Abs( scale.z ) ) );
		return mesh.LodForScreenSize( texelsPerUnit, largest );
	}

	internal override void Dispose()
	{
		if ( defaultMaterial is not null && --s_defaultMaterialUsers == 0 )
		{
			s_defaultMaterial.Dispose();
			s_defaultMaterial = null;
		}

		defaultMaterial = null;
		volumeRing.Dispose();
		skinningContext?.Dispose();
		skinningContext = null;
	}

	/// <summary>
	/// Worker-safe when no custom objects will draw, and no draw callbacks will run.
	/// </summary>
	internal override bool CanRecordOffMainThread( ViewPass view, MeshLayer layer )
	{
		var lists = view.State<RunLists>( this );
		if ( !layer.Depth && !view.IsShadow && (lists.Callbacks & layer.Runs) != 0 ) return false;
		return !layer.DrawsCustomObjectsIn( view ) || (lists.Custom & layer.Runs) == 0;
	}

	/// <summary>
	/// Only custom objects' draws and draws with callbacks need the main thread; the meshes around them can go to workers.
	/// </summary>
	internal override void MainThreadRuns( ViewPass view, MeshLayer layer, Span<bool> mainThread )
	{
		var lists = view.State<RunLists>( this );
		var customs = layer.DrawsCustomObjectsIn( view ) && (lists.Custom & layer.Runs) != 0;
		var callbacks = !layer.Depth && !view.IsShadow && (lists.Callbacks & layer.Runs) != 0;
		if ( !customs && !callbacks ) return;

		var index = 0;
		for ( int bit = 0; bit < lists.ItemsByBit.Length; bit++ )
		{
			if ( ((int)layer.Runs & (1 << bit)) == 0 ) continue;

			var runs = CollectionsMarshal.AsSpan( lists.ByBit[bit] );
			foreach ( var item in CollectionsMarshal.AsSpan( lists.ItemsByBit[bit] ) )
			{
				ref readonly var run = ref runs[item.Run];
				mainThread[index++] = (customs && run.Custom is not null) || (callbacks && run.Callbacks is not null);
			}
		}
	}

	internal override bool ReadsFrameBuffer( ViewPass view, MeshRuns runs ) => (view.State<RunLists>( this ).FrameBufferReads & runs) != 0;

	/// <summary>
	/// Count draws in the layer's selected lists, in list order: a layer records draws, not runs.
	/// </summary>
	internal override int RunCount( ViewPass view, MeshLayer layer )
	{
		var lists = view.State<RunLists>( this );
		if ( OverlaysOneRun( layer, lists ) ) return 1;
		var items = 0;
		for ( int bit = 0; bit < lists.ItemsByBit.Length; bit++ )
		{
			if ( ((int)layer.Runs & (1 << bit)) != 0 ) items += lists.ItemsByBit[bit].Count;
		}

		return items;
	}

	/// <summary>
	/// Draw a range of the layer's draws, applying each list's fade combo.
	/// </summary>
	internal override void Draw( GpuContext context, ViewPass view, MeshLayer layer, int first, int count, ref RenderStats stats )
	{
		var lists = view.State<RunLists>( this );
		if ( OverlaysOneRun( layer, lists ) )
		{
			if ( first == 0 && count > 0 ) DrawOverlays( context, view, lists, layer, ref stats );
			return;
		}

		var end = first + count;

		// Each list's part of [first, end), in order
		var start = 0;
		for ( int bit = 0; bit < lists.ByBit.Length; bit++ )
		{
			if ( (layer.Runs & Lists[bit].Runs) == 0 ) continue;
			DrawListRange( context, view, CollectionsMarshal.AsSpan( lists.ByBit[bit] ), CollectionsMarshal.AsSpan( lists.ItemsByBit[bit] ), layer, first, end, ref start, Lists[bit].Fade, ref stats, Lists[bit].Runs );
		}
	}

	void DrawListRange( GpuContext context, ViewPass view, ReadOnlySpan<Run> runs, ReadOnlySpan<DrawItem> items, MeshLayer layer, int first, int end, ref int start, bool fade, ref RenderStats stats, MeshRuns list )
	{
		var from = Math.Max( first - start, 0 );
		var to = Math.Min( end - start, items.Length );
		start += items.Length;
		if ( from >= to ) return;

		if ( fade ) context.SetCombo( OpaqueFade, 1 );
		DrawItems( context, view, runs, items[from..to], layer, ref stats, list );
		if ( fade ) context.SetCombo( OpaqueFade, 0 );
	}

	/// <summary>
	/// Draw items of <paramref name="list"/>'s runs; <paramref name="left"/>: draws the device's GPU-driven path took from the
	/// opaque list and gave back.
	/// </summary>
	void DrawItems( GpuContext context, ViewPass view, ReadOnlySpan<Run> runs, ReadOnlySpan<DrawItem> items, MeshLayer layer, ref RenderStats stats, MeshRuns list, bool left = false )
	{
		var depth = layer.Depth;
		var mode = layer.ShaderMode;
		var shadow = layer.LayerType == MeshLayerType.Shadow;
		var translucentLayer = layer.LayerType == MeshLayerType.Translucent;
		var depthBias = shadow ? view.DepthBias : 0;
		var slopeBias = shadow ? view.SlopeScaledDepthBias : 0;

		// The early prepass can only cull with last frame's chain. The late prepass only draws culled runs, the early one
		// already drew everything else
		var phase = context.Occlusion is { } occlusion && (layer.OcclusionPhase != OcclusionPhase.Early || occlusion.CullsEarly) ? layer.OcclusionPhase : OcclusionPhase.None;
		var culledOnly = layer.OcclusionPhase == OcclusionPhase.Late;

		// Skinned draws' vertex-cache combo, set only when it changes: setting it costs the material bind
		var vertexAnimation = 0;

		// The object whose stretch of draws is being recorded, if it has callbacks (CRenderBatchList::m_pLastCallbackBeforeObject)
		DrawCallbacks open = null;

		foreach ( var item in items )
		{
			ref readonly var run = ref runs[item.Run];
			if ( culledOnly && run.CullDraw < 0 ) continue;
			if ( context.Census ) context.CensusNote = CensusNote( run, list, left );

			// Around each stretch of an object's draws in colour layers: its after-work when another object's draw comes,
			// then the next one's before-work (CRenderBatchList::DrawCurrentPrimitives, FlushAfterRenderCallback)
			if ( !depth && !view.IsShadow && run.Callbacks != open )
			{
				SwitchCallbacks( context, view, layer, open, run.Callbacks, ref vertexAnimation );
				open = run.Callbacks;
			}

			// Bind the custom transform, draw, then restore frame targets.
			if ( run.Custom is { } custom )
			{
				if ( !layer.DrawsCustomObjectsIn( view ) ) continue;
				if ( layer.LayerType == MeshLayerType.DepthPrepass && !custom.DepthPrepass ) continue;

				// Same first-reader copy as mesh batches (CRenderBatchList::DrawCurrentPrimitives).
				if ( run.ReadsFrameBuffer && !depth ) context.CopyFrameBuffer();

				// Custom objects draw without the vertex-cache combo, and may leave it set either way (SceneObjectDrawer
				// sets it per draw): so it's clear on both sides of them
				if ( vertexAnimation != 0 ) context.SetCombo( CsVertexAnimation, vertexAnimation = 0 );

				context.BindInstances( 1, run.FirstInstance );
				using ( Zones.CustomObject.Start() ) custom.Render( context, view, layer );
				context.SetCombo( CsVertexAnimation, 0 );
				context.RestoreBound();
				stats.CustomDraws++;
				continue;
			}

			// Copy on the first reader per layer (MakeFrameBufferCopy).
			if ( run.ReadsFrameBuffer && !depth ) context.CopyFrameBuffer();

			var mesh = run.Mesh;
			var drawIndex = item.Draw;
			ref readonly var draw = ref mesh.DrawsForLod( run.Lod )[drawIndex];

			// Forward draws use lightmaps or probes (CBaseSceneObjectDesc::DrawArray, CalculateBatchFlags).
			// Shared attributes preserve material binds between draws.

			var material = MaterialFor( run, draw, depth, layer.ForceDepthFastPath, out var stripPixelShader );
			var probe = depth || run.Probe is null || !RenderMesh.NeedsLightProbe( material ) ? null : run.Probe;
			context.SetObjectAttributes( run.Attributes, draw.Lightmapped ? (depth ? null : run.Lightmap) : probe );

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
			}

			if ( (vertexCache ? 1 : 0) != vertexAnimation )
			{
				vertexAnimation = vertexCache ? 1 : 0;
				context.SetCombo( CsVertexAnimation, vertexAnimation );
			}

			// What its textures need, which the bind tells texture streaming (CMeshDrawPrimitive_t::m_nRequiredTextureSize)
			var textureSize = TextureSizes.ForDraw( draw.UvDensity, run.TextureScale, run.TextureFallback );

			if ( vertexCache )
			{
				if ( !context.DrawVertexCache( material, mode, mesh, draw, first, run.Count, depthBias, slopeBias, stripPixelShader, textureSize ) ) continue;
			}
			else
			{
				var drawn = run.CullDraw >= 0 && phase != OcclusionPhase.None
					? context.DrawModelCulled( material, mode, mesh, draw, phase, first, run.CullDraw + drawIndex, depthBias, slopeBias, stripPixelShader, run.Skinned, textureSize )
					: context.DrawModel( material, mode, mesh, draw, first, run.Count, depthBias, slopeBias, stripPixelShader, run.Skinned, textureSize );
				if ( !drawn ) continue;
			}

			stats.DrawnTriangles += (long)(draw.IndexCount / 3) * run.Count;
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

		if ( open is not null ) SwitchCallbacks( context, view, layer, open, null, ref vertexAnimation );

		if ( vertexAnimation != 0 ) context.SetCombo( CsVertexAnimation, 0 );
		context.SetObjectAttributes( null );
	}

	/// <summary>
	/// Run <paramref name="closing"/>'s after-work and <paramref name="opening"/>'s before-work, then restore what they may
	/// have changed, as a custom object's draw does.
	/// </summary>
	static void SwitchCallbacks( GpuContext context, ViewPass view, MeshLayer layer, DrawCallbacks closing, DrawCallbacks opening, ref int vertexAnimation )
	{
		var after = closing is { After: true };
		var before = opening is { Before: true };
		if ( !after && !before ) return;

		if ( vertexAnimation != 0 ) context.SetCombo( CsVertexAnimation, vertexAnimation = 0 );
		context.SetObjectAttributes( null );

		if ( after ) closing.Render( context, view, layer, true );
		if ( before ) opening.Render( context, view, layer, false );

		context.SetCombo( CsVertexAnimation, 0 );
		context.RestoreBound();
	}

	/// <summary>
	/// Select depth overrides or strip opaque non-alpha-tested pixel shaders (<c>CSceneSystem::ShouldOverrideDepthMaterial</c>).
	/// Only fast-path depth draws dither when faded.
	/// </summary>
	GpuMaterial MaterialFor( in Run run, in RenderMesh.Draw draw, bool depth, bool forceDepthOnly, out bool stripPixelShader )
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

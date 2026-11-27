using System.Runtime.InteropServices;

namespace Borealis.Features;

/// <summary>
/// Resident aggregates (<see cref="GpuDevice.DrawsAggregatesResident"/>): every aggregate of the world goes to the device
/// in one list a view kind keeps between frames, which views hand over whole instead of preparing their own, so the
/// device keeps what it makes of them (buckets, records, multi-draws) and the GPU culls every fragment. A list changes only
/// when an aggregate is added, removed or changes how it draws, or its kept transforms move; a view takes the list of its
/// kind (main, or which casters a shadow view takes) and of the tag classes it shows. A shadow view's list holds the casters
/// its static filter takes; for the sun's far cascade cache, those that aren't static too while they keep still, each with
/// when it last moved, and the GPU keeps those the view's settled stamps take
/// (<see cref="GpuContext.ResidentFilter"/>). Aggregates a list doesn't hold go the view's own way. Resident static meshes
/// (<see cref="GpuDevice.DrawsMeshesResident"/>) join the lists in groups of one model drawn alike, a batch per LOD and draw,
/// which the GPU culls and picks LODs for (<see cref="GpuContext.ResidentMeshes"/>); the mesh feature doesn't prepare them.
/// A map's static overlays (<see cref="GpuDevice.DrawsOverlaysResident"/>) join main views' lists as a list per render order,
/// in the order the mesh feature would draw them, which the GPU culls keeping that order (<see cref="OverlayOrder"/>).
/// </summary>
internal sealed partial class AggregateRenderFeature
{
	/// <summary>One kind of view's resident aggregates: the same object, and the same batches, while its version is.</summary>
	internal sealed class ResidentList
	{
		public readonly List<Batch> Batches = new();

		/// <summary>By world index: whether this list draws the aggregate or mesh, so the view's own batches leave it out.</summary>
		public bool[] Holds = [];

		/// <summary>Its resident meshes, by group, after its aggregates.</summary>
		public readonly List<MeshGroup> MeshGroups = new();

		/// <summary>Batches the device takes: the aggregates', then a batch per group, LOD and draw.</summary>
		public int DeviceCount;

		/// <summary>Whether a mesh it holds reads the frame copy: colour layers take it before the call.</summary>
		public bool ReadsFrameBuffer;

		/// <summary>
		/// For casters whose settling decides (<see cref="ResidentCasters.Settled"/>, <see cref="ResidentCasters.SettledNotStatic"/>):
		/// by batch, each aggregate's moved word (<see cref="GpuContext.MovedWord"/>), which the GPU keeps by the view's stamps.
		/// </summary>
		public uint[] Moved;

		public int Version;

		/// <summary>Triangles and fragments of all its batches, for the stats.</summary>
		public long Triangles;
		public int Fragments;

		/// <summary>The batches as the device takes them, per depth kind, made once a frame (<see cref="DeviceBatches"/>).</summary>
		public readonly GpuContext.GpuDrivenBatch[][] Device = new GpuContext.GpuDrivenBatch[4][];
		public readonly int[] DeviceGeneration = [-1, -1, -1, -1];

		/// <summary>Per depth kind, moved each time <see cref="Device"/>'s batches come out different from the last time they were made.</summary>
		public readonly int[] DeviceStamp = new int[4];

		/// <summary>Per depth kind, whether <see cref="Device"/> holds its mesh groups' batches, which no frame changes.</summary>
		public readonly bool[] DeviceMeshes = new bool[4];

		/// <summary>Per depth kind, the frame a layer that draws it was planned in (<see cref="WantDeviceBatches"/>), for setup to make its batches.</summary>
		public readonly int[] DeviceWanted = [-1, -1, -1, -1];

		/// <summary>
		/// Per depth kind, what <see cref="Device"/>'s batches were last made with besides the list itself: the members' first
		/// instances (<see cref="residentInstances"/>) and the depth-only material.
		/// </summary>
		public readonly int[] DeviceInstances = new int[4];
		public readonly GpuMaterial[] DeviceDepthOnly = new GpuMaterial[4];

		/// <summary>Per depth kind, <see cref="GpuMaterial.DepthOnlyEpoch"/> when its batches' materials were last asked.</summary>
		public readonly int[] DeviceDepthEpoch = [-1, -1, -1, -1];

		/// <summary>Unforced depth batches that can change their material without changing the list.</summary>
		internal DepthBatchGroups DepthBatches;

		/// <summary>
		/// Per depth kind, arrays that start with <see cref="Device"/>'s batches, for calls that hand the device more batches
		/// after them (<see cref="TakeCombined"/>): the list is copied into one when it changes, not into every call's.
		/// </summary>
		public readonly Stack<Combined>[] Spare = [new(), new(), new(), new()];

		/// <summary>A main view's static overlays: a list per render order whose every overlay is resident, lowest first.</summary>
		public readonly List<OverlayOrder> Overlays = new();

		/// <summary>Whether it draws anything the views would otherwise collect and prepare.</summary>
		public bool HoldsAny => DeviceCount > 0 || Overlays.Count > 0;
	}

	/// <summary>
	/// A call's batches: a resident list's device batches as they were at <see cref="Stamp"/> and <see cref="Version"/>,
	/// then the call's own, which it clears when done.
	/// </summary>
	internal sealed class Combined
	{
		public GpuContext.GpuDrivenBatch[] Batches = [];
		public int Stamp = -1, Version = -1, Count = -1;
	}

	/// <summary>
	/// An array for one call that starts with <paramref name="list"/>'s <paramref name="device"/> batches of depth kind
	/// <paramref name="kind"/> and has room for <paramref name="total"/>: a spare one already holding them as they are now
	/// where there is one, so only a change to the list or its batches (<see cref="ResidentList.DeviceStamp"/>) copies them. Hand it back with
	/// <see cref="ReturnCombined"/>.
	/// </summary>
	internal Combined TakeCombined( ResidentList list, int kind, ReadOnlySpan<GpuContext.GpuDrivenBatch> device, int total )
	{
		Combined combined;
		int stamp;
		lock ( list )
		{
			if ( !list.Spare[kind].TryPop( out combined ) ) combined = new Combined();
			stamp = list.DeviceStamp[kind];
		}

		if ( combined.Batches.Length < total )
		{
			if ( combined.Batches.Length > 0 ) BatchPool.Return( combined.Batches, true );
			combined.Batches = BatchPool.Rent( total );
			combined.Stamp = -1;
		}

		if ( combined.Stamp != stamp || combined.Version != list.Version || combined.Count != device.Length )
		{
			device.CopyTo( combined.Batches );
			System.Threading.Interlocked.Increment( ref combinedCopies );
			combined.Stamp = stamp;
			combined.Version = list.Version;
			combined.Count = device.Length;
		}

		return combined;
	}

	// Dropped lists' batch arrays, reused by the lists made after them
	static readonly System.Buffers.ArrayPool<GpuContext.GpuDrivenBatch> BatchPool = System.Buffers.ArrayPool<GpuContext.GpuDrivenBatch>.Shared;

	static void ReleaseList( ResidentList list )
	{
		lock ( list )
		{
			for ( int kind = 0; kind < 4; kind++ )
			{
				if ( list.Device[kind] is { } device ) BatchPool.Return( device, true );
				list.Device[kind] = null;
				list.DeviceGeneration[kind] = -1;
				while ( list.Spare[kind].TryPop( out var combined ) )
				{
					if ( combined.Batches.Length > 0 ) BatchPool.Return( combined.Batches, true );
					combined.Batches = [];
				}
			}
		}
	}

	/// <summary>Times <see cref="TakeCombined"/> copied a resident list's batches, for tests.</summary>
	internal int CombinedCopies => combinedCopies;
	int combinedCopies;

	/// <summary>Hand back an array from <see cref="TakeCombined"/>, its call's own batches (from the list's, to <paramref name="end"/>) cleared.</summary>
	internal static void ReturnCombined( ResidentList list, int kind, Combined combined, int end )
	{
		combined.Batches.AsSpan( combined.Count, Math.Max( end - combined.Count, 0 ) ).Clear();
		lock ( list ) list.Spare[kind].Push( combined );
	}

	/// <summary>
	/// One render order's resident static overlays, in the order the mesh feature sorts them (<see cref="MeshRenderFeature.ResidentOverlayKey"/>),
	/// in groups of neighbours that share a bin (model, override, lightmap and probe) as its runs would, and their batches, a
	/// batch per group and draw. The overlay layer draws it as one call that keeps its order
	/// (<see cref="GpuContext.GpuDrivenCull.KeepsOrder"/>). The same object, with the same batches, while its list is.
	/// </summary>
	internal sealed class OverlayOrder
	{
		public int RenderOrder;
		public int Version;
		public readonly List<MeshGroup> Groups = new();
		public GpuContext.GpuDrivenBatch[] Device = [];

		/// <summary>Triangles and overlays of all its batches, for the stats.</summary>
		public long Triangles;
		public int Fragments;
	}

	/// <summary>What a resident aggregate was when its lists were made: any change makes them again.</summary>
	struct Member
	{
		public AggregateObject Aggregate;
		public int Index;
		public int DrawVersion;
		public bool Visible, GameLayers, CastShadows, IsStatic, IsWorld, HiddenInCaptures, Overlay, Lightmapped;

		public LayerMatch LayerMatch;
		public uint[] Tags;
		public int SubWorld;
		public ShaderParameters LightmapAttributes;
		public int Class;

		/// <summary>When it last moved (<see cref="RenderWorld.MovedStamps"/>), for lists whose casters' settling decides.</summary>
		public ulong Moved;

		/// <summary>Its first kept transform slot, or -1 while it has frame entries (its first frame): then it isn't resident.</summary>
		public int FirstSlot;

		/// <summary>Its fragments' arguments' first index in the frame's arguments: the same every frame.</summary>
		public int FirstArg;

		/// <summary>Its first instance id at the last look, which quiet frames give it again.</summary>
		public int FirstInstance;
	}

	/// <summary>
	/// What a resident static mesh was when its lists were made. Whatever decides whether it's resident, its group or its
	/// class gives it a new draw version when it changes; its bounds and scale a new entry version.
	/// </summary>
	struct MeshMember
	{
		public MeshObject Mesh;
		public int Index;
		public int DrawVersion;
		public int EntryVersion;
		public int Class;
		public ulong Moved;
		public ShaderParameters Probe;

		/// <summary>A static overlay: it goes in main views' overlay lists (<see cref="OverlayOrder"/>), not their mesh groups.</summary>
		public bool Overlay;

		/// <summary>Its kept transform slot, or -1 while it has none: then it isn't resident.</summary>
		public int Slot;
	}

	/// <summary>
	/// Resident meshes of one model drawn alike, in a list. In main views each mesh keeps its own lightmap and probe
	/// (<see cref="GpuContext.ResidentMeshes.Lightmaps"/>); <see cref="Lightmap"/> and <see cref="Probe"/> are the first's.
	/// </summary>
	internal sealed class MeshGroup
	{
		public RenderMesh Mesh;
		public GpuMaterial Override;
		public bool OverrideBlends;
		public ShaderParameters Lightmap, Probe;
		public GpuContext.ResidentMeshes Owner;
		public RenderObject[] Members;

		/// <summary>
		/// The LODs (<see cref="RenderMesh.LodAnimation.Bit"/>) a list that splits them (<see cref="ResidentCasters.SettledLods"/>,
		/// <see cref="ResidentCasters.StaticLods"/>) leaves out because they animate vertices, as its meshes did when it was made:
		/// the view over the cache draws those. Zero in any other list.
		/// </summary>
		public ulong LiveLods;
	}

	/// <summary>
	/// What a resident instanced object was when its lists were made: any change to how it draws makes them again, a change to
	/// its placements or transform (its entry version) only its owner.
	/// </summary>
	struct InstancedMember
	{
		public InstancedMeshObject Object;
		public int Index;
		public int DrawVersion;
		public int EntryVersion;
		public int Class;
		public ulong Moved;
		public bool CastShadows, GameLayers, IsStatic;

		/// <summary>The first slot of its kept run, or -1 while it has none (its first frame): then it isn't resident.</summary>
		public int Slot;
	}

	/// <summary>An instanced object's owner is found by the object and the list kind it's drawn in.</summary>
	readonly record struct InstancedKey( RenderObject Object, ResidentCasters Casters, uint Mask );

	/// <summary>
	/// An instanced object's owner in one kind of list, kept while what it was made from is the same: identity and version
	/// decide, never its arrays. <see cref="Version"/> is the list version that last drew it, as <see cref="MeshOwner"/>'s.
	/// </summary>
	sealed class InstancedOwner
	{
		public GpuContext.ResidentMeshes Owner;
		public int Version;
		public InstancedMeshObject.Placement Placement;
		public RenderMesh Mesh;
		public int Slot;
		public uint Moved;
		public MeshGroup Group;
	}

	/// <summary>
	/// What a list's mesh group is found by: in main views, whether its meshes have a lightmap and probe and what those
	/// attributes hold but their lighting records (<see cref="GpuDevice.LightingFreeKey"/>), so meshes lit differently
	/// share a group. The group keeps its owner while its meshes are the same.
	/// </summary>
	readonly record struct MeshGroupKey( ResidentCasters Casters, uint Mask, RenderMesh Mesh, GpuMaterial Override, long? Lightmap, long? Probe, int Order = 0, int First = -1 );

	/// <summary>A group's owner and the list version that last drew it: one no list of the last version drew is let go.</summary>
	sealed class MeshOwner
	{
		public GpuContext.ResidentMeshes Owner;
		public int Version;

		/// <summary>The group last made with it and its meshes, taken again while they're the same.</summary>
		public MeshGroup Group;
		public MeshObject[] Members;
	}

	/// <summary>An aggregate's tag class: what decides whether a view's root shows it.</summary>
	readonly record struct ClassKey( uint[] Tags, bool IsWorld, int SubWorld, bool HiddenInCaptures, bool Overlay );

	/// <summary>Tag classes a world's resident aggregates may fall in; more and none are resident.</summary>
	const int MaxResidentClasses = 32;

	/// <summary>Resident aggregates are the device's choice; this turns them off for the tests' comparisons.</summary>
	internal static bool ResidentEnabled { get; set; } = true;

	Member[] members = [];
	int memberCount;
	GpuBuffer.IndirectDrawIndexedArguments[] residentArgs = [];
	int residentArgCount;
	int residentArgsVersion;

	/// <summary>Moved each time a member's first instance does (<see cref="RefreshResident"/>): the lists' device batches name them.</summary>
	int residentInstances;

	int argPrefixCount;
	int argPrefixVersion = -1;
	RenderWorld residentWorld;
	int residentStructure = -1;
	int residentVersion;
	int residentPrograms;

	/// <summary>The last list version any list was made in: owners none of its lists drew are let go.</summary>
	int listedVersion;
	readonly List<ClassKey> residentClasses = new();
	readonly Dictionary<(ResidentCasters Casters, uint Mask), ResidentList> residentLists = new();

	MeshMember[] meshMembers = [];
	int meshMemberCount;

	InstancedMember[] instancedMembers = [];
	int instancedMemberCount;
	(InstancedMeshObject Object, int DrawVersion)[] instancedKnown = [];
	readonly Dictionary<InstancedKey, InstancedOwner> instancedOwners = new();

	bool residentMeshes;
	long residentVolumesKey;
	readonly Dictionary<MeshGroupKey, MeshOwner> meshOwners = new();

	/// <summary>
	/// By world index, the meshes found resident at their draw version, entry version and probe volumes then, with their main
	/// views' probe: a rescan after a change works out again only what changed (and meshes that weren't resident, which may
	/// be once their shaders' variants exist).
	/// </summary>
	(MeshObject Mesh, int DrawVersion, int EntryVersion, long Volumes, ShaderParameters Probe)[] candidates = [];

	/// <summary>Resident candidates worked out from scratch since the feature was made, for the tests.</summary>
	internal int ResidentCandidatesChecked { get; private set; }

	/// <summary>Resident mesh owners let go since the feature was made, for the tests.</summary>
	internal int ResidentOwnersReleased { get; private set; }

	/// <summary>
	/// Meshes that moved (their entry version moved, as a transform, bounds or tint change moves it) or changed how they draw
	/// (their draw version moved), by the frame they last did and their entry and draw versions then: they go the views' own
	/// way until they've kept still <see cref="SettleFrames"/> frames, as their records hold their bounds and draws. A mesh
	/// whose draws change every frame stays the views' own, rather than every list being made again each frame.
	/// </summary>
	readonly Dictionary<MeshObject, (int Frame, int Version, int Draw)> restless = new( ReferenceEqualityComparer.Instance );

	/// <summary>Frames a mesh that moved stays still before it's resident again.</summary>
	internal static int SettleFrames { get; set; } = 60;

	/// <summary>
	/// Aggregates that aren't static whose moved stamp moved, by the frame it last did: lists whose casters' settling decides
	/// hold them again once they've stayed still <see cref="SettleFrames"/> frames, as those lists hold each one's stamp.
	/// </summary>
	readonly Dictionary<RenderObject, int> restlessAggregates = new( ReferenceEqualityComparer.Instance );

	/// <summary>Lists whose casters' settling decides made since the feature was made, for the tests.</summary>
	internal int SettledListsMade { get; private set; }

	/// <summary>Meshes a resident list held whose draws the device left: they stay with the views' own batches.</summary>
	readonly HashSet<RenderObject> meshesLeft = new( ReferenceEqualityComparer.Instance );
	volatile bool meshesLeftChanged;

	/// <summary>
	/// A resident mesh group's draw the device left (from any recording thread): its meshes go the views' own way from the
	/// next frame, and miss this one.
	/// </summary>
	void ResidentMeshesLeft( ResidentList list, GpuContext.ResidentMeshes owner ) => MeshesLeft( list.MeshGroups, owner );

	void MeshesLeft( List<MeshGroup> groups, GpuContext.ResidentMeshes owner )
	{
		foreach ( var group in groups )
		{
			if ( !ReferenceEquals( group.Owner, owner ) ) continue;
			lock ( meshesLeft )
			{
				foreach ( var mesh in group.Members ) meshesLeft.Add( mesh );
			}

			meshesLeftChanged = true;
		}
	}

	/// <summary>Which aggregates a view's resident list holds: a main view's, or a shadow view's casters by its <see cref="StaticFilter"/>.</summary>
	enum ResidentCasters
	{
		Main,
		All,
		Static,
		NotStatic,

		/// <summary>The far cascade's cache: static casters and those that aren't, while they keep still.</summary>
		Settled,

		/// <summary>A baked sun's far cascade cache: casters that aren't static, while they keep still.</summary>
		SettledNotStatic,

		/// <summary>
		/// <see cref="Settled"/> for the far cascade's cache, which picks LODs by its own texels (<see cref="ViewPass.SplitsLods"/>):
		/// it also holds the static casters that animate vertices at some LODs, at the LODs that don't.
		/// </summary>
		SettledLods,

		/// <summary><see cref="Static"/> for the far cascade's cache, as <see cref="SettledLods"/>.</summary>
		StaticLods,
	}

	/// <summary>Whether a list's casters are taken by when each last moved, on the GPU.</summary>
	static bool Settles( ResidentCasters casters ) => casters is ResidentCasters.Settled or ResidentCasters.SettledNotStatic or ResidentCasters.SettledLods;

	/// <summary>Whether a list holds the LODs of a static caster that don't animate vertices, of one that animates others.</summary>
	static bool SplitsLods( ResidentCasters casters ) => casters is ResidentCasters.SettledLods or ResidentCasters.StaticLods;

	/// <summary>
	/// Whether a shadow list of these casters leaves out an object that animates vertices: its cache can't hold it, as the
	/// collect doesn't, and the view over the cache draws it. A list that splits LODs keeps one that doesn't animate at all of them.
	/// </summary>
	static bool LeavesOutAnimating( ResidentCasters casters, RenderObject obj ) => obj.AnimatesVertices && (SplitsLods( casters ) ? obj.AnimatesAllLods : Settles( casters ));

	/// <summary>Lists made since the feature was made, for the tests.</summary>
	internal int ResidentListsMade { get; private set; }

	/// <summary>A view's resident list after <see cref="Prepare"/>, or null - for the tests.</summary>
	internal ResidentList ResidentFor( ViewPass pass ) => pass.State<Batches>( this ).Resident;

	/// <summary>
	/// A view's GPU-driven state and resident list, worked out once a frame by whichever feature prepares it first (the mesh
	/// feature asks for the meshes the list holds before this one prepares); the frame's first refreshes what's resident.
	/// </summary>
	void PrepareResident( RenderWorld world, ViewPass pass, TransformBuffer transforms )
	{
		var state = pass.State<Batches>( this );
		if ( state.ResidentGeneration == transforms.Generation ) return;
		state.ResidentGeneration = transforms.Generation;

		var device = RenderHost.Current?.Device;
		state.OneRun = Occlusion is null && device is { DrawsAggregatesGpuDriven: true };
		state.Resident = null;
		// A world with culling boxes is culled on the CPU, object by object
		var resident = state.OneRun && ResidentEnabled && device.DrawsAggregatesResident && world.CullingBoxes.Count == 0;

		// The frame's views all write into one args buffer: the resident aggregates' first, at the same place every frame
		if ( argGeneration != transforms.Generation )
		{
			argGeneration = transforms.Generation;
			argCount = 0;
			argPrefixCount = 0;
			lock ( residentViews ) residentViews.Clear();
			if ( resident )
			{
				using var zone = Zones.ResidentRefresh.Start();
				RefreshResident( world, transforms );
			}
			else
			{
				// A frame without a resident prefix can overwrite those positions with view-local args.
				argPrefixVersion = -1;
			}
		}

		if ( resident ) state.Resident = ResidentListFor( pass, device.CullsAggregatesGpuDriven ? WorkerSize( device ) : 0 );
		if ( state.Resident is not null )
		{
			lock ( residentViews ) residentViews.Add( pass );
		}
	}

	/// <summary>This frame's views with a resident list, which setup hands the device's resident cull.</summary>
	readonly List<ViewPass> residentViews = new();

	/// <summary>
	/// Whether a prepared view draws anything from a resident list, which its visible lists don't show.
	/// </summary>
	internal bool DrawsResident( ViewPass pass ) => pass.State<Batches>( this ).Resident is { } list && (list.Batches.Count > 0 || list.MeshGroups.Count > 0 || list.Overlays.Count > 0);

	readonly List<GpuContext.GpuDrivenCull> residentCulls = new();

	/// <summary>
	/// In setup, hand the device both worlds' prepared views, in one call: the device groups their culls by the visibility
	/// stamps they bind, so the skybox's join the main world's dispatches where they can.
	/// </summary>
	internal void CullResident( GpuContext context, AggregateRenderFeature skybox )
	{
		try
		{
			AppendResidentCulls( residentCulls );
			skybox?.AppendResidentCulls( residentCulls );
			if ( residentCulls.Count > 0 ) context.CullResident( CollectionsMarshal.AsSpan( residentCulls ) );
		}
		finally
		{
			residentCulls.Clear();
		}
	}

	void AppendResidentCulls( List<GpuContext.GpuDrivenCull> into )
	{
		lock ( residentViews )
		{
			foreach ( var pass in residentViews )
			{
				if ( pass.State<Batches>( this ).Resident is not { } list ) continue;
				into.Add( CullFor( pass, true, list, list.Version, list.DeviceCount ) );
			}
		}
	}

	/// <summary>
	/// For the main view's collect, the frame's transforms begun: by world index, the aggregates and meshes its resident list
	/// draws, which the collect leaves out; null when it draws none.
	/// </summary>
	internal bool[] CollectHolds( RenderWorld world, ViewPass pass, TransformBuffer transforms )
	{
		PrepareResident( world, pass, transforms );
		return pass.State<Batches>( this ).Resident is { HoldsAny: true } list ? list.Holds : null;
	}

	/// <summary>
	/// For the mesh feature, before it prepares a view: by world index, the meshes the view's resident list draws, which it
	/// then leaves out; null when the list holds none.
	/// </summary>
	internal bool[] ResidentMeshHolds( RenderWorld world, ViewPass pass, TransformBuffer transforms )
	{
		PrepareResident( world, pass, transforms );
		return pass.State<Batches>( this ).Resident is { } list && (list.MeshGroups.Count > 0 || list.Overlays.Count > 0) ? list.Holds : null;
	}

	/// <summary>A prepared main view's resident static overlays, a list per render order, lowest first; null or empty for none.</summary>
	internal List<OverlayOrder> ResidentOverlays( ViewPass pass ) => pass.IsShadow ? null : pass.State<Batches>( this ).Resident?.Overlays;

	/// <summary>
	/// A view drew static overlays of its own in a render order its list holds, which only a change the lists haven't seen
	/// yet does (an overlay that wasn't one): the next frame finds the resident objects again.
	/// </summary>
	internal void OverlaysStale() => overlaysStale = true;

	volatile bool overlaysStale;

	/// <summary>
	/// Once a frame, before any view prepares: look for added and removed aggregates and changes to how each draws, put
	/// every resident aggregate's arguments first in the frame's (at the same place every frame) and its transforms in the
	/// frame's buffer. A change makes the lists again.
	/// </summary>
	void RefreshResident( RenderWorld world, TransformBuffer transforms )
	{
		var meshesWanted = MeshesResident();
		var overlaysWanted = meshesWanted && OverlaysResident();
		var settled = meshesWanted && Settle( world, transforms.Generation );
		var volumesKey = meshesWanted && world.DrawnLightProbeVolumes.Count > 0 ? LightProbeVolume.Key( world.DrawnLightProbeVolumes, 0 ) : 0;

		// A material's program compiled in the background since: meshes it kept from being resident may be now, and meshes whose
		// draws the device left while it compiled come back (a draw it still can't take leaves again, missing one frame)
		var programs = meshesWanted ? RenderHost.Current?.Device?.ProgramsVersion ?? 0 : 0;
		if ( programs != residentPrograms )
		{
			lock ( meshesLeft ) meshesLeft.Clear();
		}

		if ( !ReferenceEquals( world, residentWorld ) || world.MeshStructure != residentStructure || meshesWanted != residentMeshes || overlaysWanted != residentOverlays || volumesKey != residentVolumesKey
			|| meshesLeftChanged || overlaysStale || settled || programs != residentPrograms )
		{
			residentPrograms = programs;
			Rescan( world );
		}

		// The resident prefix keeps its place and is rewritten only when its contents changed.
		if ( argPrefixVersion != residentArgsVersion )
		{
			if ( args.Length < residentArgCount ) Array.Resize( ref args, Math.Max( residentArgCount, args.Length * 2 ) );
			residentArgs.AsSpan( 0, residentArgCount ).CopyTo( args );
			argPrefixVersion = residentArgsVersion;
		}
		argCount = argPrefixCount = residentArgCount;

		// Nothing resident changed since a look that found every one kept: this frame is that one again
		if ( Quiet( world, transforms ) ) return;
		var firstInstance = transforms.InstanceCount;
		var allKept = true;

		var drawVersions = world.DrawVersions;
		var entryVersions = world.EntryVersions;
		var movedStamps = world.MovedStamps;
		var changed = false;
		var settling = SettleAggregates( transforms.Generation );
		for ( int m = 0; m < memberCount; m++ )
		{
			ref var member = ref members[m];
			var aggregate = member.Aggregate;
			var index = member.Index;

			if ( member.DrawVersion != drawVersions[index] || member.Visible != aggregate.Visible
				|| member.GameLayers != aggregate.GameLayers || member.CastShadows != aggregate.CastShadows || member.IsStatic != aggregate.IsStatic
				|| member.IsWorld != aggregate.IsWorld || member.HiddenInCaptures != aggregate.HiddenInCaptures || member.Overlay != aggregate.Overlay
				|| member.LayerMatch != aggregate.LayerMatch || !ReferenceEquals( member.Tags, aggregate.Tags ) || member.SubWorld != world.SubWorlds[index]
				|| member.Lightmapped != aggregate.Lightmapped || !ReferenceEquals( member.LightmapAttributes, aggregate.LightmapAttributes ) )
			{
				// Rarely: its arguments may have moved too, so look at everything again
				Rescan( world );
				RefreshResident( world, transforms );
				return;
			}

			// Moved: lists whose casters' settling decides leave it out until it keeps still
			if ( !member.IsStatic && member.Moved != movedStamps[index] )
			{
				member.Moved = movedStamps[index];
				restlessAggregates[aggregate] = transforms.Generation;
				settling = true;
			}

			// Its transforms, kept between frames, rewritten when its entry version moves
			if ( aggregate.Fragments.Length == 0 ) continue;
			var instance = WriteTransforms( aggregate, index, entryVersions[index], transforms );

			// The lists' device batches name its first instance: one that moved has them look again (DeviceBatches). Quiet
			// frames give each aggregate this one again, so they keep theirs
			if ( member.FirstInstance != instance )
			{
				member.FirstInstance = instance;
				residentInstances++;
			}

			var slot = transforms.InstanceSlot( instance );
			if ( slot >= transforms.FrameBase ) slot = -1;
			allKept &= slot >= 0;
			if ( slot != member.FirstSlot )
			{
				member.FirstSlot = slot;
				changed = true;
			}
		}

		// Instanced objects: a kept run of an entry per placement, rewritten when its entry version moves (which the placements'
		// replacement does, with their bounds); the group's owner is made again then
		var instancedEntryVersions = world.EntryVersions;
		for ( int m = 0; m < instancedMemberCount; m++ )
		{
			ref var member = ref instancedMembers[m];
			var obj = member.Object;
			var index = member.Index;
			if ( member.DrawVersion != drawVersions[index] || member.CastShadows != obj.CastShadows || member.GameLayers != obj.GameLayers || member.IsStatic != obj.IsStatic )
			{
				Rescan( world );
				RefreshResident( world, transforms );
				return;
			}

			// Moved: lists whose casters' settling decides leave it out until it keeps still
			if ( !member.IsStatic && member.Moved != movedStamps[index] )
			{
				member.Moved = movedStamps[index];
				restlessAggregates[obj] = transforms.Generation;
				settling = true;
			}

			var slot = transforms.KeptRunFor( index, obj, obj.RunLength, instancedEntryVersions[index], out var write );
			if ( write ) obj.WriteEntries( transforms, slot, true );
			allKept &= slot >= 0;
			if ( slot != member.Slot || member.EntryVersion != instancedEntryVersions[index] )
			{
				member.Slot = slot;
				member.EntryVersion = instancedEntryVersions[index];
				changed = true;
			}
		}

		// Resident meshes: their kept transforms, rewritten when their entry version moves, which moves their bounds too
		var matrices = world.LocalToWorld;
		var moved = false;
		for ( int m = 0; m < meshMemberCount; m++ )
		{
			ref var member = ref meshMembers[m];
			var index = member.Index;
			// Moved or drawn another way: the views' own way until it settles (every one that changed, then the lists again once);
			// a change to how it casts moves its moved stamp too
			if ( member.DrawVersion != drawVersions[index] || member.EntryVersion != entryVersions[index] || (!member.Mesh.IsStatic && member.Moved != movedStamps[index]) )
			{
				restless[member.Mesh] = (transforms.Generation, entryVersions[index], drawVersions[index]);
				moved = true;
				continue;
			}

			var slot = member.Mesh.Mesh.HasDrawTints ? TintedRun( member.Mesh, index, matrices[index], entryVersions[index], transforms )
				: transforms.KeptSlotFor( index, member.Mesh, matrices[index], entryVersions[index] );
			if ( slot >= transforms.FrameBase ) slot = -1;
			allKept &= slot >= 0;
			if ( slot != member.Slot )
			{
				member.Slot = slot;
				changed = true;
			}
		}

		if ( moved )
		{
			Rescan( world );
			RefreshResident( world, transforms );
			return;
		}

		MarkMembers( world );
		if ( changed ) DropResidentLists();
		else if ( settling ) DropSettledLists();
		else if ( allKept ) KeepQuiet( world, transforms, firstInstance );
	}

	/// <summary>
	/// By world index, after each frame's refresh: the aggregates and meshes the resident lists draw (<see cref="MemberKept"/>),
	/// whether each casts shadows and is static. A shadow view with a resident list draws every kept one its static filter
	/// and tag classes take, so its culling leaves them out (<see cref="LeaveResidentOut"/>).
	/// </summary>
	internal byte[] ResidentMembers => memberFlags;

	internal const byte MemberKept = 1, MemberCasts = 2, MemberStatic = 4;
	byte[] memberFlags = [];

	/// <summary>Whether a view with this filter takes a kept member's casting, as the culling's own tests would (settling aside).</summary>
	internal static bool TakesMember( byte flags, StaticFilter filter ) => (flags & MemberCasts) != 0 && filter switch
	{
		StaticFilter.NoStatic or StaticFilter.SettledNoStatic => (flags & MemberStatic) == 0,
		_ => true,
	};

	void MarkMembers( RenderWorld world )
	{
		memberVersion++;
		if ( memberFlags.Length < world.Count ) memberFlags = new byte[Math.Max( world.Count, memberFlags.Length * 2 )];
		else Array.Clear( memberFlags );

		for ( int m = 0; m < memberCount; m++ )
		{
			ref readonly var member = ref members[m];
			if ( member.FirstSlot < 0 ) continue;
			memberFlags[member.Index] = (byte)(MemberKept | (CastsShadow( member.Aggregate ) ? MemberCasts : 0) | (member.IsStatic ? MemberStatic : 0));
		}

		for ( int m = 0; m < meshMemberCount; m++ )
		{
			ref readonly var member = ref meshMembers[m];
			if ( member.Slot < 0 ) continue;
			memberFlags[member.Index] = (byte)(MemberKept | (Meshes.CastsShadow( member.Mesh ) ? MemberCasts : 0) | (member.Mesh.CachesAsStatic ? MemberStatic : 0));
		}

		for ( int m = 0; m < instancedMemberCount; m++ )
		{
			ref readonly var member = ref instancedMembers[m];
			if ( member.Slot < 0 ) continue;
			memberFlags[member.Index] = (byte)(MemberKept | (Meshes.CastsShadow( member.Object ) ? MemberCasts : 0) | (member.Object.CachesAsStatic ? MemberStatic : 0));
		}
	}

	/// <summary>
	/// Before the shadow views' culls start: each view's resident list, and for a view that has one, the members it leaves out of
	/// its culling (<see cref="ViewPass.ResidentMembers"/>). Not for what's drawn over the far cascade's cache, which counts the
	/// casters it takes. Off for the tests' comparisons.
	/// </summary>
	internal void LeaveResidentOut( RenderWorld world, ReadOnlySpan<ViewPass> passes, TransformBuffer transforms )
	{
		if ( !ShadowsLeaveResidentOut ) return;
		foreach ( var pass in passes )
		{
			if ( pass.StaticFilter == StaticFilter.Unsettled ) continue;
			PrepareResident( world, pass, transforms );
			if ( pass.State<Batches>( this ).Resident is not { } list ) continue;
			pass.ResidentMembers = memberFlags;

			// Its query leaves the members out too, so it can't tell which of its list's casters are in its frustum: a list with
			// any counts as having casters
			pass.ResidentInView = list.DeviceCount > 0;
			var tree = pass.StaticFilter is StaticFilter.NoStatic or StaticFilter.SettledNoStatic ? world.DynamicTree : world.Tree;
			pass.TreeKept = TreeKept( tree, memberFlags, memberFlags, memberVersion );
		}
	}

	internal static bool ShadowsLeaveResidentOut { get; set; } = true;

	/// <summary>
	/// For the main view's collect, after <see cref="CollectHolds"/>: the world tree's node marks leaving out what its resident
	/// list holds, or null when its marks are not ready.
	/// </summary>
	internal bool[] CollectTreeKept( RenderWorld world, ViewPass pass )
	{
		if ( pass.State<Batches>( this ).Resident is not { HoldsAny: true } list ) return null;
		return TreeKept( world.Tree, MemoryMarshal.Cast<bool, byte>( list.Holds.AsSpan() ), list, list.Version );
	}

	/// <summary>A tree and source version seen together, with node marks once that pair spans two frames.</summary>
	sealed class TreeMarks
	{
		public Culling.SpatialTree Tree;
		public int TreeVersion;
		public object Source;
		public int SourceVersion;
		public int SeenAt;
		public bool Ready;
		public bool[] Kept = [];
	}

	readonly TreeMarks[] treeMarks = [new(), new(), new(), new()];
	int nextMarks;

	/// <summary>Bumped each time the members are marked again (<see cref="MarkMembers"/>).</summary>
	int memberVersion;

	/// <summary>
	/// A tree's node marks for leaving out what <paramref name="leftOut"/> holds (<see cref="Culling.SpatialTree.Mark"/>).
	/// A changed tree or source uses the cull's existing per-object filter for its first frame. Only build the marks
	/// when they can be reused, rather than traverse the whole tree every frame while it changes.
	/// </summary>
	bool[] TreeKept( Culling.SpatialTree tree, ReadOnlySpan<byte> leftOut, object source, int sourceVersion )
	{
		foreach ( var marks in treeMarks )
		{
			if ( !ReferenceEquals( marks.Tree, tree ) || marks.TreeVersion != tree.Version || !ReferenceEquals( marks.Source, source ) || marks.SourceVersion != sourceVersion )
				continue;

			if ( !marks.Ready )
			{
				if ( marks.SeenAt == argGeneration ) return null;
				tree.Mark( leftOut, ref marks.Kept );
				marks.Ready = true;
			}

			return marks.Kept;
		}

		var made = treeMarks[nextMarks++ % treeMarks.Length];
		made.Tree = tree;
		made.TreeVersion = tree.Version;
		made.Source = source;
		made.SourceVersion = sourceVersion;
		made.SeenAt = argGeneration;
		made.Ready = false;
		return null;
	}

	/// <summary>
	/// Take every frame after a look that found nothing resident changed and every member kept as that one, while no object
	/// the lists hold changes (<see cref="RenderWorld.ChangedAt"/>); off for the tests' comparisons.
	/// </summary>
	internal static bool QuietRefresh { get; set; } = true;

	/// <summary>Frames between keeping each member's kept slots used while quiet, well inside <see cref="TransformBuffer.SlotLifetime"/>.</summary>
	const int QuietTouchFrames = 16;

	/// <summary><see cref="RenderWorld.Changes"/> at the look the quiet frames take, or -1 while there's none.</summary>
	long quietChanges = -1;
	TransformBuffer quietTransforms;

	/// <summary>The transforms' frame the last refresh ran in: a frame without one (resident off, or the system not drawn) looks again.</summary>
	int quietFrame = -1;

	/// <summary>Where that look's instance ids started, and they: every frame writes the same.</summary>
	int quietFirstInstance;
	uint[] quietIds = [];
	int quietIdCount;

	/// <summary>The members' world indices, aggregates then meshes, and the next member a quiet frame keeps used.</summary>
	int[] quietIndices = [];
	int quietTouch;

	/// <summary>Frames that repeated the last look, for the tests.</summary>
	internal int QuietFrames { get; private set; }

	void KeepQuiet( RenderWorld world, TransformBuffer transforms, int firstInstance )
	{
		if ( !QuietRefresh || restless.Count > 0 || restlessAggregates.Count > 0 ) return;
		quietChanges = world.Changes;
		quietTransforms = transforms;
		quietFrame = transforms.Frame;
		quietFirstInstance = firstInstance;

		var ids = transforms.InstancesFrom( firstInstance );
		if ( quietIds.Length < ids.Length ) quietIds = new uint[ids.Length];
		ids.CopyTo( quietIds );
		quietIdCount = ids.Length;

		var count = memberCount + meshMemberCount + instancedMemberCount;
		if ( quietIndices.Length < count ) quietIndices = new int[count];
		for ( int m = 0; m < memberCount; m++ ) quietIndices[m] = members[m].Index;
		for ( int m = 0; m < meshMemberCount; m++ ) quietIndices[memberCount + m] = meshMembers[m].Index;
		for ( int m = 0; m < instancedMemberCount; m++ ) quietIndices[memberCount + meshMemberCount + m] = instancedMembers[m].Index;
	}

	/// <summary>A member's first kept slot by its place among the aggregates, meshes and instanced objects.</summary>
	int QuietSlot( int m ) => m < memberCount ? members[m].FirstSlot : m - memberCount < meshMemberCount ? meshMembers[m - memberCount].Slot : instancedMembers[m - memberCount - meshMemberCount].Slot;

	/// <summary>
	/// A frame like the last look, when no object the lists hold has changed since: its instance ids again, at the same place,
	/// each aggregate's transforms current, and a few members' kept slots used, so none is given back while quiet.
	/// </summary>
	bool Quiet( RenderWorld world, TransformBuffer transforms )
	{
		if ( quietChanges < 0 ) return false;
		if ( !QuietRefresh || !ReferenceEquals( transforms, quietTransforms ) || transforms.Frame != quietFrame + 1 || transforms.InstanceCount != quietFirstInstance
			|| restless.Count > 0 || restlessAggregates.Count > 0 )
		{
			quietChanges = -1;
			return false;
		}

		var count = memberCount + meshMemberCount + instancedMemberCount;
		if ( world.Changes != quietChanges )
		{
			var changedAt = world.ChangedAt;
			foreach ( var index in quietIndices.AsSpan( 0, count ) )
			{
				if ( changedAt[index] > quietChanges )
				{
					quietChanges = -1;
					return false;
				}
			}

			// Only objects the lists don't hold changed
			quietChanges = world.Changes;
		}

		// Replay the resident prefix and register its runs for any pass that draws them the classic way this frame.
		transforms.AddInstances( quietIds.AsSpan( 0, quietIdCount ) );
		for ( int m = 0; m < memberCount; m++ )
		{
			ref readonly var member = ref members[m];
			if ( member.FirstSlot < 0 ) continue;
			transforms.SetAggregateFirstInstance( member.Index, member.FirstInstance );
		}

		quietFrame = transforms.Frame;

		var touches = (count + QuietTouchFrames - 1) / QuietTouchFrames;
		for ( int i = 0; i < touches; i++ )
		{
			if ( quietTouch >= count ) quietTouch = 0;
			var m = quietTouch++;
			transforms.KeepUsed( QuietSlot( m ) );
		}

		QuietFrames++;
		return true;
	}

	/// <summary>
	/// Forget aggregates that moved and have since kept still <see cref="SettleFrames"/> frames, or left the world; true when
	/// lists whose casters' settling decides must be made again.
	/// </summary>
	bool SettleAggregates( int frame )
	{
		if ( restlessAggregates.Count == 0 ) return false;

		List<RenderObject> done = null;
		var settled = false;
		foreach ( var (aggregate, moved) in restlessAggregates )
		{
			var gone = aggregate.IndexIn( residentWorld ) < 0;
			if ( !gone && frame - moved < SettleFrames ) continue;
			(done ??= new()).Add( aggregate );
			settled |= !gone;
		}

		if ( done is not null )
		{
			foreach ( var aggregate in done ) restlessAggregates.Remove( aggregate );
		}

		return settled;
	}

	readonly List<(ResidentCasters, uint)> staleListKeys = new();

	/// <summary>
	/// Drop the lists whose casters' settling decides, to be made again, keeping the rest. Their groups the new lists don't
	/// keep are let go when every list is next dropped.
	/// </summary>
	void DropSettledLists()
	{
		staleListKeys.Clear();
		foreach ( var key in residentLists.Keys )
		{
			if ( Settles( key.Casters ) ) staleListKeys.Add( key );
		}

		foreach ( var key in staleListKeys )
		{
			ReleaseList( residentLists[key] );
			residentLists.Remove( key );
		}
	}

	/// <summary>
	/// A resident mesh with draw tints: its kept run of an entry per draw of every LOD, each its tint times the draw's (as
	/// <see cref="TransformBuffer.TintedSlotsFor"/> writes them as frame entries), or -1 the frame before it's made.
	/// </summary>
	static int TintedRun( MeshObject obj, int index, in Matrix matrix, int version, TransformBuffer transforms )
	{
		var mesh = obj.Mesh;
		var count = 0;
		for ( int lod = 0; lod < mesh.LodCount; lod++ ) count += mesh.DrawsForLod( lod ).Length;
		var first = transforms.KeptRunFor( index, obj, count, version, out var write );
		if ( first < 0 || !write ) return first;

		var tint = obj.Tint;
		var slot = first;
		for ( int lod = 0; lod < mesh.LodCount; lod++ )
		{
			foreach ( ref readonly var draw in mesh.DrawsForLod( lod ) )
			{
				var drawTint = draw.Tint ?? Vector3.One;
				transforms.WriteKept( slot++, matrix, new Color( tint.r * drawTint.x, tint.g * drawTint.y, tint.b * drawTint.z, tint.a ) );
			}
		}

		return first;
	}

	/// <summary>
	/// Follow the meshes that moved or changed how they draw: one that does again restarts its wait, one gone or no longer a
	/// mesh of this world is forgotten; true when one has kept still long enough to be resident again.
	/// </summary>
	bool Settle( RenderWorld world, int frame )
	{
		if ( restless.Count == 0 ) return false;

		var entryVersions = world.EntryVersions;
		var drawVersions = world.DrawVersions;
		var settled = false;
		var done = settleDone;
		foreach ( var (mesh, (moved, version, draw)) in restless )
		{
			var index = mesh.IndexIn( world );
			if ( index < 0 )
			{
				done.Add( mesh );
				continue;
			}

			if ( entryVersions[index] != version || drawVersions[index] != draw )
			{
				done.Add( mesh );
				continue;
			}

			if ( frame - moved < SettleFrames ) continue;
			done.Add( mesh );
			settled = true;
		}

		foreach ( var mesh in done )
		{
			var index = mesh.IndexIn( world );
			if ( index >= 0 && (entryVersions[index] != restless[mesh].Version || drawVersions[index] != restless[mesh].Draw) ) restless[mesh] = (frame, entryVersions[index], drawVersions[index]);
			else restless.Remove( mesh );
		}

		done.Clear();
		return settled;
	}

	// Settle's meshes to look at again, kept: a moving mesh is looked at every frame
	readonly List<MeshObject> settleDone = new();

	/// <summary>Whether meshes may be resident now: the device draws them so, and no debug view tints them per frame.</summary>
	bool MeshesResident() => Meshes is { DebugViews: DebugViews.None, UseMeshMaterials: true } && RenderHost.Current?.Device is { DrawsMeshesResident: true };

	/// <summary>Whether static overlays may be resident now, with the meshes (<see cref="GpuDevice.DrawsOverlaysResident"/>).</summary>
	static bool OverlaysResident() => RenderHost.Current?.Device is { DrawsOverlaysResident: true };

	/// <summary>Whether <see cref="Rescan"/> made static overlays resident.</summary>
	bool residentOverlays;

	/// <summary>
	/// Static overlays that aren't resident (not candidates, moving, left by the device, or of no class), by world index and
	/// tag class (-1 for none): a main view's list leaves out every render order one of them is drawn in, which the view
	/// then draws all of itself; but for one that draws the classic way after its order's call anyway (<see cref="MeshRenderFeature.OverlayDrawnAfter"/>),
	/// which the view draws after the list.
	/// </summary>
	readonly List<(int Index, int Class, bool After)> overlayBlockers = new();

	/// <summary>Find every aggregate of the world again, its tag class and its arguments' place.</summary>
	void Rescan( RenderWorld world )
	{
		using var zone = Zones.ResidentRescan.Start();
		residentWorld = world;
		residentStructure = world.MeshStructure;
		quietChanges = -1;
		memberCount = 0;
		meshMemberCount = 0;
		instancedMemberCount = 0;
		residentArgCount = 0;
		residentArgsVersion++;
		residentClasses.Clear();
		residentMeshes = MeshesResident();
		residentOverlays = residentMeshes && OverlaysResident();
		overlayBlockers.Clear();
		meshesLeftChanged = false;
		overlaysStale = false;
		var volumes = world.DrawnLightProbeVolumes;
		residentVolumesKey = residentMeshes && volumes.Count > 0 ? LightProbeVolume.Key( volumes, 0 ) : 0;

		var objects = world.Objects;
		var drawVersions = world.DrawVersions;
		var subWorlds = world.SubWorlds;
		for ( int i = 0; i < objects.Length; i++ )
		{
			if ( objects[i] is not AggregateObject aggregate ) continue;

			var key = new ClassKey( aggregate.Tags, aggregate.IsWorld, subWorlds[i], aggregate.HiddenInCaptures, aggregate.Overlay );
			var cls = ClassOf( key );
			if ( cls < 0 )
			{
				// Too many tag classes: none resident, every view prepares its own
				memberCount = 0;
				residentArgCount = 0;
				break;
			}

			if ( memberCount == members.Length ) Array.Resize( ref members, Math.Max( 16, members.Length * 2 ) );
			var fragments = aggregate.Fragments;
			if ( residentArgs.Length < residentArgCount + fragments.Length ) Array.Resize( ref residentArgs, Math.Max( residentArgCount + fragments.Length, residentArgs.Length * 2 ) );
			for ( int f = 0; f < fragments.Length; f++ ) residentArgs[residentArgCount + f] = fragments[f].Args;

			members[memberCount++] = new Member
			{
				Aggregate = aggregate,
				Index = i,
				DrawVersion = drawVersions[i],
				Visible = aggregate.Visible,
				GameLayers = aggregate.GameLayers,
				CastShadows = aggregate.CastShadows,
				IsStatic = aggregate.IsStatic,
				IsWorld = aggregate.IsWorld,
				HiddenInCaptures = aggregate.HiddenInCaptures,
				Overlay = aggregate.Overlay,
				LayerMatch = aggregate.LayerMatch,
				Tags = aggregate.Tags,
				SubWorld = subWorlds[i],
				Lightmapped = aggregate.Lightmapped,
				LightmapAttributes = aggregate.LightmapAttributes,
				Class = cls,
				Moved = world.MovedStamps[i],
				FirstSlot = -1,
				FirstArg = residentArgCount,
			};

			residentArgCount += fragments.Length;
		}

		// Static meshes that may be resident, with their main views' probes; one that's the same object at the same versions as
		// when it was last found resident is taken as it was
		var device = RenderHost.Current?.Device;
		if ( residentMeshes && device is not null )
		{
			var entryVersions = world.EntryVersions;
			var centers = world.BoundsCenter;
			var extents = world.BoundsExtents;
			if ( candidates.Length < objects.Length ) Array.Resize( ref candidates, Math.Max( objects.Length, candidates.Length * 2 ) );
			for ( int i = 0; i < objects.Length; i++ )
			{
				if ( objects[i] is InstancedMeshObject instanced )
				{
					ScanInstanced( world, i, instanced, device );
					continue;
				}

				if ( objects[i] is not MeshObject mesh ) continue;

				// A static overlay that isn't resident keeps its render order out of the lists
				var overlay = mesh.LayerMatch == LayerMatch.StaticOverlay;
				if ( overlay && !residentOverlays ) continue;
				if ( restless.ContainsKey( mesh ) )
				{
					if ( overlay ) BlockOverlay( mesh, i );
					continue;
				}

				ref var known = ref candidates[i];
				if ( !ReferenceEquals( known.Mesh, mesh ) || known.DrawVersion != drawVersions[i] || known.EntryVersion != entryVersions[i] || known.Volumes != residentVolumesKey )
				{
					known = default;
					ResidentCandidatesChecked++;
					if ( !(overlay ? Meshes.ResidentOverlayCandidate( mesh, device ) : Meshes.ResidentCandidate( mesh, device )) )
					{
						if ( overlay ) BlockOverlay( mesh, i, Meshes.OverlayDrawnAfter( mesh, device ) );
						continue;
					}

					known = (mesh, drawVersions[i], entryVersions[i], residentVolumesKey, MeshRenderFeature.ResidentProbe( mesh, volumes, residentVolumesKey, centers[i], extents[i] ));
				}

				lock ( meshesLeft )
				{
					if ( meshesLeft.Count > 0 && meshesLeft.Contains( mesh ) )
					{
						if ( overlay ) BlockOverlay( mesh, i );
						continue;
					}
				}

				var cls = ClassOf( new ClassKey( mesh.Tags, mesh.IsWorld, subWorlds[i], mesh.HiddenInCaptures, false ) );
				if ( cls < 0 )
				{
					if ( overlay ) overlayBlockers.Add( (i, -1, false) );
					continue;
				}

				if ( meshMemberCount == meshMembers.Length ) Array.Resize( ref meshMembers, Math.Max( 64, meshMembers.Length * 2 ) );
				meshMembers[meshMemberCount++] = new MeshMember
				{
					Mesh = mesh,
					Index = i,
					DrawVersion = drawVersions[i],
					EntryVersion = entryVersions[i],
					Class = cls,
					Moved = world.MovedStamps[i],
					Probe = known.Probe,
					Overlay = overlay,
					Slot = -1,
				};
			}
		}

		DropResidentLists();
	}

	void BlockOverlay( MeshObject mesh, int index, bool after = false ) => overlayBlockers.Add( (index, ClassOf( new ClassKey( mesh.Tags, mesh.IsWorld, residentWorld.SubWorlds[index], mesh.HiddenInCaptures, false ) ), after) );

	int ClassOf( in ClassKey key )
	{
		for ( int c = 0; c < residentClasses.Count; c++ )
		{
			var known = residentClasses[c];
			if ( known.IsWorld == key.IsWorld && known.SubWorld == key.SubWorld && known.HiddenInCaptures == key.HiddenInCaptures && known.Overlay == key.Overlay
				&& known.Tags.AsSpan().SequenceEqual( key.Tags ) ) return c;
		}

		if ( residentClasses.Count == MaxResidentClasses ) return -1;
		residentClasses.Add( key );
		return residentClasses.Count - 1;
	}

	readonly List<MeshGroupKey> staleOwnerKeys = new();
	readonly List<InstancedKey> staleInstancedKeys = new();

	/// <summary>
	/// Drop every list, to be made again. Owners no list of the last version that made any drew are let go, so the device
	/// frees their records; the rest may be kept by the next version's lists. A version dropped before any list was made
	/// (a rescan, then the slots it finds) lets nothing go, or every group would be made again with the same meshes. With
	/// no resident meshes left, every owner goes.
	/// </summary>
	void DropResidentLists()
	{
		foreach ( var list in residentLists.Values ) ReleaseList( list );
		residentLists.Clear();
		staleOwnerKeys.Clear();
		var before = meshMemberCount + instancedMemberCount == 0 ? int.MaxValue : listedVersion;
		foreach ( var (key, owner) in meshOwners )
		{
			if ( owner.Version < before ) staleOwnerKeys.Add( key );
		}

		foreach ( var key in staleOwnerKeys )
		{
			ReleaseOwner( meshOwners[key].Owner );
			meshOwners.Remove( key );
		}

		staleInstancedKeys.Clear();
		foreach ( var (key, owner) in instancedOwners )
		{
			if ( owner.Version < before ) staleInstancedKeys.Add( key );
		}

		foreach ( var key in staleInstancedKeys )
		{
			ReleaseOwner( instancedOwners[key].Owner );
			instancedOwners.Remove( key );
		}

		residentVersion++;
	}

	void ReleaseOwner( GpuContext.ResidentMeshes owner )
	{
		ResidentOwnersReleased++;
		RenderHost.Current?.Device?.ReleaseResident( owner );
	}

	/// <summary>
	/// The resident list a view draws: its kind's (a shadow view's holds static casters, and only for views that take them)
	/// for the tag classes its root shows. Null when it draws none.
	/// </summary>
	ResidentList ResidentListFor( ViewPass pass, int textureSize )
	{
		if ( memberCount == 0 && meshMemberCount == 0 && instancedMemberCount == 0 ) return null;

		var shadow = pass.IsShadow;
		var settled = RenderHost.Current?.Device is { DrawsSettledResident: true };
		var casters = !shadow ? ResidentCasters.Main : pass.StaticFilter switch
		{
			StaticFilter.Any => ResidentCasters.All,
			StaticFilter.NoStatic => ResidentCasters.NotStatic,
			// A cache's settled casters depend on when each settled: the GPU decides, or the cache takes its static casters from
			// a list and the rest go the view's own way. What's drawn over it every frame goes its own way: the collect visits
			// every caster anyway, and keeps only the few that haven't settled
			StaticFilter.Settled when pass.SplitsLods => settled ? ResidentCasters.SettledLods : ResidentCasters.StaticLods,
			StaticFilter.Settled => settled ? ResidentCasters.Settled : ResidentCasters.Static,
			StaticFilter.SettledNoStatic when settled => ResidentCasters.SettledNotStatic,
			_ => (ResidentCasters)(-1),
		};
		if ( casters < 0 ) return null;

		// The classes the root shows, as the collect's tag test would have it for any aggregate of each
		var mask = 0u;
		for ( int c = 0; c < residentClasses.Count; c++ )
		{
			var key = residentClasses[c];
			if ( RootShows( pass.Root, key ) ) mask |= 1u << c;
		}

		if ( residentLists.TryGetValue( (casters, mask), out var list ) ) return list;

		using var zone = Zones.ResidentLists.Start();
		list = new ResidentList { Version = residentVersion };
		listedVersion = residentVersion;
		if ( list.Holds.Length < residentWorld.Count ) list.Holds = new bool[residentWorld.Count];
		var settles = Settles( casters );
		for ( int m = 0; m < memberCount; m++ )
		{
			ref readonly var member = ref members[m];
			var aggregate = member.Aggregate;
			if ( member.FirstSlot < 0 || (mask & 1u << member.Class) == 0 || !member.Visible ) continue;
			if ( shadow ? !(member.CastShadows && member.LayerMatch == LayerMatch.None && TakesCaster( casters, member )) : !member.GameLayers ) continue;
			if ( settles && !member.IsStatic && restlessAggregates.ContainsKey( aggregate ) ) continue;

			var fragments = aggregate.Fragments;
			long indices = 0;
			var count = 0;
			for ( int i = 0; i < fragments.Length; i++ )
			{
				if ( shadow && (fragments[i].Flags & FragmentFlags.DoNotCastShadows) != 0 ) continue;
				indices += fragments[i].Args.IndexCount;
				count++;
			}

			if ( count == 0 ) continue;

			// Fragments that cast no shadow are culled on the GPU; the batch keeps every argument for the classic way
			list.Batches.Add( new Batch( aggregate, member.Index, member.FirstArg, fragments.Length, -1, indices / 3, SortKey( aggregate ), textureSize ) );
			list.Holds[member.Index] = true;
			list.Triangles += indices / 3;
			list.Fragments += count;
		}

		list.Batches.Sort( ByKey );
		list.DeviceCount = list.Batches.Count;
		if ( settles )
		{
			// Each aggregate's moved word, as it was when it last moved: a stamp that moves makes the list again
			var stamps = residentWorld.MovedStamps;
			list.Moved = new uint[list.Batches.Count];
			for ( int i = 0; i < list.Moved.Length; i++ )
			{
				var aggregate = list.Batches[i].Aggregate;
				list.Moved[i] = GpuContext.MovedWord( stamps[aggregate.IndexIn( residentWorld )], aggregate.IsStatic );
			}

			SettledListsMade++;
		}

		if ( meshMemberCount > 0 ) AddMeshGroups( list, casters, mask, shadow );
		if ( instancedMemberCount > 0 ) AddInstancedGroups( list, casters, mask, shadow );
		if ( meshMemberCount > 0 && !shadow && residentOverlays ) AddOverlays( list, mask );
		residentLists[(casters, mask)] = list;
		ResidentListsMade++;
		return list;
	}

	/// <summary>
	/// A list's resident meshes, in groups of one model drawn alike: a shadow view's casters by model and override, a main
	/// view's by its lightmap and probe too. A group whose meshes are as they were keeps its owner, so the device keeps its records.
	/// </summary>
	void AddMeshGroups( ResidentList list, ResidentCasters casters, uint mask, bool shadow )
	{
		var device = RenderHost.Current?.Device;
		var groups = groupMembers;
		for ( int m = 0; m < meshMemberCount; m++ )
		{
			ref readonly var member = ref meshMembers[m];
			var obj = member.Mesh;
			if ( member.Slot < 0 || member.Overlay || (mask & 1u << member.Class) == 0 ) continue;
			if ( shadow ? !(obj.CastShadows && TakesCaster( casters, SplitsLods( casters ) ? obj.IsStatic : obj.CachesAsStatic )) : !obj.GameLayers ) continue;

			// One that animates vertices never settles: the far cascade's cache leaves it to what's drawn over it, as the collect does
			if ( LeavesOutAnimating( casters, obj ) ) continue;
			list.ReadsFrameBuffer |= !shadow && obj.WantsFrameBufferCopy;

			var lightmap = shadow || obj.LightmapAttributes is null ? (long?)null : device?.LightingFreeKey( obj.LightmapAttributes ) ?? 0;
			var probe = shadow || member.Probe is null ? (long?)null : device?.LightingFreeKey( member.Probe ) ?? 0;
			var key = new MeshGroupKey( casters, mask, obj.Mesh, obj.MaterialOverride, lightmap, probe );
			if ( !groups.TryGetValue( key, out var members ) ) groups[key] = members = spareGroupMembers.TryPop( out var spare ) ? spare : new List<int>();
			members.Add( m );
		}

		var centers = residentWorld.BoundsCenter;
		var extents = residentWorld.BoundsExtents;
		foreach ( var (key, members) in groups )
		{
			// Worked out in scratch: copied into arrays of their own only for a new owner
			var count = members.Count;
			if ( groupScratch.Slots.Length < count ) groupScratch = new GroupScratch( Math.Max( count, groupScratch.Slots.Length * 2 ) );
			var groupCenters = groupScratch.Centers.AsSpan( 0, count );
			var groupExtents = groupScratch.Extents.AsSpan( 0, count );
			var slots = groupScratch.Slots.AsSpan( 0, count );
			var scales = groupScratch.Scales.AsSpan( 0, count );
			var lightmaps = groupScratch.Lightmaps.AsSpan( 0, count );
			var probes = groupScratch.Probes.AsSpan( 0, count );
			var moved = groupScratch.Moved.AsSpan( 0, count );
			var objects = groupScratch.Objects.AsSpan( 0, count );
			var (hasLightmaps, hasProbes, hasMoved) = (key.Lightmap.HasValue, key.Probe.HasValue, Settles( casters ));
			for ( int i = 0; i < count; i++ )
			{
				ref readonly var member = ref meshMembers[members[i]];
				var obj = member.Mesh;
				groupCenters[i] = centers[member.Index];
				groupExtents[i] = extents[member.Index];
				slots[i] = member.Slot;
				var scale = obj.Transform.Scale;
				scales[i] = obj.LodOverride >= 0 ? -1 - obj.LodOverride : MathF.Max( MathF.Abs( scale.x ), MathF.Max( MathF.Abs( scale.y ), MathF.Abs( scale.z ) ) );
				lightmaps[i] = hasLightmaps ? obj.LightmapAttributes : null;
				probes[i] = hasProbes ? member.Probe : null;
				moved[i] = hasMoved ? GpuContext.MovedWord( member.Moved, obj.IsStatic ) : 0;
				objects[i] = obj;
				list.Holds[member.Index] = true;
			}

			// Each mesh's own lighting only where they differ
			hasLightmaps &= !AllSame( lightmaps );
			hasProbes &= !AllSame( probes );
			if ( !hasMoved ) moved = default;

			// The same meshes as last time keep their owner, and so the device's records of them; a changed group's old owner
			// is let go
			var firstMember = meshMembers[members[0]];
			meshOwners.TryGetValue( key, out var kept );
			var owner = kept?.Owner;
			if ( owner is null || !owner.Slots.AsSpan().SequenceEqual( slots ) || !owner.Centers.AsSpan().SequenceEqual( groupCenters )
				|| !owner.Extents.AsSpan().SequenceEqual( groupExtents ) || !owner.LodScales.AsSpan().SequenceEqual( scales )
				|| !SameLighting( owner.Lightmaps, lightmaps, hasLightmaps ) || !SameLighting( owner.Probes, probes, hasProbes ) || !owner.Moved.AsSpan().SequenceEqual( moved ) )
			{
				if ( owner is not null ) ReleaseOwner( owner );
				owner = new GpuContext.ResidentMeshes( key.Mesh, groupCenters.ToArray(), groupExtents.ToArray(), slots.ToArray(), scales.ToArray(), hasLightmaps ? lightmaps.ToArray() : null,
					hasProbes ? probes.ToArray() : null, hasMoved ? moved.ToArray() : null );
				meshOwners[key] = kept = new MeshOwner { Owner = owner };
			}

			kept.Version = residentVersion;
			if ( kept.Members is null || !SameObjects( kept.Members, objects ) ) kept.Members = objects.ToArray();

			var first = firstMember.Mesh;
			var overrideBlends = key.Override is not null && (first.OverrideTranslucent || first.OverrideAlphaTest);
			var lightmap = key.Lightmap.HasValue ? first.LightmapAttributes : null;
			var probe = key.Probe.HasValue ? firstMember.Probe : null;
			var live = SplitsLods( casters ) ? first.Animation.Animating : 0;
			if ( kept.Group is not { } group || !ReferenceEquals( group.Members, kept.Members ) || group.Mesh != key.Mesh || group.Override != key.Override || group.OverrideBlends != overrideBlends
				|| group.Lightmap != lightmap || group.Probe != probe || group.Owner != owner || group.LiveLods != live )
			{
				kept.Group = group = new MeshGroup
				{
					Members = kept.Members,
					Mesh = key.Mesh,
					Override = key.Override,
					OverrideBlends = overrideBlends,
					Lightmap = lightmap,
					Probe = probe,
					Owner = owner,
					LiveLods = live,
				};
			}

			list.MeshGroups.Add( group );

			for ( int lod = 0; lod < key.Mesh.LodCount; lod++ ) list.DeviceCount += key.Mesh.DrawsForLod( lod ).Length;
			list.Triangles += (long)key.Mesh.TrianglesForLod( 0 ) * count;
			list.Fragments += count;
		}

		foreach ( var (_, members) in groups )
		{
			members.Clear();
			spareGroupMembers.Push( members );
		}

		groups.Clear();
		groupScratch.Lightmaps.AsSpan().Clear();
		groupScratch.Probes.AsSpan().Clear();
		groupScratch.Objects.AsSpan().Clear();
	}

	// AddMeshGroups's scratch (main thread): each group's member indices, lists to hold them, and a group's values
	readonly Dictionary<MeshGroupKey, List<int>> groupMembers = new();
	readonly Stack<List<int>> spareGroupMembers = new();
	GroupScratch groupScratch = new( 64 );

	sealed class GroupScratch( int size )
	{
		public readonly Vector3[] Centers = new Vector3[size], Extents = new Vector3[size];
		public readonly int[] Slots = new int[size];
		public readonly float[] Scales = new float[size];
		public readonly ShaderParameters[] Lightmaps = new ShaderParameters[size], Probes = new ShaderParameters[size];
		public readonly uint[] Moved = new uint[size];
		public readonly MeshObject[] Objects = new MeshObject[size];
	}

	static bool SameObjects( MeshObject[] a, ReadOnlySpan<MeshObject> b )
	{
		if ( a.Length != b.Length ) return false;
		for ( int i = 0; i < a.Length; i++ )
		{
			if ( !ReferenceEquals( a[i], b[i] ) ) return false;
		}

		return true;
	}

	/// <summary>
	/// Note an instanced object of the world as a resident member if it may be: it has placements, the device takes its draws
	/// (worked out once per draw version, as a mesh's candidate is), the device hasn't left it and its tag class is one of few.
	/// </summary>
	void ScanInstanced( RenderWorld world, int i, InstancedMeshObject obj, GpuDevice device )
	{
		if ( obj.Count == 0 || obj.Mesh is null ) return;

		var drawVersions = world.DrawVersions;
		if ( instancedKnown.Length < world.Count ) Array.Resize( ref instancedKnown, Math.Max( world.Count, instancedKnown.Length * 2 ) );
		ref var known = ref instancedKnown[i];
		if ( !ReferenceEquals( known.Object, obj ) || known.DrawVersion != drawVersions[i] )
		{
			known = default;
			ResidentCandidatesChecked++;
			if ( !Meshes.ResidentCandidate( obj, device ) ) return;
			known = (obj, drawVersions[i]);
		}

		lock ( meshesLeft )
		{
			if ( meshesLeft.Count > 0 && meshesLeft.Contains( obj ) ) return;
		}

		var cls = ClassOf( new ClassKey( obj.Tags, obj.IsWorld, world.SubWorlds[i], obj.HiddenInCaptures, false ) );
		if ( cls < 0 ) return;

		if ( instancedMemberCount == instancedMembers.Length ) Array.Resize( ref instancedMembers, Math.Max( 8, instancedMembers.Length * 2 ) );
		instancedMembers[instancedMemberCount++] = new InstancedMember
		{
			Object = obj,
			Index = i,
			DrawVersion = drawVersions[i],
			EntryVersion = world.EntryVersions[i],
			Class = cls,
			Moved = world.MovedStamps[i],
			CastShadows = obj.CastShadows,
			GameLayers = obj.GameLayers,
			IsStatic = obj.IsStatic,
			Slot = -1,
		};
	}

	/// <summary>
	/// A list's resident instanced objects, a group each: its owner is made from the object's placements (their world bounds,
	/// scales and its run's slots) and kept while the object, its placements and its run are the same - never by comparing arrays.
	/// </summary>
	void AddInstancedGroups( ResidentList list, ResidentCasters casters, uint mask, bool shadow )
	{
		var settles = Settles( casters );
		for ( int m = 0; m < instancedMemberCount; m++ )
		{
			ref readonly var member = ref instancedMembers[m];
			var obj = member.Object;
			if ( member.Slot < 0 || (mask & 1u << member.Class) == 0 ) continue;
			if ( shadow ? !(obj.CastShadows && TakesCaster( casters, SplitsLods( casters ) ? obj.IsStatic : obj.CachesAsStatic )) : !obj.GameLayers ) continue;

			// As for meshes and aggregates: the far cascade's cache leaves out what animates, and what moved until it keeps still
			if ( LeavesOutAnimating( casters, obj ) || (settles && !obj.IsStatic && restlessAggregates.ContainsKey( obj )) ) continue;

			var model = obj.Mesh;
			var placement = obj.Placed;
			var moved = settles ? GpuContext.MovedWord( member.Moved, obj.IsStatic ) : 0;
			var key = new InstancedKey( obj, casters, mask );
			instancedOwners.TryGetValue( key, out var kept );
			if ( kept is null || !ReferenceEquals( kept.Placement, placement ) || !ReferenceEquals( kept.Mesh, model ) || kept.Slot != member.Slot || kept.Moved != moved )
			{
				if ( kept is not null ) ReleaseOwner( kept.Owner );

				// The placements' arrays are never changed once made, so the owner shares them
				var count = placement.Centers.Length;
				var per = obj.EntriesPerInstance;
				var slots = new int[count];
				for ( int i = 0; i < count; i++ ) slots[i] = member.Slot + i * per;
				uint[] movedWords = null;
				if ( settles )
				{
					movedWords = new uint[count];
					Array.Fill( movedWords, moved );
				}

				// Cross-faded LODs in main views, for draws whose material can dither them; shadows switch at once
				var owner = new GpuContext.ResidentMeshes( model, placement.Centers, placement.Extents, slots, placement.Scales, null, null, movedWords ) { FadesLods = !shadow && model.LodCount > 1 && DithersLodFade( model ) };
				instancedOwners[key] = kept = new InstancedOwner { Owner = owner, Placement = placement, Mesh = model, Slot = member.Slot, Moved = moved };
			}

			kept.Version = residentVersion;
			var live = SplitsLods( casters ) ? obj.Animation.Animating : 0;
			if ( kept.Group is null || kept.Group.LiveLods != live ) kept.Group = new MeshGroup { Members = [obj], Mesh = model, Owner = kept.Owner, LiveLods = live };
			list.MeshGroups.Add( kept.Group );
			list.Holds[member.Index] = true;

			for ( int lod = 0; lod < model.LodCount; lod++ ) list.DeviceCount += model.DrawsForLod( lod ).Length;
			list.Triangles += (long)model.TrianglesForLod( 0 ) * placement.Centers.Length;
			list.Fragments += placement.Centers.Length;
		}
	}

	/// <summary>Whether a draw of <paramref name="model"/> has a material that dithers a LOD cross-fade.</summary>
	static bool DithersLodFade( RenderMesh model )
	{
		for ( int lod = 0; lod < model.LodCount; lod++ )
		{
			foreach ( ref readonly var draw in model.DrawsForLod( lod ) )
			{
				if ( draw.Material is { DithersLodFade: true } ) return true;
			}
		}

		return false;
	}

	/// <summary>
	/// A main view's resident static overlays: per render order, in the mesh feature's sort order
	/// (<see cref="MeshRenderFeature.ResidentOverlayKey"/>), only where every overlay the view draws of that order is resident,
	/// so a render order is drawn all from its list or all the view's own way.
	/// </summary>
	void AddOverlays( ResidentList list, uint mask )
	{
		var objects = residentWorld.Objects;
		HashSet<int> blocked = null;
		foreach ( var (index, cls, after) in overlayBlockers )
		{
			if ( index >= objects.Length || objects[index] is not MeshObject { GameLayers: true, LayerMatch: LayerMatch.StaticOverlay } blocker ) continue;
			if ( cls >= 0 && (mask & 1u << cls) == 0 ) continue;

			// One with callbacks records on the main thread: the view draws every overlay itself
			if ( blocker.Callbacks is not null ) return;
			if ( !after ) (blocked ??= new()).Add( blocker.RenderOrder );
		}

		var entries = new List<(ulong Key, int Member)>();
		for ( int m = 0; m < meshMemberCount; m++ )
		{
			ref readonly var member = ref meshMembers[m];
			var obj = member.Mesh;
			if ( !member.Overlay || (mask & 1u << member.Class) == 0 || !obj.GameLayers ) continue;
			if ( member.Slot < 0 )
			{
				(blocked ??= new()).Add( obj.RenderOrder );
				continue;
			}

			entries.Add( (MeshRenderFeature.ResidentOverlayKey( obj, member.Probe, member.Index ), m) );
		}

		entries.Sort( static ( a, b ) => a.Key != b.Key ? a.Key.CompareTo( b.Key ) : a.Member.CompareTo( b.Member ) );
		for ( int i = 0; i < entries.Count; )
		{
			var end = i + 1;
			while ( end < entries.Count && entries[end].Key >> 32 == entries[i].Key >> 32 ) end++;
			var order = (int)(uint)(entries[i].Key >> 32) ^ int.MinValue;
			if ( blocked is null || !blocked.Contains( order ) ) list.Overlays.Add( OverlayOrderOf( list, mask, order, CollectionsMarshal.AsSpan( entries )[i..end] ) );
			i = end;
		}
	}

	/// <summary>
	/// One render order's overlays as a list: groups of neighbours in one bin, as the mesh feature's runs, each a group of
	/// resident meshes whose owner is kept while its meshes are the same.
	/// </summary>
	OverlayOrder OverlayOrderOf( ResidentList list, uint mask, int renderOrder, ReadOnlySpan<(ulong Key, int Member)> entries )
	{
		var device = RenderHost.Current?.Device;
		var centers = residentWorld.BoundsCenter;
		var extents = residentWorld.BoundsExtents;
		var order = new OverlayOrder { RenderOrder = renderOrder, Version = residentVersion };
		var deviceCount = 0;
		for ( int g = 0; g < entries.Length; )
		{
			ref readonly var first = ref meshMembers[entries[g].Member];
			var obj = first.Mesh;
			var end = g + 1;
			while ( end < entries.Length && SameOverlayBin( first, meshMembers[entries[end].Member] ) ) end++;

			var count = end - g;
			var groupCenters = new Vector3[count];
			var groupExtents = new Vector3[count];
			var slots = new int[count];
			var scales = new float[count];
			var objects = new MeshObject[count];
			for ( int i = 0; i < count; i++ )
			{
				ref readonly var member = ref meshMembers[entries[g + i].Member];
				groupCenters[i] = centers[member.Index];
				groupExtents[i] = extents[member.Index];
				slots[i] = member.Slot;
				var scale = member.Mesh.Transform.Scale;
				scales[i] = MathF.Max( MathF.Abs( scale.x ), MathF.Max( MathF.Abs( scale.y ), MathF.Abs( scale.z ) ) );
				objects[i] = member.Mesh;
				list.Holds[member.Index] = true;
			}

			var lightmap = obj.LightmapAttributes is null ? (long?)null : device?.LightingFreeKey( obj.LightmapAttributes ) ?? 0;
			var probe = first.Probe is null ? (long?)null : device?.LightingFreeKey( first.Probe ) ?? 0;
			var key = new MeshGroupKey( ResidentCasters.Main, mask, obj.Mesh, obj.MaterialOverride, lightmap, probe, renderOrder, first.Index );
			meshOwners.TryGetValue( key, out var kept );
			var owner = kept?.Owner;
			if ( owner is null || !owner.Slots.AsSpan().SequenceEqual( slots ) || !owner.Centers.AsSpan().SequenceEqual( groupCenters )
				|| !owner.Extents.AsSpan().SequenceEqual( groupExtents ) || !owner.LodScales.AsSpan().SequenceEqual( scales ) )
			{
				if ( owner is not null ) ReleaseOwner( owner );
				owner = new GpuContext.ResidentMeshes( obj.Mesh, groupCenters, groupExtents, slots, scales );
				meshOwners[key] = kept = new MeshOwner { Owner = owner };
			}

			kept.Version = residentVersion;
			order.Groups.Add( new MeshGroup
			{
				Members = objects,
				Mesh = obj.Mesh,
				Override = obj.MaterialOverride,
				OverrideBlends = obj.MaterialOverride is not null && (obj.OverrideTranslucent || obj.OverrideAlphaTest),
				Lightmap = obj.LightmapAttributes,
				Probe = first.Probe,
				Owner = owner,
			} );

			deviceCount += obj.Mesh.DrawsForLod( 0 ).Length;
			order.Triangles += (long)obj.Mesh.TrianglesForLod( 0 ) * count;
			order.Fragments += count;
			g = end;
		}

		// Made once: they name no frame's instances, and only the overlay layer draws them
		order.Device = new GpuContext.GpuDrivenBatch[deviceCount];
		MeshDeviceBatches( order.Groups, false, false, order.Device );
		return order;
	}

	/// <summary>Whether two resident overlays share a bin, as the mesh feature's runs need (<see cref="MeshRenderFeature.Bin"/>).</summary>
	static bool SameOverlayBin( in MeshMember a, in MeshMember b )
	{
		var x = a.Mesh;
		var y = b.Mesh;
		return ReferenceEquals( x.Mesh, y.Mesh ) && ReferenceEquals( x.MaterialOverride, y.MaterialOverride ) && ReferenceEquals( x.LightmapAttributes, y.LightmapAttributes )
			&& ReferenceEquals( a.Probe, b.Probe ) && (x.MaterialOverride is null || (x.OverrideTranslucent || x.OverrideAlphaTest) == (y.OverrideTranslucent || y.OverrideAlphaTest));
	}

	static bool AllSame( ReadOnlySpan<ShaderParameters> attributes )
	{
		for ( int i = 1; i < attributes.Length; i++ )
		{
			if ( !ReferenceEquals( attributes[i], attributes[0] ) ) return false;
		}

		return true;
	}

	static bool SameLighting( ShaderParameters[] a, ShaderParameters[] b ) => SameLighting( a, b, b is not null );

	static bool SameLighting( ShaderParameters[] a, ReadOnlySpan<ShaderParameters> b, bool hasB )
	{
		if ( a is null || !hasB ) return a is null && !hasB;
		if ( a.Length != b.Length ) return false;
		for ( int i = 0; i < a.Length; i++ )
		{
			if ( !ReferenceEquals( a[i], b[i] ) ) return false;
		}

		return true;
	}

	/// <summary>
	/// Mesh groups as the device takes them for a depth kind: a batch per group, LOD and draw. False while a depth draw's
	/// material may still change which material draws it (<see cref="GpuMaterial.DepthOnlyPending"/>), to make them again.
	/// </summary>
	internal bool MeshDeviceBatches( List<MeshGroup> groups, bool depth, bool forceDepthOnly, Span<GpuContext.GpuDrivenBatch> made )
	{
		var at = 0;
		var settled = true;
		foreach ( var group in groups )
		{
			var mesh = group.Mesh;
			var owner = group.Owner;
			for ( int lod = 0; lod < mesh.LodCount; lod++ )
			{
				var draws = mesh.DrawsForLod( lod );

				// What a list that splits LODs leaves to the view over the cache: no batch for a LOD whose draws animate vertices
				if ( (group.LiveLods & RenderMesh.LodAnimation.Bit( lod )) != 0 )
				{
					made.Slice( at, draws.Length ).Clear();
					at += draws.Length;
					continue;
				}

				for ( int d = 0; d < draws.Length; d++ )
				{
					ref readonly var draw = ref draws[d];
					if ( depth && group.Override is null && draw.Material is { DepthOnlyPending: true } ) settled = false;
					var material = Meshes.MaterialFor( group.Override, group.OverrideBlends, draw, depth, forceDepthOnly, out var strip );
					if ( material is null )
					{
						made[at++] = default;
						continue;
					}

					// The first mesh's lighting, and each mesh's own where the group's meshes differ in it
					var probe = depth || group.Probe is null || !RenderMesh.NeedsLightProbe( material ) ? null : group.Probe;
					var lighting = draw.Lightmapped ? (depth ? null : group.Lightmap) : probe;
					var meshLighting = lighting is null ? null : draw.Lightmapped ? owner.Lightmaps : owner.Probes;
					made[at++] = new GpuContext.GpuDrivenBatch( material, mesh, draw, lighting, strip, d, owner.Count, 0, (long)(draw.IndexCount / 3) * owner.Count, owner, TextureSize: 0, Lod: lod, MeshLighting: meshLighting );
				}
			}
		}

		return settled;
	}

	/// <summary>Whether a shadow view's list of these casters holds an aggregate.</summary>
	static bool TakesCaster( ResidentCasters casters, in Member member ) => TakesCaster( casters, member.IsStatic );

	/// <summary>Whether a shadow view's list of these casters holds a static or moving caster.</summary>
	static bool TakesCaster( ResidentCasters casters, bool isStatic ) => casters switch
	{
		ResidentCasters.Static or ResidentCasters.StaticLods => isStatic,
		ResidentCasters.NotStatic or ResidentCasters.SettledNotStatic => !isStatic,
		_ => true,
	};

	/// <summary>Whether a view whose root is <paramref name="root"/> shows an aggregate of this class (<see cref="RenderView.Shows(RenderObject, int)"/>).</summary>
	static bool RootShows( RenderView root, in ClassKey key )
	{
		var subWorld = key.SubWorld;
		if ( subWorld != 0 && (root.SubWorlds & (1UL << (subWorld - 1))) == 0 ) return false;
		if ( root.Capture && (key.HiddenInCaptures || key.Overlay) ) return false;
		return root.ShowsTags( key.Tags, key.IsWorld );
	}

	/// <summary>
	/// The unforced depth prefix, grouped by source material and whether it strips pixels off the fast path.
	/// Each group has the same outcome in <see cref="MaterialFor"/>; its members are a chain of batch indices.
	/// </summary>
	internal sealed class DepthBatchGroups
	{
		readonly int[] next;
		readonly List<int> heads = new();

		public DepthBatchGroups( ReadOnlySpan<GpuContext.GpuDrivenBatch> batches )
		{
			next = new int[batches.Length];
			var groups = new Dictionary<GpuMaterial, (int KeepsPixel, int StripsPixel)>( ReferenceEqualityComparer.Instance );
			for ( int i = 0; i < batches.Length; i++ )
			{
				var draw = batches[i].Draw;
				if ( draw.ShadowFastPath || draw.Material is not { } material ) continue;

				ref var pair = ref CollectionsMarshal.GetValueRefOrAddDefault( groups, material, out _ );
				ref var group = ref (draw.Translucent || draw.AlphaTest ? ref pair.KeepsPixel : ref pair.StripsPixel);
				if ( group == 0 )
				{
					next[i] = -1;
					heads.Add( i );
					group = heads.Count;
				}
				else
				{
					next[i] = heads[group - 1];
					heads[group - 1] = i;
				}
			}
		}

		public bool Refresh( AggregateRenderFeature feature, Span<GpuContext.GpuDrivenBatch> batches, bool rebuilt )
		{
			var changed = false;
			foreach ( var head in heads )
			{
				ref var first = ref batches[head];
				var material = feature.MaterialFor( first.Draw, true, false, out var strip );
				if ( !rebuilt && ReferenceEquals( first.Material, material ) && first.StripPixelShader == strip ) continue;

				// On a rebuild the members were made individually: bring them all to the group's current choice.
				for ( int i = head; i >= 0; i = next[i] )
				{
					ref var batch = ref batches[i];
					if ( ReferenceEquals( batch.Material, material ) && batch.StripPixelShader == strip ) continue;
					batch = batch with { Material = material, StripPixelShader = strip };
					changed = true;
				}
			}

			return changed;
		}
	}

	/// <summary>Which of a list's device batch arrays a layer draws: by whether it draws depth, and whether it forces the depth-only material.</summary>
	static int DeviceKind( bool depth, bool forceDepthOnly ) => (depth ? 1 : 0) | (forceDepthOnly ? 2 : 0);

	/// <summary>
	/// As the frame is planned (<see cref="RunWeights"/>, main thread): a layer will draw a view's resident list, so setup
	/// makes its depth kind's batches. Not a layer drawing only occlusion culled batches, which never takes the list.
	/// </summary>
	void WantDeviceBatches( Batches state, MeshLayer layer )
	{
		if ( state.Resident is not { } list || state.ResidentGeneration != argGeneration || layer.OcclusionPhase == OcclusionPhase.Late ) return;
		list.DeviceWanted[DeviceKind( layer.Depth, layer.ForceDepthFastPath )] = argGeneration;
	}

	/// <summary>
	/// In setup, before recording starts: each resident list's device batches for the depth kinds the frame's layers draw it
	/// with, so the workers recording them read them as they are rather than queue on the list to make them.
	/// </summary>
	void MakeDeviceBatches()
	{
		lock ( residentViews )
		{
			foreach ( var pass in residentViews )
			{
				var state = pass.State<Batches>( this );
				if ( state.Resident is not { DeviceCount: > 0 } list || state.ResidentGeneration != argGeneration ) continue;
				for ( int kind = 0; kind < 4; kind++ )
				{
					if ( list.DeviceWanted[kind] == argGeneration ) DeviceBatches( list, (kind & 1) != 0, (kind & 2) != 0 );
				}
			}
		}
	}

	/// <summary>
	/// A resident list's batches as the device takes them for a depth kind, made once a frame for every view and worker that
	/// draws the list: in setup for the kinds the frame's layers draw (<see cref="MakeDeviceBatches"/>), else by the first
	/// call that asks, under the list's lock. Made again only where they can have changed: the list's batches, its moved
	/// words and each aggregate's mesh are the list's for its life, and a change to an aggregate's lightmap makes the lists
	/// again; the members' first instances move with <see cref="residentInstances"/>; and a depth draw that isn't forced
	/// onto the depth-only material takes it as its material's <see cref="GpuMaterial.DepthOnlyNow"/> says at the time.
	/// </summary>
	ReadOnlySpan<GpuContext.GpuDrivenBatch> DeviceBatches( ResidentList list, bool depth, bool forceDepthOnly )
	{
		var kind = DeviceKind( depth, forceDepthOnly );

		// Made this frame: the generation is written after the batches, so a worker that reads it reads them made
		if ( System.Threading.Volatile.Read( ref list.DeviceGeneration[kind] ) == argGeneration && list.Device[kind] is { } ready ) return ready.AsSpan( 0, list.DeviceCount );

		lock ( list )
		{
			// Read before the materials are asked: a change while they are is asked again next frame
			var epoch = GpuMaterial.DepthOnlyEpoch;
			var made = list.Device[kind];
			if ( list.DeviceGeneration[kind] == argGeneration && made is not null ) return made.AsSpan( 0, list.DeviceCount );

			var changed = false;
			var batches = CollectionsMarshal.AsSpan( list.Batches );
			var rebuilt = made is null || made.Length < list.DeviceCount || !ReferenceEquals( list.DeviceDepthOnly[kind], depthOnlyMaterial );
			if ( rebuilt )
			{
				if ( made is null || made.Length < list.DeviceCount )
				{
					if ( made is not null ) BatchPool.Return( made, true );
					list.Device[kind] = made = BatchPool.Rent( list.DeviceCount );
					made.AsSpan().Clear();
					list.DeviceMeshes[kind] = false;
					if ( depth && !forceDepthOnly ) list.DepthBatches = null;
					changed = true;
				}

				var moved = list.Moved;
				for ( int i = 0; i < batches.Length; i++ )
				{
					var batch = DeviceBatch( batches[i], depth, forceDepthOnly );
					if ( moved is not null ) batch = batch with { Moved = moved[i] };
					changed |= !made[i].Equals( batch );
					made[i] = batch;
				}

				list.DeviceDepthOnly[kind] = depthOnlyMaterial;
			}
			else if ( list.DeviceInstances[kind] != residentInstances )
			{
				for ( int i = 0; i < batches.Length; i++ )
				{
					ref var batch = ref made[i];
					var instance = transforms.AggregateFirstInstance( batches[i].ObjectIndex );
					if ( batch.Material is null || instance == batch.FirstInstance ) continue;
					batch = batch with { FirstInstance = instance };
					changed = true;
				}
			}

			// A material's depth-only answer only changes with the epoch (or the batches were made again)
			if ( depth && !forceDepthOnly && depthOnlyMaterial is not null && !batches.IsEmpty && (rebuilt || list.DeviceDepthEpoch[kind] != epoch) )
			{
				var prefix = made.AsSpan( 0, batches.Length );
				var groups = list.DepthBatches ??= new DepthBatchGroups( prefix );
				changed |= groups.Refresh( this, prefix, rebuilt );
			}

			list.DeviceInstances[kind] = residentInstances;
			list.DeviceDepthEpoch[kind] = epoch;

			// Mesh groups' batches name no frame's instances: made once their materials have settled
			if ( !list.DeviceMeshes[kind] )
			{
				list.DeviceMeshes[kind] = MeshDeviceBatches( list.MeshGroups, depth, forceDepthOnly, made.AsSpan( batches.Length, list.DeviceCount - batches.Length ) );
				changed = true;
			}

			if ( changed ) list.DeviceStamp[kind]++;
			System.Threading.Volatile.Write( ref list.DeviceGeneration[kind], argGeneration );
			return made.AsSpan( 0, list.DeviceCount );
		}
	}
}

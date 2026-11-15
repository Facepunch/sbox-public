using System.Runtime.InteropServices;

namespace Borealis.Features;

/// <summary>
/// Resident aggregates (<see cref="GpuDevice.DrawsAggregatesResident"/>): every aggregate of the world goes to the device
/// in one list a view kind keeps between frames, which views hand over whole instead of preparing their own, so the
/// device keeps what it makes of them (buckets, records, multi-draws) and the GPU culls every fragment. A list changes only
/// when an aggregate is added, removed or changes how it draws, or its kept transforms move; a view takes the list of its
/// kind (main, or which casters a shadow view takes) and of the tag classes it shows. A shadow view's list holds the casters
/// its static filter takes whether or not they've settled: static ones for a far cascade cache, none for what's drawn over
/// it. Casters whose settling decides, and aggregates a list doesn't hold, go the view's own way. Resident static meshes
/// (<see cref="GpuDevice.DrawsMeshesResident"/>) join the lists in groups of one model drawn alike, a batch per LOD and draw,
/// which the GPU culls and picks LODs for (<see cref="GpuContext.ResidentMeshes"/>); the mesh feature doesn't prepare them.
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

		public int Version;

		/// <summary>Triangles and fragments of all its batches, for the stats.</summary>
		public long Triangles;
		public int Fragments;

		/// <summary>The batches as the device takes them, per depth kind, made once a frame (<see cref="DeviceBatches"/>).</summary>
		public readonly GpuContext.GpuDrivenBatch[][] Device = new GpuContext.GpuDrivenBatch[4][];
		public readonly int[] DeviceGeneration = [-1, -1, -1, -1];

		/// <summary>Per depth kind, whether <see cref="Device"/> holds its mesh groups' batches, which no frame changes.</summary>
		public readonly bool[] DeviceMeshes = new bool[4];
	}

	/// <summary>What a resident aggregate was when its lists were made: any change makes them again.</summary>
	struct Member
	{
		public AggregateObject Aggregate;
		public int Index;
		public int DrawVersion;
		public object Fragments;
		public bool Visible, GameLayers, CastShadows, IsStatic, IsWorld, HiddenInCaptures, Overlay, Lightmapped;

		public LayerMatch LayerMatch;
		public uint[] Tags;
		public int SubWorld;
		public ShaderParameters LightmapAttributes;
		public int Class;

		/// <summary>Its first kept transform slot, or -1 while it has frame entries (its first frame): then it isn't resident.</summary>
		public int FirstSlot;

		/// <summary>Its fragments' arguments' first index in the frame's arguments: the same every frame.</summary>
		public int FirstArg;
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
		public ShaderParameters Probe;

		/// <summary>Its kept transform slot, or -1 while it has none: then it isn't resident.</summary>
		public int Slot;
	}

	/// <summary>Resident meshes of one model drawn alike, in a list.</summary>
	internal sealed class MeshGroup
	{
		public RenderMesh Mesh;
		public GpuMaterial Override;
		public bool OverrideBlends;
		public ShaderParameters Lightmap, Probe;
		public GpuContext.ResidentMeshes Owner;
		public MeshObject[] Members;
	}

	/// <summary>What a list's mesh group is found by; the group keeps its owner while its meshes are the same.</summary>
	readonly record struct MeshGroupKey( ResidentCasters Casters, uint Mask, RenderMesh Mesh, GpuMaterial Override, ShaderParameters Lightmap, ShaderParameters Probe );

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
	RenderWorld residentWorld;
	int residentStructure = -1;
	int residentVersion;
	readonly List<ClassKey> residentClasses = new();
	readonly Dictionary<(ResidentCasters Casters, uint Mask), ResidentList> residentLists = new();

	MeshMember[] meshMembers = [];
	int meshMemberCount;
	bool residentMeshes;
	long residentVolumesKey;
	readonly Dictionary<MeshGroupKey, GpuContext.ResidentMeshes> meshOwners = new();

	/// <summary>
	/// Meshes that moved (their entry version moved, as a transform, bounds or tint change moves it), by the frame they last
	/// moved and the entry version then: they go the views' own way until they've stayed still <see cref="SettleFrames"/>
	/// frames, as their records hold their bounds.
	/// </summary>
	readonly Dictionary<MeshObject, (int Frame, int Version)> restless = new( ReferenceEqualityComparer.Instance );

	/// <summary>Frames a mesh that moved stays still before it's resident again.</summary>
	internal static int SettleFrames { get; set; } = 60;

	/// <summary>Meshes a resident list held whose draws the device left: they stay with the views' own batches.</summary>
	readonly HashSet<MeshObject> meshesLeft = new( ReferenceEqualityComparer.Instance );
	volatile bool meshesLeftChanged;

	/// <summary>
	/// A resident mesh group's draw the device left (from any recording thread): its meshes go the views' own way from the
	/// next frame, and miss this one.
	/// </summary>
	void ResidentMeshesLeft( ResidentList list, GpuContext.ResidentMeshes owner )
	{
		foreach ( var group in list.MeshGroups )
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
	}

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
		var resident = state.OneRun && ResidentEnabled && device.DrawsAggregatesResident;

		// The frame's views all write into one args buffer: the resident aggregates' first, at the same place every frame
		if ( argGeneration != transforms.Generation )
		{
			argGeneration = transforms.Generation;
			argCount = 0;
			if ( resident ) RefreshResident( world, transforms );
		}

		if ( resident ) state.Resident = ResidentListFor( pass, device.CullsAggregatesGpuDriven ? WorkerSize( device ) : 0 );
	}

	/// <summary>
	/// For the mesh feature, before it prepares a view: by world index, the meshes the view's resident list draws, which it
	/// then leaves out; null when the list holds none.
	/// </summary>
	internal bool[] ResidentMeshHolds( RenderWorld world, ViewPass pass, TransformBuffer transforms )
	{
		PrepareResident( world, pass, transforms );
		return pass.State<Batches>( this ).Resident is { MeshGroups.Count: > 0 } list ? list.Holds : null;
	}

	/// <summary>
	/// Once a frame, before any view prepares: look for added and removed aggregates and changes to how each draws, put
	/// every resident aggregate's arguments first in the frame's (at the same place every frame) and its transforms in the
	/// frame's buffer. A change makes the lists again.
	/// </summary>
	void RefreshResident( RenderWorld world, TransformBuffer transforms )
	{
		var meshesWanted = MeshesResident();
		var settled = meshesWanted && Settle( world, transforms.Generation );
		var volumesKey = meshesWanted && world.LightProbeVolumes.Count > 0 ? LightProbeVolume.Key( world.LightProbeVolumes ) : 0;
		if ( !ReferenceEquals( world, residentWorld ) || world.Structure != residentStructure || meshesWanted != residentMeshes || volumesKey != residentVolumesKey || meshesLeftChanged || settled ) Rescan( world );

		// Their arguments, first in the frame's, so each keeps its place
		if ( args.Length < residentArgCount ) Array.Resize( ref args, Math.Max( residentArgCount, args.Length * 2 ) );
		residentArgs.AsSpan( 0, residentArgCount ).CopyTo( args );
		argCount = residentArgCount;

		var drawVersions = world.DrawVersions;
		var entryVersions = world.EntryVersions;
		var changed = false;
		for ( int m = 0; m < memberCount; m++ )
		{
			ref var member = ref members[m];
			var aggregate = member.Aggregate;
			var index = member.Index;

			if ( member.DrawVersion != drawVersions[index] || !ReferenceEquals( member.Fragments, aggregate.FragmentsKey ) || member.Visible != aggregate.Visible
				|| member.GameLayers != aggregate.GameLayers || member.CastShadows != aggregate.CastShadows || member.IsStatic != aggregate.IsStatic
				|| member.IsWorld != aggregate.IsWorld || member.HiddenInCaptures != aggregate.HiddenInCaptures || member.Overlay != aggregate.Overlay
				|| member.LayerMatch != aggregate.LayerMatch || !ReferenceEquals( member.Tags, aggregate.Tags ) || member.SubWorld != aggregate.SubWorld
				|| member.Lightmapped != aggregate.Lightmapped || !ReferenceEquals( member.LightmapAttributes, aggregate.LightmapAttributes ) )
			{
				// Rarely: its arguments may have moved too, so look at everything again
				Rescan( world );
				RefreshResident( world, transforms );
				return;
			}

			// Its transforms, kept between frames, rewritten when its entry version moves
			if ( aggregate.Fragments.Length == 0 ) continue;
			WriteTransforms( aggregate, index, entryVersions[index], transforms );
			var slot = transforms.InstanceSlot( aggregate.FirstInstance );
			if ( slot >= transforms.FrameBase ) slot = -1;
			if ( slot != member.FirstSlot )
			{
				member.FirstSlot = slot;
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
			if ( member.DrawVersion != drawVersions[index] )
			{
				Rescan( world );
				RefreshResident( world, transforms );
				return;
			}

			// Moved: the views' own way until it settles (every one that moved, then the lists again once)
			if ( member.EntryVersion != entryVersions[index] )
			{
				restless[member.Mesh] = (transforms.Generation, entryVersions[index]);
				moved = true;
				continue;
			}

			var slot = member.Mesh.Mesh.HasDrawTints ? TintedRun( member.Mesh, index, matrices[index], entryVersions[index], transforms )
				: transforms.KeptSlotFor( index, member.Mesh, matrices[index], entryVersions[index] );
			if ( slot >= transforms.FrameBase ) slot = -1;
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

		if ( changed ) DropResidentLists();
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
	/// Follow the meshes that moved: one that moves again restarts its wait, one gone or no longer a mesh of this world is
	/// forgotten; true when one has stayed still long enough to be resident again.
	/// </summary>
	bool Settle( RenderWorld world, int frame )
	{
		if ( restless.Count == 0 ) return false;

		var entryVersions = world.EntryVersions;
		var settled = false;
		List<MeshObject> done = null;
		foreach ( var (mesh, (moved, version)) in restless )
		{
			if ( !ReferenceEquals( mesh.World, world ) || mesh.Index < 0 )
			{
				(done ??= new()).Add( mesh );
				continue;
			}

			if ( entryVersions[mesh.Index] != version )
			{
				(done ??= new()).Add( mesh );
				continue;
			}

			if ( frame - moved < SettleFrames ) continue;
			(done ??= new()).Add( mesh );
			settled = true;
		}

		if ( done is null ) return false;
		foreach ( var mesh in done )
		{
			if ( ReferenceEquals( mesh.World, world ) && mesh.Index >= 0 && entryVersions[mesh.Index] != restless[mesh].Version ) restless[mesh] = (frame, entryVersions[mesh.Index]);
			else restless.Remove( mesh );
		}

		return settled;
	}

	/// <summary>Whether meshes may be resident now: the device draws them so, and no debug view tints them per frame.</summary>
	bool MeshesResident() => Meshes is { DebugViews: DebugViews.None, UseMeshMaterials: true } && RenderHost.Current?.Device is { DrawsMeshesResident: true };

	/// <summary>Find every aggregate of the world again, its tag class and its arguments' place.</summary>
	void Rescan( RenderWorld world )
	{
		residentWorld = world;
		residentStructure = world.Structure;
		memberCount = 0;
		meshMemberCount = 0;
		residentArgCount = 0;
		residentClasses.Clear();
		residentMeshes = MeshesResident();
		meshesLeftChanged = false;
		var volumes = world.LightProbeVolumes;
		residentVolumesKey = residentMeshes && volumes.Count > 0 ? LightProbeVolume.Key( volumes ) : 0;

		var objects = world.Objects;
		var drawVersions = world.DrawVersions;
		for ( int i = 0; i < objects.Length; i++ )
		{
			if ( objects[i] is not AggregateObject aggregate ) continue;

			var key = new ClassKey( aggregate.Tags, aggregate.IsWorld, aggregate.SubWorld, aggregate.HiddenInCaptures, aggregate.Overlay );
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
				Fragments = aggregate.FragmentsKey,
				Visible = aggregate.Visible,
				GameLayers = aggregate.GameLayers,
				CastShadows = aggregate.CastShadows,
				IsStatic = aggregate.IsStatic,
				IsWorld = aggregate.IsWorld,
				HiddenInCaptures = aggregate.HiddenInCaptures,
				Overlay = aggregate.Overlay,
				LayerMatch = aggregate.LayerMatch,
				Tags = aggregate.Tags,
				SubWorld = aggregate.SubWorld,
				Lightmapped = aggregate.Lightmapped,
				LightmapAttributes = aggregate.LightmapAttributes,
				Class = cls,
				FirstSlot = -1,
				FirstArg = residentArgCount,
			};

			residentArgCount += fragments.Length;
		}

		// Static meshes that may be resident, with their main views' probes
		var device = RenderHost.Current?.Device;
		if ( residentMeshes && device is not null )
		{
			var entryVersions = world.EntryVersions;
			var centers = world.BoundsCenter;
			var extents = world.BoundsExtents;
			for ( int i = 0; i < objects.Length; i++ )
			{
				if ( objects[i] is not MeshObject mesh || restless.ContainsKey( mesh ) || !Meshes.ResidentCandidate( mesh, device ) ) continue;
				lock ( meshesLeft )
				{
					if ( meshesLeft.Count > 0 && meshesLeft.Contains( mesh ) ) continue;
				}

				var cls = ClassOf( new ClassKey( mesh.Tags, mesh.IsWorld, mesh.SubWorld, mesh.HiddenInCaptures, false ) );
				if ( cls < 0 ) continue;

				if ( meshMemberCount == meshMembers.Length ) Array.Resize( ref meshMembers, Math.Max( 64, meshMembers.Length * 2 ) );
				meshMembers[meshMemberCount++] = new MeshMember
				{
					Mesh = mesh,
					Index = i,
					DrawVersion = drawVersions[i],
					EntryVersion = entryVersions[i],
					Class = cls,
					Probe = MeshRenderFeature.ResidentProbe( mesh, volumes, residentVolumesKey, centers[i], extents[i] ),
					Slot = -1,
				};
			}
		}

		DropResidentLists();
	}

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

	void DropResidentLists()
	{
		residentLists.Clear();
		residentVersion++;
	}

	/// <summary>
	/// The resident list a view draws: its kind's (a shadow view's holds static casters, and only for views that take them)
	/// for the tag classes its root shows. Null when it draws none.
	/// </summary>
	ResidentList ResidentListFor( ViewPass pass, int textureSize )
	{
		if ( memberCount == 0 && meshMemberCount == 0 ) return null;

		var shadow = pass.IsShadow;
		var casters = !shadow ? ResidentCasters.Main : pass.StaticFilter switch
		{
			StaticFilter.Any => ResidentCasters.All,
			// A far cascade cache's settled casters that aren't static, and what's drawn over it, depend on when each settled
			StaticFilter.OnlyStatic or StaticFilter.Settled => ResidentCasters.Static,
			StaticFilter.NoStatic => ResidentCasters.NotStatic,
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

		list = new ResidentList { Version = residentVersion };
		if ( list.Holds.Length < residentWorld.Count ) list.Holds = new bool[residentWorld.Count];
		for ( int m = 0; m < memberCount; m++ )
		{
			ref readonly var member = ref members[m];
			var aggregate = member.Aggregate;
			if ( member.FirstSlot < 0 || (mask & 1u << member.Class) == 0 || !member.Visible ) continue;
			if ( shadow ? !(member.CastShadows && member.LayerMatch == LayerMatch.None && TakesCaster( casters, member )) : !member.GameLayers ) continue;

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
			list.Batches.Add( new Batch( aggregate, member.FirstArg, fragments.Length, -1, indices / 3, SortKey( aggregate ), textureSize ) );
			list.Holds[member.Index] = true;
			list.Triangles += indices / 3;
			list.Fragments += count;
		}

		list.Batches.Sort( ByKey );
		list.DeviceCount = list.Batches.Count;
		if ( meshMemberCount > 0 ) AddMeshGroups( list, casters, mask, shadow );
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
		var groups = new Dictionary<MeshGroupKey, List<int>>();
		for ( int m = 0; m < meshMemberCount; m++ )
		{
			ref readonly var member = ref meshMembers[m];
			var obj = member.Mesh;
			if ( member.Slot < 0 || (mask & 1u << member.Class) == 0 ) continue;
			if ( shadow ? !(obj.CastShadows && TakesCaster( casters, obj.IsStatic )) : !obj.GameLayers ) continue;
			list.ReadsFrameBuffer |= !shadow && obj.WantsFrameBufferCopy;

			var key = new MeshGroupKey( casters, mask, obj.Mesh, obj.MaterialOverride, shadow ? null : obj.LightmapAttributes, shadow ? null : member.Probe );
			if ( !groups.TryGetValue( key, out var members ) ) groups[key] = members = new List<int>();
			members.Add( m );
		}

		var centers = residentWorld.BoundsCenter;
		var extents = residentWorld.BoundsExtents;
		foreach ( var (key, members) in groups )
		{
			var count = members.Count;
			var groupCenters = new Vector3[count];
			var groupExtents = new Vector3[count];
			var slots = new int[count];
			var scales = new float[count];
			for ( int i = 0; i < count; i++ )
			{
				ref readonly var member = ref meshMembers[members[i]];
				var obj = member.Mesh;
				groupCenters[i] = centers[member.Index];
				groupExtents[i] = extents[member.Index];
				slots[i] = member.Slot;
				var scale = obj.Transform.Scale;
				scales[i] = obj.LodOverride >= 0 ? -1 - obj.LodOverride : MathF.Max( MathF.Abs( scale.x ), MathF.Max( MathF.Abs( scale.y ), MathF.Abs( scale.z ) ) );
				list.Holds[member.Index] = true;
			}

			// The same meshes as last time keep their owner, and so the device's records of them
			if ( !meshOwners.TryGetValue( key, out var owner ) || !owner.Slots.AsSpan().SequenceEqual( slots ) || !owner.Centers.AsSpan().SequenceEqual( groupCenters )
				|| !owner.Extents.AsSpan().SequenceEqual( groupExtents ) || !owner.LodScales.AsSpan().SequenceEqual( scales ) )
				meshOwners[key] = owner = new GpuContext.ResidentMeshes( key.Mesh, groupCenters, groupExtents, slots, scales );

			var first = meshMembers[members[0]].Mesh;
			var objects = new MeshObject[count];
			for ( int i = 0; i < count; i++ ) objects[i] = meshMembers[members[i]].Mesh;
			list.MeshGroups.Add( new MeshGroup
			{
				Members = objects,
				Mesh = key.Mesh,
				Override = key.Override,
				OverrideBlends = key.Override is not null && (first.OverrideTranslucent || first.OverrideAlphaTest),
				Lightmap = key.Lightmap,
				Probe = key.Probe,
				Owner = owner,
			} );

			for ( int lod = 0; lod < key.Mesh.LodCount; lod++ ) list.DeviceCount += key.Mesh.DrawsForLod( lod ).Length;
			list.Triangles += (long)key.Mesh.TrianglesForLod( 0 ) * count;
			list.Fragments += count;
		}
	}

	/// <summary>A list's mesh groups as the device takes them for a depth kind: a batch per group, LOD and draw.</summary>
	void MeshDeviceBatches( ResidentList list, bool depth, bool forceDepthOnly, Span<GpuContext.GpuDrivenBatch> made )
	{
		var at = 0;
		foreach ( var group in list.MeshGroups )
		{
			var mesh = group.Mesh;
			var owner = group.Owner;
			for ( int lod = 0; lod < mesh.LodCount; lod++ )
			{
				var draws = mesh.DrawsForLod( lod );
				for ( int d = 0; d < draws.Length; d++ )
				{
					ref readonly var draw = ref draws[d];
					var material = Meshes.MaterialFor( group.Override, group.OverrideBlends, draw, depth, forceDepthOnly, out var strip );
					if ( material is null )
					{
						made[at++] = default;
						continue;
					}

					var probe = depth || group.Probe is null || !RenderMesh.NeedsLightProbe( material ) ? null : group.Probe;
					var lighting = draw.Lightmapped ? (depth ? null : group.Lightmap) : probe;
					made[at++] = new GpuContext.GpuDrivenBatch( material, mesh, draw, lighting, strip, d, owner.Count, 0, (long)(draw.IndexCount / 3) * owner.Count, owner, TextureSize: 0, Lod: lod );
				}
			}
		}
	}

	/// <summary>Whether a shadow view's list of these casters holds an aggregate.</summary>
	static bool TakesCaster( ResidentCasters casters, in Member member ) => TakesCaster( casters, member.IsStatic );

	/// <summary>Whether a shadow view's list of these casters holds a static or moving caster.</summary>
	static bool TakesCaster( ResidentCasters casters, bool isStatic ) => casters switch
	{
		ResidentCasters.Static => isStatic,
		ResidentCasters.NotStatic => !isStatic,
		_ => true,
	};

	/// <summary>Whether a view whose root is <paramref name="root"/> shows an aggregate of this class (<see cref="RenderView.Shows"/>).</summary>
	static bool RootShows( RenderView root, in ClassKey key )
	{
		var subWorld = key.SubWorld;
		if ( subWorld != 0 && (root.SubWorlds & (1UL << (subWorld - 1))) == 0 ) return false;
		if ( root.Capture && (key.HiddenInCaptures || key.Overlay) ) return false;
		return root.ShowsTags( key.Tags, key.IsWorld );
	}

	/// <summary>
	/// A resident list's batches as the device takes them for a depth kind, made once a frame for every view and worker that
	/// draws the list (each frame's first instances move; nothing else does).
	/// </summary>
	ReadOnlySpan<GpuContext.GpuDrivenBatch> DeviceBatches( ResidentList list, bool depth, bool forceDepthOnly )
	{
		var kind = (depth ? 1 : 0) | (forceDepthOnly ? 2 : 0);
		lock ( list )
		{
			var made = list.Device[kind];
			if ( list.DeviceGeneration[kind] == argGeneration && made is not null ) return made.AsSpan( 0, list.DeviceCount );

			if ( made is null || made.Length < list.DeviceCount ) list.Device[kind] = made = new GpuContext.GpuDrivenBatch[list.DeviceCount];
			var batches = CollectionsMarshal.AsSpan( list.Batches );
			for ( int i = 0; i < batches.Length; i++ ) made[i] = DeviceBatch( batches[i], depth, forceDepthOnly );

			// Mesh groups' batches name no frame's instances: made once
			if ( !list.DeviceMeshes[kind] ) MeshDeviceBatches( list, depth, forceDepthOnly, made.AsSpan( batches.Length, list.DeviceCount - batches.Length ) );
			list.DeviceMeshes[kind] = true;
			list.DeviceGeneration[kind] = argGeneration;
			return made.AsSpan( 0, list.DeviceCount );
		}
	}
}

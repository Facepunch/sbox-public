using Borealis.Gpu;
using Borealis.Source2.Shaders;
using System.Runtime.InteropServices;

namespace Borealis.Backend;

/// <summary>
/// The resident cull (<see cref="BackendDevice.GpuDrivenResidentCull"/>, docs/reference/borealis-gpu-driven.md): in setup,
/// after every view has prepared, one dispatch of <c>borealis_resident_cull_cs</c> culls every view's resident plans, a
/// thread per entry looping over the views that draw its plan, into a region per view and plan laid out as a call's own.
/// The call of that kind in that view then draws from it without culling. Which plans a view draws is what its calls drew
/// last frame (<see cref="ResidentCullState"/>); a call the setup didn't cull for culls itself, as before.
/// </summary>
internal sealed partial class BackendContext
{
	/// <summary>One view's cull of one plan, made in setup; the call of the plan's kind in that view draws from it, once.</summary>
	internal sealed class ResidentAssignment
	{
		public GpuDrivenPlan Plan;
		public object View;

		/// <summary>Its view's index in the setup's views, while the setup writes it.</summary>
		public int ViewIndex;
		public BackendDevice.VisibilityView Visibility;
		public BufferHandle Stamps;
		public CulledRegion Region;
		public int Fragments;
		public bool Lists;

		/// <summary>
		/// Forward's lists from its plan's records (<see cref="GpuDrivenPlan.IdsFrom"/>): the same view's prepass is culled in the
		/// same dispatch, so it lists what that prepass draws ids for instead of reading its stamps after it.
		/// </summary>
		public bool FromPrepass;

		/// <summary>
		/// A prepass whose culling stamps its fragments for its forward pass to read: not where forward lists from its plan's
		/// records (a forward pass that culls its own without stamps draws its fragments whole).
		/// </summary>
		public bool WritesStamps;
		public int Taken;

		/// <summary>A prepass culled in two phases (<see cref="BackendDevice.HiZOcclusion"/>): its region has a Hi-Z tail.</summary>
		public bool HiZ;
	}

	readonly List<GpuDrivenPlan.CallKey> residentKeys = new();
	readonly List<(int View, GpuDrivenPlan Plan, bool Lists, BackendDevice.VisibilityView Visibility)> residentFound = new();
	readonly Dictionary<GpuDrivenPlan, GpuDrivenPlan> residentPrepasses = new();
	readonly HashSet<uint> residentIds = new();
	readonly List<ResidentAssignment> residentWork = new();
	readonly List<GpuDrivenPlan> residentItems = new();
	readonly List<ResidentAssignment> residentOrdered = new();
	readonly List<List<ResidentAssignment>> residentGroups = new();
	readonly List<GpuDrivenPlan> residentExpands = new();
	readonly HashSet<BufferHandle> residentStamps = new();
	ShaderAttributes residentAttributes;
	byte[] residentConstants;

	const int ResidentItemBytes = ResidentCullItem.Size;
	const int ResidentViewBytes = ResidentCullView.Size;
	const int ResidentHeaderBytes = ResidentMeshHeader.Size;

	/// <summary>Bytes of a plan's survivor flags in a view's region: a word per batch, which the resident cull sets for the draw counters.</summary>
	static int SeenBytes( GpuDrivenPlan plan ) => (plan.Batches * 4 + 15) & ~15;

	/// <summary>
	/// Bytes of a view's region for a plan, as <see cref="DrawGpuCulled"/> lays out a call's, and its offsets. After the view
	/// record a word per batch is cleared for the cull to set where the batch has a survivor (<see cref="SeenBytes"/>), so
	/// that its stats count each batch once; the multi-draws follow.
	/// </summary>
	static (int Bytes, int Runs, int Listed, int Instances, int FragmentWords, int Commands, int HiZ) ResidentRegion( GpuDrivenPlan plan, bool ids, bool lists, bool hiz = false )
	{
		var fragments = plan.Fragments;
		var slots = fragments * (plan.Key.Meshlets ? 1 + 2 * (int)MeshletChunks : lists ? 2 : 1);
		var runs = CullViewBytes + SeenBytes( plan );
		var listed = (runs + plan.Runs.Length * MultiDraw.Size + 15) & ~15;
		var instances = listed + (lists ? ListedArguments.Size + fragments * ListedFragment.Size : 0);
		var fragmentWords = instances + slots * GpuDrivenTables.InstanceStride;
		var commands = (fragmentWords + (ids ? slots * 4 : 0) + 15) & ~15;
		var bytes = (commands + slots * GpuDrivenTables.CommandStride + 15) & ~15;
		var tail = hiz ? bytes : 0;
		if ( hiz ) bytes += HiZTailBytes( plan.Runs.Length * MultiDraw.Size, fragments );
		return (bytes, runs, listed, instances, fragmentWords, commands, tail);
	}

	/// <summary>
	/// Bytes of a region's two-phase Hi-Z tail (<see cref="BackendDevice.HiZOcclusion"/>), at a multiple of 16: the second
	/// phase's multi-draws (as many as the first's, <paramref name="runBytes"/>), its listed fragments' arguments, then the
	/// candidates' arguments, their count and the second phase's occluded count (32 bytes) and a 64-byte candidate per fragment.
	/// </summary>
	static int HiZTailBytes( int runBytes, int fragments ) => ((runBytes + 15) & ~15) + ListedArguments.Size + HiZCandidates.Size + fragments * HiZCandidate.Size;

	/// <summary>
	/// Write a region's Hi-Z tail at <paramref name="tail"/>: the second phase's multi-draws as the first's <paramref name="runs"/>
	/// (first slots, zeroed counts), and both arguments with no groups. Its offsets: multi-draws, listed fragments, candidates.
	/// </summary>
	static (int Runs2, int Listed2, int Candidates) WriteHiZTail( Span<byte> region, int tail, ReadOnlySpan<uint> runs )
	{
		var runs2 = tail;
		var listed2 = tail + ((runs.Length * 4 + 15) & ~15);
		var candidates = listed2 + ListedArguments.Size;
		runs.CopyTo( MemoryMarshal.Cast<byte, uint>( region.Slice( runs2, runs.Length * 4 ) ) );
		MemoryMarshal.Write( region.Slice( listed2, ListedArguments.Size ), new ListedArguments { GroupsY = 1, GroupsZ = 1 } );
		MemoryMarshal.Write( region.Slice( candidates, HiZCandidates.Size ), new HiZCandidates { GroupsY = 1, GroupsZ = 1 } );
		return (runs2, listed2, candidates);
	}

	/// <summary>Whether this call's culls count what they let through into the device's <see cref="CullStats"/>.</summary>
	bool CountsOnDevice( in GpuDrivenCull cull ) => cull.StatsSlot > 0 && device.CullStats is not null;

	/// <summary>The view record's flags for the culls' counting: bit 4 counts, bit 5 a depth pass (no colour), bits 8 to 15 the slot.</summary>
	uint StatsFlags( in GpuDrivenCull cull, bool noColor ) => CountsOnDevice( cull ) ? 16u | (noColor ? 32u : 0) | (uint)cull.StatsSlot << 8 : 0;

	/// <summary>
	/// A view record (<see cref="CullViewRecord"/>): the six planes, camera and size threshold, flags (shadow and its
	/// resident filter, whether resident meshes skip the size test, the filter's settled and restless stamps), LOD camera
	/// and LOD width, and how fragments pick generated levels (<see cref="LodMode"/>).
	/// </summary>
	void WriteCullView( in GpuDrivenCull cull, Span<byte> bytes, bool noColor, uint hizBits = uint.MaxValue, uint hizCandidates = 0 )
	{
		ref var view = ref MemoryMarshal.AsRef<CullViewRecord>( bytes[..CullViewRecord.Size] );
		var planes = view.Planes;
		(planes[0], planes[1], planes[2], planes[3], planes[4], planes[5]) = (cull.Left, cull.Right, cull.Bottom, cull.Top, cull.Near, cull.Far);
		view.CameraAndThreshold = cull.CameraAndThreshold;
		view.Flags = (cull.Shadow ? 1u : 0u) | (uint)cull.Filter << 1 | StatsFlags( cull, noColor );
		view.KeepsSmallMeshes = cull.KeepsSmallMeshes ? 1u : 0u;
		view.SettledStamp = cull.Filter != ResidentFilter.None ? cull.SettledStamp : 0;
		view.RestlessStamp = cull.Filter != ResidentFilter.None ? cull.RestlessStamp : 0;
		view.LodCamera = cull.LodCamera;
		view.LodWidth = cull.LodWidth;
		view.LodTexels = cull.LodTexels ? 1u : 0u;
		(view.LodMode, view.LodUnits) = LodMode( cull );

		// Two-phase Hi-Z occlusion (bit 3): last frame's visible bits' bindless slot and the candidates' byte offset in the
		// region's buffer, in the words a resident filter (never with it) uses
		if ( hizCandidates != 0 )
		{
			view.Flags |= 8;
			(view.SettledStamp, view.RestlessStamp) = (hizBits, hizCandidates);
		}
		System.Threading.Interlocked.Increment( ref device.LodModes[view.LodMode & 3] );
	}

	/// <summary>
	/// How a view's culls pick fragments' generated levels (<c>PickLod</c>): 0 not at all, 1 by distance, 2 by texels, 3 by
	/// distance with each fragment's level kept in the view's slice (bits 8-15) of <see cref="Lods.LodStore.State"/>; and
	/// the error's units: pixels per unit at distance 1, or texels per unit, over <see cref="BackendDevice.LodPixels"/>.
	/// Every call of a view picks the same way, so its prepass and forward draw the same triangles.
	/// </summary>
	(uint Mode, float Scale) LodMode( in GpuDrivenCull cull )
	{
		if ( !cull.GeneratedLods || device.LodStore is null ) return (0, 0);
		var limit = device.LodPixels;
		if ( cull.LodTexels ) return cull.LodWidth > 0 ? (2u, cull.LodWidth / limit) : (0, 0);
		if ( !(cull.LodCamera.w > 0) || !(cull.LodWidth > 0) ) return (0, 0);
		var scale = cull.LodWidth * 0.5f / (cull.LodCamera.w * limit);
		var slice = cull.View is not null && device.ExistingVisibility( cull.View ) is not null ? device.LodStore.SliceFor( cull.View ) : -1;
		return (slice >= 0 ? 3u | (uint)slice << 8 : 1u, scale);
	}

	/// <summary>
	/// In setup, after every view has prepared: cull each view's resident plans its calls drew last frame, in one dispatch,
	/// for the calls to draw from. A forward pass that compacts lists what its prepass draws ids for, from its plan's records;
	/// one whose records don't name its prepass's plan reads the stamps its prepass's culling wrote, in a second dispatch.
	/// </summary>
	internal override void CullResident( ReadOnlySpan<GpuDrivenCull> views )
	{
		if ( !GpuDriven || recording is null || !UsesPlans || !device.GpuDrivenResidentCull || device.ResidentCullShader is not { } shader ) return;

		var frame = device.GpuDrivenFrame;
		var culls = device.ResidentCulls;
		culls.BeginFrame( frame, Gpu );

		// Each view's plans of the kinds its calls drew
		var r = recording;
		CloseScope( flushClears: false );
		residentExpands.Clear();
		residentWork.Clear();
		residentFound.Clear();
		for ( int v = 0; v < views.Length; v++ )
		{
			ref readonly var cull = ref views[v];
			if ( cull.Plan is null || cull.View is null || cull.PlanBatches <= 0 ) continue;

			residentKeys.Clear();
			culls.Keys( cull.View, cull.Plan, frame, residentKeys );
			if ( residentKeys.Count == 0 ) continue;

			var plans = device.PlansFor( cull.Plan );
			foreach ( var key in residentKeys )
			{
				var plan = plans.Find( key, cull.PlanVersion, cull.PlanBatches, device.GpuDrivenTables.VisibilityEpoch );
				if ( plan is null || plan.Fragments == 0 || plan.Runs.Length == 0 ) continue;

				// The visibility-driven pass's state, cleared now rather than before the segments: the prepass's culling stamps it
				BackendDevice.VisibilityView visibility = null;
				if ( key.Ids || key.Lists )
				{
					visibility = device.ExistingVisibility( cull.View );
					if ( visibility is null ) continue;
					BeginGpuDrivenView( cull.View );
					if ( visibility.ClearedFrame != frame ) continue;
				}

				residentFound.Add( (v, plan, key.Lists, visibility) );
			}
		}

		// A forward plan has one entry map for all its views. Prefer the pairing whose map already exists.
		if ( device.GpuDrivenResidentListsFromIds )
		{
			foreach ( var (v, plan, lists, _) in residentFound )
			{
				if ( !lists || PrepassOf( v ) is not { } prepass ) continue;
				if ( ReferenceEquals( plan.IdsFrom, prepass ) ) residentPrepasses[plan] = prepass;
				else residentPrepasses.TryAdd( plan, prepass );
			}
		}

		foreach ( var (v, plan, lists, visibility) in residentFound )
		{
			residentPrepasses.TryGetValue( plan, out var prepass );
			if ( !PlanRecords( plan, prepass ) ) continue;
			var assignment = culls.Add( views[v].View, plan, frame );
			if ( assignment is null ) continue;
			assignment.ViewIndex = v;
			assignment.Visibility = visibility;
			assignment.Stamps = visibility?.Stamps ?? default;
			assignment.Lists = lists;
			assignment.FromPrepass = prepass is not null && ReferenceEquals( prepass, PrepassOf( v ) );
			assignment.WritesStamps = false;
			if ( assignment.FromPrepass ) System.Threading.Interlocked.Increment( ref device.ResidentCullListsFromIds );
			assignment.Fragments = plan.Fragments;
			residentWork.Add( assignment );
		}

		// Not kept past the cull: it names views' visibility, which a context kept with its render system would hold on to
		residentFound.Clear();
		residentPrepasses.Clear();

		foreach ( var work in residentWork )
		{
			if ( work.Lists || !work.Plan.Key.Ids || work.Visibility is null ) continue;
			work.WritesStamps = true;
			foreach ( var other in residentWork )
			{
				if ( !other.Lists || !ReferenceEquals( other.View, work.View ) ) continue;
				work.WritesStamps = !other.FromPrepass;
				if ( work.WritesStamps ) break;
			}
		}

		foreach ( var work in residentWork ) work.HiZ = !work.Lists && work.Plan.Key.Ids && HiZFor( views[work.ViewIndex], work.Visibility, work.Plan.Fragments );

		if ( residentWork.Count == 0 ) return;

		// Dispatches: those that read no stamps, then those that do (forward lists their prepass's culling stamped); each binds
		// one view's stamps, and culls that touch none join whichever
		foreach ( var group in residentGroups ) group.Clear();
		var groups = 0;
		for ( int phase = 0; phase < 2; phase++ )
		{
			var phaseStart = groups;
			foreach ( var work in residentWork )
			{
				if ( (work.Lists && !work.FromPrepass) != (phase == 1) ) continue;
				var found = -1;
				for ( int g = phaseStart; g < groups && found < 0; g++ )
				{
					var other = GroupVisibility( residentGroups[g] );
					var stamps = StampsOf( work );
					if ( stamps is null || other is null || ReferenceEquals( other, stamps ) ) found = g;
				}

				if ( found < 0 )
				{
					found = groups++;
					if ( residentGroups.Count < groups ) residentGroups.Add( new() );
				}

				residentGroups[found].Add( work );
			}
		}

		var timer = device.PassTimer;
		var scope = timer?.Begin( r.List, "Resident cull", PassTimer.ScopeKind.Own, count: false ) ?? -1;
		r.List.Marker( "Resident cull" );

		// Only the copied prefixes feed expansion; the expansion writes the remaining records for the cull.
		foreach ( var plan in residentExpands )
		{
			r.List.Barrier( new BufferBarrier
			{
				Buffer = plan.Records,
				Size = plan.EntryRecords,
				SourceStage = PipelineStage.Transfer,
				SourceAccess = MemoryAccess.TransferWrite,
				DestinationStage = PipelineStage.ComputeShader,
				DestinationAccess = MemoryAccess.ShaderRead,
			} );
		}
		foreach ( var plan in residentExpands ) ExpandResident( shader, plan );
		foreach ( var plan in residentExpands )
			ResidentBufferReady( r.List, plan.Records, PipelineStage.ComputeShader, MemoryAccess.ShaderRead, plan.EntryRecords );

		// The frame regions are fresh; only persistent state needs ordering against earlier culls and compactions.
		const PipelineStage compute = PipelineStage.ComputeShader;
		const MemoryAccess shaderReadWrite = MemoryAccess.ShaderRead | MemoryAccess.ShaderWrite;
		var levels = device.LodStore?.State ?? default;
		var stats = device.CullStats?.Buffer ?? default;
		if ( levels.IsValid ) ResidentBufferReady( r.List, levels, compute, shaderReadWrite );
		if ( stats.IsValid ) ResidentBufferReady( r.List, stats, compute, shaderReadWrite );
		residentStamps.Clear();
		foreach ( var work in residentWork )
		{
			if ( StampsOf( work ) is not null && work.Stamps.IsValid && residentStamps.Add( work.Stamps ) )
				ResidentBufferReady( r.List, work.Stamps, compute, shaderReadWrite );
			if ( work.HiZ )
			{
				var bits = work.Visibility.HiZVisible[work.Visibility.HiZCurrent ^ 1];
				if ( residentStamps.Add( bits ) ) ResidentBufferReady( r.List, bits, compute, MemoryAccess.ShaderRead );
			}
		}

		for ( int g = 0; g < groups; g++ )
		{
			if ( g > 0 && ReadsStamps( residentGroups[g][0] ) && !ReadsStamps( residentGroups[g - 1][0] ) )
			{
				// Publish the prepass stamps to forward's culls, along with any levels they keep.
				PublishResidentStamps( r.List, MemoryAccess.ShaderRead );
				if ( levels.IsValid ) ResidentBufferReady( r.List, levels, compute, shaderReadWrite );
			}
			DispatchResident( shader, residentGroups[g], views, frame );
		}

		// The later lists read indirect arguments and instance records, not index data, from these regions.
		foreach ( var work in residentWork )
		{
			var key = work.Plan.Key;
			var stages = PipelineStage.DrawIndirect | PipelineStage.VertexShader;
			if ( key.MeshTasks ) stages |= PipelineStage.TaskShader | PipelineStage.MeshShader;
			var access = MemoryAccess.IndirectRead | MemoryAccess.ShaderRead;
			if ( key.Ids || key.Lists || key.Meshlets )
			{
				stages |= compute;
				access |= MemoryAccess.ShaderWrite;
			}
			var bytes = ResidentRegion( work.Plan, key.Ids, work.Lists || key.Meshlets, work.HiZ ).Bytes;
			ResidentBufferReady( r.List, work.Region.Buffer, stages, access, work.Region.Offset, bytes );
		}
		PublishResidentStamps( r.List, shaderReadWrite );
		if ( levels.IsValid ) ResidentBufferReady( r.List, levels, compute, shaderReadWrite );
		if ( stats.IsValid ) ResidentBufferReady( r.List, stats, compute, shaderReadWrite );
		r.List.Marker( "end Resident cull" );
		timer?.End( r.List, scope );
	}

	static void ResidentBufferReady( CommandList list, BufferHandle buffer, PipelineStage stages, MemoryAccess access, long offset = 0, long size = 0 )
	{
		list.Barrier( new BufferBarrier
		{
			Buffer = buffer,
			Offset = offset,
			Size = size,
			SourceStage = PipelineStage.ComputeShader,
			SourceAccess = MemoryAccess.ShaderWrite,
			DestinationStage = stages,
			DestinationAccess = access,
		} );
	}

	void PublishResidentStamps( CommandList list, MemoryAccess access )
	{
		residentStamps.Clear();
		foreach ( var work in residentWork )
		{
			if ( work.WritesStamps && work.Stamps.IsValid && residentStamps.Add( work.Stamps ) )
				ResidentBufferReady( list, work.Stamps, PipelineStage.ComputeShader, access );
		}
	}

	/// <summary>
	/// A plan's multi-draws' first command slots and zeroed counts, as a view's region holds them: a compacted one's slots
	/// follow the plan's own fragments (<see cref="ResidentRun"/>).
	/// </summary>
	static uint[] ResidentRunWords( GpuDrivenPlan plan )
	{
		var words = new uint[plan.Runs.Length * 2];
		for ( int i = 0; i < plan.Runs.Length; i++ )
		{
			words[i * 2] = (uint)ResidentRun( plan, i, default ).First;
			if ( plan.Runs[i].Count == TaskTail ) words[i * 2 + 1] = 1;
		}
		return words;
	}

	/// <summary>A plan's multi-draw as a view's region draws it: a compacted one from <paramref name="compacted"/>, after the plan's fragments.</summary>
	static (int Bucket, BufferHandle Page, IndexFormat Format, int First, int Count) ResidentRun( GpuDrivenPlan plan, int i, BufferHandle compacted, BufferHandle compacted16 = default )
	{
		var run = plan.Runs[i];
		var page = run.Format == IndexFormat.UInt16 && compacted16.IsValid ? compacted16 : compacted;
		return plan.Compacted[i] ? run with { Page = page, First = run.First - plan.CompactBase + plan.Fragments } : run;
	}

	static bool ReadsStamps( ResidentAssignment work ) => work.Lists && !work.FromPrepass;

	/// <summary>The prepass plan this setup culls for view <paramref name="view"/> with ids, or null.</summary>
	GpuDrivenPlan PrepassOf( int view )
	{
		foreach ( var (v, plan, lists, _) in residentFound )
		{
			if ( v == view && !lists && plan.Key.Ids ) return plan;
		}

		return null;
	}

	/// <summary>The visibility whose stamps a cull writes or reads, or null for one that touches none.</summary>
	static BackendDevice.VisibilityView StampsOf( ResidentAssignment work ) => work.WritesStamps || ReadsStamps( work ) ? work.Visibility : null;

	/// <summary>The visibility whose stamps a group's dispatch binds.</summary>
	static BackendDevice.VisibilityView GroupVisibility( List<ResidentAssignment> group )
	{
		foreach ( var work in group )
		{
			if ( StampsOf( work ) is { } stamps ) return stamps;
		}

		return null;
	}

	/// <summary>One dispatch over a group's plans: the items, the views, and each view's region, in one allocation.</summary>
	void DispatchResident( ShaderFile shader, List<ResidentAssignment> group, ReadOnlySpan<GpuDrivenCull> views, uint frame )
	{
		var tables = device.GpuDrivenTables;

		// Items by plan, in order of first appearance; a plan's views after one another
		residentItems.Clear();
		foreach ( var work in group )
		{
			if ( !residentItems.Contains( work.Plan ) ) residentItems.Add( work.Plan );
		}

		residentOrdered.Clear();
		foreach ( var plan in residentItems )
		{
			foreach ( var work in group )
			{
				if ( ReferenceEquals( work.Plan, plan ) ) residentOrdered.Add( work );
			}
		}

		group.Clear();
		group.AddRange( residentOrdered );

		var headerBytes = (residentItems.Count * ResidentItemBytes + group.Count * ResidentViewBytes + 15) & ~15;
		var bytes = headerBytes;
		var stagedBytes = headerBytes;
		// Keep the host mapping for diagnostics that read Hi-Z's candidate counters.
		var deviceRegion = DeviceCullRegions && !device.HiZOcclusion;
		foreach ( var work in group )
		{
			var ids = work.Plan.Key.Ids;
			var listing = work.Lists || work.Plan.Key.Meshlets && (work.Visibility is not null || !ids);
			var layout = ResidentRegion( work.Plan, ids, listing, work.HiZ );
			bytes += layout.Bytes;
			stagedBytes += layout.Listed + (listing ? ListedArguments.Size : 0);
		}

		var list = recording.List;
		var (buffer, slot, offset) = deviceRegion ? tables.AllocateDeviceCull( bytes ) : tables.Allocate( bytes );
		var upload = deviceRegion ? list.AllocateUpload( stagedBytes, 16 ) : default;
		var data = deviceRegion ? upload.Data : Gpu.Map( buffer ).Slice( (int)offset, bytes );
		var items = MemoryMarshal.Cast<byte, ResidentCullItem>( data[..(residentItems.Count * ResidentItemBytes)] );
		var viewRecords = MemoryMarshal.Cast<byte, ResidentCullView>( data.Slice( residentItems.Count * ResidentItemBytes, group.Count * ResidentViewBytes ) );
		var visibility = GroupVisibility( group );

		var at = headerBytes;
		var stagedAt = headerBytes;
		var entries = 0u;
		var item = -1;
		for ( int w = 0; w < group.Count; w++ )
		{
			var work = group[w];
			var plan = work.Plan;
			var ids = plan.Key.Ids;
			if ( item < 0 || !ReferenceEquals( residentItems[item], plan ) )
			{
				item++;
				entries = (entries + 63) & ~63u;
				items[item] = new ResidentCullItem { FirstEntry = entries, Entries = (uint)plan.Entries, Records = (uint)plan.RecordsSlot, EntryMap = (uint)plan.EntryMap, FirstView = (uint)w, EntryRecords = (uint)plan.EntryRecords, Headers = (uint)plan.Headers };
				entries += (uint)plan.Entries;
			}

			items[item].Views++;

			// The view's region, as a call lays out its own: a prepass's meshlets in its view's buffers, a shadow call's in the
			// shadow views' (it has no visibility)
			var meshlets = plan.Key.Meshlets && (work.Visibility is not null || !ids);
			var listing = work.Lists || meshlets;
			var layout = ResidentRegion( plan, ids, listing, work.HiZ );
			var inputBytes = layout.Listed + (listing ? ListedArguments.Size : 0);
			var region = deviceRegion ? data.Slice( stagedAt, inputBytes ) : data.Slice( at, layout.Bytes );
			var (compacted, compacted16) = meshlets ? MeshletCompacted( work.Visibility ) : work.Visibility is { } own ? (own.Compacted, default) : default;
			var regionOffset = offset + at;

			var runWords = plan.ResidentRunWords ??= ResidentRunWords( plan );
			runWords.CopyTo( MemoryMarshal.Cast<byte, uint>( region.Slice( layout.Runs, runWords.Length * 4 ) ) );
			var hiz = work.HiZ ? WriteHiZTail( region, layout.HiZ, runWords ) : default;
			WriteCullView( views[work.ViewIndex], region, plan.Key.NoColor, work.HiZ ? HiZBitsSlot( work.Visibility ) : uint.MaxValue, work.HiZ ? (uint)(regionOffset + hiz.Candidates) : 0 );
			region.Slice( CullViewBytes, SeenBytes( plan ) ).Clear();

			if ( listing )
			{
				MemoryMarshal.Write( region.Slice( layout.Listed, ListedArguments.Size ), new ListedArguments { GroupsX = 0, GroupsY = 1, GroupsZ = 1, Entries = 0 } );
			}

			var firstInstance = (uint)((regionOffset + layout.Instances) / GpuDrivenTables.InstanceStride);
			work.Region = new CulledRegion( buffer, slot, regionOffset, layout.Runs, layout.Listed, layout.Instances, layout.FragmentWords, layout.Commands, firstInstance, listing ? compacted : default, meshlets ? compacted16 : default, hiz.Runs2, hiz.Listed2, hiz.Candidates );
			viewRecords[w] = new ResidentCullView
			{
				View = (uint)regionOffset,
				Instances = (uint)(regionOffset + layout.Instances),
				Commands = (uint)(regionOffset + layout.Commands),
				Runs = (uint)(regionOffset + layout.Runs),
				FirstInstance = firstInstance,
				FragmentWords = (uint)(regionOffset + layout.FragmentWords) - firstInstance * 4,
				Listed = listing ? (uint)(regionOffset + layout.Listed) : 0,
				Flags = (work.FromPrepass ? 1u : 0u) | (work.WritesStamps ? 2u : 0u) | (meshlets ? 4u : 0u),
			};
			if ( deviceRegion )
			{
				list.CopyBuffer( upload.Buffer, upload.Offset + stagedAt, buffer, regionOffset, inputBytes );
				stagedAt += inputBytes;
			}
			at += layout.Bytes;
		}

		var slots = tables.Slots;
		var lodTable = device.LodStore?.Table ?? (-1, 0u);
		residentConstants ??= new byte[Marshal.SizeOf<ResidentCullConstants>()];
		MemoryMarshal.Write( residentConstants, new ResidentCullConstants
		{
			Items = (uint)offset,
			ItemCount = (uint)residentItems.Count,
			Views = (uint)(offset + residentItems.Count * ResidentItemBytes),
			Entries = entries,
			Fragments = (uint)tables.FragmentSlot,
			Draws = (uint)slots.Draws,
			Frame = frame,
			FrameSlot = (uint)slot,
			MeshletRuns = device.MeshletStore is { } store ? (uint)store.RunSlotCovering( tables.DrawRecords ) : uint.MaxValue,
			LodTable = lodTable.Slot >= 0 ? (uint)lodTable.Slot : uint.MaxValue,
			LodCount = lodTable.Records,
		} );
		if ( deviceRegion )
		{
			list.CopyBuffer( upload.Buffer, upload.Offset, buffer, offset, headerBytes );
			BarrierCullInputs( list );
		}
		DispatchResidentShader( shader, buffer, visibility?.Stamps ?? default, (int)entries );
		device.CountResidentCull( residentItems.Count, group.Count, entries );
	}

	/// <summary>
	/// A new plan's entry records and batch headers, from the fragment and draw tables in host memory into its records in
	/// video memory: once, so the culls read only video memory (<see cref="ResidentCullEntryRecord"/>).
	/// </summary>
	void ExpandResident( ShaderFile shader, GpuDrivenPlan plan )
	{
		var tables = device.GpuDrivenTables;
		residentConstants ??= new byte[Marshal.SizeOf<ResidentCullConstants>()];
		MemoryMarshal.Write( residentConstants, new ResidentCullConstants
		{
			Entries = (uint)plan.Entries,
			Fragments = (uint)tables.FragmentSlot,
			Draws = (uint)tables.Slots.Draws,
			Expand = (uint)plan.EntryRecords,
			ExpandMap = (uint)plan.EntryMap,
			ExpandHeaders = (uint)plan.Headers,
		} );
		DispatchResidentShader( shader, plan.Records, default, plan.Entries );
		plan.EntryRecordsWritten = true;
	}

	const string FrameAttribute = "BorealisCullFrame";
	const string StampsAttribute = "BorealisCullStamps";
	const string LevelsAttribute = "BorealisCullLevels";
	const string StatsAttribute = "BorealisCullStats";
	const string ConstantsAttribute = "BorealisResidentCull_t";
	ShaderFile residentShader;
	ShaderVariant residentVariant;
	PipelineHandle residentPipeline;
	bool residentGeneric;

	/// <summary>
	/// One dispatch of the resident cull's shader over <paramref name="threads"/> threads: its pipeline made once, and its
	/// transient set filled straight from the frame's region, the stamps (a placeholder for none) and <see cref="residentConstants"/>,
	/// without the generic binding's attribute lookups (which a variant with material constants still takes).
	/// </summary>
	void DispatchResidentShader( ShaderFile shader, BufferHandle frameBuffer, BufferHandle stamps, int threads )
	{
		cullDrawer ??= new ShaderDrawer( device );
		residentAttributes ??= new ShaderAttributes();
		var levels = device.LodStore?.State ?? default;
		var stats = device.CullStats?.Buffer ?? default;
		if ( !ReferenceEquals( residentShader, shader ) )
		{
			residentShader = shader;
			residentVariant = cullDrawer.Variant( shader, residentAttributes );
			residentPipeline = residentVariant?.Compute?.Bytecode is null ? default : cullDrawer.ReflectedComputePipeline( residentVariant );
			residentGeneric = residentVariant is not null && System.Linq.Enumerable.Any( residentVariant.Resources, r => r.Kind == ShaderResourceKind.MaterialConstants );
			if ( residentPipeline.IsValid ) HandFilledConstants.CheckSize( residentVariant.Compute, ConstantsAttribute, residentConstants.Length );
		}

		if ( residentGeneric )
		{
			residentAttributes.Clear();
			residentAttributes.Set( FrameAttribute, frameBuffer );
			if ( stamps.IsValid ) residentAttributes.Set( StampsAttribute, stamps );
			if ( levels.IsValid ) residentAttributes.Set( LevelsAttribute, levels );
			if ( stats.IsValid ) residentAttributes.Set( StatsAttribute, stats );
			residentAttributes.SetData( ConstantsAttribute, residentConstants );
			cullDrawer.Dispatch( recording.List, shader, residentAttributes, threads, 1, 1 );
			return;
		}

		if ( !residentPipeline.IsValid ) return;

		var variant = residentVariant;
		var resources = variant.Resources;
		var list = recording.List;
		list.SetPipeline( residentPipeline );
		Span<Binding> bindings = stackalloc Binding[16];
		for ( int set = 0; set < variant.Sets.Length; set++ )
		{
			if ( !variant.Sets[set].IsValid ) continue;

			var count = 0;
			for ( int i = 0; i < resources.Count; i++ )
			{
				var resource = resources[i];
				if ( resource.Set != set ) continue;
				if ( count == bindings.Length ) throw new InvalidOperationException( $"{variant} binds more than {bindings.Length} resources in set {set}" );

				var name = resource.Name;
				if ( resource.Kind == ShaderResourceKind.ConstantBuffer && name == ConstantsAttribute )
				{
					var size = (residentConstants.Length + 15) & ~15;
					var allocation = list.AllocateUpload( size, Gpu.Capabilities.UniformAlignment );
					allocation.Data[..size].Clear();
					residentConstants.CopyTo( allocation.Data );
					bindings[count++] = Binding.Uniform( resource.Binding, allocation.Buffer, allocation.Offset, size );
				}
				else if ( resource.Kind == ShaderResourceKind.Buffer && name == FrameAttribute ) bindings[count++] = Binding.Storage( resource.Binding, frameBuffer );
				else if ( resource.Kind == ShaderResourceKind.Buffer && name == StampsAttribute && stamps.IsValid ) bindings[count++] = Binding.Storage( resource.Binding, stamps );
				else if ( resource.Kind == ShaderResourceKind.Buffer && name == LevelsAttribute && levels.IsValid ) bindings[count++] = Binding.Storage( resource.Binding, levels );
				else if ( resource.Kind == ShaderResourceKind.Buffer && name == StatsAttribute && stats.IsValid ) bindings[count++] = Binding.Storage( resource.Binding, stats );
				else bindings[count++] = device.Defaults.For( resource );
			}

			list.SetBindings( set, variant.Sets[set], bindings[..count] );
		}

		var groupSize = Math.Max( variant.Compute.Bytecode.ThreadGroupSize.Item1, 1 );
		list.Dispatch( (threads + groupSize - 1) / groupSize, 1, 1 );
	}

	/// <summary>
	/// A plan's batch records, entry map and a resident filter's moved words in a buffer of its own, written the first time a
	/// setup culls it (plans never change once made); false when there's nothing to cull. A forward plan that compacts marks
	/// the entries <paramref name="prepass"/> draws ids for (<see cref="GpuDrivenPlan.IdsFrom"/>).
	/// </summary>
	bool PlanRecords( GpuDrivenPlan plan, GpuDrivenPlan prepass )
	{
		// Written again for a forward plan whose view's prepass now draws with another plan (the old buffer is let go)
		if ( plan.Records.IsValid && !(plan.Key.Lists && prepass is not null && !ReferenceEquals( plan.IdsFrom, prepass )) )
		{
			// Copied by a setup that then culled nothing: its entry records are still to write
			if ( !plan.EntryRecordsWritten && !residentExpands.Contains( plan ) ) residentExpands.Add( plan );
			return true;
		}

		if ( plan.Records.IsValid ) device.ResidentCulls.Release( plan.Records, Gpu );

		// The prepass's batches that draw ids, by their first visibility fragment: forward's batch of the same owner, LOD and
		// draw has the same, and lists its fragments the prepass keeps, which the prepass keeps as forward does
		residentIds.Clear();
		if ( plan.Key.Lists && prepass is not null )
		{
			var prepassRecords = prepass.BatchRecords;
			foreach ( var batch in MemoryMarshal.Cast<uint, BatchRecord>( prepassRecords.AsSpan() ) )
			{
				if ( (batch.Flags & 1) != 0 && batch.FirstVisibility != GpuDrivenTables.NoVisibility ) residentIds.Add( batch.FirstVisibility );
			}

			plan.IdsFrom = prepass;
		}

		var records = plan.BatchRecords;
		var batches = records.Length / (CullBatchBytes / 4);
		var entries = 0;
		foreach ( var batch in MemoryMarshal.Cast<uint, BatchRecord>( records.AsSpan() ) ) entries += (int)batch.Count;
		if ( entries == 0 ) return false;

		var mapOffset = records.Length * 4;
		var movedOffset = mapOffset + entries * Marshal.SizeOf<ResidentCullEntry>();
		var entryRecordsOffset = (movedOffset + (plan.Moved?.Length ?? 0) * 4 + 15) & ~15;
		var headersOffset = entryRecordsOffset + entries * Marshal.SizeOf<ResidentCullEntryRecord>();
		var bytes = headersOffset + batches * ResidentHeaderBytes;
		var copied = entryRecordsOffset;
		// Written into the frame's chunk and copied into video memory, before this setup's dispatches
		var buffer = Gpu.CreateBuffer( new BufferInfo { Size = bytes, Usage = BufferUsage.Storage | BufferUsage.CopyDestination, Memory = MemoryLocation.Device, Name = "Borealis resident plan" } );
		var (staging, _, stagingOffset) = device.GpuDrivenTables.Allocate( copied );
		var data = Gpu.Map( staging ).Slice( (int)stagingOffset, copied );
		MemoryMarshal.AsBytes( records.AsSpan() ).CopyTo( data );

		// A resident filter's moved words after the entry map, which each record's sixth word finds
		if ( plan.Moved is { } moved )
		{
			MemoryMarshal.AsBytes( moved.AsSpan() ).CopyTo( data[movedOffset..] );
			var written = MemoryMarshal.Cast<byte, BatchRecord>( data[..mapOffset] );
			for ( int b = 0; b < plan.MovedAt.Length; b++ )
			{
				if ( plan.MovedAt[b] >= 0 ) written[b].Moved = (uint)(movedOffset + plan.MovedAt[b] * 4);
			}
		}
		var map = MemoryMarshal.Cast<byte, ResidentCullEntry>( data[mapOffset..] );
		var at = 0;
		var batchRecords = MemoryMarshal.Cast<uint, BatchRecord>( records.AsSpan() );
		for ( int b = 0; b < batches; b++ )
		{
			var batch = batchRecords[b];
			var ids = (batch.Flags & 2) != 0 && residentIds.Contains( batch.FirstVisibility ) ? ResidentCullEntry.PrepassIds : 0;
			for ( uint e = 0; e < batch.Count; e++ ) map[at++] = new ResidentCullEntry { Batch = (uint)b | ids, Element = e };
		}

		recording.List.CopyBuffer( staging, stagingOffset, buffer, 0, copied );
		plan.RecordsSlot = Gpu.BindlessIndex( buffer );
		plan.Entries = entries;
		plan.EntryMap = mapOffset;
		plan.EntryRecords = entryRecordsOffset;
		plan.Headers = headersOffset;
		plan.EntryRecordsWritten = false;
		residentExpands.Add( plan );
		plan.Records = buffer;
		device.ResidentCulls.Own( plan, buffer );
		return true;
	}

	/// <summary>
	/// Draw a call's resident prefix from what the setup culled for its view and plan, then the view's own batches after it
	/// as a call of their own; false, having drawn nothing, when the setup culled nothing for it.
	/// </summary>
	bool DrawResidentCulled( StringToken shaderMode, ReadOnlySpan<GpuDrivenBatch> batches, ReadOnlySpan<GpuBuffer.IndirectDrawIndexedArguments> args, TransformBuffer transforms, int depthBias, float slopeScaledDepthBias, Span<bool> drawn, in GpuDrivenCull cull, GpuDrivenPlan plan, int prefix )
	{
		if ( !device.GpuDrivenResidentCull || cull.View is null || plan.Drawn.Length != prefix ) return false;

		var (compact, shaders, visibility, ids, lists) = CullState( cull );
		var meshlets = MeshletsFor( cull, ids );
		if ( !compact || ids != plan.Key.Ids || lists != plan.Key.Lists || meshlets != plan.Key.Meshlets || (meshlets && MeshTasksFor( cull, ids )) != plan.Key.MeshTasks ) return false;

		var frame = device.GpuDrivenFrame;
		if ( device.ResidentCulls.Take( cull.View, plan, frame ) is not { } assignment ) return false;
		if ( (ids || lists) && (!ReferenceEquals( assignment.Visibility, visibility ) || assignment.Stamps != visibility.Stamps) ) return false;
		if ( lists && assignment.Region.Compacted != visibility.Compacted ) return false;

		var start = device.TimeDraws ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
		var r = recording;
		var depth = r.Depth is null ? Format.Unknown : BackendDevice.FormatOf( r.Depth );
		var samples = (r.Color ?? r.Depth).Samples;
		var resolver = Resolver( device.MaterialCache );
		if ( ids && shaders.Ids is not null ) idsShaderVariant ??= Variant( shaders.Ids, "Default" );

		gpuDrivenBuckets = plan.Buckets;
		gpuDrivenBucketCount = plan.Buckets.Count;
		try
		{
			cullRuns.Clear();
			for ( int i = 0; i < plan.Runs.Length; i++ ) cullRuns.Add( ResidentRun( plan, i, assignment.Region.Compacted, assignment.Region.Compacted16 ) );

			// A prepass whose own batches follow: their call marks these ids with its own
			markLater = ids && prefix < batches.Length && device.MergedMarks;
			DrawCulledRegion( cull, assignment.Region, 0, compact, shaders, visibility, ids, lists, meshlets, depth, samples, depthBias, slopeScaledDepthBias, resolver, assignment.Fragments, dispatch: false );
			plan.Drawn.AsSpan().CopyTo( drawn );
			TimeGpuDriven( start, drawn[..prefix], assignment.Fragments );
		}
		finally
		{
			markLater = false;
			gpuDrivenBuckets = ownBuckets;
			gpuDrivenBucketCount = 0;
		}

		System.Threading.Interlocked.Increment( ref device.ResidentCullCalls );

		// The view's own batches: a call of their own, with buckets, culling, ids and compaction of its own; the resident
		// ids are marked with theirs, or here when they drew none
		if ( CountsOnDevice( cull ) ) GpuCountedBatches = prefix;
		try
		{
			if ( prefix < batches.Length && EnsureScope() )
				DrawBucketed( shaderMode, batches[prefix..], args, transforms, depthBias, slopeScaledDepthBias, drawn[prefix..], cull with { Plan = null, PlanBatches = 0 }, null, 0 );
		}
		finally
		{
			MarkUnmarked();
		}

		return true;
	}
}

/// <summary>
/// The resident cull's state on a device: the plans each view's calls drew (by view and resident list, with the frame
/// last drawn), this frame's assignments, and the buffers of plans' records.
/// </summary>
internal sealed class ResidentCullState
{
	readonly Dictionary<(object View, object List), List<(GpuDrivenPlan.CallKey Key, uint Frame, uint Kept)>> calls = new();
	readonly Stack<List<(GpuDrivenPlan.CallKey Key, uint Frame, uint Kept)>> spareCalls = new();
	const int MaxSpareCalls = 256;
	readonly Dictionary<(object View, GpuDrivenPlan Plan), BackendContext.ResidentAssignment> assigned = new();
	readonly List<BackendContext.ResidentAssignment> pool = new();
	readonly List<(WeakReference<GpuDrivenPlan> Plan, BufferHandle Buffer)> owned = new();
	int pooled;
	uint frame;

	/// <summary>A call's kind is kept this many frames after it was last drawn.</summary>
	public const uint KeptFrames = 4;

	/// <summary>
	/// A shadow view's call kinds are kept this many frames: a light's maps are drawn when the update budget gets to them,
	/// which for a small light among many can be longer than <see cref="KeptFrames"/>, and a shadow view's kind doesn't change.
	/// </summary>
	public const uint KeptShadowFrames = 120;

	/// <summary>A call drew <paramref name="key"/> of <paramref name="list"/> in <paramref name="view"/>, whose kinds are kept <paramref name="kept"/> frames.</summary>
	public void Seen( object view, object list, in GpuDrivenPlan.CallKey key, uint now, uint kept = KeptFrames )
	{
		lock ( calls )
		{
			if ( !calls.TryGetValue( (view, list), out var keys ) ) calls[(view, list)] = keys = spareCalls.TryPop( out var spare ) ? spare : new();
			for ( int i = 0; i < keys.Count; i++ )
			{
				if ( keys[i].Key != key ) continue;
				keys[i] = (key, now, kept);
				return;
			}

			keys.Add( (key, now, kept) );
		}
	}

	/// <summary>The kinds of call a view drew of a list in the last few frames (<see cref="Seen"/>).</summary>
	public void Keys( object view, object list, uint now, List<GpuDrivenPlan.CallKey> into )
	{
		lock ( calls )
		{
			if ( !calls.TryGetValue( (view, list), out var keys ) ) return;
			foreach ( var (key, seen, kept) in keys )
			{
				if ( now - seen <= kept ) into.Add( key );
			}
		}
	}

	/// <summary>
	/// Before a setup's cull: on a new frame, forget last frame's assignments and calls not drawn for a while, and destroy
	/// the records of plans no call finds any more.
	/// </summary>
	public void BeginFrame( uint now, Device gpu )
	{
		if ( frame == now ) return;
		frame = now;

		// The assignments let go of what they named: kept in the pool, they held views (and so their visibility resources)
		// after the views had gone
		lock ( assigned )
		{
			for ( int i = 0; i < pooled; i++ )
			{
				var assignment = pool[i];
				(assignment.Plan, assignment.View, assignment.Visibility, assignment.Stamps) = (null, null, null, default);
			}

			assigned.Clear();
			pooled = 0;
		}

		lock ( calls )
		{
			// A dictionary may remove entries while it's enumerated; emptied lists are kept for the next view's calls (views come
			// and go as things move about)
			foreach ( var (key, keys) in calls )
			{
				for ( int i = keys.Count - 1; i >= 0; i-- )
				{
					if ( now - keys[i].Frame > keys[i].Kept ) keys.RemoveAt( i );
				}

				if ( keys.Count == 0 && calls.Remove( key ) && spareCalls.Count < MaxSpareCalls ) spareCalls.Push( keys );
			}
		}

		lock ( owned )
		{
			for ( int i = owned.Count - 1; i >= 0; i-- )
			{
				if ( owned[i].Plan.TryGetTarget( out var plan ) && !plan.Retired ) continue;
				gpu.Destroy( owned[i].Buffer );
				owned.RemoveAt( i );
			}
		}
	}

	/// <summary>
	/// A view that won't draw again (its owner disposed it): forget its calls and assignments now rather than when frames
	/// age them out, which at shutdown or with the view's renderer gone never happens.
	/// </summary>
	public void ForgetView( object view )
	{
		lock ( calls )
		{
			// Removing while enumerating is allowed for Dictionary
			foreach ( var key in calls.Keys )
			{
				if ( ReferenceEquals( key.View, view ) ) calls.Remove( key );
			}
		}

		lock ( assigned )
		{
			foreach ( var key in assigned.Keys )
			{
				if ( ReferenceEquals( key.View, view ) ) assigned.Remove( key );
			}

			foreach ( var assignment in pool )
			{
				if ( !ReferenceEquals( assignment.View, view ) ) continue;
				assignment.View = null;
				assignment.Plan = null;
			}
		}
	}

	/// <summary>How many views it keeps calls or assignments of, for tests.</summary>
	internal int HeldViews
	{
		get
		{
			var views = new HashSet<object>( ReferenceEqualityComparer.Instance );
			lock ( calls )
			{
				foreach ( var key in calls.Keys ) views.Add( key.View );
			}

			lock ( assigned )
			{
				foreach ( var key in assigned.Keys ) views.Add( key.View );
				foreach ( var assignment in pool )
				{
					if ( assignment.View is not null ) views.Add( assignment.View );
				}
			}

			return views.Count;
		}
	}

	/// <summary>Destroy every plan's records (the device is going).</summary>
	public void Dispose( Device gpu )
	{
		lock ( owned )
		{
			foreach ( var (_, buffer) in owned ) gpu.Destroy( buffer );
			owned.Clear();
		}
	}

	/// <summary>Destroy a plan's records buffer now (written again): after the frames that read it, as every destroy is.</summary>
	public void Release( BufferHandle buffer, Device gpu )
	{
		lock ( owned )
		{
			for ( int i = 0; i < owned.Count; i++ )
			{
				if ( owned[i].Buffer != buffer ) continue;
				owned.RemoveAt( i );
				gpu.Destroy( buffer );
				return;
			}
		}
	}

	/// <summary>A plan's records buffer, destroyed when the plan is retired or gone.</summary>
	public void Own( GpuDrivenPlan plan, BufferHandle buffer )
	{
		lock ( owned ) owned.Add( (new WeakReference<GpuDrivenPlan>( plan ), buffer) );
	}

	/// <summary>A new assignment of <paramref name="plan"/> in <paramref name="view"/> this frame; null when it has one.</summary>
	public BackendContext.ResidentAssignment Add( object view, GpuDrivenPlan plan, uint now )
	{
		lock ( assigned )
		{
			if ( frame != now || assigned.ContainsKey( (view, plan) ) ) return null;
			if ( pooled == pool.Count ) pool.Add( new() );
			var assignment = pool[pooled++];
			assignment.Plan = plan;
			assignment.View = view;
			assignment.Taken = 0;
			assigned[(view, plan)] = assignment;
			return assignment;
		}
	}

	/// <summary>This frame's assignment of <paramref name="plan"/> in <paramref name="view"/>, for the one call that draws it.</summary>
	public BackendContext.ResidentAssignment Take( object view, GpuDrivenPlan plan, uint now )
	{
		lock ( assigned )
		{
			if ( frame != now || !assigned.TryGetValue( (view, plan), out var assignment ) || assignment.Taken != 0 ) return null;
			assignment.Taken = 1;
			return assignment;
		}
	}
}


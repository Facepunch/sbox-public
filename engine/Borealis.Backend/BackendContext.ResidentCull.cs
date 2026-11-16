using Borealis.Gpu;
using Borealis.Source2.Shaders;
using System.Runtime.InteropServices;

namespace Borealis.Backend;

/// <summary>
/// The resident cull (<see cref="BackendDevice.GpuDrivenResidentCull"/>, docs/managed/borealis-gpu-driven.md): in setup,
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
	}

	readonly List<GpuDrivenPlan.CallKey> residentKeys = new();
	readonly List<(int View, GpuDrivenPlan Plan, bool Lists, BackendDevice.VisibilityView Visibility)> residentFound = new();
	readonly HashSet<uint> residentIds = new();
	readonly List<ResidentAssignment> residentWork = new();
	readonly List<GpuDrivenPlan> residentItems = new();
	readonly List<ResidentAssignment> residentOrdered = new();
	readonly List<List<ResidentAssignment>> residentGroups = new();
	readonly List<GpuDrivenPlan> residentExpands = new();
	ShaderAttributes residentAttributes;
	bool residentRecordsCopied;
	byte[] residentConstants;

	const int ResidentItemBytes = 32;
	const int ResidentViewBytes = 32;
	const int ResidentHeaderBytes = 32;

	/// <summary>Bytes of a view's region for a plan, as <see cref="DrawGpuCulled"/> lays out a call's, and its offsets.</summary>
	static (int Bytes, int Runs, int Listed, int Instances, int FragmentWords, int Commands) ResidentRegion( GpuDrivenPlan plan, bool ids, bool lists )
	{
		var fragments = plan.Fragments;
		var slots = fragments * (plan.Key.Meshlets && ids ? 1 + 2 * (int)MeshletChunks : lists ? 2 : 1);
		var runs = CullViewBytes;
		var listed = (runs + plan.Runs.Length * 8 + 15) & ~15;
		var instances = listed + (lists ? 16 + fragments * 32 : 0);
		var fragmentWords = instances + slots * GpuDrivenTables.InstanceStride;
		var commands = (fragmentWords + (ids ? slots * 4 : 0) + 15) & ~15;
		var bytes = (commands + slots * GpuDrivenTables.CommandStride + 15) & ~15;
		return (bytes, runs, listed, instances, fragmentWords, commands);
	}

	/// <summary>
	/// A view record: the six planes, camera and size threshold, flags (shadow and its resident filter, whether resident
	/// meshes skip the size test, the filter's settled and restless stamps), LOD camera and LOD width (160 bytes).
	/// </summary>
	static void WriteCullView( in GpuDrivenCull cull, Span<byte> bytes )
	{
		var view = MemoryMarshal.Cast<byte, float>( bytes[..CullViewBytes] );
		Span<Vector4> planes = [cull.Left, cull.Right, cull.Bottom, cull.Top, cull.Near, cull.Far, cull.CameraAndThreshold];
		for ( int p = 0; p < planes.Length; p++ )
		{
			view[p * 4] = planes[p].x;
			view[p * 4 + 1] = planes[p].y;
			view[p * 4 + 2] = planes[p].z;
			view[p * 4 + 3] = planes[p].w;
		}

		var viewWords = MemoryMarshal.Cast<byte, uint>( bytes[..CullViewBytes] );
		viewWords[28] = (cull.Shadow ? 1u : 0u) | (uint)cull.Filter << 1;
		viewWords[29] = cull.KeepsSmallMeshes ? 1u : 0u;
		viewWords[30] = cull.Filter != ResidentFilter.None ? cull.SettledStamp : 0;
		viewWords[31] = cull.Filter != ResidentFilter.None ? cull.RestlessStamp : 0;
		view[32] = cull.LodCamera.x;
		view[33] = cull.LodCamera.y;
		view[34] = cull.LodCamera.z;
		view[35] = cull.LodCamera.w;
		view[36] = cull.LodWidth;
		viewWords[37] = cull.LodTexels ? 1u : 0u;
		viewWords[38] = viewWords[39] = 0;
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
		residentRecordsCopied = false;
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
				var plan = plans.Find( key, cull.PlanVersion, cull.PlanBatches );
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

		// Records, a forward pass's naming the prepass plan its view draws ids with, then the assignments
		foreach ( var (v, plan, lists, visibility) in residentFound )
		{
			var prepass = lists && device.GpuDrivenResidentListsFromIds ? PrepassOf( v ) : null;
			if ( !PlanRecords( plan, prepass ) ) continue;
			var assignment = culls.Add( views[v].View, plan, frame );
			if ( assignment is null ) continue;
			assignment.ViewIndex = v;
			assignment.Visibility = visibility;
			assignment.Stamps = visibility?.Stamps ?? default;
			assignment.Lists = lists;
			assignment.FromPrepass = lists && prepass is not null && ReferenceEquals( plan.IdsFrom, prepass );
			if ( assignment.FromPrepass ) System.Threading.Interlocked.Increment( ref device.ResidentCullListsFromIds );
			assignment.Fragments = plan.Fragments;
			residentWork.Add( assignment );
		}

		foreach ( var work in residentWork )
		{
			if ( work.Lists || !work.Plan.Key.Ids || work.Visibility is null ) continue;
			work.WritesStamps = true;
			foreach ( var other in residentWork )
			{
				if ( other.FromPrepass && ReferenceEquals( other.View, work.View ) ) work.WritesStamps = false;
			}
		}

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

		// New plans' records copied in, then their entry records written from the tables
		if ( residentRecordsCopied ) r.List.Barrier( PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.ComputeShader, MemoryAccess.ShaderRead | MemoryAccess.ShaderWrite );
		foreach ( var plan in residentExpands ) ExpandResident( shader, plan );

		// After last frame's culls and compactions of the same stamps
		r.List.Barrier( PipelineStage.ComputeShader, MemoryAccess.ShaderRead | MemoryAccess.ShaderWrite, PipelineStage.ComputeShader, MemoryAccess.ShaderRead | MemoryAccess.ShaderWrite );
		for ( int g = 0; g < groups; g++ )
		{
			if ( g > 0 && ReadsStamps( residentGroups[g][0] ) && !ReadsStamps( residentGroups[g - 1][0] ) )
				r.List.Barrier( PipelineStage.ComputeShader, MemoryAccess.ShaderWrite, PipelineStage.ComputeShader, MemoryAccess.ShaderRead | MemoryAccess.ShaderWrite );
			DispatchResident( shader, residentGroups[g], views, frame );
		}

		// The calls' compaction, ids and multi-draws read what it wrote, in later lists
		r.List.Barrier( PipelineStage.ComputeShader, MemoryAccess.ShaderWrite, PipelineStage.DrawIndirect | PipelineStage.VertexInput | PipelineStage.VertexShader | PipelineStage.ComputeShader, MemoryAccess.IndirectRead | MemoryAccess.IndexRead | MemoryAccess.ShaderRead | MemoryAccess.ShaderWrite );
		r.List.Marker( "end Resident cull" );
		timer?.End( r.List, scope );
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
		foreach ( var work in group ) bytes += ResidentRegion( work.Plan, work.Plan.Key.Ids, work.Lists || work.Plan.Key.Meshlets ).Bytes;

		var (buffer, slot, offset) = tables.Allocate( bytes );
		var data = Gpu.Map( buffer ).Slice( (int)offset, bytes );
		var items = MemoryMarshal.Cast<byte, ResidentCullItem>( data[..(residentItems.Count * ResidentItemBytes)] );
		var viewRecords = MemoryMarshal.Cast<byte, ResidentCullView>( data.Slice( residentItems.Count * ResidentItemBytes, group.Count * ResidentViewBytes ) );
		var visibility = GroupVisibility( group );

		var at = headerBytes;
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

			// The view's region, as a call lays out its own
			var meshlets = plan.Key.Meshlets && work.Visibility is not null;
			var listing = work.Lists || meshlets;
			var layout = ResidentRegion( plan, ids, listing );
			var region = data.Slice( at, layout.Bytes );
			var compacted = work.Visibility is not { } own ? default : meshlets ? device.PrepassCompactedFor( own ) : own.Compacted[frame % own.Compacted.Length];
			var regionOffset = offset + at;
			WriteCullView( views[work.ViewIndex], region );

			var runWords = plan.ResidentRunWords ??= ResidentRunWords( plan );
			runWords.CopyTo( MemoryMarshal.Cast<byte, uint>( region.Slice( layout.Runs, runWords.Length * 4 ) ) );

			if ( listing )
			{
				var arguments = MemoryMarshal.Cast<byte, uint>( region.Slice( layout.Listed, 16 ) );
				arguments[0] = 0;
				arguments[1] = 1;
				arguments[2] = 1;
			}

			var firstInstance = (uint)((regionOffset + layout.Instances) / GpuDrivenTables.InstanceStride);
			work.Region = new CulledRegion( buffer, slot, regionOffset, layout.Runs, layout.Listed, layout.Instances, layout.FragmentWords, layout.Commands, firstInstance, listing ? compacted : default, meshlets ? work.Visibility.PrepassCompacted16 : default );
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
			at += layout.Bytes;
		}

		var slots = tables.Slots;
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
			MeshletRuns = device.MeshletStore is { } store ? (uint)store.RunSlot : uint.MaxValue,
		} );
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
			for ( int word = 0; word < prepassRecords.Length; word += CullBatchBytes / 4 )
			{
				if ( (prepassRecords[word + 6] & 1) != 0 && prepassRecords[word + 4] != GpuDrivenTables.NoVisibility ) residentIds.Add( prepassRecords[word + 4] );
			}

			plan.IdsFrom = prepass;
		}

		var records = plan.BatchRecords;
		var batches = records.Length / (CullBatchBytes / 4);
		var entries = 0;
		for ( int b = 0; b < batches; b++ ) entries += (int)records[b * (CullBatchBytes / 4) + 1];
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
			var words = MemoryMarshal.Cast<byte, uint>( data[..mapOffset] );
			for ( int b = 0; b < plan.MovedAt.Length; b++ )
			{
				if ( plan.MovedAt[b] >= 0 ) words[b * (CullBatchBytes / 4) + 5] = (uint)(movedOffset + plan.MovedAt[b] * 4);
			}
		}
		var map = MemoryMarshal.Cast<byte, ResidentCullEntry>( data[mapOffset..] );
		var at = 0;
		for ( int b = 0; b < batches; b++ )
		{
			var count = records[b * (CullBatchBytes / 4) + 1];
			var at4 = b * (CullBatchBytes / 4);
			var ids = (records[at4 + 6] & 2) != 0 && residentIds.Contains( records[at4 + 4] ) ? ResidentCullEntry.PrepassIds : 0;
			for ( uint e = 0; e < count; e++ ) map[at++] = new ResidentCullEntry { Batch = (uint)b | ids, Element = e };
		}

		recording.List.CopyBuffer( staging, stagingOffset, buffer, 0, copied );
		residentRecordsCopied = true;
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
		if ( lists && assignment.Region.Compacted != visibility.Compacted[frame % visibility.Compacted.Length] ) return false;

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
			DrawCulledRegion( cull, assignment.Region, 0, compact, shaders, visibility, ids, lists, meshlets, depth, samples, depthBias, slopeScaledDepthBias, resolver, assignment.Fragments, dispatch: false );
			plan.Drawn.AsSpan().CopyTo( drawn );
			TimeGpuDriven( start, drawn[..prefix], assignment.Fragments );
		}
		finally
		{
			gpuDrivenBuckets = ownBuckets;
			gpuDrivenBucketCount = 0;
		}

		System.Threading.Interlocked.Increment( ref device.ResidentCullCalls );

		// The view's own batches: a call of their own, with buckets, culling, ids and compaction of its own
		if ( prefix < batches.Length && EnsureScope() )
			DrawBucketed( shaderMode, batches[prefix..], args, transforms, depthBias, slopeScaledDepthBias, drawn[prefix..], cull with { Plan = null, PlanBatches = 0 }, null, 0 );

		return true;
	}
}

/// <summary>
/// The resident cull's state on a device: the plans each view's calls drew (by view and resident list, with the frame
/// last drawn), this frame's assignments, and the buffers of plans' records.
/// </summary>
internal sealed class ResidentCullState
{
	readonly Dictionary<(object View, object List), List<(GpuDrivenPlan.CallKey Key, uint Frame)>> calls = new();
	readonly Dictionary<(object View, GpuDrivenPlan Plan), BackendContext.ResidentAssignment> assigned = new();
	readonly List<BackendContext.ResidentAssignment> pool = new();
	readonly List<(WeakReference<GpuDrivenPlan> Plan, BufferHandle Buffer)> owned = new();
	int pooled;
	uint frame;

	/// <summary>A call's kind is kept this many frames after it was last drawn.</summary>
	const uint KeptFrames = 4;

	/// <summary>A call drew <paramref name="key"/> of <paramref name="list"/> in <paramref name="view"/>.</summary>
	public void Seen( object view, object list, in GpuDrivenPlan.CallKey key, uint now )
	{
		lock ( calls )
		{
			if ( !calls.TryGetValue( (view, list), out var keys ) ) calls[(view, list)] = keys = new();
			for ( int i = 0; i < keys.Count; i++ )
			{
				if ( keys[i].Key != key ) continue;
				keys[i] = (key, now);
				return;
			}

			keys.Add( (key, now) );
		}
	}

	/// <summary>The kinds of call a view drew of a list in the last few frames.</summary>
	public void Keys( object view, object list, uint now, List<GpuDrivenPlan.CallKey> into )
	{
		lock ( calls )
		{
			if ( !calls.TryGetValue( (view, list), out var keys ) ) return;
			foreach ( var (key, seen) in keys )
			{
				if ( now - seen <= KeptFrames ) into.Add( key );
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

		lock ( assigned )
		{
			assigned.Clear();
			pooled = 0;
		}

		lock ( calls )
		{
			List<(object, object)> stale = null;
			foreach ( var (key, keys) in calls )
			{
				for ( int i = keys.Count - 1; i >= 0; i-- )
				{
					if ( now - keys[i].Frame > KeptFrames ) keys.RemoveAt( i );
				}
				if ( keys.Count == 0 ) (stale ??= new()).Add( key );
			}

			if ( stale is not null )
			{
				foreach ( var key in stale ) calls.Remove( key );
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


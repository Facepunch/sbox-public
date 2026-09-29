using NativeEngine;
using System.Diagnostics;

namespace Sandbox.SceneRenderer;

/// <summary>
/// Plans, records and submits frame segments, following native's <c>CRenderBatchList</c>.
/// Mesh work can record in parallel; custom objects and engine stages stay on the main thread.
/// Segments own their writable state and submit in order. Small frames share one context.
/// </summary>
internal sealed class FrameRecorder : IDisposable
{
	/// <summary>
	/// The most pool threads a frame records on at once.
	/// </summary>
	static readonly int MaxRecordThreads = Math.Clamp( Environment.ProcessorCount - 1, 1, 8 );

	/// <summary>
	/// Minimum total runs needed to justify worker recording.
	/// </summary>
	const int ParallelRunThreshold = 64;

	/// <summary>
	/// The fewest draws worth a segment of their own (<see cref="RenderFeature.RunWeights"/>).
	/// </summary>
	const int MinDrawsPerSegment = 32;

	enum SegmentKind : byte
	{
		/// <summary>Drawing: <see cref="Segment.Count"/> of the <see cref="work"/> from <see cref="Segment.Index"/>.</summary>
		Draw,
		/// <summary>Consecutive main-thread layers from one frame.</summary>
		Layers,
	}

	struct Segment
	{
		public SegmentKind Kind;
		public RenderFrame Frame;
		public int Index;
		public int Count;

		/// <summary>Draw segments: the first work's first run, and the last work's end run.</summary>
		public int First, End;

		/// <summary>Estimated draw count; heaviest segments are claimed first.</summary>
		public int Weight;

		public bool OnMainThread;

		/// <summary>Layers recorded on the async compute queue (<see cref="RenderLayer.AsyncCompute"/>), after their hand-off.</summary>
		public bool Async;

		/// <summary>The first segment after the async one that isn't beside it: it waits for it, and takes its layers' results back.</summary>
		public bool Join;

		public RenderContext Context;
		public RenderStats Stats;
		public double Ms;
	}

	/// <summary>
	/// Scheduled layer, its frame and draw-work range.
	/// </summary>
	struct PlannedLayer
	{
		public RenderFrame Frame;
		public RenderLayer Layer;
		public int WorkFrom, WorkTo;
	}

	/// <summary>
	/// One view's layer, split into segments by estimated draw count.
	/// </summary>
	struct DrawWork
	{
		public ViewPass View;
		public MeshLayer Layer;
		public int Runs;

		/// <summary>Where its runs' draws start in <see cref="draws"/>: <see cref="Runs"/> + 1 running totals, from 0.</summary>
		public int DrawsFrom;
	}

	// Per-work prefix sums of draw counts.
	int[] draws = new int[256];
	int drawCount;

	readonly List<RenderContext> contexts = new();
	Segment[] segments = new Segment[16];
	int segmentCount;

	PlannedLayer[] layers = new PlannedLayer[32];
	int layerCount;

	/// <summary>
	/// Why a layer is in the plan or isn't (<see cref="Snapshot"/>).
	/// </summary>
	internal enum LayerDecision : byte
	{
		/// <summary>Scheduled.</summary>
		Kept,
		/// <summary>It can't run this frame (<see cref="RenderLayer.IsNeeded"/>): nothing to draw, or what it needs is off.</summary>
		NotNeeded,
		/// <summary>It makes something (<see cref="RenderLayer.Writes"/>) no later kept layer reads.</summary>
		Unread,
	}

	/// <summary>
	/// Every layer of every planned frame, in order, and what became of it.
	/// </summary>
	struct LayerRecord
	{
		public RenderFrame Frame;
		public RenderLayer Layer;
		public LayerDecision Decision;
		public FrameResources Reads;

		/// <summary>What it reads that no earlier kept layer of its frame makes this frame - bloom's input without bloom objects,
		/// which reads as black, as native's does; the depth chain without a prepass.</summary>
		public FrameResources Missing;

		/// <summary>Its index in <see cref="layers"/> when kept, or -1.</summary>
		public int Planned;
	}

	LayerRecord[] records = new LayerRecord[64];
	int recordCount;

	DrawWork[] work = new DrawWork[16];
	int workCount;

	// Setup contexts submit in frame order, before segments.
	readonly List<RenderFrame> frames = new();

	/// <summary>
	/// Segments recorded on the thread pool: <see cref="workerSegments"/>, claimed heaviest first.
	/// </summary>
	JobBatch recordJobs;
	int[] workerSegments = new int[16];

	/// <summary>
	/// Whether workers record segments. Otherwise all work shares the first frame's context.
	/// </summary>
	public bool Parallel { get; private set; }

	/// <summary>
	/// The segment recorded on the async compute queue this frame, or -1. There's one at most: the main frame's compute layers
	/// after its prepass.
	/// </summary>
	int asyncSegment = -1;

	/// <summary>
	/// The async segment's graphics work before it (<see cref="RenderLayer.HandOff"/>), which signals it to start.
	/// </summary>
	RenderContext handOff;

	/// <summary>
	/// Plan active layers, child frames and segments before setup. Enable workers when enough work exists.
	/// </summary>
	public void Plan( RenderFrame frame, bool parallel, bool asyncCompute = false )
	{
		segmentCount = 0;
		layerCount = 0;
		recordCount = 0;
		workCount = 0;
		drawCount = 0;
		frames.Clear();

		AddFrame( frame );

		// Small frames cannot amortize per-segment context creation and submission.
		var runs = 0;
		for ( int w = 0; w < workCount; w++ )
			runs += work[w].Runs;

		Parallel = parallel && runs >= ParallelRunThreshold;
		var maxSegments = Parallel ? MaxRecordThreads : 1;

		// Async compute needs contexts of its own, so only a frame recorded in parallel
		asyncSegment = -1;
		var useAsync = Parallel && asyncCompute && RenderContext.SupportsAsyncCompute;
		var awaiting = false;

		// Preserve layer order; merge adjacent main-thread layers from the same frame.
		for ( int l = 0; l < layerCount; l++ )
		{
			ref var planned = ref layers[l];
			var layer = planned.Layer;

			// The main frame's compute layers, after something to signal them, together in one segment
			if ( useAsync && layer.AsyncCompute && planned.Frame == frames[0] && segmentCount > 0 && (asyncSegment < 0 || (awaiting && asyncSegment == segmentCount - 1)) )
			{
				if ( asyncSegment >= 0 )
				{
					segments[asyncSegment].Count++;
					continue;
				}

				AddSegment( SegmentKind.Layers, planned.Frame, l, true );
				asyncSegment = segmentCount - 1;
				segments[asyncSegment].Async = true;
				segments[asyncSegment].Context.AsyncCompute = true;
				awaiting = true;
				continue;
			}

			// The first layer that isn't beside the async work waits for it, in a segment of its own
			var join = awaiting && !layer.BesideAsyncCompute;
			var first = segmentCount;

			if ( layer.IsShared ) AddDrawSegments( planned.Frame, planned.WorkFrom, planned.WorkTo, maxSegments );
			else if ( !join && segmentCount > 0 && segments[segmentCount - 1] is { Kind: SegmentKind.Layers, Async: false } last && last.Frame == planned.Frame ) segments[segmentCount - 1].Count++;
			else AddSegment( SegmentKind.Layers, planned.Frame, l, true );

			if ( join && segmentCount > first )
			{
				segments[first].Join = true;
				awaiting = false;
			}
		}

		// Nothing after it to wait for it: record it on the graphics queue after all
		if ( awaiting )
		{
			segments[asyncSegment].Async = false;
			segments[asyncSegment].Context.AsyncCompute = false;
			asyncSegment = -1;
		}
	}

	/// <summary>
	/// Short names of <see cref="FrameResources"/>, by bit, for the plan overlay.
	/// </summary>
	static readonly string[] ResourceNames = ["chain", "qdepth", "bloom", "bloom in", "refract"];

	/// <summary>
	/// The last plan, for <c>overlay_scene_plan</c>: each frame's layers, whether each ran and why not, what it made and read
	/// (and of that, what nothing earlier made), its draw work and the segment it recorded in, and the segments.
	/// </summary>
	internal Sandbox.Rendering.ManagedFramePlan Snapshot( string camera )
	{
		var plan = new Sandbox.Rendering.ManagedFramePlan { Camera = camera, Parallel = Parallel, Resources = ResourceNames };

		// The segment each planned layer and each draw work starts in
		var layerSegment = new int[layerCount];
		var workSegment = new int[workCount];
		Array.Fill( layerSegment, -1 );
		Array.Fill( workSegment, -1 );

		for ( int i = 0; i < segmentCount; i++ )
		{
			ref var segment = ref segments[i];
			var first = segment.Kind == SegmentKind.Layers ? layers[segment.Index].Layer.Name : work[segment.Index].Layer.Name;
			plan.Segments.Add( new Sandbox.Rendering.ManagedFramePlan.Segment { Worker = Parallel && !segment.OnMainThread, Async = segment.Async, Draws = segment.Weight, First = first } );

			var owners = segment.Kind == SegmentKind.Layers ? layerSegment : workSegment;
			for ( int n = segment.Index; n < segment.Index + segment.Count && n < owners.Length; n++ )
				if ( owners[n] < 0 ) owners[n] = i;
		}

		// A frame's records are together: a child frame's come after its parent's
		Sandbox.Rendering.ManagedFramePlan.Frame current = null;
		RenderFrame currentFrame = null;
		for ( int i = 0; i < recordCount; i++ )
		{
			ref var record = ref records[i];
			if ( record.Frame != currentFrame )
			{
				currentFrame = record.Frame;
				current = new Sandbox.Rendering.ManagedFramePlan.Frame { Name = record.Frame.View?.Name ?? "", Skybox = record.Frame.IsSkybox };
				plan.Frames.Add( current );
			}

			var layer = new Sandbox.Rendering.ManagedFramePlan.Layer
			{
				Name = record.Layer.Name,
				State = record.Decision switch
				{
					LayerDecision.Kept => Sandbox.Rendering.ManagedFramePlan.LayerState.Runs,
					LayerDecision.NotNeeded => Sandbox.Rendering.ManagedFramePlan.LayerState.NotNeeded,
					_ => Sandbox.Rendering.ManagedFramePlan.LayerState.Culled,
				},
				Makes = (uint)record.Layer.Writes,
				Reads = (uint)record.Reads,
				NotMade = (uint)record.Missing,
				Segment = -1,
			};

			if ( record.Planned >= 0 && record.Planned < layerCount && layers[record.Planned].Layer == record.Layer )
			{
				ref var planned = ref layers[record.Planned];
				if ( planned.Layer.IsShared )
				{
					layer.Views = planned.WorkTo - planned.WorkFrom;
					for ( int w = planned.WorkFrom; w < planned.WorkTo; w++ )
					{
						layer.Runs += work[w].Runs;
						layer.Draws += DrawsIn( w, 0, work[w].Runs );
					}

					if ( planned.WorkTo > planned.WorkFrom ) layer.Segment = workSegment[planned.WorkFrom];
				}
				else
				{
					layer.Segment = layerSegment[record.Planned];
				}
			}

			current.Layers.Add( layer );
		}

		return plan;
	}

	/// <summary>
	/// Each segment's recording time, once recorded, into a plan <see cref="Snapshot"/> made of this frame.
	/// </summary>
	internal void FillTimes( Sandbox.Rendering.ManagedFramePlan plan )
	{
		for ( int i = 0; i < segmentCount && i < plan.Segments.Count; i++ )
		{
			var segment = plan.Segments[i];
			segment.Ms = segments[i].Ms;
			plan.Segments[i] = segment;
		}
	}

	/// <summary>
	/// Scheduled layers in order, for tests.
	/// </summary>
	internal (RenderFrame Frame, RenderLayer Layer)[] Planned
	{
		get
		{
			var planned = new (RenderFrame, RenderLayer)[layerCount];
			for ( int l = 0; l < layerCount; l++ )
				planned[l] = (layers[l].Frame, layers[l].Layer);
			return planned;
		}
	}

	/// <summary>
	/// Schedule a frame's layers, expanding child frames in place. A layer that can't run is left out, and so is one that
	/// makes something no later kept layer of the frame reads - worked out backwards, as Unity's render graph culls passes.
	/// </summary>
	public void AddFrame( RenderFrame frame )
	{
		frames.Add( frame );

		var first = recordCount;
		foreach ( var layer in frame.System.Layers )
		{
			if ( recordCount == records.Length ) Array.Resize( ref records, records.Length * 2 );
			records[recordCount++] = new LayerRecord { Frame = frame, Layer = layer, Decision = layer.IsNeeded( frame ) ? LayerDecision.Kept : LayerDecision.NotNeeded, Planned = -1 };
		}

		var end = recordCount;

		// Backwards: a producer stays while something after it that stays reads what it makes
		var read = FrameResources.None;
		for ( int i = end - 1; i >= first; i-- )
		{
			ref var record = ref records[i];
			if ( record.Decision != LayerDecision.Kept ) continue;

			var writes = record.Layer.Writes;
			if ( writes != FrameResources.None && (writes & read) == 0 )
			{
				record.Decision = LayerDecision.Unread;
				continue;
			}

			record.Reads = record.Layer.Reads( frame );
			read |= record.Reads;
		}

		// Forwards: what each reads that nothing before it makes - a prepass that's off takes the depth chain with it
		var made = FrameResources.None;
		for ( int i = first; i < end; i++ )
		{
			ref var record = ref records[i];
			if ( record.Decision != LayerDecision.Kept ) continue;

			record.Missing = record.Reads & ~made;
			made |= record.Layer.Writes;
		}

		// A child frame's layers go in its place, appending records past this frame's
		for ( int i = first; i < end; i++ )
		{
			if ( records[i].Decision != LayerDecision.Kept ) continue;

			records[i].Planned = layerCount;
			records[i].Layer.AddTo( this, frame );
		}
	}

	/// <summary>
	/// Schedule a layer and its draw work.
	/// </summary>
	public void AddLayer( RenderFrame frame, RenderLayer layer )
	{
		if ( layerCount == layers.Length ) Array.Resize( ref layers, layers.Length * 2 );

		var from = workCount;
		layer.AddWork( this, frame );
		layers[layerCount++] = new PlannedLayer { Frame = frame, Layer = layer, WorkFrom = from, WorkTo = workCount };
	}

	/// <summary>
	/// Add a shared layer's view and run weights.
	/// </summary>
	public void AddWork( RenderFrame frame, ViewPass view, MeshLayer layer )
	{
		if ( workCount == work.Length ) Array.Resize( ref work, work.Length * 2 );
		var runs = frame.System.RunCount( view, layer );
		work[workCount++] = new DrawWork { View = view, Layer = layer, Runs = runs, DrawsFrom = drawCount };

		// Prefix sums give constant-time draw counts for any range.
		if ( drawCount + runs + 1 > draws.Length ) Array.Resize( ref draws, Math.Max( drawCount + runs + 1, draws.Length * 2 ) );
		var totals = draws.AsSpan( drawCount, runs + 1 );
		frame.System.RunWeights( view, layer, totals[1..] );
		totals[0] = 0;
		for ( int r = 1; r <= runs; r++ )
			totals[r] += totals[r - 1];
		drawCount += runs + 1;
	}

	/// <summary>
	/// Roughly how many draws work <paramref name="w"/>'s runs <paramref name="from"/> to <paramref name="to"/> (exclusive) record.
	/// </summary>
	int DrawsIn( int w, int from, int to ) => draws[work[w].DrawsFrom + to] - draws[work[w].DrawsFrom + from];

	void AddSegment( SegmentKind kind, RenderFrame frame, int index, bool onMainThread, int count = 1 )
	{
		if ( segmentCount == segments.Length ) Array.Resize( ref segments, segments.Length * 2 );
		if ( segmentCount == contexts.Count ) contexts.Add( new RenderContext( frame.System.FrameContext ) );
		contexts[segmentCount].AsyncCompute = false;
		segments[segmentCount] = new Segment { Kind = kind, Frame = frame, Index = index, Count = count, OnMainThread = onMainThread, Context = contexts[segmentCount] };
		segmentCount++;
	}

	/// <summary>
	/// Split work by draw count, allowing splits within views. Empty work still clears its target;
	/// segments containing custom objects stay on the main thread.
	/// </summary>
	void AddDrawSegments( RenderFrame frame, int from, int to, int maxSegments )
	{
		if ( from >= to ) return;

		var total = 0;
		for ( int w = from; w < to; w++ )
			total += DrawsIn( w, 0, work[w].Runs );

		var share = maxSegments <= 1 ? int.MaxValue : Math.Max( MinDrawsPerSegment, (total + maxSegments - 1) / maxSegments );
		var firstSegment = segmentCount;
		int segWork = from, segFirst = 0, taken = 0;
		var onMain = false;

		for ( int w = from; w < to; w++ )
		{
			var runs = work[w].Runs;
			var workOnMain = !frame.System.CanRecordOffMainThread( work[w].View, work[w].Layer );
			onMain |= workOnMain;

			for ( int r = w == segWork ? segFirst : 0; r < runs; r++ )
			{
				taken += DrawsIn( w, r, r + 1 );
				if ( taken < share ) continue;

				// End the segment at this run boundary.
				AddDrawSegment( frame, segWork, w, segFirst, r + 1, onMain, taken );
				taken = 0;

				if ( r + 1 == runs )
				{
					segWork = w + 1;
					segFirst = 0;
					onMain = false;
				}
				else
				{
					segWork = w;
					segFirst = r + 1;
					onMain = workOnMain;
				}
			}
		}

		if ( segWork >= to ) return;

		// Merge small remainders unless that would move worker work to the main thread.
		if ( segmentCount > firstSegment && taken < share / 2 && (!onMain || segments[segmentCount - 1].OnMainThread) )
		{
			ref var last = ref segments[segmentCount - 1];
			last.Count = to - last.Index;
			last.End = work[to - 1].Runs;
			last.Weight += taken;
			return;
		}

		AddDrawSegment( frame, segWork, to - 1, segFirst, work[to - 1].Runs, onMain, taken );
	}

	/// <summary>
	/// Add an inclusive work range with an exclusive final run bound.
	/// </summary>
	void AddDrawSegment( RenderFrame frame, int firstWork, int lastWork, int firstRun, int endRun, bool onMainThread, int weight )
	{
		AddSegment( SegmentKind.Draw, frame, firstWork, onMainThread, lastWork - firstWork + 1 );
		ref var segment = ref segments[segmentCount - 1];
		segment.First = firstRun;
		segment.End = endRun;
		segment.Weight = weight;
	}

	/// <summary>
	/// Run layer prerequisites, then record segments. Accumulate counts per frame and timings in <paramref name="stats"/>.
	/// </summary>
	public void Record( ref RenderStats stats )
	{
		for ( int l = 0; l < layerCount; l++ )
			layers[l].Layer.BeforeSegments( layers[l].Frame, layers[l].Frame.System.FrameContext );

		var start = Stopwatch.GetTimestamp();
		var workers = 0;
		for ( int i = 0; i < segmentCount; i++ )
			if ( Parallel && !segments[i].OnMainThread ) workers++;

		if ( workerSegments.Length < workers ) Array.Resize( ref workerSegments, Math.Max( workers, workerSegments.Length * 2 ) );
		var claimable = 0;
		for ( int i = 0; i < segmentCount && workers > 0; i++ )
			if ( !segments[i].OnMainThread ) workerSegments[claimable++] = i;

		// Claim heaviest work first to balance completion times. Submission order is unchanged.
		for ( int i = 1; i < claimable; i++ )
		{
			var slot = workerSegments[i];
			var j = i - 1;
			for ( ; j >= 0 && segments[workerSegments[j]].Weight < segments[slot].Weight; j-- )
				workerSegments[j + 1] = workerSegments[j];
			workerSegments[j + 1] = slot;
		}

		recordJobs ??= new JobBatch( claim => RecordSegment( workerSegments[claim] ) );
		recordJobs.Start( workers, MaxRecordThreads );

		// Record main-thread work, then help workers. Always join before accessing their output.
		foreach ( var frame in frames )
			frame.StagesMs = 0;

		var mainDone = start;
		try
		{
			for ( int i = 0; i < segmentCount; i++ )
			{
				if ( !Parallel || segments[i].OnMainThread ) RecordSegment( i );
			}

			mainDone = Stopwatch.GetTimestamp();
			recordJobs.Help();
		}
		finally
		{
			using ( Zones.RecordWait.Start() ) recordJobs.Wait();
		}

		stats.RecordMs = Stopwatch.GetElapsedTime( start ).TotalMilliseconds;
		stats.RecordWaitMs = Stopwatch.GetElapsedTime( mainDone ).TotalMilliseconds;


		for ( int i = 0; i < segmentCount; i++ )
		{
			ref var segment = ref segments[i];
			RenderStats.AddCounts( ref segment.Frame.Recorded, segment.Stats );
			if ( !segment.OnMainThread && Parallel ) stats.WorkerRecordMs += segment.Ms;
		}

		foreach ( var frame in frames )
			stats.StagesMs += frame.StagesMs;
	}

	void RecordSegment( int slot )
	{
		using var zone = Zones.Segment.Start();
		ref var segment = ref segments[slot];
		var start = Stopwatch.GetTimestamp();
		var stats = new RenderStats();
		var rc = segment.Context;
		var frame = segment.Frame;

		var recording = RenderContext.Recording;
		try
		{
			// The async segment's graphics work first, into a context submitted just before it
			if ( segment.Async )
			{
				handOff ??= new RenderContext( frame.System.FrameContext );
				handOff.BeginSegment( frame.System.FrameContext, null );
				handOff.InheritViewConstants( frame.ConstantsKey );
				RenderContext.Recording = handOff;

				for ( int l = segment.Index; l < segment.Index + segment.Count; l++ )
				{
					frame.UseFrameConstants( handOff );
					layers[l].Layer.HandOff( frame, handOff );
					handOff.EndPass();
				}
			}

			// Context command memory is thread-owned (CRenderBatchList::Start). Serial recording shares the first context.
			rc.BeginSegment( frame.System.FrameContext, Parallel ? null : frames[0].System.FrameContext );
			rc.InheritViewConstants( frame.ConstantsKey );
			RenderContext.Recording = rc;

			// What the async layers left for graphics, now they're done
			if ( segment.Join )
			{
				ref var compute = ref segments[asyncSegment];
				for ( int l = compute.Index; l < compute.Index + compute.Count; l++ )
					layers[l].Layer.TakeBack( compute.Frame, rc );
			}

			RecordSegment( ref segment, rc, frame, ref stats );
		}
		finally
		{
			RenderContext.Recording = recording;
		}

		segment.Stats = stats;
		segment.Ms = Stopwatch.GetElapsedTime( start ).TotalMilliseconds;
	}

	void RecordSegment( ref Segment segment, RenderContext rc, RenderFrame frame, ref RenderStats stats )
	{
		switch ( segment.Kind )
		{
			case SegmentKind.Draw:
				{
					var last = segment.Index + segment.Count - 1;
					for ( int w = segment.Index; w <= last; w++ )
					{
						ref var item = ref work[w];
						var from = w == segment.Index ? segment.First : 0;
						var to = w == last ? segment.End : item.Runs;

						var scope = rc.BeginGpuScope( item.Layer.Name );
						item.Layer.Begin( frame, rc, item.View, start: from == 0 );
						frame.System.DrawRange( rc, item.View, item.Layer, from, to - from, ref stats );
						rc.EndPass();
						rc.EndGpuScope( scope );
						if ( from == 0 && item.View.IsShadow ) stats.ShadowViews++;
					}
					break;
				}

			case SegmentKind.Layers:
				{
					// Reset constants per layer; each layer binds its own targets.
					for ( int l = segment.Index; l < segment.Index + segment.Count; l++ )
					{
						var scope = rc.BeginGpuScope( layers[l].Layer.Name );
						frame.UseFrameConstants( rc );
						layers[l].Layer.Record( frame, rc, ref stats );
						rc.EndPass();
						rc.EndGpuScope( scope );
					}
					break;
				}
		}
	}

	/// <summary>
	/// Submit setup contexts, then segments in order. The first submission sets GPU timing. The async segment goes to the
	/// compute queue after its hand-off, as native's dependent layers do (<c>CSceneSystem::SubmitViews</c>): the hand-off signals
	/// it, and it signals the segment that joins it, which waits. Between them, the graphics queue draws the shadow maps.
	/// </summary>
	public void Submit( SwapChainHandle_t swapChain )
	{
		frames[0].System.FrameContext.Submit( swapChain );
		for ( int f = 1; f < frames.Count; f++ )
			frames[f].System.FrameContext.SubmitNext();

		RenderSemaphoreHandle_t computeDone = default;
		for ( int i = 0; i < segmentCount; i++ )
		{
			ref var segment = ref segments[i];
			if ( segment.Async )
			{
				var handedOff = handOff.SignalAtEnd();
				handOff.SubmitNext();
				segment.Context.WaitAtBegin( handedOff );
				computeDone = segment.Context.SignalAtEnd();
			}
			else if ( segment.Join )
			{
				segment.Context.WaitAtBegin( computeDone );
			}

			segment.Context.SubmitNext();
		}
	}

	/// <summary>
	/// Discard all recordings after a failure.
	/// </summary>
	public void End()
	{
		handOff?.End();
		for ( int i = 0; i < segmentCount; i++ )
			segments[i].Context.End();

		for ( int f = frames.Count - 1; f >= 0; f-- )
			frames[f].System.FrameContext.End();
	}

	/// <summary>
	/// Release submitted frame references.
	/// </summary>
	public void Clear()
	{
		for ( int i = 0; i < segmentCount; i++ )
			segments[i].Frame = null;
		for ( int l = 0; l < layerCount; l++ )
			layers[l].Frame = null;
		frames.Clear();
	}

	/// <summary>
	/// Release segment contexts and their native attributes.
	/// </summary>
	public void Dispose()
	{
		foreach ( var context in contexts )
			context.Dispose();
		contexts.Clear();
		handOff?.Dispose();
		handOff = null;
	}
}

namespace Sandbox.Rendering;

/// <summary>
/// A frame's plan as the managed scene renderer made it - which layers ran, why the rest didn't, what each made and read,
/// and how recording split across threads - for the frame plan on the dev tools's Rendering tab (<c>DebugOverlay.ScenePlan</c>).
/// The renderer publishes one for each frame it draws into a swap chain while something wants it (<see cref="Wanted"/>).
/// </summary>
internal sealed class ManagedFramePlan
{
	static double wantedUntil;

	/// <summary>
	/// Whatever shows a plan says so each frame it draws one; the renderer only builds plans for half a second after.
	/// </summary>
	public static void Want() => wantedUntil = RealTime.Now + 0.5;

	/// <summary>
	/// Whether something showed a plan in the last half second (<see cref="Want"/>).
	/// </summary>
	public static bool Wanted => RealTime.Now < wantedUntil;

	/// <summary>
	/// How often a plan is made while one is wanted. Nothing reads them faster - the dev tools samples ten times a second -
	/// and making one every frame, its layers timed and counted, cost a few megabytes a second of garbage.
	/// </summary>
	const double Interval = 0.1;

	static double publishedAt;

	/// <summary>
	/// Whether to make a plan of this frame: one is wanted, and the last is <see cref="Interval"/> old.
	/// </summary>
	public static bool Due => Wanted && RealTime.Now - publishedAt >= Interval;

	// The tag, handover time, engine frame and camera of the frames recently drawn into a swap chain, oldest overwritten
	// first, while a plan is wanted. Tags start at 1, so 0 is no tag
	static readonly (long Tag, long Ticks, ulong Frame, object Camera)[] handedOver = new (long, long, ulong, object)[64];
	static int handedOverNext;
	static long lastTag;
	static ulong lastNotedFrame;

	// Each camera's settled delay (SubmitDelayTicks), by the camera's view: cameras sit at different places in a GPU
	// frame, so theirs differ, and names needn't be unique
	static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, System.Runtime.CompilerServices.StrongBox<long>> submitDelays = new();

	/// <summary>
	/// How long after the renderer hands the frame of <paramref name="camera"/> (its view) over the GPU's timeline for it
	/// starts, in <see cref="System.Diagnostics.Stopwatch"/> ticks: the render thread submits it a little later, after any
	/// camera before it. Measured from GPU frames whose submit sampled the host clock (<see cref="NoteGpuFrame"/>),
	/// settled; 0 until one has.
	/// </summary>
	public static long SubmitDelayTicks( object camera ) => camera is not null && submitDelays.TryGetValue( camera, out var delay ) ? delay.Value : 0;

	/// <summary>
	/// The renderer handed the frame of <paramref name="camera"/> (its view), drawn into a swap chain, over for submitting
	/// at <paramref name="ticks"/>. Returns the tag to submit it with (<c>IRenderDevice::SetSubmitTag</c>), which comes
	/// back on its GPU frame's host submits, or 0 when nothing wants plans.
	/// </summary>
	public static long NoteHandedOver( long ticks, object camera )
	{
		if ( !Wanted ) return 0;
		var tag = ++lastTag;
		handedOver[handedOverNext] = (tag, ticks, Application.FrameCount, camera);
		handedOverNext = (handedOverNext + 1) % handedOver.Length;
		return tag;
	}

	// The engine frame the frame tagged tag was handed over in, or 0
	static ulong FrameOf( long tag )
	{
		if ( tag == 0 ) return 0;
		foreach ( var h in handedOver )
			if ( h.Tag == tag ) return h.Frame;
		return 0;
	}

	/// <summary>
	/// A GPU frame came back with a host submit of the frame tagged <paramref name="tag"/>, at
	/// <paramref name="submitTicks"/> on the host clock and <paramref name="submitMs"/> into its timeline. Every camera
	/// handed over in the same engine frame is in that GPU frame, tagged or not - the render thread can batch several
	/// cameras' work into one submission, which keeps only the first's tag - so how much later than each was handed over
	/// the timeline started is the delay to line that camera's side up by.
	/// </summary>
	public static void NoteGpuFrame( long tag, long submitTicks, double submitMs )
	{
		var frame = FrameOf( tag );
		if ( submitTicks <= 0 || frame == 0 || frame == lastNotedFrame ) return;
		lastNotedFrame = frame;

		var origin = TimelineOrigin( submitTicks, submitMs );
		foreach ( var (_, handed, f, camera) in handedOver )
		{
			if ( f != frame || camera is null ) continue;

			var delay = origin - handed;
			if ( submitDelays.TryGetValue( camera, out var settled ) ) settled.Value = (long)(settled.Value * 0.8 + delay * 0.2);
			else submitDelays.Add( camera, new System.Runtime.CompilerServices.StrongBox<long>( delay ) );
		}
	}

	/// <summary>
	/// Where a GPU frame's timeline starts on the host clock, from a host submit <paramref name="submitMs"/> into it at
	/// <paramref name="submitTicks"/>.
	/// </summary>
	public static long TimelineOrigin( long submitTicks, double submitMs ) =>
		submitTicks - (long)(submitMs * System.Diagnostics.Stopwatch.Frequency / 1000.0);

	// Recently published frames' own plans, for pairing with GPU frames by tag
	static readonly ManagedFramePlan[] published = new ManagedFramePlan[32];
	static int publishedNext;

	/// <summary>
	/// The published plan of the frame submitted with <paramref name="tag"/>, else of another camera's frame handed over
	/// with it, whose tag a batched submission may have lost (<see cref="NoteGpuFrame"/>); or null when there wasn't one:
	/// plans are only made ten times a second, so most GPU frames have none.
	/// </summary>
	public static ManagedFramePlan WithTag( long tag )
	{
		if ( tag == 0 ) return null;
		foreach ( var plan in published )
			if ( plan is not null && plan.SubmitTag == tag ) return plan;

		var frame = FrameOf( tag );
		if ( frame == 0 ) return null;
		foreach ( var plan in published )
			if ( plan is not null && plan.SubmitTag != 0 && FrameOf( plan.SubmitTag ) == frame ) return plan;
		return null;
	}

	/// <summary>
	/// The last plan published, its times settled with the ones before (<see cref="Publish"/>).
	/// </summary>
	public static ManagedFramePlan Latest { get; private set; }

	/// <summary>
	/// What became of a layer.
	/// </summary>
	public enum LayerState : byte
	{
		/// <summary>It ran.</summary>
		Runs,
		/// <summary>It couldn't run this frame: nothing to draw, or what it needs is off.</summary>
		NotNeeded,
		/// <summary>It makes something no later layer that ran reads.</summary>
		Culled,
	}

	/// <summary>
	/// One layer of a frame.
	/// </summary>
	public struct Layer
	{
		public string Name;
		public LayerState State;

		/// <summary>Bits of <see cref="Resources"/>: what it makes, what it reads, and what it read that wasn't made.</summary>
		public uint Makes, Reads, NotMade;

		/// <summary>Its draw work: views, runs and roughly how many draws.</summary>
		public int Views, Runs, Draws;

		/// <summary>The first segment it records in, or -1.</summary>
		public int Segment;

		/// <summary>Where it runs among all the frames' layers, child frames' included, or -1 if it doesn't. A layer that
		/// runs a child frame has the place of the child's first layer.</summary>
		public int Order;

		/// <summary>The frame in <see cref="Frames"/> it runs in its place - the 3D skybox's - or -1.</summary>
		public int ChildFrame;

		/// <summary>What recording it cost on the CPU, in milliseconds, across the <see cref="Pieces"/> it was recorded in,
		/// threads added together.</summary>
		public double CpuMs;
		public int Pieces;

		/// <summary>What it recorded: draws, triangles, and material binds - full ones, and similar materials' partial ones.</summary>
		public int Recorded, Binds, SimilarBinds;
		public long Triangles;
	}

	/// <summary>
	/// One frame: the camera's, or its 3D skybox's.
	/// </summary>
	public sealed class Frame
	{
		public string Name;
		public bool Skybox;
		public readonly List<Layer> Layers = new();
	}

	/// <summary>
	/// A share of the frame's recording, in submission order.
	/// </summary>
	public struct Segment
	{
		/// <summary>Recorded on a worker thread, rather than the main thread.</summary>
		public bool Worker;

		/// <summary>Submitted to the async compute queue, to run beside the graphics queue's work.</summary>
		public bool Async;

		/// <summary>Roughly how many draws it records.</summary>
		public int Draws;

		/// <summary>How long it took to record, in milliseconds.</summary>
		public double Ms;

		/// <summary>The first layer it records.</summary>
		public string First;

		/// <summary>When it was recorded (<see cref="System.Diagnostics.Stopwatch"/> ticks), and on which thread.</summary>
		public long StartTicks, EndTicks;
		public int Thread;

		/// <summary>When it was recorded, in milliseconds from where the GPU's timeline starts (<see cref="StartMs"/>).</summary>
		public double StartMs, EndMs;
	}

	/// <summary>
	/// The frame's main-thread phases, in milliseconds (<c>RenderStats</c>): record is wall time, of which record wait is
	/// waiting on workers; workers is their total.
	/// </summary>
	public double CollectMs, PrepareMs, SetupMs, RecordMs, RecordWaitMs, SubmitMs, WorkerMs;

	/// <summary>
	/// When the frame started being made and when it was first submitted (<see cref="System.Diagnostics.Stopwatch"/> ticks), and the thread
	/// that made it.
	/// </summary>
	public long StartTicks, SubmitTicks;
	public int MainThread;

	/// <summary>
	/// The tag its submission carried to the GPU's timings (<see cref="NoteHandedOver"/>), or 0.
	/// </summary>
	public long SubmitTag;

	/// <summary>
	/// When the frame started being made, in milliseconds before the GPU's timeline for it starts - its first submit,
	/// which the render thread makes <see cref="SubmitDelayTicks"/> after the frame is handed over - so the CPU's work
	/// lines up with the GPU's from here.
	/// </summary>
	public double StartMs;

	/// <summary>
	/// When the frame was handed over to be submitted, in milliseconds from where the GPU's timeline starts: a little
	/// before it, by <see cref="SubmitDelayTicks"/>.
	/// </summary>
	public double SubmitStartMs;

	/// <summary>
	/// The camera the frame is of.
	/// </summary>
	public string Camera;

	/// <summary>
	/// The camera's view, which says which camera it is when names are shared: its <see cref="SubmitDelayTicks"/>.
	/// </summary>
	public object View;

	/// <summary>
	/// Whether any segment was recorded on workers.
	/// </summary>
	public bool Parallel;

	/// <summary>
	/// The names of what layers make and read, by bit.
	/// </summary>
	public string[] Resources = [];

	public List<Frame> Frames { get; private set; } = new();
	public List<Segment> Segments { get; private set; } = new();

	/// <summary>
	/// Set its times in milliseconds from <paramref name="zero"/>, on the host clock.
	/// </summary>
	void PlaceTimes( long zero )
	{
		double Ms( long ticks ) => SubmitTicks == 0 ? 0 : (ticks - zero) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
		StartMs = Ms( StartTicks );
		SubmitStartMs = Ms( SubmitTicks );
		for ( int i = 0; i < Segments.Count; i++ )
		{
			var segment = Segments[i];
			segment.StartMs = Ms( segment.StartTicks );
			segment.EndMs = Ms( segment.EndTicks );
			Segments[i] = segment;
		}
	}

	/// <summary>
	/// A copy with its times from where its own GPU frame's timeline started (<paramref name="originTicks"/>, from
	/// <see cref="TimelineOrigin"/>), rather than its camera's settled <see cref="SubmitDelayTicks"/>.
	/// </summary>
	public ManagedFramePlan AlignedTo( long originTicks )
	{
		var copy = (ManagedFramePlan)MemberwiseClone();
		copy.Segments = new List<Segment>( Segments );
		copy.PlaceTimes( originTicks );
		return copy;
	}

	/// <summary>
	/// Publish this plan as the latest frame's, and as <see cref="Latest"/> a copy of it with its times averaged with the
	/// last one's while the frame keeps its shape - the same camera and segments - so the overlay's numbers settle rather
	/// than flicker frame to frame.
	/// </summary>
	public void Publish()
	{
		// Plans come ten times a second, so this settles over about a second
		const double keep = 0.7;
		publishedAt = RealTime.Now;

		PlaceTimes( SubmitTicks + SubmitDelayTicks( View ) );

		published[publishedNext] = this;
		publishedNext = (publishedNext + 1) % published.Length;

		if ( Latest is not { } last || last.View != View || last.Segments.Count != Segments.Count )
		{
			Latest = this;
			return;
		}

		// The settled copy: this frame's plan, its times moved part way from the last settled one's
		var settled = (ManagedFramePlan)MemberwiseClone();
		settled.Segments = new( Segments );
		settled.Frames = new( Frames.Count );

		for ( int i = 0; i < Segments.Count; i++ )
		{
			var segment = Segments[i];
			var was = last.Segments[i];
			segment.Ms = was.Ms * keep + segment.Ms * (1 - keep);
			segment.StartMs = was.StartMs * keep + segment.StartMs * (1 - keep);
			segment.EndMs = was.EndMs * keep + segment.EndMs * (1 - keep);
			settled.Segments[i] = segment;
		}

		// Layers settle too while the frames keep their layers
		for ( int f = 0; f < Frames.Count; f++ )
		{
			var frame = new Frame { Name = Frames[f].Name, Skybox = Frames[f].Skybox };
			frame.Layers.AddRange( Frames[f].Layers );
			settled.Frames.Add( frame );

			if ( f >= last.Frames.Count ) continue;
			var layers = frame.Layers;
			var lastLayers = last.Frames[f].Layers;
			if ( layers.Count != lastLayers.Count ) continue;

			for ( int l = 0; l < layers.Count; l++ )
			{
				var layer = layers[l];
				if ( layer.Name != lastLayers[l].Name ) continue;
				layer.CpuMs = lastLayers[l].CpuMs * keep + layer.CpuMs * (1 - keep);
				layers[l] = layer;
			}
		}

		settled.StartMs = last.StartMs * keep + StartMs * (1 - keep);
		settled.SubmitStartMs = last.SubmitStartMs * keep + SubmitStartMs * (1 - keep);

		settled.CollectMs = last.CollectMs * keep + CollectMs * (1 - keep);
		settled.PrepareMs = last.PrepareMs * keep + PrepareMs * (1 - keep);
		settled.SetupMs = last.SetupMs * keep + SetupMs * (1 - keep);
		settled.RecordMs = last.RecordMs * keep + RecordMs * (1 - keep);
		settled.RecordWaitMs = last.RecordWaitMs * keep + RecordWaitMs * (1 - keep);
		settled.SubmitMs = last.SubmitMs * keep + SubmitMs * (1 - keep);
		settled.WorkerMs = last.WorkerMs * keep + WorkerMs * (1 - keep);

		Latest = settled;
	}
}

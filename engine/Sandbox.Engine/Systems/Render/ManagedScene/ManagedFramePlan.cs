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

	/// <summary>
	/// The last plan published.
	/// </summary>
	public static ManagedFramePlan Latest { get; set; }

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

		/// <summary>When it was recorded, in milliseconds from the frame's first submit (<see cref="Publish"/>).</summary>
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
	/// When the frame started being made, in milliseconds before its first submit: the GPU's timeline starts at that
	/// submit, so the CPU's work lines up with it from here.
	/// </summary>
	public double StartMs;

	/// <summary>
	/// The camera the frame is of.
	/// </summary>
	public string Camera;

	/// <summary>
	/// Whether any segment was recorded on workers.
	/// </summary>
	public bool Parallel;

	/// <summary>
	/// The names of what layers make and read, by bit.
	/// </summary>
	public string[] Resources = [];

	public readonly List<Frame> Frames = new();
	public readonly List<Segment> Segments = new();

	/// <summary>
	/// Publish this plan as the latest, its times averaged with the last one's while the frame keeps its shape - the same
	/// camera and segments - so the overlay's numbers settle rather than flicker frame to frame.
	/// </summary>
	public void Publish()
	{
		// Plans come ten times a second, so this settles over about a second
		const double keep = 0.7;
		publishedAt = RealTime.Now;

		double Ms( long ticks ) => SubmitTicks == 0 ? 0 : (ticks - SubmitTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
		StartMs = Ms( StartTicks );
		for ( int i = 0; i < Segments.Count; i++ )
		{
			var segment = Segments[i];
			segment.StartMs = Ms( segment.StartTicks );
			segment.EndMs = Ms( segment.EndTicks );
			Segments[i] = segment;
		}

		if ( Latest is { } last && last.Camera == Camera && last.Segments.Count == Segments.Count )
		{
			for ( int i = 0; i < Segments.Count; i++ )
			{
				var segment = Segments[i];
				var was = last.Segments[i];
				segment.Ms = was.Ms * keep + segment.Ms * (1 - keep);
				segment.StartMs = was.StartMs * keep + segment.StartMs * (1 - keep);
				segment.EndMs = was.EndMs * keep + segment.EndMs * (1 - keep);
				Segments[i] = segment;
			}

			// Layers settle too while the frames keep their layers
			for ( int f = 0; f < Frames.Count && f < last.Frames.Count; f++ )
			{
				var layers = Frames[f].Layers;
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

			StartMs = last.StartMs * keep + StartMs * (1 - keep);

			CollectMs = last.CollectMs * keep + CollectMs * (1 - keep);
			PrepareMs = last.PrepareMs * keep + PrepareMs * (1 - keep);
			SetupMs = last.SetupMs * keep + SetupMs * (1 - keep);
			RecordMs = last.RecordMs * keep + RecordMs * (1 - keep);
			RecordWaitMs = last.RecordWaitMs * keep + RecordWaitMs * (1 - keep);
			SubmitMs = last.SubmitMs * keep + SubmitMs * (1 - keep);
			WorkerMs = last.WorkerMs * keep + WorkerMs * (1 - keep);
		}

		Latest = this;
	}
}

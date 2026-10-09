namespace Sandbox.Rendering;

/// <summary>
/// A frame's plan as the managed scene renderer made it - which layers ran, why the rest didn't, what each made and read,
/// and how recording split across threads - for the <c>overlay_scene_plan</c> overlay (<c>DebugOverlay.ScenePlan</c>).
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
	}

	/// <summary>
	/// The frame's main-thread phases, in milliseconds (<c>RenderStats</c>): record is wall time, of which record wait is
	/// waiting on workers; workers is their total.
	/// </summary>
	public double CollectMs, PrepareMs, SetupMs, RecordMs, RecordWaitMs, SubmitMs, WorkerMs;

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
		const double keep = 0.9;

		if ( Latest is { } last && last.Camera == Camera && last.Segments.Count == Segments.Count )
		{
			for ( int i = 0; i < Segments.Count; i++ )
			{
				var segment = Segments[i];
				segment.Ms = last.Segments[i].Ms * keep + segment.Ms * (1 - keep);
				Segments[i] = segment;
			}

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

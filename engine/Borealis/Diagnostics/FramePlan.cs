namespace Borealis;

/// <summary>
/// How a frame was planned and recorded, for a profiler's view of it (<see cref="RenderHost.PublishFramePlan"/>): each
/// frame's layers, whether each ran and why not, what each made and read, its draw work, and the recording segments.
/// </summary>
public sealed class FramePlan
{
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
		/// <summary>The layer's name.</summary>
		public string Name;

		/// <summary>Whether it ran.</summary>
		public LayerState State;

		/// <summary>Bits of <see cref="Resources"/>: what it makes, what it reads, and what it read that wasn't made.</summary>
		public uint Makes, Reads, NotMade;

		/// <summary>Its draw work: views, runs and roughly how many draws.</summary>
		public int Views, Runs, Draws;

		/// <summary>The first segment it records in, or -1.</summary>
		public int Segment;

		/// <summary>Where it runs among all the frames' layers, or -1 if it doesn't. A layer that runs a child frame has the
		/// place of the child's first layer.</summary>
		public int Order;

		/// <summary>The frame in <see cref="Frames"/> it runs in its place - the 3D skybox's - or -1.</summary>
		public int ChildFrame;

		/// <summary>What recording it cost on the CPU, in milliseconds, across the <see cref="Pieces"/> it was recorded in.</summary>
		public double CpuMs;

		/// <summary>How many pieces it was recorded in.</summary>
		public int Pieces;

		/// <summary>What it recorded: draws, and full and partial (similar material) binds.</summary>
		public int Recorded, Binds, SimilarBinds;

		/// <summary>Triangles it drew.</summary>
		public long Triangles;
	}

	/// <summary>
	/// One frame: the camera's, or its 3D skybox's.
	/// </summary>
	public sealed class Frame
	{
		/// <summary>The view's name.</summary>
		public string Name;

		/// <summary>Whether it's a 3D skybox.</summary>
		public bool Skybox;

		/// <summary>Its layers, in order.</summary>
		public readonly List<Layer> Layers = new();
	}

	/// <summary>
	/// A share of the frame's recording, in submission order.
	/// </summary>
	public struct Segment
	{
		/// <summary>Recorded on a worker thread, rather than the main thread.</summary>
		public bool Worker;

		/// <summary>Submitted to the async compute queue.</summary>
		public bool Async;

		/// <summary>Roughly how many draws it records.</summary>
		public int Draws;

		/// <summary>How long it took to record, in milliseconds.</summary>
		public double Ms;

		/// <summary>The first layer it records.</summary>
		public string First;

		/// <summary>When it was recorded (<see cref="System.Diagnostics.Stopwatch"/> ticks).</summary>
		public long StartTicks, EndTicks;

		/// <summary>The managed thread it was recorded on.</summary>
		public int Thread;
	}

	/// <summary>
	/// The frame's main-thread phases, in milliseconds (<see cref="RenderStats"/>): record is wall time, of which record
	/// wait is waiting on workers; workers is their total.
	/// </summary>
	public double CollectMs, PrepareMs, SetupMs, RecordMs, RecordWaitMs, SubmitMs, WorkerMs;

	/// <summary>
	/// When the frame started being made and when it was handed over to be submitted (<see cref="System.Diagnostics.Stopwatch"/> ticks).
	/// </summary>
	public long StartTicks, SubmitTicks;

	/// <summary>
	/// The thread that made it.
	/// </summary>
	public int MainThread;

	/// <summary>
	/// The tag its submission carried (<see cref="RenderHost.Submitting"/>), or 0.
	/// </summary>
	public long SubmitTag;

	/// <summary>
	/// The camera's name.
	/// </summary>
	public string Camera;

	/// <summary>
	/// The view the frame is of.
	/// </summary>
	public RenderView View;

	/// <summary>
	/// Whether any segment was recorded on workers.
	/// </summary>
	public bool Parallel;

	/// <summary>
	/// The names of what layers make and read, by bit.
	/// </summary>
	public string[] Resources = [];

	/// <summary>
	/// The frames, a child frame after its parent.
	/// </summary>
	public List<Frame> Frames { get; } = new();

	/// <summary>
	/// The recording segments, in submission order.
	/// </summary>
	public List<Segment> Segments { get; } = new();
}

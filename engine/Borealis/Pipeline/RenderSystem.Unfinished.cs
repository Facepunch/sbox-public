namespace Borealis;

/// <summary>
/// A child view's frame left to finish after its workers (docs/reference/borealis.md, "View setups and child views"): a view
/// rendered while another system records on the same thread (a water body's refraction, drawn from a draw's callback in
/// its parent's segment) hands its workers their segments, records its own and returns, so the thread carries on with
/// the parent's segments while the workers record. The parent waits for them, submits the child and runs what waits for
/// its submission (<see cref="AfterSubmit"/>) before it submits itself, so the child still reaches the GPU first.
/// </summary>
public sealed partial class RenderSystem
{
	/// <summary>Whether child views are left to finish after their workers. <c>BOREALIS_DEFERCHILD=0</c> turns it off.</summary>
	internal static bool LeaveChildrenUnfinished { get; set; } = Environment.GetEnvironmentVariable( "BOREALIS_DEFERCHILD" ) != "0";

	// What Record began that Finish carries on with
	struct Unfinished
	{
		public RenderWorld World;
		public RenderView View;
		public RenderStats Stats;
		public long Start, Collected, Prepared, AllocStart, AllocCollected, AllocPrepared;
		public FramePlan Plan;
		public RenderFrame SkyFrame;
		public bool Window;
		public int ChildrenFrom;
	}

	Unfinished unfinished;

	// The frames left to finish on this thread, oldest first
	[ThreadStatic] static List<RenderSystem> unfinishedOnThread;

	static List<RenderSystem> UnfinishedOnThread => unfinishedOnThread ??= new();

	// What waits for this system's frame to be submitted
	readonly List<(Action<GpuTexture> Action, GpuTexture Texture)> afterSubmit = new();

	/// <summary>
	/// Whether this system's frames may be left to finish after their workers when another system records on this thread:
	/// its caller reads what they drew only through draws its parent records after (<see cref="AfterSubmit"/>).
	/// </summary>
	internal bool MayFinishLater { get; set; }

	/// <summary>Whether the frame being recorded is left to finish after its workers: a child view's, not a window's.</summary>
	bool LeavesUnfinished( in RenderOutput output ) => LeaveChildrenUnfinished && MayFinishLater && recordingHere is not null && !output.Window;

	/// <summary>Whether this system's last frame is still waiting for its workers, for the tests.</summary>
	internal bool IsUnfinished => unfinished.World is not null;

	/// <summary>
	/// Run <paramref name="action"/> on <paramref name="texture"/> once this system's last frame is submitted: now if it is,
	/// else when it's finished. For what moves a target the frame drew into for others to read.
	/// </summary>
	internal void AfterSubmit( Action<GpuTexture> action, GpuTexture texture )
	{
		ArgumentNullException.ThrowIfNull( action );
		if ( unfinished.World is null )
		{
			action( texture );
			return;
		}

		afterSubmit.Add( (action, texture) );
	}

	void RunAfterSubmit()
	{
		try
		{
			foreach ( var (action, texture) in afterSubmit ) action( texture );
		}
		finally
		{
			afterSubmit.Clear();
		}
	}

	/// <summary>
	/// Finish the frames left unfinished on this thread since <paramref name="from"/> of them were, oldest first: a parent's
	/// child views, before it submits. The first failure is thrown once all are finished.
	/// </summary>
	static void FinishChildren( int from )
	{
		var left = UnfinishedOnThread;
		Exception failed = null;
		while ( left.Count > from )
		{
			try
			{
				left[from].Finish();
			}
			catch ( Exception e )
			{
				failed ??= e;
			}
		}

		if ( failed is not null ) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw( failed );
	}
}

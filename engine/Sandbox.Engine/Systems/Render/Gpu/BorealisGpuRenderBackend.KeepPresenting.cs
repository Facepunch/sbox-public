namespace Sandbox;

internal sealed partial class BorealisGpuRenderBackend
{
	/// <summary>
	/// Whether the frame thread keeps the game window presenting while the main thread is busy past a frame it handed over
	/// (a map's mount, a long load step): after <see cref="KeepPresentingAfter"/> it presents the window's last output again
	/// every <see cref="KeepPresentingEvery"/> until the main thread joins. <c>BOREALIS_KEEP_PRESENTING=0</c> turns it off.
	/// A window that stops presenting for seconds in immediate mode while a load makes and frees gigabytes of video memory
	/// can have the GPU reset under it (RX 7900 XTX); presenting through the stall keeps the present path moving.
	/// </summary>
	internal static bool KeepPresenting = System.Environment.GetEnvironmentVariable( "BOREALIS_KEEP_PRESENTING" ) != "0";

	/// <summary>How long the main thread may stay away from a frame handed over before the window presents its last output again.</summary>
	internal static TimeSpan KeepPresentingAfter = TimeSpan.FromMilliseconds( 100 );

	/// <summary>How often the window presents its last output again while the main thread stays away (about 30 Hz).</summary>
	internal static TimeSpan KeepPresentingEvery = TimeSpan.FromMilliseconds( 33 );

	// The hand-over the frame thread may present again for (its frame start done), the last the main thread joined, and the
	// window's output then; both hand-over numbers are BorealisGpuFrameThread.HandOvers. Between the main thread's join and a
	// tick, keepLock: once the main thread has joined a hand-over nothing presents for it again
	readonly object keepLock = new();
	long keepFor, keepJoined;
	long keepStarted;
	Borealis.Gpu.TextureHandle keepOutput;

	/// <summary>Frames the window presented again while the main thread was busy, for the tests and stats.</summary>
	internal long FramesPresentedAgain { get; private set; }

	static readonly Sandbox.Diagnostics.PerformanceStats.Timings PresentAgainTiming = Sandbox.Diagnostics.PerformanceStats.Timings.Get( "Borealis.Gpu window presented again" );

	// On the main thread at a hand-over: what the frame thread presents if the main thread stays away
	void ArmKeepPresenting( BorealisGpuWindow window )
	{
		keepOutput = KeepPresenting && window?.Target is { } target && textures.TryGetValue( target, out var backing ) ? backing.Handle : default;
	}

	// On the frame thread at the end of its work for a hand-over (StartAhead): from now on it may present for it
	void StartKeepPresenting()
	{
		if ( !keepOutput.IsValid ) return;
		lock ( keepLock )
		{
			keepFor = frameThread.HandOvers;
			keepStarted = System.Diagnostics.Stopwatch.GetTimestamp();
		}
	}

	// On the main thread before it joins the frame thread: nothing presents for this hand-over again, and a tick going on
	// finishes first
	void StopKeepPresenting()
	{
		if ( frameThread is null ) return;
		lock ( keepLock ) keepJoined = frameThread.HandOvers;
	}

	/// <summary>
	/// On the frame thread once its work for a hand-over is done and until the next (<see cref="BorealisGpuFrameThread"/>'s
	/// idle): present the window's last output again if the main thread hasn't joined in <see cref="KeepPresentingAfter"/>.
	/// Returns how long to wait before asking again, or a negative time to stop asking until the next hand-over.
	/// </summary>
	internal TimeSpan KeepPresentingIdle()
	{
		lock ( keepLock )
		{
			if ( keepFor <= keepJoined || aheadWindow is not { } window || gpu.IsLost ) return System.Threading.Timeout.InfiniteTimeSpan;

			var waited = System.Diagnostics.Stopwatch.GetElapsedTime( keepStarted );
			if ( waited < KeepPresentingAfter ) return KeepPresentingAfter - waited;

			var doing = BorealisGpuFrameThread.Doing;
			BorealisGpuFrameThread.Doing = "presenting the last frame again";
			try
			{
				using ( PresentAgainTiming.Scope() )
				{
					if ( window.PresentAgain( gpu, keepOutput ) ) FramesPresentedAgain++;
				}
			}
			catch ( Exception e )
			{
				// Never expected: said once, and the window waits for the main thread as before
				if ( !warnedPresentAgain )
				{
					warnedPresentAgain = true;
					Log.Warning( e, $"Borealis.Gpu: the window couldn't present its last frame again while the game was busy: {e.Message}" );
				}

				return System.Threading.Timeout.InfiniteTimeSpan;
			}
			finally
			{
				BorealisGpuFrameThread.Doing = doing;
			}

			return KeepPresentingEvery;
		}
	}

	bool warnedPresentAgain;
}

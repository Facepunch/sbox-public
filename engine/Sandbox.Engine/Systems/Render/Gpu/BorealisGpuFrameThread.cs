using Sandbox.Utility;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Sandbox;

/// <summary>
/// The game window's frame thread on Borealis.Gpu (docs/reference/borealis-render-thread.md, step 1): once the main thread
/// has presented a frame it hands the next device frame's start over (the wait for the frame before last on the GPU, the
/// slot's recycle) and the acquire of the window's next image, and goes on with its frame end and the next frame's game
/// code. Its first use of the device in that frame joins (<see cref="BorealisGpuRenderBackend.FollowFrame"/>). Recording,
/// submitting and presenting stay on the main thread.
/// </summary>
internal sealed class BorealisGpuFrameThread : IDisposable
{
	/// <summary>
	/// Whether the game runs one: not with <c>-norenderthread</c> (serial, for A/B runs and bisecting), with
	/// <c>-serialrecording</c>, or with <c>BOREALIS_RENDERTHREAD=0</c>.
	/// </summary>
	internal static readonly bool Enabled = !CommandLine.HasSwitch( "-norenderthread" ) && !CommandLine.HasSwitch( "-serialrecording" )
		&& System.Environment.GetEnvironmentVariable( "BOREALIS_RENDERTHREAD" ) != "0";

	/// <summary>
	/// Whether the thread also draws the game window's frames (the render thread, docs/reference/borealis-render-thread.md):
	/// on with the frame thread, unless <c>-framethread</c> or <c>BOREALIS_RENDERTHREAD=frame</c> keeps the drawing on the main
	/// thread with only the device frame's start on this one. <c>-renderthread</c> is accepted as before.
	/// </summary>
	internal static readonly bool Draws = Enabled && !CommandLine.HasSwitch( "-framethread" ) && System.Environment.GetEnvironmentVariable( "BOREALIS_RENDERTHREAD" ) != "frame";

	/// <summary>The longest <see cref="Dispose"/> waits for the thread; one still busy is left to the process's exit.</summary>
	internal static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds( 2 );

	readonly Action work;
	readonly Func<TimeSpan> idle;
	readonly Thread thread;
	readonly bool timed, markRenderThread;

	// Kept events: waiting on them allocates nothing once each has blocked once
	readonly ManualResetEventSlim go = new( false, 64 );
	readonly ManualResetEventSlim done = new( true, 64 );
	volatile bool stopping;
	ExceptionDispatchInfo error;

	/// <summary>Whether the work handed over has finished (it may not have been joined yet).</summary>
	public bool Done => done.IsSet;

	/// <summary>Handed over and not joined yet (the main thread's view).</summary>
	public bool Pending { get; private set; }

	/// <summary>Hand-overs so far, and those whose join had to wait for the thread, for the tests.</summary>
	public long HandOvers { get; private set; }

	/// <inheritdoc cref="HandOvers"/>
	public long JoinsWaited { get; private set; }

	static readonly Sandbox.Diagnostics.PerformanceStats.Timings JoinTiming = Sandbox.Diagnostics.PerformanceStats.Timings.Get( "Borealis.Gpu frame join" );
	static readonly Sandbox.Diagnostics.PerformanceStats.Timings AheadTiming = Sandbox.Diagnostics.PerformanceStats.Timings.Get( "Borealis.Gpu frame ahead" );

	/// <summary>
	/// A thread that runs <paramref name="work"/> each time it's handed a frame; <paramref name="timed"/> times it and the
	/// joins that wait in the engine's timings (which need the engine's native side). Once the work is done and until the
	/// next hand-over the thread calls <paramref name="idle"/>, if given, again after each time it returns, until it returns a
	/// negative time (the window presenting its last frame again while the main thread is away).
	/// </summary>
	public BorealisGpuFrameThread( Action work, string name = "Borealis.Gpu frame", bool timed = true, bool renderThread = false, Func<TimeSpan> idle = null )
	{
		this.work = work;
		this.idle = idle;
		this.timed = timed;
		markRenderThread = renderThread;
		thread = new Thread( Loop ) { IsBackground = true, Name = name, Priority = ThreadPriority.AboveNormal };
		thread.Start();
	}

	/// <summary>Run the work on the thread. The last hand-over must have been joined.</summary>
	public void HandOver()
	{
		if ( Pending ) throw new InvalidOperationException( "The frame thread's last frame wasn't joined" );
		if ( stopping ) return;
		Pending = true;
		HandOvers++;
		done.Reset();
		go.Set();
	}

	/// <summary>
	/// Wait until the work handed over has finished, and throw what it threw. Nothing when nothing was handed over.
	/// </summary>
	public void Join()
	{
		if ( !Pending ) return;
		Pending = false;
		if ( !done.IsSet )
		{
			JoinsWaited++;
			using ( timed ? JoinTiming.Scope() : default )
				WaitWithoutSyncContext( done, LongJoin, thread.Name, ref warnedLongJoin );
		}

		if ( error is { } e )
		{
			error = null;
			e.Throw();
		}
	}

	static readonly TimeSpan LongJoin = TimeSpan.FromSeconds( 5 );
	bool warnedLongJoin;

	/// <summary>
	/// The main thread's wait for one of the frame's threads (<paramref name="name"/>), without its synchronization context:
	/// the main thread's would run queued game continuations mid-frame (one that uses the device would join or wait again from
	/// inside this wait), and the runtime allocates a handle array for every blocking wait it forwards to it. A wait longer
	/// than <paramref name="longWait"/> is said once with where each thread is, then waits on.
	/// </summary>
	internal static void WaitWithoutSyncContext( ManualResetEventSlim signal, TimeSpan longWait, string name, ref bool warned )
	{
		var syncContext = SynchronizationContext.Current;
		try
		{
			SynchronizationContext.SetSynchronizationContext( null );
			if ( signal.Wait( longWait ) ) return;

			// Never expected
			if ( !warned )
			{
				warned = true;
				Log.Warning( $"The main thread has waited {longWait.TotalSeconds:F0} s for the {name} thread, which is at '{Doing}'; the main thread at {new System.Diagnostics.StackTrace( 1, false )}" );
			}

			signal.Wait();
		}
		finally
		{
			SynchronizationContext.SetSynchronizationContext( syncContext );
		}
	}

	/// <summary>What the thread's work says it's doing (a step's static name), for a join that waits too long.</summary>
	internal static string Doing { get; set; } = "idle";

	void Loop()
	{
		// It draws the frames handed over with the main thread's rendering code (BorealisGpuRenderBackend.HandOverRenderFrame)
		if ( markRenderThread ) ThreadSafe.MarkRenderThread();

		while ( true )
		{
			go.Wait();
			go.Reset();
			if ( stopping ) return;

			try
			{
				using ( timed ? AheadTiming.Scope() : default ) work();
				Doing = "idle";
			}
			catch ( Exception e )
			{
				error = ExceptionDispatchInfo.Capture( e );
			}
			finally
			{
				done.Set();
			}

			// Until the next hand-over, which ends the wait at once
			if ( idle is not null )
			{
				var wait = idle();
				while ( wait >= TimeSpan.Zero && !stopping && !go.Wait( wait ) ) wait = idle();
			}
		}
	}

	/// <summary>
	/// Stop the thread, waiting at most <see cref="StopTimeout"/> for the work it's doing, without the caller's synchronization
	/// context (as <see cref="WaitWithoutSyncContext"/>).
	/// </summary>
	public void Dispose()
	{
		stopping = true;
		var syncContext = SynchronizationContext.Current;
		try
		{
			SynchronizationContext.SetSynchronizationContext( null );
			if ( Pending )
			{
				Pending = false;
				done.Wait( StopTimeout );
			}

			go.Set();
			thread.Join( StopTimeout );
		}
		finally
		{
			SynchronizationContext.SetSynchronizationContext( syncContext );
		}
	}
}

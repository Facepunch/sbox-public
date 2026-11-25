using Borealis.Gpu;
using NativeEngine;
using Sandbox.Utility;
using System.Diagnostics;
using System.Threading;

namespace Sandbox;

/// <summary>
/// The game window's input-to-GPU latency, and the deadline latency mode (docs/reference/borealis-render-thread.md). Each
/// frame's input is timed where it's sampled, and a watcher thread times when the GPU finishes the frame's last work after
/// its present, so <c>Borealis input to GPU</c> is the time from a frame's input to its last GPU work done. With the render
/// thread drawing, the main thread sleeps before input until a deadline: the end of the GPU's work on the frame just handed
/// over, predicted from the frames the GPU finished (their median period over 16), minus what the main thread and the render
/// thread work on the next one before it's submitted (each the median of the last 16 frames; the main thread's
/// without its waits for the render thread) and a 1 ms margin, sleeping no longer than the GPU's own work in a recent frame.
/// Nothing sleeps while a loading screen is up, and the frames behind one don't count: the medians start again from the
/// first frame after it goes. Off with
/// <c>-nolatencymode</c> or <c>BOREALIS_LATENCY=0</c>; nothing sleeps while frames draw on the main thread.
/// </summary>
internal static class BorealisGpuFrameLatency
{
	/// <summary>Whether frames' latency is timed at all: not with <c>BOREALIS_LATENCY=0</c>.</summary>
	internal static readonly bool Timing = System.Environment.GetEnvironmentVariable( "BOREALIS_LATENCY" ) != "0";

	/// <summary>Whether the main thread sleeps to the deadline before input when the render thread draws.</summary>
	internal static readonly bool DeadlineMode = Timing && !CommandLine.HasSwitch( "-nolatencymode" );

	const int Ring = 64;
	const int Window = 16;
	const double MarginMs = 1;

	// Per frame (by number, in a ring): when its input was sampled, how long the main thread took from there to the hand-over,
	// how long the render thread drew it, and when the GPU finished it
	static readonly long[] inputAt = new long[Ring];
	static readonly ulong[] inputFrame = new ulong[Ring];
	static readonly double[] handOverMs = new double[Ring];
	static readonly double[] joinMs = new double[Ring];
	static readonly double[] drawMs = new double[Ring];
	static readonly long[] gpuEndAt = new long[Ring];
	static readonly ulong[] gpuEndFrame = new ulong[Ring];
	static ulong lastGpuFrame;
	static readonly Lock gpuEnds = new();

	// Frames presented and not yet seen done by the GPU, for the watcher: one producer at a time (the thread presenting)
	static readonly (ulong Frame, SyncPoint Point)[] presented = new (ulong, SyncPoint)[Ring];
	static long presentedWritten, presentedRead;
	static readonly SemaphoreSlim presentedCount = new( 0 );
	static Device watched;
	static Thread watcher;

	static readonly Sandbox.Diagnostics.PerformanceStats.Timings LatencyTiming = Sandbox.Diagnostics.PerformanceStats.Timings.Get( "Borealis input to GPU" );
	static readonly Sandbox.Diagnostics.PerformanceStats.Timings SleepTiming = Sandbox.Diagnostics.PerformanceStats.Timings.Get( "Borealis latency sleep" );

	/// <summary>The last frame's input-to-GPU latency in milliseconds, 0 before one is known.</summary>
	internal static double LastMs { get; private set; }

	/// <summary>Frames whose latency was timed, for the tests and stats.</summary>
	internal static long Timed { get; private set; }

	/// <summary>Frames whose input waited for the deadline, for the tests and stats.</summary>
	internal static long Slept { get; private set; }

	static double Ms( long ticks ) => ticks * 1000.0 / Stopwatch.Frequency;

	/// <summary>On the main thread, just before the frame samples input.</summary>
	internal static void InputSampled( ulong frame )
	{
		var i = (int)(frame % Ring);
		inputAt[i] = Stopwatch.GetTimestamp();
		inputFrame[i] = frame;
		handOverMs[i] = 0;
		joinMs[i] = 0;
		drawMs[i] = 0;
	}

	/// <summary>On the main thread: it waited <paramref name="ms"/> to join the render thread in <paramref name="frame"/>.</summary>
	internal static void Joined( ulong frame, double ms )
	{
		var i = (int)(frame % Ring);
		if ( inputFrame[i] == frame ) joinMs[i] += ms;
	}

	/// <summary>
	/// On the main thread: the frame's drawing was handed to the render thread. What it took from its input, less its waits to
	/// join the render thread, is the main thread's part of the frame.
	/// </summary>
	internal static void HandedOver( ulong frame )
	{
		var i = (int)(frame % Ring);
		if ( inputFrame[i] == frame ) handOverMs[i] = Math.Max( Ms( Stopwatch.GetTimestamp() - inputAt[i] ) - joinMs[i], 0.01 );
	}

	/// <summary>On the render thread: it drew the frame in <paramref name="ms"/>.</summary>
	internal static void Drawn( ulong frame, double ms ) => drawMs[(int)(frame % Ring)] = ms;

	/// <summary>
	/// On the thread that presented <paramref name="frame"/> (the render thread, or the main thread drawing inline): the GPU's
	/// last work for it is <paramref name="point"/>, which the watcher waits for.
	/// </summary>
	internal static void Presented( Device device, ulong frame, SyncPoint point )
	{
		if ( !Timing || stopped || device is null || point.Value == 0 ) return;
		if ( watched != device )
		{
			// The game window's device; another one (a test's) isn't watched
			if ( watched is not null ) return;
			watched = device;
			watcher = new Thread( Watch ) { IsBackground = true, Name = "Borealis.Gpu latency", Priority = ThreadPriority.AboveNormal };
			watcher.Start();
		}

		var written = Interlocked.Read( ref presentedWritten );
		if ( written - Interlocked.Read( ref presentedRead ) >= Ring ) return;
		presented[written % Ring] = (frame, point);
		Interlocked.Increment( ref presentedWritten );
		presentedCount.Release();
	}

	/// <summary>
	/// The game window's device is going (the process is ending): the watcher lets go of it and ends, and nothing is watched
	/// from then on. A watcher left waiting past the device crashed the process at exit now and then.
	/// </summary>
	internal static void Forget( Device device )
	{
		if ( watched != device ) return;
		stopped = true;
		Interlocked.Exchange( ref watched, null );
		presentedCount.Release();
		watcher?.Join( 1000 );
		watcher = null;
	}

	static volatile bool stopped;


	static void Watch()
	{
		while ( true )
		{
			presentedCount.Wait();
			if ( stopped ) return;
			var read = Interlocked.Read( ref presentedRead );
			var (frame, point) = presented[read % Ring];
			Interlocked.Increment( ref presentedRead );

			var device = Volatile.Read( ref watched );
			while ( device is { IsLost: false } && !stopped && !device.TryWait( point, TimeSpan.FromMilliseconds( 250 ) ) ) device = Volatile.Read( ref watched );
			if ( device is null || device.IsLost || stopped ) continue;

			GpuFinished( frame, Stopwatch.GetTimestamp() );
		}
	}

	/// <summary>The GPU finished <paramref name="frame"/>'s last work at <paramref name="at"/> (a <see cref="Stopwatch"/> timestamp).</summary>
	internal static void GpuFinished( ulong frame, long at )
	{
		var i = (int)(frame % Ring);
		lock ( gpuEnds )
		{
			gpuEndAt[i] = at;
			gpuEndFrame[i] = frame;
			if ( frame > lastGpuFrame ) lastGpuFrame = frame;
		}

		if ( inputFrame[i] != frame ) return;
		var ms = Ms( at - inputAt[i] );
		LastMs = ms;
		Timed++;
		LatencyTiming.AddMillisecondsFromAnyThread( ms );
	}

	/// <summary>
	/// On the main thread before the next frame samples input, once <paramref name="handedOver"/> (the frame just ended) was
	/// handed to the render thread: sleep to the deadline, if it's ahead. The time slept, in milliseconds.
	/// </summary>
	internal static double SleepBeforeInput( ulong handedOver )
	{
		if ( !DeadlineMode || watched is null ) return 0;

		var now = Stopwatch.GetTimestamp();
		var sleep = PlannedSleep( handedOver, now, LoadingScreen.IsVisible, (RenderBackend.Default as BorealisGpuRenderBackend)?.GpuBusyMs );
		if ( sleep < 0.1 ) return 0;

		Sdl.DelayPrecise( (ulong)(sleep * 1_000_000) );
		var slept = Ms( Stopwatch.GetTimestamp() - now );
		SleepTiming.AddMilliseconds( slept );
		Slept++;
		return slept;
	}

	// The first frame the deadline is predicted from: the frames behind a loading screen (seconds of loading, then the slow
	// frames the screen holds while the first ones draw) say nothing about the GPU's period once it goes
	static ulong firstSteady;

	/// <summary>
	/// How long the main thread should sleep at <paramref name="now"/> (a <see cref="Stopwatch"/> timestamp) before the frame
	/// after <paramref name="handedOver"/> samples input, in milliseconds: 0 while <paramref name="loading"/> (a loading screen
	/// is up) and until enough frames after it are known, and never longer than <paramref name="gpuBusyMs"/>, the GPU's own
	/// work in a recent frame. Where the GPU sets the pace the deadline is nearer than that anyway; where the frames do (slow
	/// ones after a load, a hitch), the GPU's period is theirs, and a sleep as long as it made the next period longer still.
	/// </summary>
	internal static double PlannedSleep( ulong handedOver, long now, bool loading, double? gpuBusyMs = null )
	{
		if ( loading )
		{
			firstSteady = handedOver + 1;
			return 0;
		}

		Span<double> periods = stackalloc double[Window];
		Span<double> handOvers = stackalloc double[Window];
		Span<double> draws = stackalloc double[Window];
		int p = 0, h = 0, d = 0;
		ulong lastFrame;
		long lastEnd;
		lock ( gpuEnds )
		{
			lastFrame = lastGpuFrame;
			if ( lastFrame == 0 || gpuEndFrame[(int)(lastFrame % Ring)] != lastFrame ) return 0;
			lastEnd = gpuEndAt[(int)(lastFrame % Ring)];

			// The GPU's period: between consecutive frames it finished
			for ( ulong f = lastFrame; f > 1 && f - 1 >= firstSteady && p < Window; f-- )
			{
				int a = (int)(f % Ring), b = (int)((f - 1) % Ring);
				if ( gpuEndFrame[a] != f || gpuEndFrame[b] != f - 1 ) break;
				periods[p++] = Ms( gpuEndAt[a] - gpuEndAt[b] );
			}
		}

		for ( ulong f = handedOver; f > 0 && f >= firstSteady && handedOver - f < Window; f-- )
		{
			var i = (int)(f % Ring);
			if ( inputFrame[i] != f ) break;
			if ( handOverMs[i] > 0 ) handOvers[h++] = handOverMs[i];
			if ( drawMs[i] > 0 ) draws[d++] = drawMs[i];
		}

		if ( p < Window / 2 || h < Window / 2 || d < Window / 2 || handedOver <= lastFrame ) return 0;

		// When the GPU finishes the frame handed over, which the next one should be submitted just before
		var period = Percentile( periods[..p], 0.5 );
		var end = Ms( lastEnd ) + (handedOver - lastFrame) * period;
		var deadline = end - Percentile( handOvers[..h], 0.5 ) - Percentile( draws[..d], 0.5 ) - MarginMs;
		return Math.Max( Math.Min( Math.Min( deadline - Ms( now ), period ), gpuBusyMs ?? period ), 0 );
	}

	/// <summary>Forget every frame timed, for the tests.</summary>
	internal static void ResetForTests()
	{
		lock ( gpuEnds )
		{
			Array.Clear( inputFrame );
			Array.Clear( gpuEndFrame );
			lastGpuFrame = 0;
		}
		firstSteady = 0;
	}

	static double Percentile( Span<double> values, double at )
	{
		values.Sort();
		return values[Math.Clamp( (int)(at * (values.Length - 1) + 0.5), 0, values.Length - 1 )];
	}
}

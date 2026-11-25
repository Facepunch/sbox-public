using System.Diagnostics;
using Sandbox;

namespace BorealisTests;

/// <summary>
/// The deadline latency mode's sleep (<see cref="BorealisGpuFrameLatency.PlannedSleep"/>): predicted from the frames the
/// GPU finished, but not from the slow frames behind a loading screen, which once made the first frames shown after a load
/// sleep for 60-150 ms each.
/// </summary>
[TestClass]
[DoNotParallelize]
public class FrameLatencyTests
{
	static readonly long Start = Stopwatch.GetTimestamp();
	static long At( double ms ) => Start + (long)(ms * Stopwatch.Frequency / 1000);

	[TestInitialize]
	public void Reset() => BorealisGpuFrameLatency.ResetForTests();

	/// <summary>A frame handed over at <paramref name="ms"/> that the render thread drew in <paramref name="drawMs"/>.</summary>
	static void HandOver( ulong frame, double drawMs )
	{
		BorealisGpuFrameLatency.InputSampled( frame );
		BorealisGpuFrameLatency.HandedOver( frame );
		BorealisGpuFrameLatency.Drawn( frame, drawMs );
	}

	[TestMethod]
	public void SteadyFramesSleepToTheDeadline()
	{
		// 10 ms frames, each finished by the GPU 25 ms after it was handed over
		double sleep = 0;
		for ( ulong f = 1; f <= 40; f++ )
		{
			HandOver( f, 5 );
			if ( f > 2 ) BorealisGpuFrameLatency.GpuFinished( f - 2, At( (f - 2) * 10 + 25 ) );
			sleep = BorealisGpuFrameLatency.PlannedSleep( f, At( f * 10 ), loading: false );
		}

		Assert.IsTrue( sleep > 0 && sleep <= 10, $"slept {sleep:F1} ms" );
	}

	[TestMethod]
	public void SlowFramesDontSleepPastTheGpusWork()
	{
		// 140 ms frames (slow ones after a load, or sleeps feeding the next period), the GPU busy 12 ms in each and done
		// 15 ms after each was handed over
		for ( ulong f = 1; f <= 20; f++ )
		{
			HandOver( f, 5 );
			BorealisGpuFrameLatency.GpuFinished( f, At( f * 140 + 15 ) );
		}

		// Then a quick frame: the GPU finishes it 12 ms after it's handed over, whatever the last periods were
		HandOver( 21, 5 );
		var sleep = BorealisGpuFrameLatency.PlannedSleep( 21, At( 20 * 140 + 25 ), loading: false, gpuBusyMs: 12 );
		Assert.IsTrue( sleep <= 12, $"slept {sleep:F1} ms" );
	}

	[TestMethod]
	public void FramesBehindALoadingScreenDontCount()
	{
		// 20 frames behind the screen, 200 ms each
		for ( ulong f = 1; f <= 20; f++ )
		{
			HandOver( f, 150 );
			if ( f > 1 ) BorealisGpuFrameLatency.GpuFinished( f - 1, At( (f - 1) * 200 + 150 ) );
			Assert.AreEqual( 0, BorealisGpuFrameLatency.PlannedSleep( f, At( f * 200 ), loading: true ), "slept behind the screen" );
		}

		// Then it goes: 10 ms frames, finished 8 ms after each was handed over
		BorealisGpuFrameLatency.GpuFinished( 20, At( 20 * 200 + 150 ) );
		for ( ulong f = 21; f <= 60; f++ )
		{
			HandOver( f, 5 );
			var now = 4000 + (f - 20) * 10.0;
			var sleep = BorealisGpuFrameLatency.PlannedSleep( f, At( now ), loading: false );
			Assert.IsTrue( sleep <= 10, $"frame {f} after the screen slept {sleep:F1} ms" );
			BorealisGpuFrameLatency.GpuFinished( f, At( now + 8 ) );
		}
	}
}

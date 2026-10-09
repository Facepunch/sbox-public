using NativeEngine;
using Sandbox.Utility;

namespace Sandbox.Engine;

/// <summary>
/// Keeps frames on an absolute deadline grid, avoiding drift from work and sleep overhead.
/// Shared by game, editor and headless loops; the caller supplies the frame-rate limit.
/// </summary>
[SkipHotload]
internal sealed class FramePacer
{
	static readonly Superluminal sleepScope = new( "Sleep For Max FPS", Color.Gray );
	readonly FastTimer clock = FastTimer.StartNew();
	double nextFrameDeadlineMs;

	/// <summary>Wait for the next frame deadline and return the actual time spent waiting, in milliseconds.</summary>
	internal double Wait( double framesPerSecond )
	{
		double nowMs = clock.ElapsedMilliSeconds;
		double deadlineMs = GetNextDeadline( nowMs, framesPerSecond );
		if ( nowMs >= deadlineMs ) return 0;

		using var _ = sleepScope.Start();
		// SDL handles high-resolution sleeping and the final spin; our deadlines absorb any oversleep.
		Sdl.DelayPrecise( (ulong)((deadlineMs - nowMs) * 1_000_000) );

		return clock.ElapsedMilliSeconds - nowMs;
	}

	internal double GetNextDeadline( double nowMs, double framesPerSecond )
	{
		if ( framesPerSecond <= 0 )
			return nextFrameDeadlineMs = 0;

		double periodMs = Math.Min( 1000.0 / framesPerSecond, 100 ); // minimum 10 fps
		double deadlineMs = nextFrameDeadlineMs > 0 ? nextFrameDeadlineMs + periodMs : nowMs + periodMs;

		// After a hitch, resume from now instead of trying to catch up in a burst.
		if ( nowMs - deadlineMs > periodMs )
			deadlineMs = nowMs;

		return nextFrameDeadlineMs = deadlineMs;
	}
}

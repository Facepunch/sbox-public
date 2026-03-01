
using Sandbox.Diagnostics;

namespace Sandbox;

/// <summary>
/// Access to time.
/// </summary>
public static class RealTime
{
	static RealTime()
	{
		timeMeasure = FastTimer.StartNew();

		var epoch = new DateTime( 2022, 1, 1, 1, 1, 1, DateTimeKind.Utc );
		var now = DateTime.UtcNow;

		nowOffset = (now - epoch).TotalSeconds;
	}

	static FastTimer timeMeasure;
	static double nowOffset;

	/// <summary>
	/// The time since the game startup, in seconds.
	/// </summary>
	public static float Now => (float)NowDouble;

	/// <summary>
	/// The time since the game startup as a double, in seconds.
	/// </summary>
	public static double NowDouble => timeMeasure.ElapsedSeconds;

	/// <summary>
	/// The number of a seconds since a set point in time. This value should match between servers and clients. If they have their timezone set correctly.
	/// </summary>
	public static double GlobalNow => (nowOffset + timeMeasure.ElapsedSeconds);

	/// <summary>
	/// The time delta (in seconds) between the last frame and the current (for all intents and purposes)
	/// </summary>
	public static float Delta { get; internal set; }

	/// <summary>
	/// Like Delta but smoothed to avoid large disparities between deltas
	/// </summary>
	public static float SmoothDelta { get; internal set; }

	static double LastTick;

	internal static void Update( double now )
	{
		if ( LastTick > 0 )
		{
			Delta = (float)(now - LastTick).Clamp( 0.0, 2.0 );

			SmoothDelta = MathX.Lerp( SmoothDelta, Delta, 0.1f );
		}

		LastTick = now;
	}
}

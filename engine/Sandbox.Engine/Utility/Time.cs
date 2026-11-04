using Sandbox.Utility;

namespace Sandbox;

[Expose]
public class Time
{
	/// <summary>
	/// The time since the game startup.
	/// </summary>
	public static float Now { get; set; }

	/// <summary>
	/// The delta between the last frame and the current (for all intents and purposes).
	/// </summary>
	public static float Delta { get; set; }

	/// <summary>
	/// The time since the game startup as a double.
	/// </summary>
	public static double NowDouble { get; set; }

	// Audio.Time , Audio.TimeDelta - if these are needed

	//public static double Sound => g_pSoundSystem.AudioStateHostTime();
	//public static double SoundDelta => g_pSoundSystem.AudioStateFrameTime();

	internal static void Update( double now, double delta )
	{
		Now = (float)now;
		Delta = (float)delta;
		NowDouble = now;
	}

	/// <summary>
	/// Temporarily override the game clock, restoring it when the scope is disposed.
	/// </summary>
	public static IDisposable Scope( double now, double delta ) => PushScope( now, delta );

	/// <summary>
	/// <see cref="Scope"/> without allocating. Use with <c>using var</c>.
	/// </summary>
	internal static TimeScope PushScope( double now, double delta ) => new( now, delta );

	internal struct TimeScope : IDisposable
	{
		readonly double _nowDouble;
		readonly float _delta;
		readonly float _now;

		public TimeScope( double now, double delta )
		{
			_nowDouble = NowDouble;
			_delta = Delta;
			_now = Now;

			Update( now, delta );
		}

		public readonly void Dispose()
		{
			NowDouble = _nowDouble;
			Delta = _delta;
			Now = _now;
		}
	}
}

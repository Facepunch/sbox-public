namespace Sandbox.Audio;

/// <summary>
/// Velocity from position changes, smoothed so uneven frame times don't make the pitch warble. A jump for one update (a
/// teleport, a respawn, a network correction, a camera cut) is ignored; a new speed that holds for two updates is taken.
/// </summary>
internal struct VelocityTracker
{
	const float HalfLife = 0.03f;

	// A change of speed between two updates bigger than this isn't movement: about 50 m/s, thousands of g at 60 fps
	const float MaxJump = 50f * 39.37f;

	Vector3 _last, _lastRaw;
	double _lastTime;
	bool _started;

	public Vector3 Velocity { get; private set; }

	/// <summary>
	/// A speed has been measured. Until then <see cref="Velocity"/> is zero and means nothing.
	/// </summary>
	public bool Known { get; private set; }

	public void Update( Vector3 position, double time )
	{
		if ( !_started )
		{
			_started = true;
			_last = position;
			_lastTime = time;
			return;
		}

		float dt = (float)(time - _lastTime);
		if ( dt <= 0f ) return;

		var velocity = (position - _last) / dt;
		_last = position;
		_lastTime = time;

		bool jump = (velocity - _lastRaw).Length > MaxJump;
		_lastRaw = velocity;
		if ( jump ) return;

		Velocity = Known ? Vector3.Lerp( Velocity, velocity, 1f - MathF.Pow( 0.5f, dt / HalfLife ) ) : velocity;
		Known = true;
	}
}

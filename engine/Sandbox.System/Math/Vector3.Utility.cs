using Sandbox;

public partial struct Vector3
{
	/// <summary>
	/// This direction randomly deflected within a cone <paramref name="degrees"/> wide, centred on it.
	/// Use for bullet spread. Pass a <paramref name="random"/> to control the randomness (e.g. a seeded
	/// one, so peers agree on the deflection) - null uses the shared random.
	/// </summary>
	public readonly Vector3 WithAimCone( float degrees, System.Random random = null ) => WithAimCone( degrees, degrees, random );

	/// <summary>
	/// This direction randomly deflected within a cone <paramref name="horizontalDegrees"/> wide and
	/// <paramref name="verticalDegrees"/> tall, centred on it. Use for bullet spread. Pass a
	/// <paramref name="random"/> to control the randomness (e.g. a seeded one, so peers agree on the
	/// deflection) - null uses the shared random.
	/// </summary>
	public readonly Vector3 WithAimCone( float horizontalDegrees, float verticalDegrees, System.Random random = null )
	{
		// LookAt on a near-zero vector would deflect it to an arbitrary direction - leave it alone.
		if ( IsNearZeroLength )
			return this;

		random ??= System.Random.Shared;

		var rotation = Rotation.LookAt( this );

		rotation *= new Angles(
			random.Float( -verticalDegrees, verticalDegrees ) * 0.5f,
			random.Float( -horizontalDegrees, horizontalDegrees ) * 0.5f,
			0 );

		return rotation.Forward;
	}

	/// <summary>
	/// Everything you need to smooth damp a Vector3. Just call Update every frame.
	/// </summary>
	public record struct SmoothDamped( Vector3 Current, Vector3 Target, float SmoothTime )
	{
		public Vector3 Velocity;

		public void Update( float timeDelta )
		{
			Current = SmoothDamp( Current, Target, ref Velocity, SmoothTime, timeDelta );
		}
	}

	/// <summary>
	/// Everything you need to create a springy Vector3
	/// </summary>
	public record struct SpringDamped
	{
		public Vector3 Current;
		public Vector3 Target;
		public float Frequency;
		public float Damping;
		public Vector3 Velocity;

		[Obsolete]
		public float SmoothTime;

		public SpringDamped( Vector3 current, Vector3 target, float frequency = 2.0f, float damping = 0.5f )
		{
			Current = current;
			Target = target;
			Frequency = frequency;
			Damping = damping;
		}

		[Obsolete( "SmoothTime is deprecated and no longer used. Use the constructor without SmoothTime instead." )]
		public SpringDamped( Vector3 current, Vector3 target, float smoothTime, float frequency = 2.0f, float damping = 0.5f )
		{
			Current = current;
			Target = target;
			Frequency = frequency;
			Damping = damping;
		}

		public void Update( float timeDelta )
		{
			Current = SpringDamp( Current, Target, ref Velocity, timeDelta, Frequency, Damping );
		}
	}
}

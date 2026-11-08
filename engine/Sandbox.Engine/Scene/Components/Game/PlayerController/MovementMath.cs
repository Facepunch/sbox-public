namespace Sandbox.Movement;

internal static class MovementMath
{
	internal const float NonJumpVelocity = 140.0f;
	internal const float JumpSpeed = 268.3281572999747f;

	internal static float SurfaceFriction( float friction ) => MathF.Min( friction * 1.25f, 1.0f );

	internal static Vector3 Friction( Vector3 velocity, float friction, float stopSpeed, float surfaceFriction, float delta )
	{
		var speed = velocity.Length;
		if ( speed < 0.1f ) return velocity;
		var control = speed < stopSpeed ? stopSpeed : speed;
		var drop = control * (friction * surfaceFriction) * delta;
		var newSpeed = MathF.Max( speed - drop, 0 );
		return newSpeed == speed ? velocity : velocity * (newSpeed / speed);
	}

	internal static Vector3 Accelerate( Vector3 velocity, Vector3 direction, float wishSpeed, float acceleration, float surfaceFriction, float delta )
	{
		var addSpeed = wishSpeed - velocity.Dot( direction );
		if ( addSpeed <= 0 ) return velocity;
		var speed = acceleration * delta * wishSpeed * surfaceFriction;
		return velocity + direction * MathF.Min( speed, addSpeed );
	}

	internal static Vector3 AirAccelerate( Vector3 velocity, Vector3 direction, float wishSpeed, float acceleration, float cap, float surfaceFriction, float delta )
	{
		var addSpeed = MathF.Min( wishSpeed, cap ) - velocity.Dot( direction );
		if ( addSpeed <= 0 ) return velocity;
		// Uncapped wish speed and multiplication order preserve reference behavior.
		var speed = acceleration * wishSpeed * delta * surfaceFriction;
		return velocity + direction * MathF.Min( speed, addSpeed );
	}

	// Keep the operation order of each half-step for reference rounding.
	internal static float StartGravity( float velocity, float gravity, float scale, float delta )
	{
		return (float)(velocity - (scale * gravity * 0.5 * delta));
	}

	internal static float FinishGravity( float velocity, float gravity, float scale, float delta )
	{
		return (float)(velocity - (scale * gravity * delta * 0.5));
	}

	internal static float Jump( float velocity, float speed, bool ducked )
	{
		return ducked ? speed : velocity + speed;
	}

	internal static Vector3 CheckVelocity( Vector3 velocity, float maximum )
	{
		for ( int i = 0; i < 3; i++ )
		{
			var value = velocity[i];
			velocity[i] = float.IsNaN( value ) ? 0 : value.Clamp( -maximum, maximum );
		}
		return velocity;
	}
}

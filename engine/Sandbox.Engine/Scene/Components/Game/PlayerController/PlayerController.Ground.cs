namespace Sandbox;

public sealed partial class PlayerController : Component
{
	/// <summary>
	/// The object we're standing on. Null if we're standing on nothing.
	/// </summary>
	public GameObject GroundObject { get; set; }

	/// <summary>
	/// The collider component we're standing on. Null if we're standing nothing
	/// </summary>
	public Component GroundComponent { get; set; }

	/// <summary>
	/// If we're stnding on a surface this is it
	/// </summary>
	public Surface GroundSurface { get; set; }

	/// <summary>
	/// The friction property of the ground we're standing on.
	/// </summary>
	public float GroundFriction { get; set; }

	/// <summary>
	/// Are we standing on a surface that is physically dynamic
	/// </summary>
	public bool GroundIsDynamic { get; set; }

	TimeUntil _timeUntilAllowedGround = 0;

	/// <summary>
	/// Amount of time since this character was last on the ground
	/// </summary>
	public TimeSince TimeSinceGrounded { get; private set; } = 0;

	/// <summary>
	/// Amount of time since this character was last not on the ground
	/// </summary>
	public TimeSince TimeSinceUngrounded { get; private set; } = 0;

	/// <summary>
	/// Prevent being grounded for a number of seconds
	/// </summary>
	public void PreventGrounding( float seconds )
	{
		_timeUntilAllowedGround = MathF.Max( _timeUntilAllowedGround, seconds );
		UpdateGroundFromTraceResult( default );
	}

	/// <summary>
	/// Restores grounding, meant to be called during PreventGrounding( float seconds )
	/// </summary>
	public void RestoreGrounding()
	{
		_timeUntilAllowedGround = 0;
	}

	/// <summary>
	/// Lift player up and place a skin level above the ground
	/// </summary>
	internal void Reground( float stepSize )
	{
		if ( !IsOnGround )
			return;

		// Don't keep regrounding if we're not moving
		if ( Body.Sleeping )
			return;

		var currentPosition = WorldPosition;

		float radiusScale = 1.0f;
		var tr = TraceBody( currentPosition + Vector3.Up * 1, currentPosition + Vector3.Down * stepSize, radiusScale, 0.5f );

		while ( tr.StartedSolid )
		{
			radiusScale = radiusScale - 0.1f;
			if ( radiusScale < 0.7f )
				return;

			tr = TraceBody( currentPosition + Vector3.Up * 1, currentPosition + Vector3.Down * stepSize, radiusScale, 0.5f );
		}

		if ( tr.StartedSolid )
		{
			return;
		}

		if ( tr.Hit )
		{
			var targetPosition = tr.EndPosition + Vector3.Up * 0.01f;
			var delta = currentPosition - targetPosition;
			if ( delta == Vector3.Zero ) return;

			WorldPosition = targetPosition;

			// when stepping down, clear out the gravity velocity to avoid
			// it thinking we're falling and building up like crazy
			if ( delta.z > 0.01f )
			{
				var velocity = Body.Velocity;
				velocity.z = 0;
				Body.Velocity = velocity;
			}
		}
	}

	void CategorizeGround()
	{
		var groundVel = GroundVelocity.z;
		bool wasOnGround = IsOnGround;

		if ( !Mode.AllowGrounding )
		{
			PreventGrounding( 0.1f );
			UpdateGroundFromTraceResult( default );
			return;
		}

		// ground is pushing us crazy, stop being grounded
		if ( groundVel > 250 )
		{
			PreventGrounding( 0.3f );
			UpdateGroundFromTraceResult( default );
			return;
		}

		var velocity = Velocity - GroundVelocity;
		if ( _timeUntilAllowedGround > 0 || groundVel > 300 )
		{
			UpdateGroundFromTraceResult( default );
			return;
		}

		var from = WorldPosition + Vector3.Up * 4;
		var to = WorldPosition + Vector3.Down * 2;

		float radiusScale = 1;
		var tr = TraceBody( from, to, radiusScale, 0.5f );

		while ( tr.StartedSolid || (tr.Hit && !Mode.IsStandableSurface( tr )) )
		{
			radiusScale = radiusScale - 0.1f;
			if ( radiusScale < 0.7f )
			{
				UpdateGroundFromTraceResult( default );
				return;
			}

			tr = TraceBody( from, to, radiusScale, 0.5f );
		}

		if ( tr.StartedSolid )
		{
			UpdateGroundFromTraceResult( default );
			return;
		}

		if ( !tr.StartedSolid && tr.Hit && Mode.IsStandableSurface( tr ) )
		{
			UpdateGroundFromTraceResult( tr );
		}
		else
		{
			UpdateGroundFromTraceResult( default );
		}
	}

	void UpdateGroundFromTraceResult( SceneTraceResult tr )
	{
		var wasGrounded = IsOnGround;

		var body = tr.Body;

		GroundObject = body?.GameObject;
		GroundComponent = body?.Component;
		GroundSurface = tr.Surface;
		GroundIsDynamic = true;

		if ( GroundObject is not null )
		{
			TimeSinceGrounded = 0;
			_groundTransform = GroundObject.WorldTransform;
			GroundFriction = tr.Surface.Friction;

			if ( tr.Component is Collider collider )
			{
				if ( collider.Friction.HasValue )
					GroundFriction = collider.Friction.Value;

				GroundIsDynamic = collider.IsDynamic;
			}
		}
		else
		{
			TimeSinceUngrounded = 0;
			_groundTransform = default;
		}

		if ( wasGrounded != IsOnGround )
		{
			UpdateBody();
		}
	}
}

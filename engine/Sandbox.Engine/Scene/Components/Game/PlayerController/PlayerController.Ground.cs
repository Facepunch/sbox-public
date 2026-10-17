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
	/// The normal of the walkable surface beneath us.
	/// </summary>
	public Vector3 GroundNormal { get; private set; } = Vector3.Up;

	/// <summary>
	/// Are we standing on a surface that is physically dynamic
	/// </summary>
	public bool GroundIsDynamic { get; set; }

	TimeUntil _timeUntilAllowedGround = 0;
	PhysicsShape _groundShape;

	// Moving surfaces need native contact forces rather than static-ground walking.
	internal bool GroundUsesContactFriction
	{
		get
		{
			if ( GroundIsDynamic )
				return true;

			if ( !_groundShape.IsValid() )
				return false;

			var body = _groundShape.Body;
			// Keyframe controllers can clear instantaneous velocity before the next physics step.
			return !_groundShape.SurfaceVelocity.IsNearZeroLength
				|| (body.BodyType == PhysicsBodyType.Keyframed
					&& (!GroundVelocity.IsNearZeroLength || !body.Velocity.IsNearZeroLength || !body.AngularVelocity.IsNearZeroLength));
		}
	}

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
		if ( stepSize <= 0 || !CanStep || GroundUsesContactFriction )
			return;

		var from = WorldPosition;
		var drop = stepSize * MathF.Abs( WorldTransform.UniformScale );
		if ( !TryFindGround( from, 2, drop, out var ground, out var landing ) )
			return;

		if ( ground.Body?.BodyType == PhysicsBodyType.Dynamic )
			return;

		var height = (landing - from).Dot( UpDirection );
		if ( height > _groundedFeetLift + _skin || height < -drop - _skin )
			return;

		var downward = TraceMovement( from, landing, _stepClearance );
		if ( downward.StartedSolid || (downward.Hit && (downward.EndPosition - landing).Length > _stepClearance)
			|| TraceMovement( landing, landing ).StartedSolid )
			return;

		ApplyGroundStep( landing, ground );
	}

	void CategorizeGround()
	{
		var up = UpDirection;
		var groundVel = GroundVelocity.Dot( up );

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

		if ( _timeUntilAllowedGround > 0 || groundVel > 300 )
		{
			UpdateGroundFromTraceResult( default );
			return;
		}

		if ( TryFindGround( WorldPosition, 4, 2, out var tr, out _ ) )
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
		_groundShape = tr.Shape;
		GroundSurface = tr.Surface;
		GroundNormal = tr.Hit ? tr.Normal : UpDirection;
		GroundIsDynamic = true;

		if ( GroundObject is not null )
		{
			TimeSinceGrounded = 0;
			_groundTransform = GroundObject.WorldTransform;
			var friction = _groundShape.IsValid() ? _groundShape.Friction : -1;
			GroundFriction = friction >= 0 ? friction : tr.Surface.Friction;
			GroundIsDynamic = body.BodyType == PhysicsBodyType.Dynamic;
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

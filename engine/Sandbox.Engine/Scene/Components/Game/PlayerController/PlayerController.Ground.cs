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
	/// The surface we're standing on.
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
	bool _canCoyoteJump;
	internal bool CanCoyoteJump => _canCoyoteJump && CoyoteTime > 0 && TimeSinceGrounded <= CoyoteTime;

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
		var preciseDrop = !Scene.Is2D && _groundShape.IsValid() && _groundShape.IsHullShape;
		if ( preciseDrop )
		{
			// Refinement rays must not snap through a steep hull to the floor behind it.
			var upward = TraceMovement( from, from + UpDirection * 2, 0 );
			var top = upward.StartedSolid ? from : upward.EndPosition;
			var support = TraceMovement( top, from - UpDirection * drop, 0, includeGroundClearance: true );
			if ( support.Hit && (support.StartedSolid ? support.Shape != _groundShape : !Mode.IsStandableSurface( support )) ) return;
		}
		if ( !TryFindGround( from, 2, drop, out var ground, out var landing, preciseDrop: preciseDrop ) )
			return;

		if ( ground.Body?.BodyType == PhysicsBodyType.Dynamic )
			return;

		var height = (landing - from).Dot( UpDirection );
		if ( height > _groundedFeetLift + _skin * 2 || height < -drop - _skin )
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

		if ( !Mode.AllowGrounding )
		{
			PreventGrounding( 0.1f );
			UpdateGroundFromTraceResult( default );
			return;
		}

		var relative = Body.Velocity - GroundVelocity;
		var movingUpRapidly = relative.Dot( up ) > Movement.MovementMath.NonJumpVelocity;
		if ( IsOnGround )
		{
			// Slope following is not a jump; explicit jumps already clear ground.
			if ( !GroundUsesContactFriction && Mode is Movement.MoveModeWalk walk && walk.IsStaticGroundMove ) movingUpRapidly = false;
			else movingUpRapidly &= relative.Dot( GroundNormal ) > Movement.MovementMath.NonJumpVelocity;
		}
		if ( _timeUntilAllowedGround > 0 || movingUpRapidly )
		{
			UpdateGroundFromTraceResult( default );
			return;
		}

		var scale = MathF.Abs( WorldTransform.UniformScale );
		var preciseGround = !IsOnGround || (!Scene.Is2D && !GroundUsesContactFriction && _groundShape.IsValid() && _groundShape.IsHullShape);
		if ( !TryFindGround( WorldPosition, 4 * scale, 2 * scale, out var ground, out _, requireContact: !IsOnGround, preciseDrop: preciseGround ) )
			ground = FindGroundSupport( up, scale, preciseGround );
		UpdateGroundFromTraceResult( ground );
	}

	SceneTraceResult FindGroundSupport( Vector3 up, float scale, bool preciseGround )
	{
		var hull = BodyBox( 1, 0.5f );
		SceneTraceResult Probe( BBox bounds ) => Scene.Trace.Box( bounds, WorldPosition + up * (4 * scale), WorldPosition - up * (2 * scale) )
			.IgnoreGameObjectHierarchy( GameObject ).WithCollisionRules( Tags ).UseHitPosition().Run();

		var supportHull = new BBox( hull.Mins * scale, hull.Maxs * scale );
		var support = Probe( supportHull );
		if ( IsGroundSupport( support, supportHull, up, scale, preciseGround ) ) return support;

		// Probe quadrants when a steep face masks walkable ground.
		for ( int quadrant = 0; quadrant < 4; quadrant++ )
		{
			var mins = hull.Mins * scale;
			var maxs = hull.Maxs * scale;
			if ( (quadrant & 1) == 0 ) maxs.x = 0; else mins.x = 0;
			var secondAxis = Scene.Is2D ? 2 : 1;
			if ( (quadrant & 2) == 0 ) maxs[secondAxis] = 0; else mins[secondAxis] = 0;
			supportHull = new BBox( mins, maxs );
			support = Probe( supportHull );
			if ( IsGroundSupport( support, supportHull, up, scale, preciseGround ) ) return support;
		}
		return default;
	}

	bool IsGroundSupport( SceneTraceResult hit, BBox hull, Vector3 up, float scale, bool preciseGround )
	{
		if ( !hit.Hit || !Mode.IsStandableSurface( hit ) ) return false;
		if ( preciseGround && !hit.StartedSolid && hit.Body?.BodyType == PhysicsBodyType.Static && hit.Shape.IsValid() && hit.Shape.IsHullShape )
		{
			// Use the surface plane, not the sweep's separation-biased endpoint.
			var normal = hit.Normal;
			var footprint = normal.x * (normal.x >= 0 ? hull.Mins.x : hull.Maxs.x)
				+ normal.y * (normal.y >= 0 ? hull.Mins.y : hull.Maxs.y)
				+ normal.z * (normal.z >= 0 ? hull.Mins.z : hull.Maxs.z);
			if ( (WorldPosition - hit.HitPosition).Dot( normal ) + footprint >= 2 * scale * normal.Dot( up ) ) return false;
		}
		if ( !hit.StartedSolid ) return true;

		// Retain embedded support only with native contact.
		return IsOnGround && _groundShape.IsValid()
			&& hit.Body == _groundShape.Body && Body.PhysicsBody.IsTouching( hit.Body, false );
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
			_canCoyoteJump = true;
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

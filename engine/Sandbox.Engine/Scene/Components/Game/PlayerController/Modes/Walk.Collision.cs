namespace Sandbox.Movement;

public partial class MoveModeWalk
{
	bool _hasStaticMove;
	bool _nearStaticSurface;
	Vector3 _staticMovePosition;
	Vector3 _staticMoveVelocity;
	PhysicsShape _previousSlideContact;
	Vector3 _previousSlideNormal;
	internal bool IsStaticGroundMove => _physicsStep && !HasPhysicalObstacle( _movementStart, Controller.Body.PhysicsBody.Position );

	// Static sweeps must not replace impulses from movable bodies or conveyors.
	bool HasPhysicalObstacle( Vector3 from, Vector3 to )
	{
		var scale = MathF.Abs( Controller.WorldTransform.UniformScale );
		var hull = Controller.BodyBox();
		var margin = new Vector3( 2 );
		foreach ( var hit in Scene.Trace.Box( new BBox( hull.Mins * scale - margin, hull.Maxs * scale + margin ), from, to )
			.IgnoreGameObjectHierarchy( Controller.GameObject ).WithCollisionRules( Controller.Tags ).RunAll() )
		{
			if ( hit.Body?.BodyType != PhysicsBodyType.Static || !hit.Shape.SurfaceVelocity.IsNearZeroLength )
				return true;
			_nearStaticSurface = true;
		}
		return false;
	}

	static Vector3 ClipStaticVelocity( Vector3 velocity, Vector3 normal )
	{
		velocity -= normal * velocity.Dot( normal );
		var adjust = velocity.Dot( normal );
		if ( adjust < 0 ) velocity -= normal * adjust;
		return velocity;
	}

	bool IsSteepStaticHull( in SceneTraceResult hit )
	{
		return hit.Hit && !hit.StartedSolid && hit.Shape.IsValid() && hit.Shape.IsHullShape
			&& hit.Body?.BodyType == PhysicsBodyType.Static && hit.Normal.Dot( Controller.UpDirection ) > 0
			&& !IsStandableSurface( hit );
	}

	void PrepareStaticMove()
	{
		var previousContact = _previousSlideContact;
		var previousNormal = _previousSlideNormal;
		_previousSlideContact = null;
		PhysicsShape lastContact = null;
		var lastNormal = Vector3.Zero;
		_hasStaticMove = false;
		_nearStaticSurface = false;
		var locking = Controller.Body.Locking;
		if ( !_physicsStep || Controller.IsProxy || (Controller.IsOnGround && Controller.GroundUsesContactFriction) || Scene.Is2D || Controller.Body.Gravity
			|| locking.X || locking.Y || locking.Z ) return;
		var position = _movementStart;
		var velocity = Controller.Body.Velocity;
		var grounded = Controller.IsOnGround;
		// A steep bank can lift a grounded slide without becoming its support surface.
		if ( grounded )
		{
			var obstruction = Controller.TraceStaticMovement( position, position + velocity * Time.Delta, previousContact, previousNormal, includeGroundClearance: true );
			var hitStatic = obstruction.Hit && !obstruction.StartedSolid && obstruction.Body?.BodyType == PhysicsBodyType.Static && obstruction.Normal.Dot( Controller.UpDirection ) < 0;
			if ( !IsSteepStaticHull( obstruction ) && !hitStatic ) return;
		}
		var original = velocity;
		var primal = velocity;
		var remaining = Time.Delta;
		var fractionSum = 0f;
		Span<Vector3> planes = stackalloc Vector3[5];
		int count = 0;
		for ( int bump = 0; bump < 4 && velocity.LengthSquared > 0; ++bump )
		{
			var end = position + velocity * remaining;
			if ( HasPhysicalObstacle( position, end ) ) return;
			var hit = Controller.TraceStaticMovement( position, end, previousContact, previousNormal, grounded );
			if ( IsSteepStaticHull( hit ) )
			{
				// Cancel cast tolerance when checking clearance past a convex ramp edge.
				var inset = Controller.TraceMovement( position, end, 0.0625f, grounded );
				if ( !inset.Hit && !inset.StartedSolid && !Controller.TraceMovement( end, end, 0, grounded ).StartedSolid )
					hit = inset;
			}
			if ( hit.StartedSolid )
			{
				var offset = hit.Normal * 0.0625f;
				var retry = Controller.TraceMovement( position + offset, end + offset, 0, grounded );
				if ( retry.StartedSolid ) return;
				position += offset;
				end += offset;
				hit = retry;
			}
			var fraction = hit.Fraction;
			if ( hit.Hit && hit.Body?.BodyType != PhysicsBodyType.Static ) return;
			fractionSum += fraction;
			if ( fraction > 0 )
			{
				position += (end - position) * fraction;
				original = velocity;
				count = 0;
			}
			if ( !hit.Hit ) break;

			// A rounded upper contact must not drive grounded feet into their support.
			if ( grounded && hit.Normal.Dot( Controller.UpDirection ) < 0 )
			{
				hit.Normal = (hit.Normal - Controller.GroundNormal * hit.Normal.Dot( Controller.GroundNormal )).Normal;
			}

			previousContact = hit.Shape;
			previousNormal = hit.Normal;
			lastContact = hit.Normal.Dot( Controller.UpDirection ) > 0 && !IsStandableSurface( hit ) ? hit.Shape : null;
			lastNormal = hit.Normal;
			// Verified hull contacts already include separation; extra offsets reorder seam contacts.
			if ( !hit.Shape.IsHullShape && (fraction > 0 || MathF.Abs( hit.Normal.Dot( Controller.UpDirection ) ) > 0.001f) )
				position += hit.Normal * 0.001f;
			remaining -= remaining * fraction;
			planes[count++] = hit.Normal;
			if ( count == 1 && !grounded )
			{
				velocity = ClipStaticVelocity( original, planes[0] );
				original = velocity;
				continue;
			}
			bool accepted = false;
			for ( int i = 0; i < count; ++i )
			{
				velocity = ClipStaticVelocity( original, planes[i] );
				accepted = true;
				for ( int j = 0; j < count; ++j )
				{
					if ( j != i && velocity.Dot( planes[j] ) < 0 )
					{
						accepted = false;
						break;
					}
				}
				if ( accepted ) break;
			}
			if ( !accepted )
			{
				if ( count != 2 )
				{
					velocity = 0;
					break;
				}
				var crease = planes[0].Cross( planes[1] ).Normal;
				velocity = crease * crease.Dot( velocity );
			}
			if ( velocity.Dot( primal ) <= 0 )
			{
				velocity = 0;
				break;
			}
		}
		if ( !_nearStaticSurface ) return;
		if ( fractionSum == 0 ) velocity = 0;
		_hasStaticMove = true;
		_staticMovePosition = position;
		_staticMoveVelocity = velocity;
		_previousSlideContact = lastContact;
		_previousSlideNormal = lastNormal;
	}

	void FinishStaticMove()
	{
		if ( !_hasStaticMove ) return;
		_hasStaticMove = false;
		var body = Controller.Body;
		// Physical bodies may have moved into the route during the solve.
		if ( HasPhysicalObstacle( _movementStart, body.PhysicsBody.Position )
			|| HasPhysicalObstacle( _movementStart, _staticMovePosition ) )
		{
			_previousSlideContact = null;
			return;
		}
		body.WorldPosition = _staticMovePosition;
		body.Velocity = _staticMoveVelocity;
	}

	void ResolveStaticOverlap()
	{
		if ( !_physicsStep || Controller.IsProxy ) return;

		var body = Controller.Body;
		var end = body.PhysicsBody.Position;
		var start = _movementStart;
		// Correct static penetration without replacing contact impulses.
		for ( int plane = 0; plane < 4; plane++ )
		{
			var hit = Controller.TraceMovement( start, end, 0 );
			// Verify downward-facing contacts before they can push us into a surf bank.
			if ( hit.Hit && !hit.StartedSolid && hit.Body?.BodyType == PhysicsBodyType.Static
				&& hit.Normal.Dot( Controller.UpDirection ) < -0.001f )
				hit = Controller.TraceStaticMovement( start, end, null, Vector3.Zero );
			if ( !hit.Hit || hit.StartedSolid || hit.Body?.BodyType != PhysicsBodyType.Static
				|| hit.Normal.Dot( Controller.UpDirection ) > 0.001f ) break;
			var penetration = -(end - hit.EndPosition).Dot( hit.Normal );
			if ( penetration <= 0 ) break;
			end += hit.Normal * penetration;
			body.WorldPosition = end;
			// Reach other corner planes on the next cast.
			start += hit.Normal * 0.05f;
		}
	}

	void FinishStaticWallImpact()
	{
		if ( !_physicsStep || Controller.IsProxy || !Controller.IsOnGround || Scene.Is2D || Controller.GroundUsesContactFriction ) return;
		var body = Controller.Body;
		var intended = _movementStart + _movementVelocity * Time.Delta;
		if ( HasPhysicalObstacle( _movementStart, intended ) || HasPhysicalObstacle( _movementStart, body.PhysicsBody.Position ) ) return;
		var hit = Controller.TraceMovement( _movementStart, intended, 0 );
		if ( !hit.Hit || hit.StartedSolid || hit.Body?.BodyType != PhysicsBodyType.Static
			|| hit.Normal.Dot( Controller.UpDirection ) > 0.001f ) return;
		// Native CCD can stop position a tick before velocity.
		if ( (body.PhysicsBody.Position - hit.EndPosition).Dot( hit.Normal ) <= 0.05f
			&& body.Velocity.Dot( hit.Normal ) < 0 )
			body.Velocity = ClipStaticVelocity( body.Velocity, hit.Normal );
	}
}

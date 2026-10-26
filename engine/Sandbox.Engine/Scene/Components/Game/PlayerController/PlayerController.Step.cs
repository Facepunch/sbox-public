namespace Sandbox;

public sealed partial class PlayerController : Component
{
	/// <summary>
	/// Enable debug overlays for this character.
	/// </summary>
	public bool StepDebug { get; set; } = false;

	const float _stepClearance = 0.5f;

	bool _stepGrounded;

	void BeginStep()
	{
		_stepGrounded = !IsProxy && IsOnGround && Mode.AllowGrounding && _timeUntilAllowedGround <= 0;
	}

	bool CanStep => _stepGrounded && !IsProxy && Body.IsValid() && IsOnGround
		&& Mode.AllowGrounding && _timeUntilAllowedGround <= 0;

	/// <summary>
	/// Lift onto a reachable step without consuming the horizontal physics movement.
	/// </summary>
	internal void TryStep( float maxDistance )
	{
		if ( maxDistance <= 0 || !CanStep )
			return;

		maxDistance *= MathF.Abs( WorldTransform.UniformScale );
		var up = UpDirection;
		var displacement = WithoutVertical( Body.Velocity - GroundVelocity ) * Time.Delta;
		var distance = displacement.Length;
		if ( distance <= 0.0001f )
			return;

		var direction = displacement / distance;
		var lookahead = displacement + direction * _stepClearance;
		var from = WorldPosition;
		var forward = TraceStepMovement( from, from + lookahead );
		if ( !forward.Hit || (forward.StartedSolid && !Scene.Is2D && !GroundUsesContactFriction)
			|| (!forward.StartedSolid && IsPhysicalSupportHit( forward )) )
			return;

		var maxLift = maxDistance + _stepClearance + _skin;
		var maxRise = maxDistance + _skin;
		var upward = TraceMovement( from, from + up * maxLift );
		var lift = upward.Hit ? MathF.Max( 0, upward.Distance - _movementSkin ) : maxLift;
		if ( upward.StartedSolid )
		{
			if ( !TryFindContactStepLift( from, direction, maxLift, forward, out lift, out var groundOffset ) )
				return;
			maxRise += groundOffset;
		}

		if ( lift <= _skin )
			return;

		var raised = from + up * lift;
		bool TryLift( Vector3 routeDirection, float routeDistance, SceneTraceResult obstruction, SceneTraceResult clearance )
		{
			if ( clearance.StartedSolid )
				return false;

			var acrossDistance = clearance.Distance - (clearance.Hit ? _stepClearance : 0);
			var physicsContact = !Scene.Is2D && GroundUsesContactFriction;
			if ( physicsContact )
				acrossDistance = MathF.Min( acrossDistance, routeDistance );
			if ( acrossDistance <= obstruction.Distance + MathF.Min( _skin, routeDistance * 0.1f ) )
				return false;

			var top = raised + routeDirection * acrossDistance;
			if ( !TryFindGround( top, 0, lift + _skin, out var landingGround, out var landing, requireContact: physicsContact ) )
				return false;

			if ( physicsContact )
			{
				var retreat = from - routeDirection * (_groundedFeetLift + _skin);
				if ( !TryFindGround( retreat, 4, 2, out var support, out _ ) )
					return false;

				// Motion along a tilted support plane is not a step, even across different hulls.
				var stepRise = (landingGround.HitPosition - support.HitPosition).Dot( support.Normal ) / support.Normal.Dot( up );
				// Compare surface points, allowing for their world-space floating-point precision.
				var planeScale = MathF.Max( landingGround.HitPosition.Length, support.HitPosition.Length );
				var planeTolerance = 4 * (MathF.BitIncrement( planeScale ) - planeScale);
				if ( stepRise <= planeTolerance || stepRise > maxDistance + _skin )
					return false;
			}

			var rise = (landing - from).Dot( up );
			if ( rise <= _skin || rise > maxRise )
				return false;

			var downward = TraceMovement( top, landing, _stepClearance );
			if ( downward.StartedSolid || (downward.Hit && (downward.EndPosition - landing).Length > _stepClearance)
				|| TraceMovement( landing, landing ).StartedSolid )
				return false;

			var lifted = from + up * rise;
			if ( TraceMovement( lifted, lifted ).StartedSolid )
				return false;

			Body.WorldPosition = lifted;
			Body.Velocity = WithoutVertical( Body.Velocity - GroundVelocity ) + GroundVelocity;

			if ( StepDebug )
			{
				DebugOverlay.Line( from, lifted, duration: 0.1f, color: Color.Green );
				DebugOverlay.Line( raised, top, duration: 0.1f, color: Color.Green );
				DebugOverlay.Line( top, landing, duration: 0.1f, color: Color.Green );
			}
			return true;
		}

		var across = TraceMovement( raised, raised + lookahead );
		if ( TryLift( direction, distance, forward, across ) )
			return;

		if ( !across.Hit || across.StartedSolid || Mode.IsStandableSurface( across ) )
			return;

		// The side wall may hide a reachable landing. Change only the query route,
		// leaving horizontal movement and wall sliding to physics.
		var wallNormal = WithoutVertical( across.Normal ).Normal;
		var intoWall = displacement.Dot( wallNormal );
		if ( intoWall >= 0 )
			return;

		var slide = displacement - wallNormal * intoWall;
		distance = slide.Length;
		if ( distance <= 0.0001f )
			return;

		direction = slide / distance;
		lookahead = slide + direction * _stepClearance;
		forward = TraceStepMovement( from, from + lookahead );
		if ( !forward.Hit || (forward.StartedSolid && (Scene.Is2D || !GroundUsesContactFriction))
			|| (!forward.StartedSolid && IsPhysicalSupportHit( forward )) )
			return;

		across = TraceMovement( raised, raised + lookahead );
		TryLift( direction, distance, forward, across );
	}

	bool IsPhysicalSupportHit( SceneTraceResult hit )
	{
		// Walkable moving slopes must support the player's weight through native contacts.
		if ( Mode.IsStandableSurface( hit )
			&& (GroundUsesContactFriction || hit.Body?.BodyType == PhysicsBodyType.Dynamic) )
			return true;

		// A penetrating contact can make the sweep exit through the support's underside.
		return GroundUsesContactFriction && _groundShape.IsValid()
			&& hit.Body == _groundShape.Body && hit.Normal.Dot( UpDirection ) < 0;
	}

	void ApplyGroundStep( Vector3 position, SceneTraceResult ground )
	{
		if ( (position - WorldPosition).Length > _skin )
		{
			Body.WorldPosition = position;
		}

		UpdateGroundFromTraceResult( ground );
		UpdateGroundVelocity();

		var intoGround = (Body.Velocity - GroundVelocity).Dot( ground.Normal );
		if ( intoGround < 0 )
		{
			// Correct ground penetration vertically without steering along the new surface normal.
			Body.Velocity -= UpDirection * (intoGround / ground.Normal.Dot( UpDirection ));
		}
	}
}

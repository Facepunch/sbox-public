namespace Sandbox.Npcs.Layers;

// Physics mode: a dynamic body steered at the crowd's velocity; jumps and drops flown, ladders and climbs keyframed
public partial class NavigationLayer
{
	private const float Grip = 8f;
	private const float KnockSpeed = 80f;
	private const float KnockTime = 0.35f;
	private const float DropHop = 8f;
	private const int MaxFlightRetries = 2;

	private Rigidbody _body;
	private bool _simulating;
	private TimeSince _sinceKnocked = 10;
	private Vector3 _agentSetAt;
	private Vector3 _wish;

	// The walking velocity: the wish steered by the crowd round neighbours and walls
	private Vector3 _planned;
	private TimeSince _sinceWish = 10;
	private Vector3 _bodyVelocity;
	private Vector3 _groundVelocity;
	private GameObject _groundSeen;
	private Transform _groundSeenAt;
	private Vector3 _appliedGround;
	private Vector3 _appliedPush;
	private Vector3 _aimed;
	private float _frictionLoss;
	private Vector3 _animatedMove;
	private Vector3 _requestedTarget;
	private Vector3 _progressFrom;
	private TimeSince _sinceProgress;
	private bool _newMove;
	private float _closestDetour;
	private TimeSince _sinceDetourGain;
	private TimeSince _sinceDetour = 10;
	private TimeSince _sinceDetourLook;
	private Vector3 _backOff;
	private int _blockedTicks;
	private TimeSince _sinceStep;
	private bool _flying;
	private float _flightApex;
	private float _flightSpeed;
	private float _flightDamping;
	private int _flightRetries;

	// A flight always lands under physics; a body with nothing solid to stand on never walks by it
	private bool WantsSimulation => _flying || (MovementMode == MovementModes.Physics && !_traversal.HasValue && HasSolidShape());

	internal override bool SimulatesBody => WantsSimulation;

	private bool HasSolidShape() => _body.IsValid() && _body.HasSolidCollider;

	// What a flight falls at
	private float BodyGravity => MathF.Max( Scene.PhysicsWorld.Gravity.Length * (_body.Gravity ? _body.GravityScale : 0f), 1f );

	private void UpdateSimulation()
	{
		var simulate = WantsSimulation && Agent.IsValid() && Agent.Enabled;
		if ( simulate == _simulating ) return;

		_simulating = simulate;
		Agent.UpdatePosition = !simulate;
		Agent.MovedByOwner = simulate;
		SetAgentPosition();
		ClearWalk();

		// On the ground the body is ours: dynamic while physics walks it, keyframed while the agent places it
		if ( !Npc.IsOnGround || Npc.PhysicsCarrier.IsValid() ) return;

		var wasSimulated = _body.MotionEnabled;
		Npc.UpdateBodyOwner();
		if ( simulate && !wasSimulated ) Steer( Agent.Velocity.WithZ( 0 ) );
	}

	private void ResetSimulation()
	{
		var owned = _simulating || _flying;
		EndFlight();
		_simulating = false;
		ClearWalk();

		if ( owned && Agent.IsValid() )
		{
			Agent.UpdatePosition = true;
			Agent.MovedByOwner = false;
		}
	}

	private void ClearWalk()
	{
		_wish = _planned = _appliedGround = _appliedPush = _aimed = _bodyVelocity = _animatedMove = _steeringChange = default;
		_frictionLoss = 0;
		_blockedTicks = 0;
		_groundSeen = null;
	}

	private void SetAgentPosition()
	{
		_agentSetAt = WorldPosition;
		Agent.SetAgentPosition( _agentSetAt );
	}

	internal override Vector3 SteeringChange => _steeringChange;

	internal override Vector3 IntendedVelocity => _simulating ? _groundVelocity.WithZ( 0 ) + _planned + _appliedPush : default;
	private Vector3 _steeringChange;

	// Sets the body's velocity, counting it as our own doing
	private void Steer( Vector3 velocity )
	{
		_steeringChange += velocity - _body.Velocity;
		_body.Velocity = velocity;
	}

	protected override void OnFixedUpdate()
	{
		_steeringChange = default;
		if ( !Networking.IsHost || !Agent.IsValid() ) return;

		if ( _flying && !_traversal.HasValue )
			EndFlight();

		UpdateSimulation();

		// The ground's movement is measured tick to tick, only while walking on it
		if ( !_simulating || _flying )
			_groundSeen = null;

		if ( !_simulating || Npc.IsAirborne ) return;

		KeepUpright();

		// The crowd plans from where the body is and others avoid it there
		if ( WorldPosition.DistanceSquared( _agentSetAt ) > 0.0625f )
			SetAgentPosition();

		if ( _flying )
		{
			UpdateFlight();
			return;
		}

		_bodyVelocity = _body.Velocity;
		_groundVelocity = GetGroundVelocity();

		// Knocked by something: let physics play it out, and the crowd steer on from how it really moves
		if ( _sinceKnocked >= KnockTime )
			Walk();
		else
			Agent.ReportVelocity( _bodyVelocity.WithZ( 0 ) );

		CheckProgress();
	}

	private void Walk()
	{
		// Waiting its turn at a link, the crowd still points it across
		var waiting = Agent.waitingFor is not null;
		var moving = MoveTarget.HasValue && _holdMovement <= 0 && !waiting && Status == MovementStatus.Moving;
		_wish = moving ? Agent.WishVelocity.WithZ( 0 ) : default;

		// The crowd already speeds up and turns it at the agent's own rate. Grip only holds the body to it.
		_planned = moving ? Agent.SteeredVelocity.WithZ( 0 ) : default;

		// Backing off a prop it is routing round
		if ( moving && _sinceDetour < 0.3f )
			_wish = _planned = _backOff;

		var wishing = _planned.Length > 1f;
		if ( wishing ) _sinceWish = 0;

		// Moving ground is inherited at once, our own walking eases in. Standing still, a shove plays out.
		var ground = _groundVelocity.WithZ( 0 );

		// Separation out of our neighbours, a distance moved by once this crowd step
		var push = Agent.Push.WithZ( 0 ) / Time.Delta;
		if ( _sinceWish < 0.3f || ground.Length > 1f || _appliedGround.Length > 1f || push.Length > 1f || _appliedPush.Length > 1f )
		{
			var relative = _bodyVelocity.WithZ( 0 ) - _appliedGround - _appliedPush;

			// Aim past the wish by what friction took off since the last tick
			var lost = wishing && _aimed.Length > 1f ? Math.Clamp( (_aimed - relative).Dot( _planned.Normal ), 0f, _planned.Length * 0.2f ) : 0f;
			_frictionLoss = MathX.Lerp( _frictionLoss, lost, 0.5f );
			var target = wishing ? _planned + _planned.Normal * _frictionLoss : default;

			var maxDelta = MathF.Max( Agent.Acceleration, AgentSpeed * Grip ) * Time.Delta;
			relative += (target - relative).ClampLength( maxDelta );

			// Standing on moving ground: settle onto it rather than creep
			if ( !wishing && relative.Length < 40f )
				relative = default;

			Steer( (ground + relative + push).WithZ( _bodyVelocity.z ) );
			_appliedGround = ground;
			_appliedPush = push;
			_aimed = relative;
		}
		else
		{
			_aimed = _appliedPush = default;
			_frictionLoss = 0;
		}

		if ( wishing )
			TryStep();
	}

	// Upright whatever the body's locks say
	private void KeepUpright()
	{
		if ( _body.PhysicsBody is not { } physics ) return;

		if ( physics.Sleeping ) return;

		// Only when there's a tip to undo: setting it wakes the body's sync even unchanged
		var spin = physics.AngularVelocity;
		if ( spin.x != 0f || spin.y != 0f )
			physics.AngularVelocity = Vector3.Up * spin.z;
		if ( physics.Rotation.Up.z < 0.999f )
			physics.Rotation = Rotation.FromYaw( physics.Rotation.Yaw() );
	}

	private Vector3 GetGroundVelocity()
	{
		var reported = Npc.GroundComponent switch
		{
			Rigidbody rigidbody => rigidbody.GetVelocityAtPoint( WorldPosition ),
			Collider collider => collider.GetVelocityAtPoint( WorldPosition ),
			_ => default
		};

		// Ground moved by its transform reports nothing: measure how the point under us moved since the last tick
		var measured = Vector3.Zero;
		var ground = Npc.GroundObject;
		if ( ground.IsValid() && ground == _groundSeen && Time.Delta > 0f )
			measured = (ground.WorldTransform.PointToWorld( _groundSeenAt.PointToLocal( WorldPosition ) ) - WorldPosition) / Time.Delta;

		_groundSeen = ground;
		_groundSeenAt = ground.IsValid() ? ground.WorldTransform : default;
		return reported.LengthSquared >= measured.LengthSquared ? reported : measured;
	}

	// Our own walking for the animation, not the ride the ground gives us
	private Vector3 GetSimulatedMove()
	{
		var own = (_body.Velocity - _groundVelocity).WithZ( 0 );
		if ( _wish.Length < 1f && own.Length < 40f )
			own = default;

		_animatedMove = _animatedMove.LerpTo( own, MathF.Min( 1f, 12f * Time.Delta ) );
		return _animatedMove;
	}

	// A box can't climb a riser: held up against something low, lift the body onto it where it fits
	private void TryStep()
	{
		// What the body did since the last tick, not what Walk just told it
		var along = (_bodyVelocity - _groundVelocity).WithZ( 0 ).Dot( _planned.Normal );
		_blockedTicks = along < _planned.Length * 0.25f ? _blockedTicks + 1 : 0;

		var step = StepHeight;
		if ( _blockedTicks < 3 || _sinceStep < 0.1f || step <= 0f || _body.PhysicsBody is not { IsValid: true } physics ) return;

		_sinceStep = 0;

		// From a little back and up: pressed into the riser and standing on the floor, the body already touches both
		var direction = _planned.Normal;
		var start = physics.Position - direction + Vector3.Up;

		var up = SweepBody( physics, start, start + Vector3.Up * step );
		if ( up.StartedSolid ) return;

		var across = SweepBody( physics, up.EndPosition, up.EndPosition + direction * (AgentRadius + 1f) );
		if ( across.StartedSolid ) return;

		var down = SweepBody( physics, across.EndPosition, across.EndPosition.WithZ( physics.Position.z ) );
		var rise = down.EndPosition.z - physics.Position.z;
		if ( !down.Hit || down.StartedSolid || down.Normal.z < 0.7f || rise < 1f || rise > step ) return;

		physics.Position = physics.Position.WithZ( down.EndPosition.z );
		Steer( _body.Velocity.WithZ( 0 ) );
		_blockedTicks = 0;
	}

	private SceneTraceResult TraceBody( BBox hull, Vector3 from, Vector3 to ) => OursIgnored( Scene.Trace.Box( hull, from, to ) ).Run();

	private SceneTraceResult SweepBody( PhysicsBody body, Vector3 from, Vector3 to ) => OursIgnored( Scene.Trace.Body( body, new Transform( from, body.Rotation ), to ) ).Run();

	private static readonly string[] TriggerTag = ["trigger"];

	private SceneTrace OursIgnored( SceneTrace trace ) => trace.IgnoreGameObjectHierarchy( Npc.GameObject ).WithCollisionRules( Npc.Tags ).WithoutTags( TriggerTag );

	private void CheckProgress()
	{
		// Stuck only while the crowd steers it on, timed from the tick as MoveTo may come outside the scene's time
		if ( _newMove || !MoveTarget.HasValue || _planned.Length < MovingSpeed || WorldPosition.Distance( _progressFrom ) > 8f )
		{
			_newMove = false;
			_progressFrom = WorldPosition;
			_sinceProgress = 0;
			return;
		}

		// Pressing into something loose: route round it early, looking a few times a second, fail once really stuck
		var stuck = _sinceProgress > 1.5f;
		if ( _sinceProgress > 0.5f && (_sinceDetour > 1f || stuck) && (_sinceDetourLook > 0.25f || stuck) )
		{
			_sinceDetourLook = 0;
			if ( Detour() )
			{
				_sinceProgress = 0;
				return;
			}
		}

		if ( stuck )
			_moveFailed = true;
	}

	// The prop pressed against. Only props physics moves are routed round, never players or anything that moves itself.
	private Prop FindObstruction()
	{
		var radius = MathF.Max( Agent.Radius * 0.6f, 2f );
		var hull = new BBox( new Vector3( -radius, -radius, StepHeight + 1f ), new Vector3( radius, radius, MathF.Max( Agent.Height, StepHeight + 8f ) ) );
		var tr = TraceBody( hull, WorldPosition, WorldPosition + _wish.Normal * (Agent.Radius + 8f) );
		var prop = tr.Hit && tr.GameObject.IsValid() ? tr.GameObject.GetComponentInParent<Prop>() : null;
		return prop.IsValid() && prop.GetComponent<Rigidbody>() is { MotionEnabled: true } ? prop : null;
	}

	// Block the prop out of the navmesh and back off; only while detours gain ground, else it would retrace them
	private bool Detour()
	{
		if ( FindObstruction() is not { } obstruction ) return false;

		var left = RouteLeft();
		if ( left < _closestDetour - AgentRadius )
		{
			_closestDetour = left;
			_sinceDetourGain = 0;
		}
		else if ( _sinceDetourGain > NpcDetourBlocker.Lifetime )
		{
			return false;
		}

		NpcDetourBlocker.Refresh( obstruction.GameObject );
		_backOff = -_wish.Normal * 120f;
		_sinceDetour = 0;
		return true;
	}

	private float RouteLeft()
	{
		var path = Agent.GetPath();
		if ( !path.IsValid ) return float.MaxValue;

		var left = 0f;
		var from = WorldPosition;
		foreach ( var point in path.Points )
		{
			left += from.Distance( point.Position );
			from = point.Position;
		}

		return left;
	}

	internal override void OnCollision( Collision collision )
	{
		if ( MovementMode != MovementModes.Physics || !_body.IsValid() ) return;

		// Struck by something coming at us, not something we walk into
		if ( collision.Other.Body is not { BodyType: PhysicsBodyType.Dynamic } other || other.GameObject?.Tags.Has( "npc" ) != false ) return;
		if ( -other.GetVelocityAtPoint( collision.Contact.Point ).Dot( collision.Normal ) > KnockSpeed )
			_sinceKnocked = 0;
	}

	// Launched with the velocity that lands it on the link's end over an apex
	private void BeginFlight( in LinkTraversalState t )
	{
		var start = WorldPosition;
		var jump = KindOf( t.Type ) == Crossing.Jump;
		var apex = MathF.Max( MathF.Max( start.z, t.End.z ), jump ? start.z : _dropLift.z ) + (jump ? JumpHeight : DropHop);
		var rise = MathF.Sqrt( 2 * BodyGravity * MathF.Max( apex - start.z, 0 ) );
		var time = MathF.Max( rise / BodyGravity + MathF.Sqrt( 2 * MathF.Max( apex - t.End.z, 0 ) / BodyGravity ), 0.05f );

		_flying = true;
		_flightApex = apex;
		_traversalTime = 0;
		_dropLiftTime = _dropStepTime = 0;
		_traversal = t with { Start = start, Duration = time };
		UpdateSimulation();

		// A drop clears the ledge before it comes back down to it
		var across = (t.End - start).WithZ( 0 );
		var speed = across.Length / time;
		if ( !jump )
			speed = MathF.Max( speed, (_dropDeparture - start).WithZ( 0 ).Length / MathF.Max( 2 * rise / BodyGravity, 0.05f ) );

		_flightSpeed = speed;
		_flightDamping = _body.LinearDamping;
		_body.LinearDamping = 0;
		Steer( across.Normal * speed + Vector3.Up * rise );
	}

	// Over once down, fallen below the end or far too late. Only down on the end has it crossed.
	private void UpdateFlight()
	{
		_traversalTime += Time.Delta;

		var t = _traversal.Value;
		var velocity = _body.Velocity;
		var height = WorldPosition.z - t.End.z;
		var down = _traversalTime > 0.1f && velocity.z <= 1f && TraceBody( BBox.FromPositionAndSize( Vector3.Up * 4f, 4f ), WorldPosition, WorldPosition - Vector3.Up * 8f ).Hit;
		var missed = velocity.z < 0f && height < -(StepHeight + 4f);
		var late = _traversalTime >= t.Duration * 2f + 1f;

		if ( !down && !missed && !late )
		{
			SteerFlight( t, velocity, height );
			return;
		}

		EndFlight();
		SetAgentPosition();

		var arrived = down && MathF.Abs( height ) <= StepHeight + 4f && (t.End - WorldPosition).WithZ( 0 ).Length <= AgentRadius;
		if ( arrived )
			CompleteLinkTraversal();
		else
			RetryLinkTraversal();

		Agent.ReportVelocity( _body.Velocity.WithZ( 0 ) );
	}

	// Bumped back where it took off or short of the end: route again from where it is, a few times before the move fails
	private void RetryLinkTraversal()
	{
		if ( _flightRetries >= MaxFlightRetries || !MoveTarget.HasValue )
		{
			AbortLinkTraversal();
			return;
		}

		_flightRetries++;
		_traversal = null;
		_sinceTraversal = 0;
		_newMove = true;
		Agent.CompleteLinkTraversal();
		Agent.MoveTo( MoveTarget.Value );
	}

	// Holds the arc onto the end from where the body really is, so a bump still makes the jump
	private void SteerFlight( in LinkTraversalState t, Vector3 velocity, float height )
	{
		if ( _sinceKnocked < KnockTime ) return;

		var to = (t.End - WorldPosition).WithZ( 0 );
		if ( to.Length < 1f ) return;

		// A drop keeps its launch speed until past the rim
		if ( KindOf( t.Type ) == Crossing.Drop && Vector3.Dot( _dropDeparture - WorldPosition, to ) > 0f ) return;

		var falling = velocity.z * velocity.z + 2f * BodyGravity * height;
		var remaining = falling > 0f ? (velocity.z + MathF.Sqrt( falling )) / BodyGravity : 0f;

		// Down short of the end: walk the rest
		var speed = remaining > Time.Delta
			? MathF.Min( to.Length / remaining, _flightSpeed * 1.1f )
			: MathF.Min( _flightSpeed, MathF.Max( WalkSpeed, 1f ) );

		var flat = velocity.WithZ( 0 );
		flat += (to.Normal * speed - flat).ClampLength( MathF.Max( _flightSpeed, 1f ) * Grip * Time.Delta );

		// Still rising: fast enough to make the apex
		var vertical = velocity.z;
		var below = _flightApex - WorldPosition.z;
		if ( vertical > 0f && below > 1f )
			vertical = MathF.Max( vertical, MathF.Sqrt( 2f * BodyGravity * below ) - BodyGravity * Time.Delta );

		Steer( flat.WithZ( vertical ) );
	}

	private void EndFlight()
	{
		if ( !_flying ) return;

		_flying = false;
		if ( _body.IsValid() ) _body.LinearDamping = _flightDamping;
	}
}

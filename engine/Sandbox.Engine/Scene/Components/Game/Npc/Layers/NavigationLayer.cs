using Sandbox.Engine.Resources;
namespace Sandbox.Npcs.Layers;

/// <summary>
/// Walks the navmesh with a <see cref="NavMeshAgent"/>, crossing nav links by their type.
/// </summary>
public class NavigationLayer : MovementLayer
{
	/// <summary>
	/// The navmesh agent this layer drives, for a subclass that crosses a link itself.
	/// </summary>
	protected NavMeshAgent Agent { get; private set; }

	/// <summary>Distance beyond which the NPC breaks into a run.</summary>
	[Property]
	public float RunDistance { get; set; } = 200f;

	public override bool IsMoving => IsTraversingLink || Agent.IsValid() && Agent.Velocity.WithZ( 0 ).Length > 20f;

	public override Vector3? ProjectPoint( Vector3 point ) => Scene.NavMesh.GetClosestPoint( point );

	protected internal override bool UsesGround => Agent.IsValid();

	protected internal override float StepHeight => Scene.NavMesh.AgentStepSize;

	protected internal override void OnPhysicsControlStarted()
	{
		if ( !Agent.IsValid() ) return;

		// Physics has the body: the link we were crossing ends here, wherever that is
		_traversal = null;
		Agent.Enabled = false;
	}

	protected internal override void OnPhysicsControlEnded()
	{
		if ( !Agent.IsValid() ) return;

		var resumed = !Agent.Enabled;
		Agent.Enabled = true;

		// Back on the navmesh. The agent lost its target while it was off, so carry on with the move from here.
		if ( resumed && MoveTarget.HasValue && !_moveFailed )
		{
			Agent.MoveTo( MoveTarget.Value );
			_sinceTraversal = 0;
		}
	}

	private enum Crossing
	{
		Plain,
		Jump,
		Drop,
		Ladder,
		Climb,
	}

	private static Crossing KindOf( NavMeshLinkType type )
	{
		if ( type is null ) return Crossing.Plain;
		if ( type == NavMeshLinkType.Jump ) return Crossing.Jump;
		if ( type == NavMeshLinkType.Drop ) return Crossing.Drop;
		if ( type == NavMeshLinkType.Ladder ) return Crossing.Ladder;
		if ( type == NavMeshLinkType.Climb ) return Crossing.Climb;
		return Crossing.Plain;
	}

	/// <summary>How far above the higher end of a jump link the arc peaks.</summary>
	[Property, Group( "Links" )]
	public float JumpHeight { get; set; } = 32f;

	/// <summary>
	/// How fast the NPC climbs a ladder link.
	/// </summary>
	[Property, Group( "Links" )]
	public float LadderSpeed { get; set; } = 80f;

	/// <summary>
	/// How fast the NPC moves along the vertical part of a climb link.
	/// </summary>
	[Property, Group( "Links" )]
	public float ClimbSpeed { get; set; } = 60f;

	/// <summary>
	/// The link being crossed, for animation to react to. Host only.
	/// </summary>
	public readonly record struct LinkTraversalState
	{
		/// <summary>How it is being crossed: the link's type, or null when it is crossed as a plain link.</summary>
		public NavMeshLinkType Type { get; internal init; }
		/// <summary>The link component being crossed, when it has one.</summary>
		public NavMeshLink Link { get; internal init; }
		/// <summary>Where the NPC was when it entered the link.</summary>
		public Vector3 Start { get; internal init; }
		/// <summary>Where the link lets the NPC off.</summary>
		public Vector3 End { get; internal init; }
		/// <summary>Zero for a custom traversal the NPC runs itself.</summary>
		public float Duration { get; internal init; }
		/// <summary>How far through the crossing the NPC is, from 0 to 1.</summary>
		public float Progress { get; internal init; }
		/// <summary>How the NPC moved over the last update of the crossing.</summary>
		public Vector3 Velocity { get; internal init; }
	}

	/// <summary>
	/// The link being crossed right now, or null when the NPC is not on one.
	/// </summary>
	public LinkTraversalState? LinkTraversal => IsTraversingLink ? _traversal : null;

	/// <summary>
	/// Whether the NPC is crossing a navmesh link right now.
	/// </summary>
	public bool IsTraversingLink => _traversal.HasValue && Agent.IsValid() && Agent.IsTraversingLink;

	private bool _running;
	private float? _moveSpeed;
	private GameObject _moveFaceTarget;
	private LinkTraversalState? _traversal;
	private float _traversalTime;
	private Vector3 _climbFoot;
	private Vector3 _climbDirection;
	private float _climbExitLift;
	private Vector3 _dropDeparture;
	private Vector3 _dropLift;
	private float _dropLiftTime;
	private float _dropStepTime;
	private Vector3 _traversalLastPosition;
	private TimeSince _sinceTraversal = 10;
	private TimeUntil _holdMovement;
	private bool _moveFailed;

	/// <summary>
	/// Stand still for a moment, such as for a landing animation, then carry on along the path. Host only.
	/// </summary>
	public void HoldMovement( float seconds ) => _holdMovement = MathF.Max( _holdMovement, seconds );

	protected override void OnEnabled() => InitializeAgent();

	protected override void OnStart() => InitializeAgent();

	internal void InitializeAgent()
	{
		if ( Agent.IsValid() || !Npc.IsValid() ) return;

		Agent = Npc.GetComponent<NavMeshAgent>( true );
		if ( !Agent.IsValid() ) return;

		Agent.AutoTraverseLinks = false;
		Agent.LinkEnter += OnLinkEnter;

		// A layer that starts disabled never receives OnDisabled to let go of its agent.
		if ( Networking.IsHost && !Enabled )
			Agent.Enabled = false;

		// An agent nobody set up takes only the links this layer can cross. One that lists its types keeps them.
		if ( Agent.AllowAllLinkTypes && Agent.AllowedLinkTypes is not { Count: > 0 } )
		{
			Agent.AllowAllLinkTypes = false;
			Agent.AllowedLinkTypes = [.. new[] { NavMeshLinkType.Jump, NavMeshLinkType.Drop, NavMeshLinkType.Ladder, NavMeshLinkType.Climb }.Where( x => x is not null )];
		}
	}

	protected override void OnDestroy()
	{
		if ( Agent.IsValid() )
			Agent.LinkEnter -= OnLinkEnter;
	}

	// The agent is off while we are: the link ends where the NPC stands and the move is dropped.
	protected override void OnDisabled()
	{
		var crossing = _traversal.HasValue;

		_traversal = null;
		MoveTarget = null;
		_moveSpeed = null;
		_moveFaceTarget = null;
		_running = false;
		_moveFailed = false;
		_holdMovement = 0;

		if ( !Networking.IsHost )
			return;

		if ( Agent.IsValid() )
		{
			if ( crossing )
				Agent.CompleteLinkTraversal();

			// Off the navmesh until this layer has the body again, so another mover can take it anywhere
			Agent.Stop();
			Agent.Enabled = false;
		}

		if ( crossing && !GameObject.IsDestroyed && Npc.IsValid() && Npc.Animation.IsValid() )
		{
			Npc.Animation.Traversal = null;
			Npc.Animation.TraversalDirection = 0;
		}
	}

	private void OnLinkEnter()
	{
		if ( !Networking.IsHost || Agent.CurrentLinkTraversal is not { } data ) return;

		var link = data.LinkComponent;
		var linkType = data.Type;
		var start = data.AgentInitialPosition;
		var end = data.LinkExitPosition;

		var type = KindOf( linkType ) != Crossing.Plain ? linkType : null;

		if ( type == NavMeshLinkType.Drop && end.z >= start.z )
			type = NavMeshLinkType.Jump;

		_traversal = new LinkTraversalState { Type = type ?? linkType, Link = link, Start = start, End = end };
		_traversalTime = 0;
		_climbFoot = GetClimbFoot( data );
		_climbDirection = link.IsValid() ? link.ClimbDirection : Vector3.Up;
		_climbExitLift = link.IsValid() ? link.ClimbExitLift : 0;
		_traversalLastPosition = WorldPosition;

		if ( type is null && linkType is not null )
		{
			if ( OnCustomLinkTraversal( _traversal.Value ) ) return;

			// A type this layer has no traversal for, and nobody crossed it: walk it like a plain link.
			_traversal = _traversal.Value with { Type = null };
		}

		if ( KindOf( _traversal.Value.Type ) == Crossing.Drop && !PrepareDrop( _traversal.Value ) )
		{
			AbortLinkTraversal();
			return;
		}

		_traversal = _traversal.Value with { Duration = MathF.Max( GetTraversalDuration( _traversal.Value ), 0.01f ) };
	}

	/// <summary>
	/// Reached a link whose type this layer has no traversal of its own for. Add the type to the agent's
	/// <see cref="NavMeshAgent.AllowedLinkTypes"/> for it to path over such links. Return true to cross it yourself: move the <see cref="Agent"/> with
	/// <see cref="NavMeshAgent.SetAgentPosition"/>, then call <see cref="CompleteLinkTraversal"/>, or
	/// <see cref="AbortLinkTraversal"/> if it cannot finish. Otherwise it is crossed like a plain link.
	/// </summary>
	protected virtual bool OnCustomLinkTraversal( LinkTraversalState traversal ) => false;

	/// <summary>
	/// Finish the current link traversal and carry on along the path from where the agent is.
	/// A custom traversal calls this once it has moved the agent to the end with <see cref="NavMeshAgent.SetAgentPosition"/>.
	/// </summary>
	protected void CompleteLinkTraversal()
	{
		if ( !_traversal.HasValue ) return;

		_traversal = null;
		_sinceTraversal = 0;

		if ( !Agent.IsValid() ) return;

		Agent.CompleteLinkTraversal();
		if ( !MoveTarget.HasValue )
			Agent.Stop();
	}

	/// <summary>
	/// Give up the link being crossed where the NPC stands, and fail the current move. For a custom traversal
	/// that cannot finish.
	/// </summary>
	protected void AbortLinkTraversal()
	{
		if ( !_traversal.HasValue ) return;

		_traversal = null;
		_moveFailed = MoveTarget.HasValue;

		if ( !Agent.IsValid() ) return;

		Agent.CompleteLinkTraversal();
		Agent.Stop();
	}

	private float Gravity => MathF.Max( Scene.PhysicsWorld.Gravity.Length, 1 );

	private float GetTraversalDuration( in LinkTraversalState t )
	{
		switch ( KindOf( t.Type ) )
		{
			case Crossing.Jump:
				var apex = MathF.Max( t.Start.z, t.End.z ) + JumpHeight;
				return MathF.Sqrt( 2 * (apex - t.Start.z) / Gravity ) + MathF.Sqrt( 2 * (apex - t.End.z) / Gravity );

			case Crossing.Drop:
				return _dropLiftTime + _dropStepTime + MathF.Sqrt( 2 * (_dropDeparture.z - t.End.z) / Gravity );

			case Crossing.Ladder:
			case Crossing.Climb:
				var (entry, on, off, exit, climbSpeed, stepSpeed) = GetClimbPath( t );
				return (t.Start.Distance( entry ) + entry.Distance( on ) + off.Distance( exit ) + exit.Distance( t.End )) / stepSpeed + on.Distance( off ) / climbSpeed;

			default:
				return t.Start.Distance( t.End ) / MathF.Max( Agent.MaxSpeed, 1 );
		}
	}

	// Where a ladder or climb is lined up: the link's lower end as it was placed, else where it met the navmesh.
	private static Vector3 GetClimbFoot( in NavMeshAgent.LinkTraversalData data )
	{
		var snapped = data.LinkExitPosition.z > data.LinkEnterPosition.z ? data.LinkEnterPosition : data.LinkExitPosition;
		if ( !data.LinkComponent.IsValid() ) return snapped;

		var link = data.LinkComponent;
		var placed = link.WorldStartPosition.z <= link.WorldEndPosition.z ? link.WorldStartPosition : link.WorldEndPosition;

		// Placed further from the navmesh than the link reaches: the navmesh is where it can stand
		return placed.WithZ( 0 ).Distance( snapped.WithZ( 0 ) ) <= link.ConnectionRadius ? placed : snapped;
	}

	// A step onto the link's axis at the starting height, along it, and a step off at the far end.
	private (Vector3 Entry, Vector3 On, Vector3 Off, Vector3 Exit, float ClimbSpeed, float StepSpeed) GetClimbPath( in LinkTraversalState t )
	{
		var climbSpeed = MathF.Max( KindOf( t.Type ) == Crossing.Ladder ? LadderSpeed : ClimbSpeed, 1 );
		bool ascending = t.End.z > t.Start.z;
		var entry = t.Start + Vector3.Up * (ascending ? 0 : _climbExitLift);
		var exit = t.End + Vector3.Up * (ascending ? _climbExitLift : 0);
		var on = _climbFoot + _climbDirection * ((entry.z - _climbFoot.z) / _climbDirection.z);
		var off = _climbFoot + _climbDirection * ((exit.z - _climbFoot.z) / _climbDirection.z);
		return (entry, on, off, exit, climbSpeed, MathF.Max( WalkSpeed, 1 ));
	}

	private Vector3 SampleTraversal( in LinkTraversalState t, float time )
	{
		var fraction = Math.Clamp( time / t.Duration, 0, 1 );

		switch ( KindOf( t.Type ) )
		{
			case Crossing.Jump:
				{
					var apex = MathF.Max( t.Start.z, t.End.z ) + JumpHeight;
					var position = Vector3.Lerp( t.Start, t.End, fraction );
					position.z = t.Start.z + MathF.Sqrt( 2 * Gravity * (apex - t.Start.z) ) * time - 0.5f * Gravity * time * time;
					return position;
				}

			case Crossing.Drop:
				{
					if ( time < _dropLiftTime )
						return Vector3.Lerp( t.Start, _dropLift, time / _dropLiftTime );
					if ( time < _dropLiftTime + _dropStepTime )
						return Vector3.Lerp( _dropLift, _dropDeparture, (time - _dropLiftTime) / _dropStepTime );

					var fallingTime = time - _dropLiftTime - _dropStepTime;
					var fallingFraction = Math.Clamp( fallingTime / (t.Duration - _dropLiftTime - _dropStepTime), 0, 1 );
					var position = Vector3.Lerp( _dropDeparture, t.End, 1 - (1 - fallingFraction) * (1 - fallingFraction) );
					position.z = _dropDeparture.z - 0.5f * Gravity * fallingTime * fallingTime;
					return position;
				}

			case Crossing.Ladder:
			case Crossing.Climb:
				{
					var (entry, on, off, exit, climbSpeed, stepSpeed) = GetClimbPath( t );
					var entryTime = t.Start.Distance( entry ) / stepSpeed;
					if ( time < entryTime )
						return Vector3.Lerp( t.Start, entry, time / entryTime );
					time -= entryTime;

					var onTime = entry.Distance( on ) / stepSpeed;
					if ( time < onTime )
						return Vector3.Lerp( entry, on, time / onTime );

					var climbTime = on.Distance( off ) / climbSpeed;
					if ( time < onTime + climbTime )
						return Vector3.Lerp( on, off, (time - onTime) / climbTime );

					time -= onTime + climbTime;
					var offTime = off.Distance( exit ) / stepSpeed;
					if ( time < offTime )
						return Vector3.Lerp( off, exit, time / offTime );

					var exitTime = exit.Distance( t.End ) / stepSpeed;
					return exitTime > 0 ? Vector3.Lerp( exit, t.End, (time - offTime) / exitTime ) : t.End;
				}

			default:
				return Vector3.Lerp( t.Start, t.End, fraction );
		}
	}

	// Find the outside of the supporting ledge, including the agent's radius. A generated link can land
	// just below an overhanging rim, so its exit isn't necessarily a clear place to start the reverse probe.
	private bool PrepareDrop( in LinkTraversalState t )
	{
		_dropLift = _dropDeparture = t.Start;
		_dropLiftTime = _dropStepTime = 0;
		var direction = (t.End - t.Start).WithZ( 0 ).Normal;
		if ( direction.LengthSquared == 0 ) return true;

		var radius = MathF.Max( Agent.Radius, 1 );
		var step = MathF.Max( StepHeight, 0 );
		var reach = radius * 2 + step + 2;
		var outside = t.End.WithZ( t.Start.z );
		var trace = TraceDrop( outside, t.Start, -MathF.Max( step, 4 ) - 2 );
		if ( trace.StartedSolid )
			trace = TraceDrop( outside + direction * reach, t.Start, -MathF.Max( step, 4 ) - 2 );
		if ( trace.StartedSolid ) return false;
		if ( trace.Hit )
			_dropDeparture = (trace.EndPosition + direction * 2).WithZ( t.Start.z );

		// Treat a low roof rim like a normal step, with headroom and forward clearance checks.
		var across = TraceDrop( t.Start, _dropDeparture );
		if ( across.Hit || across.StartedSolid )
		{
			_dropLift += Vector3.Up * step;
			_dropDeparture += Vector3.Up * step;
			var up = TraceDrop( t.Start, _dropLift );
			across = TraceDrop( _dropLift, _dropDeparture );
			if ( up.Hit || up.StartedSolid || across.Hit || across.StartedSolid ) return false;
		}

		// If clearing an overhang takes us beyond the exit, land on the same nearby lower surface.
		// Pulling back toward the old endpoint while falling would put the hull back through the rim.
		var beyond = Vector3.Dot( _dropDeparture - t.End, direction );
		if ( beyond > 0 )
		{
			var wanted = t.End + direction * beyond;
			var extents = new Vector3( radius, radius, MathF.Max( step, 4 ) );
			var landing = Scene.NavMesh.GetClosestPoint( new BBox( wanted - extents, wanted + extents ) );
			if ( landing is not { } point || point.Distance( t.End ) > reach
				|| Vector3.Dot( point - _dropDeparture, direction ) < -0.1f ) return false;
			var fit = TraceDrop( point, point );
			if ( fit.StartedSolid ) return false;
			_traversal = t with { End = point };
		}

		var speed = MathF.Max( Agent.MaxSpeed, 1 );
		_dropLiftTime = t.Start.Distance( _dropLift ) / speed;
		_dropStepTime = _dropLift.Distance( _dropDeparture ) / speed;
		return true;
	}

	private SceneTraceResult TraceDrop( Vector3 from, Vector3 to, float bottom = 1 )
	{
		var radius = MathF.Max( Agent.Radius, 1 );
		var hull = new BBox( new Vector3( -radius, -radius, bottom ), new Vector3( radius, radius, MathF.Max( Agent.Height, 2 ) ) );
		return Scene.Trace.Box( hull, from, to )
			.IgnoreGameObjectHierarchy( Npc.GameObject )
			.WithCollisionRules( Npc.Tags )
			.WithoutTags( "trigger" )
			.Run();
	}

	private void UpdateTraversal()
	{
		var t = _traversal.Value;
		var done = false;

		if ( t.Duration > 0 )
		{
			_traversalTime += Time.Delta;
			done = _traversalTime >= t.Duration;
			var position = done ? t.End : SampleTraversal( t, _traversalTime );
			if ( KindOf( t.Type ) == Crossing.Drop )
			{
				var trace = TraceDrop( WorldPosition, position );
				if ( trace.Hit || trace.StartedSolid )
				{
					if ( !trace.StartedSolid )
					{
						Agent.SetAgentPosition( trace.EndPosition );
						Npc.WorldPosition = trace.EndPosition;
					}
					AbortLinkTraversal();
					return;
				}
			}
			Agent.SetAgentPosition( position );
			Npc.WorldPosition = position;
		}

		var velocity = Time.Delta > 0 ? (WorldPosition - _traversalLastPosition) / Time.Delta : default;
		_traversalLastPosition = WorldPosition;
		_traversal = t with { Progress = t.Duration > 0 ? Math.Clamp( _traversalTime / t.Duration, 0, 1 ) : 0, Velocity = velocity };

		if ( done )
			CompleteLinkTraversal();
	}

	private Vector3 GetTraversalFacing( in LinkTraversalState t )
	{
		// Face from the climb line toward the upper landing, rather than along a tilted ladder's axis.
		var climbing = KindOf( t.Type ) is Crossing.Ladder or Crossing.Climb;
		var upper = t.Start.z < t.End.z ? t.End : t.Start;
		if ( !climbing ) return (t.End - t.Start).WithZ( 0 );
		var onAxis = _climbFoot + _climbDirection * ((upper.z - _climbFoot.z) / _climbDirection.z);
		var facing = (upper - onAxis).WithZ( 0 );
		return facing.IsNearlyZero() ? (upper - _climbFoot).WithZ( 0 ) : facing;
	}

	public override void MoveTo( Vector3 target, float stopDistance = 10f, float? speed = null, GameObject faceTarget = null )
	{
		MoveTarget = target;
		StopDistance = stopDistance;
		_moveSpeed = speed;
		_moveFaceTarget = faceTarget;
		_moveFailed = false;

		if ( !Agent.IsValid() )
		{
			Log.Warning( $"NPC '{Npc.DisplayName}' tried to move but has no NavMeshAgent component." );
			return;
		}

		Agent.MoveTo( target );

		// Use the agent's resolved navmesh position so distance checks are accurate
		if ( Agent.TargetPosition.HasValue )
		{
			MoveTarget = Agent.TargetPosition.Value;
		}
	}

	public override void Stop()
	{
		MoveTarget = null;
		_moveSpeed = null;
		_moveFaceTarget = null;
		_running = false;
		_moveFailed = false;

		// Mid-link the agent stops once the traversal completes, rather than dropping out of the air.
		if ( Agent.IsValid() && !IsTraversingLink )
			Agent.Stop();
	}

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost ) return;

		UpdateMovement();
		var link = Npc.IsAirborne ? null : LinkTraversal;
		// Approaching the edge (and stepping over its rim) still uses the walking animation.
		if ( link is { } drop && KindOf( drop.Type ) == Crossing.Drop && _traversalTime < _dropLiftTime + _dropStepTime )
			link = null;
		Npc.Animation.Traversal = link?.Type;
		Npc.Animation.TraversalDirection = link is { } l ? Math.Sign( l.End.z - l.Start.z ) : 0;
	}

	private void UpdateMovement()
	{
		if ( !Agent.IsValid() ) return;

		// Thrown or dropped - physics has us, not the navmesh, and the animation follows the body
		if ( Npc.IsAirborne ) return;

		if ( _traversal.HasValue && !Agent.IsTraversingLink )
			_traversal = null;

		var speed = ResolveSpeed();
		Agent.MaxSpeed = _holdMovement > 0 ? 0 : speed;

		if ( _traversal.HasValue )
		{
			UpdateTraversal();

			if ( LinkTraversal is { } traversal )
			{
				FaceDirection( GetTraversalFacing( traversal ) );
				Npc.Animation.SetMove( traversal.Velocity );
				return;
			}
		}

		FaceMovement();
		Npc.Animation.SetMove( Agent.Velocity );
	}

	private void FaceDirection( Vector3 direction )
	{
		if ( direction.Length < 1f ) return;
		Npc.WorldRotation = Rotation.Slerp( Npc.WorldRotation, Rotation.LookAt( direction.Normal, Vector3.Up ), TurnSpeed * Time.Delta );
	}

	// Turn the body to face the way we're actually moving, so the NPC runs forward instead of
	// sliding sideways. While strafing it faces the move's face target instead.
	private void FaceMovement()
	{
		if ( _moveFaceTarget.IsValid() )
		{
			var direction = (_moveFaceTarget.WorldPosition - Npc.WorldPosition).WithZ( 0 );
			if ( direction.Length > 1f )
			{
				var faceRotation = Rotation.LookAt( direction.Normal, Vector3.Up );
				Npc.WorldRotation = Rotation.Slerp( Npc.WorldRotation, faceRotation, TurnSpeed * Time.Delta );
			}

			return;
		}

		var velocity = Agent.Velocity.WithZ( 0 );
		if ( velocity.Length < 20f )
			return;

		var targetRotation = Rotation.LookAt( velocity.Normal, Vector3.Up );
		Npc.WorldRotation = Rotation.Slerp( Npc.WorldRotation, targetRotation, TurnSpeed * Time.Delta );
	}

	// A speed given with the move is used as-is. Otherwise walk, and break into a run when the
	// destination is a good distance off. Hysteresis stops it flickering between walk and run
	// near the threshold.
	private float ResolveSpeed()
	{
		if ( _moveSpeed.HasValue )
			return _moveSpeed.Value;

		var speed = WalkSpeed;

		if ( !MoveTarget.HasValue )
			return speed;

		var distance = Npc.WorldPosition.Distance( MoveTarget.Value );

		if ( _running && distance < RunDistance * 0.6f )
			_running = false;
		else if ( !_running && distance > RunDistance )
			_running = true;

		return _running ? MathF.Max( speed, RunSpeed ) : speed;
	}

	internal override string GetDebugString()
	{
		if ( LinkTraversal is { } link ) return $"Nav: {link.Type?.ResourceName ?? "plain"} link {link.Progress:P0}";
		if ( !MoveTarget.HasValue ) return null;

		var status = Status;
		var dist = Npc.WorldPosition.Distance( MoveTarget.Value ).CeilToInt();
		return $"Nav: {status} ({dist}u)";
	}

	public override MovementStatus Status
	{
		get
		{
			if ( !MoveTarget.HasValue ) return MovementStatus.Idle;

			// No agent means we can never get there -- fail instead of hanging on Moving forever.
			if ( !Agent.IsValid() || _moveFailed ) return MovementStatus.Failed;

			var distance = Npc.WorldPosition.Distance( MoveTarget.Value );

			if ( distance <= StopDistance )
				return MovementStatus.Arrived;

			// The agent replans from the link exit, so it isn't navigating for a moment after a link.
			if ( !Agent.IsNavigating )
				return _sinceTraversal < 0.5f && Agent.TargetPosition.HasValue ? MovementStatus.Moving : MovementStatus.Failed;

			return MovementStatus.Moving;
		}
	}
}

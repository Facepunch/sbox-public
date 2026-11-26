namespace Sandbox.Npcs.Layers;

/// <summary>
/// Flies the NPC straight at its target and hovers above it, gaining and losing speed smoothly and sliding along
/// whatever its hull hits. There is no pathfinding: a move that gets nowhere for a moment fails.
/// Its body is kinematic and weightless until physics takes it (see <see cref="MovementLayer.OnPhysicsControlStarted"/>).
/// </summary>
public class FlyingLayer : MovementLayer
{
	/// <summary>
	/// Where a <see cref="FlyingLayer"/> may go.
	/// </summary>
	[Expose]
	public enum FlyingBounds
	{
		/// <summary>Through air and water alike.</summary>
		Anywhere,

		/// <summary>Stays out of water volumes.</summary>
		AirOnly,

		/// <summary>Stays inside water volumes: it swims.</summary>
		WaterOnly,
	}

	/// <summary>
	/// How far above a move target to fly.
	/// </summary>
	[Property]
	public float HoverHeight { get; set; } = 80f;

	/// <summary>
	/// Whether it keeps to the air or to water volumes. It stops at the edge of what it may enter.
	/// </summary>
	[Property]
	public FlyingBounds Bounds { get; set; } = FlyingBounds.Anywhere;

	/// <summary>
	/// How quickly the flyer gains and loses speed, in units per second squared. It also turns a change of
	/// direction into a curve instead of a corner.
	/// </summary>
	[Property]
	public float Acceleration { get; set; } = 400f;

	/// <summary>
	/// How fast the flyer is going and which way, on the host. Set it to knock the flyer about, such as a bounce
	/// off something it hit: it then steers back to its move at <see cref="Acceleration"/>.
	/// </summary>
	public Vector3 Velocity { get; set; }

	public override bool IsMoving => _movedVelocity.WithZ( 0 ).Length > 20f;

	// How fast the object actually moved, whoever moved it
	private Vector3 _movedVelocity;
	private Rigidbody _body;
	private bool _physics;
	private bool _gravity = true;
	private float? _moveSpeed;
	private GameObject _moveFaceTarget;

	// Where we last got somewhere, to tell a blocked move
	private Vector3 _progressFrom;
	private TimeSince _sinceProgress;

	const float StuckDistance = 8f;
	const float StuckTime = 1.5f;

	protected override void OnEnabled()
	{
		_body = GetComponent<Rigidbody>( true );
		if ( !_body.IsValid() ) return;

		// Weightless while we fly it: another mover gets the gravity it had back
		_gravity = _body.Gravity;
		_body.Gravity = false;
		Npc.UpdateBodyOwner();
	}

	protected override void OnDisabled()
	{
		Stop();
		Velocity = default;

		if ( _body.IsValid() )
			_body.Gravity = _gravity;
	}

	public override void MoveTo( Vector3 target, float stopDistance = 10f, float? speed = null, GameObject faceTarget = null )
	{
		// A fresh move, not a moving target being followed
		if ( !MoveTarget.HasValue )
		{
			_progressFrom = WorldPosition;
			_sinceProgress = 0;
		}

		MoveTarget = target + Vector3.Up * HoverHeight;
		StopDistance = stopDistance;
		_moveSpeed = speed;
		_moveFaceTarget = faceTarget;
	}

	public override void Stop()
	{
		MoveTarget = null;
		_moveSpeed = null;
		_moveFaceTarget = null;
	}

	public override MovementStatus Status
	{
		get
		{
			if ( !MoveTarget.HasValue ) return MovementStatus.Idle;
			if ( WorldPosition.Distance( MoveTarget.Value ) <= StopDistance ) return MovementStatus.Arrived;

			// Blocked, or kept inside its bounds
			return _sinceProgress > StuckTime ? MovementStatus.Failed : MovementStatus.Moving;
		}
	}

	protected internal override void OnPhysicsControlStarted() => SetPhysics( true );

	protected internal override void OnPhysicsControlEnded() => SetPhysics( false );

	private void SetPhysics( bool physics )
	{
		_physics = physics;
		Velocity = default;
		Npc.UpdateBodyOwner();
	}

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost || !Npc.IsValid() )
			return;

		var from = WorldPosition;
		var flying = !_physics && !Npc.PhysicsCarrier.IsValid();
		if ( flying )
			Fly();

		// Only a move that is under way and not there yet can be stuck
		var arrived = MoveTarget is { } goal && WorldPosition.Distance( goal ) <= StopDistance;
		if ( !flying || !MoveTarget.HasValue || arrived || WorldPosition.Distance( _progressFrom ) > StuckDistance )
		{
			_progressFrom = WorldPosition;
			_sinceProgress = 0;
		}

		_movedVelocity = Time.Delta > 0 ? (WorldPosition - from) / Time.Delta : default;
		Npc.Animation.SetMove( _movedVelocity );
		Npc.Animation.IsGrounded = false;
	}

	private void Fly()
	{
		var cruise = _moveSpeed ?? WalkSpeed;
		var wanted = Vector3.Zero;
		if ( MoveTarget is { } target )
		{
			var delta = target - WorldPosition;
			var remaining = delta.Length - StopDistance;
			if ( remaining > 0 )
			{
				var speed = cruise;
				if ( Acceleration > 0 )
					speed = MathF.Min( speed, MathF.Sqrt( 2f * Acceleration * remaining ) );

				wanted = delta.Normal * speed;
			}
		}

		var change = wanted - Velocity;
		var most = Acceleration * Time.Delta;
		Velocity = Acceleration <= 0 || change.Length <= most ? wanted : Velocity + change.Normal * most;

		var step = Velocity * Time.Delta;
		if ( step.LengthSquared > 0.000001f )
			Move( step );

		// Face who we were told to, else the way we fly: fully at a real pace, less the slower we drift, and a hover
		// keeps the facing it has.
		var facing = (_moveFaceTarget.IsValid() ? _moveFaceTarget.WorldPosition - WorldPosition : Velocity).WithZ( 0 );
		var pace = MathF.Max( cruise * 0.35f, 20f );
		var turn = _moveFaceTarget.IsValid() ? 1f : Math.Clamp( facing.Length / pace, 0f, 1f );
		if ( facing.Length > 1f )
			WorldRotation = Rotation.Slerp( WorldRotation, Rotation.LookAt( facing ), Time.Delta * TurnSpeed * turn );
	}

	private void Move( Vector3 step )
	{
		var collider = GetComponent<BoxCollider>();
		var hull = collider.IsValid() ? collider.LocalBounds : BBox.FromPositionAndSize( Vector3.Zero, 16 );
		var from = WorldPosition;
		var tr = Sweep( hull, from, from + step );

		// Stood in the floor, as a walker that takes off does: rise until the hull is clear of it
		for ( var lift = 2f; tr.StartedSolid && lift <= 32f; lift += 2f )
		{
			from = WorldPosition + Vector3.Up * lift;
			tr = Sweep( hull, from, from + step );
		}

		var position = tr.StartedSolid ? WorldPosition : tr.EndPosition;

		// Hit something: lose the speed into it and spend the rest of the step sliding along it
		if ( tr.Hit && !tr.StartedSolid )
		{
			Velocity -= tr.Normal * Vector3.Dot( Velocity, tr.Normal );

			var rest = step * (1f - tr.Fraction);
			rest -= tr.Normal * Vector3.Dot( rest, tr.Normal );

			var slide = Sweep( hull, position, position + rest );
			if ( !slide.StartedSolid )
				position = slide.EndPosition;
		}

		if ( IsInBounds( position ) )
			WorldPosition = position;
		else
			Velocity = default;
	}

	private bool IsInBounds( Vector3 position )
	{
		if ( Bounds == FlyingBounds.Anywhere )
			return true;

		var water = WaterBody.Find( Scene, position ).IsValid();
		return Bounds == FlyingBounds.WaterOnly ? water : !water;
	}

	// The hull is swept upright, so turning never pushes it into a wall. Something that moves and already overlaps
	// it does not block it: two flyers that drifted into each other would otherwise never move again.
	private SceneTraceResult Sweep( BBox hull, Vector3 from, Vector3 to )
	{
		var trace = Scene.Trace.Box( hull, from, to ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" );
		var tr = trace.Run();

		for ( var i = 0; i < 4 && tr.StartedSolid && tr.GameObject.IsValid() && tr.Body is { BodyType: not PhysicsBodyType.Static }; i++ )
		{
			trace = trace.IgnoreGameObjectHierarchy( tr.GameObject.Root );
			tr = trace.Run();
		}

		return tr;
	}
}

namespace Sandbox.Npcs.Layers;

/// <summary>
/// Handles Npc navigation
/// </summary>
public class NavigationLayer : BaseNpcLayer
{
	public NavMeshAgent Agent { get; private set; }

	public Vector3? MoveTarget { get; private set; }

	[Property]
	public float StopDistance { get; private set; } = 10f;

	/// <summary>
	/// The desired movement speed for the agent (its normal walk). Schedules can raise this.
	/// </summary>
	public float WishSpeed { get; set; } = 100f;

	/// <summary>
	/// Speed used to run when the destination is far away -- HL-style: walk when close, run
	/// when there's ground to cover. Acts as a floor, so a schedule that sets a higher
	/// <see cref="WishSpeed"/> (fleeing, charging) is never slowed down.
	/// </summary>
	[Property]
	public float RunSpeed { get; set; } = 190f;

	/// <summary>Distance beyond which the NPC breaks into a run.</summary>
	[Property]
	public float RunDistance { get; set; } = 200f;

	/// <summary>
	/// When true (the default) the NPC turns to face the direction it's moving, so it runs
	/// forward. Combat schedules set this false to strafe -- face a target while moving.
	/// </summary>
	public bool FaceMovementDirection { get; set; } = true;

	/// <summary>How quickly the body turns to face the movement direction.</summary>
	[Property]
	public float TurnSpeed { get; set; } = 4f;

	/// <summary>
	/// True while the agent is actually travelling. Standing NPCs are free to turn
	/// their body toward whatever they're looking at.
	/// </summary>
	public bool IsMoving => Agent.IsValid() && Agent.Velocity.WithZ( 0 ).Length > 20f;

	private bool _running;
	private float? _moveSpeed;
	private GameObject _moveFaceTarget;

	protected override void OnStart()
	{
		if ( !Npc.IsValid() ) return;

		Agent = Npc.GetComponent<NavMeshAgent>();
	}

	/// <summary>
	/// Command this layer to move to a target
	/// </summary>
	public void MoveTo( Vector3 target, float stopDistance = 10f, float? speed = null, GameObject faceTarget = null )
	{
		MoveTarget = target;
		StopDistance = stopDistance;
		_moveSpeed = speed;
		_moveFaceTarget = faceTarget;

		if ( !Agent.IsValid() )
		{
			// Loud failure beats a silent freeze: an NPC that moves needs a NavMeshAgent.
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

	/// <summary>
	/// Stop moving and forget the current move target.
	/// </summary>
	public void Stop()
	{
		MoveTarget = null;
		_moveSpeed = null;
		_moveFaceTarget = null;
		_running = false;

		if ( Agent.IsValid() )
			Agent.Stop();
	}

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;
		if ( !Agent.IsValid() ) return;

		// Thrown or dropped - physics has us, not the navmesh, and the animation follows the body
		if ( Npc.IsAirborne ) return;

		Agent.MaxSpeed = ResolveSpeed();
		FaceMovement();

		// Reference the (now movement-facing) body rotation so the anim blends to a forward
		// run rather than a sideways strafe.
		Npc.Animation.SetMove( Agent.Velocity, Npc.WorldRotation );
	}

	// Turn the body to face the way we're actually moving, so the NPC runs forward instead of
	// sliding sideways. Disabled while strafing (combat).
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

		if ( !FaceMovementDirection )
			return;

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

		var speed = WishSpeed;

		if ( !MoveTarget.HasValue )
			return speed;

		var distance = Npc.WorldPosition.Distance( MoveTarget.Value );

		if ( _running && distance < RunDistance * 0.6f )
			_running = false;
		else if ( !_running && distance > RunDistance )
			_running = true;

		return _running ? MathF.Max( speed, RunSpeed ) : speed;
	}

	public override string GetDebugString()
	{
		if ( !MoveTarget.HasValue ) return null;

		var status = GetStatus();
		var dist = Npc.WorldPosition.Distance( MoveTarget.Value ).CeilToInt();
		return $"Nav: {status} ({dist}u)";
	}

	/// <summary>
	/// Current navigation status — reached target, still moving, or failed.
	/// </summary>
	public TaskStatus GetStatus()
	{
		if ( !MoveTarget.HasValue ) return TaskStatus.Success;

		// No agent means we can never get there -- fail instead of hanging on Running forever.
		if ( !Agent.IsValid() ) return TaskStatus.Failed;

		var distance = Npc.WorldPosition.Distance( MoveTarget.Value );

		if ( distance <= StopDistance )
			return TaskStatus.Success;

		if ( !Agent.IsNavigating )
			return TaskStatus.Failed;

		return TaskStatus.Running;
	}

	public override void ResetLayer()
	{
		Stop();
	}
}

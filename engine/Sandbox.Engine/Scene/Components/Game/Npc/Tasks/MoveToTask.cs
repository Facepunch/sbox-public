using Sandbox.Npcs.Layers;

namespace Sandbox.Npcs.Tasks;

/// <summary>
/// Commands the NPC's movement layer to a position or GameObject, periodically updating a moving target's path.
/// Leaves the look target unchanged; the movement layer controls facing.
/// </summary>
public class MoveToTask : BaseTask
{
	/// <summary>
	/// The point to move to, when the task was given a position.
	/// </summary>
	public Vector3? TargetPosition { get; }
	/// <summary>
	/// The object to move to, followed as it moves, when the task was given one.
	/// </summary>
	public GameObject TargetObject { get; }
	/// <summary>
	/// How close to the target counts as arrived.
	/// </summary>
	public float StopDistance { get; } = 10f;
	internal float ReevaluateInterval { get; set; } = 0.5f;

	/// <summary>Movement speed for this task. Null uses the NPC's configured speed.</summary>
	public float? Speed { get; set; }

	/// <summary>
	/// Keep facing this object while moving instead of turning into the movement
	/// direction. The NPC backpedals or strafes -- short moves away from someone
	/// happen without turning our back on them.
	/// </summary>
	public GameObject FaceTarget { get; set; }

	private TimeSince _lastReevaluate;
	private MovementLayer _mover;
	private bool _requested;

	public MoveToTask( Vector3 targetPosition, float stopDistance = 10f )
	{
		TargetPosition = targetPosition;
		StopDistance = stopDistance;
	}

	public MoveToTask( GameObject targetObject, float stopDistance = 10f )
	{
		TargetObject = targetObject;
		StopDistance = stopDistance;
	}

	protected override void OnStart()
	{
		_mover = Npc.Movement;
		_requested = false;

		var pos = GetTargetPosition();
		if ( !pos.HasValue ) return;

		_mover?.MoveTo( pos.Value, StopDistance, Speed, FaceTarget );
		_requested = true;
		_lastReevaluate = 0;
	}

	protected override TaskResult OnUpdate()
	{
		if ( !Npc.Movement.IsValid() || Npc.Movement != _mover )
			return TaskResult.Failed;

		if ( TargetObject is not null && !TargetObject.IsValid() )
			return TaskResult.Failed;

		if ( TargetObject.IsValid() && _lastReevaluate > ReevaluateInterval )
		{
			var pos = GetTargetPosition();
			if ( pos.HasValue )
				Npc.Movement.MoveTo( pos.Value, StopDistance, Speed, FaceTarget );
			_lastReevaluate = 0;
		}

		return Npc.Movement.Status switch
		{
			MovementLayer.MovementStatus.Moving => TaskResult.Running,
			MovementLayer.MovementStatus.Arrived => TaskResult.Success,
			MovementLayer.MovementStatus.Failed => TaskResult.Failed,

			// Idle: the move we asked for was dropped, or there was nowhere to go to begin with
			_ => _requested ? TaskResult.Failed : TaskResult.Success,
		};
	}

	protected override void OnEnd()
	{
		Npc.Movement?.Stop();
	}

	private Vector3? GetTargetPosition()
	{
		if ( TargetObject.IsValid() )
		{
			// Navigate to the closest point on the object's bounds, not its origin.
			// This prevents the NPC from trying to walk inside large props.
			var bounds = TargetObject.GetBounds();
			return bounds.ClosestPoint( Npc.WorldPosition );
		}

		return TargetPosition;
	}
}

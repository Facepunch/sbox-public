using Sandbox.Npcs.Schedules;

namespace Sandbox.Npcs.Tasks;

/// <summary>
/// Sets a persistent look target on the AnimationLayer and waits until the NPC is facing it.
/// The look target persists after this task completes - call <see cref="Layers.AnimationLayer.ClearLookTarget"/>
/// when you no longer need it (typically in <see cref="BaseSchedule.OnEnd"/>).
/// </summary>
public class LookAtTask : BaseTask
{
	/// <summary>
	/// The point to look at, when the task was given a position.
	/// </summary>
	public Vector3? TargetPosition { get; }
	/// <summary>
	/// The object to look at, when the task was given one.
	/// </summary>
	public GameObject TargetObject { get; }

	public LookAtTask( Vector3 targetPosition )
	{
		TargetPosition = targetPosition;
	}

	public LookAtTask( GameObject gameObject )
	{
		TargetObject = gameObject;
	}

	protected override void OnStart()
	{
		if ( TargetObject.IsValid() )
			Npc.Animation.SetLookTarget( TargetObject );
		else if ( TargetPosition.HasValue )
			Npc.Animation.SetLookTarget( TargetPosition.Value );
	}

	protected override TaskResult OnUpdate()
	{
		if ( !TargetObject.IsValid() && !TargetPosition.HasValue )
			return TaskResult.Failed;

		return Npc.Animation.IsFacingTarget() ? TaskResult.Success : TaskResult.Running;
	}
}

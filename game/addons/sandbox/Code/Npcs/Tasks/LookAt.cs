namespace Sandbox.Npcs.Tasks;

/// <summary>
/// Sets a persistent look target on the AnimationLayer and waits until the NPC is facing it.
/// The look target persists after this task completes — call <see cref="Layers.AnimationLayer.ClearLookTarget"/>
/// when you no longer need it (typically in <see cref="ScheduleBase.OnEnd"/>).
/// </summary>
public class LookAt : TaskBase
{
	public Vector3? TargetPosition { get; set; }
	public GameObject TargetObject { get; set; }

	public LookAt( Vector3 targetPosition )
	{
		TargetPosition = targetPosition;
	}

	public LookAt( GameObject gameObject )
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

	protected override TaskStatus OnUpdate()
	{
		if ( !TargetObject.IsValid() && !TargetPosition.HasValue )
			return TaskStatus.Failed;

		return Npc.Animation.IsFacingTarget() ? TaskStatus.Success : TaskStatus.Running;
	}
}

using Sandbox.Npcs.Tasks;

namespace Sandbox.Npcs.Schedules;

/// <summary>
/// Move to a point, look around, and wait -- used to check out a last-known position or a
/// heard disturbance. Re-spotting a target preempts this with a higher-priority schedule.
/// </summary>
public class InvestigateSchedule : BaseSchedule
{
	/// <summary>
	/// The spot the NPC walks to and looks around at.
	/// </summary>
	public Vector3 TargetPosition { get; set; }

	/// <summary>
	/// Speed to head over at. Null walks, breaking into a run if it's far.
	/// </summary>
	public float? Speed { get; set; }

	public override int Priority => SchedulePriorities.Alert;

	protected override void OnStart()
	{
		AddTask( new MoveToTask( TargetPosition, stopDistance: 64f ) { Speed = Speed } );

		// TargetPosition is on the ground (where a shot or death happened) - look there at head height,
		// or we arrive and stare at the floor in front of us
		AddTask( new LookAtTask( TargetPosition + Vector3.Up * Npc.EyeHeight ) );
		AddTask( new WaitTask( 1f ) );
	}

	protected override void OnEnd()
	{
		// LookAt's target persists - drop it, or we keep staring at this spot through whatever's next
		Npc.Animation.ClearLookTarget();
	}
}

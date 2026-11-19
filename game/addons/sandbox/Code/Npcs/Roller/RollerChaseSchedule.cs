using Sandbox.Npcs.Roller.Tasks;

namespace Sandbox.Npcs.Roller.Schedules;

/// <summary>
/// Roller chase: roll toward target then leap at it.
/// On completion the schedule ends naturally, GetSchedule re-picks it and loops.
/// </summary>
public sealed class RollerChaseSchedule : ScheduleBase
{
	public override int Priority => SchedulePriority.Combat;

	protected override void OnStart()
	{
		(Npc as RollerNpc)?.SetHunting( true );
		AddTask( new RollerRollTask() );
		AddTask( new RollerLeapTask() );
	}

	protected override void OnEnd()
	{
		(Npc as RollerNpc)?.SetHunting( false );
	}

	protected override void OnCancelled()
	{
		(Npc as RollerNpc)?.SetHunting( false );
	}

	protected override bool ShouldCancel()
	{
		return !Npc.Senses.GetNearestVisible().IsValid();
	}
}

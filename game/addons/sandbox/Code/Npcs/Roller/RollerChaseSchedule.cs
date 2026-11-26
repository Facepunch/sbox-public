using Sandbox.Npcs.Roller.Tasks;
using Sandbox.Npcs.Schedules;

namespace Sandbox.Npcs.Roller.Schedules;

/// <summary>
/// Roller chase: roll toward target then leap at it.
/// On completion the schedule ends naturally and SelectSchedule picks it again.
/// </summary>
public sealed class RollerChaseSchedule : BaseSchedule
{
	public override int Priority => SchedulePriorities.Combat;

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
		return !Npc.Senses.FindBestTarget().IsValid();
	}
}

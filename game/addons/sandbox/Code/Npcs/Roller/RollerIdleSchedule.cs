using Sandbox.Npcs.Tasks;

namespace Sandbox.Npcs.Roller.Schedules;

/// <summary>
/// Roller idles when no target is visible — waits briefly then re-evaluates.
/// </summary>
public sealed class RollerIdleSchedule : ScheduleBase
{
	protected override void OnStart()
	{
		AddTask( new Wait( Game.Random.Float( 1f, 2.5f ) ) );
	}

}

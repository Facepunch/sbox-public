using Sandbox.Npcs.Tasks;

namespace Sandbox.Npcs.Schedules;

/// <summary>
/// Walk to a nearby prop, look at it, and maybe comment on it.
/// </summary>
public sealed class ScientistInspectPropSchedule : ScheduleBase
{
	public GameObject PropTarget { get; set; }

	/// <summary>
	/// Walking pace.
	/// </summary>
	public float Speed { get; set; } = 85f;

	protected override void OnStart()
	{
		if ( !PropTarget.IsValid() ) return;

		// Persistently track the prop — the NPC will keep looking at it
		// throughout the entire schedule without needing repeated LookAt tasks.
		Npc.Animation.SetLookTarget( PropTarget );

		// Walk toward the prop — tracks it if it moves
		AddTask( new MoveTo( PropTarget, 40f ) { Speed = Speed } );

		AddTask( new Wait( 1 ) );

		AddTask( new Wait( Game.Random.Float( 2f, 4f ) ) );
	}

	protected override void OnEnd()
	{
		Npc.Animation.ClearLookTarget();
	}
}

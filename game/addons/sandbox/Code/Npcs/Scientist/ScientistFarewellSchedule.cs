using Sandbox.Npcs.Tasks;

namespace Sandbox.Npcs.Schedules;

/// <summary>
/// Stop, turn to face whoever we're talking to, deliver a line while looking them
/// in the eyes, and only then get on with life. Used when a player dismisses the
/// scientist, so it says goodbye to their face instead of muttering it while
/// wandering off.
/// </summary>
public sealed class ScientistFarewellSchedule : ScheduleBase
{
	public GameObject Target { get; set; }
	public SoundEvent Voice { get; set; }

	public override int Priority => SchedulePriority.Ambient;

	protected override void OnStart()
	{
		if ( !Target.IsValid() )
			return;

		// Plant our feet - we might still be mid-step from following them
		Npc.Navigation.Stop();

		// Face them first, then say the line - Say waits for the speech to
		// finish, so we stand our ground until the sentence is done
		AddTask( new LookAt( Target ) );
		AddTask( new Say( Voice, 0f, Target )
		{
			Tags = ["reaction", "acknowledgement"],
			Priority = int.MaxValue
		} );
	}

	protected override void OnEnd()
	{
		Npc.Animation.ClearLookTarget();
	}
}

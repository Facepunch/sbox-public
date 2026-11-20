using Sandbox.Npcs.Layers;
using Sandbox.Npcs.Tasks;

namespace Sandbox.Npcs.Schedules;

/// <summary>
/// Stop, turn to face whoever we're talking to, deliver a line while looking them
/// in the eyes, and only then get on with life. Used when a player dismisses the
/// scientist, so it says goodbye to their face instead of muttering it while
/// wandering off.
/// </summary>
public sealed class ScientistFarewellSchedule : BaseSchedule
{
	public GameObject Target { get; set; }
	public SoundEvent Voice { get; set; }

	public override int Priority => SchedulePriorities.Ambient;

	protected override void OnStart()
	{
		if ( !Target.IsValid() )
			return;

		// Plant our feet - we might still be mid-step from following them
		Npc.Movement.Stop();

		// SayTask keeps the schedule active until speech finishes.
		AddTask( new LookAtTask( Target ) );
		AddTask( new SayTask( Voice, new SpeechOptions { LookAt = Target, Tags = ["reaction", "acknowledgement"], Priority = int.MaxValue } ) );
	}

	protected override void OnEnd()
	{
		Npc.Animation.ClearLookTarget();
	}
}

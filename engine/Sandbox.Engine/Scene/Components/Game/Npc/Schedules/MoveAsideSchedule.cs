using Sandbox.Npcs.Tasks;

namespace Sandbox.Npcs.Schedules;

/// <summary>
/// A player is walking into us -- step sideways out of their path and let them
/// through. We step to whichever side of their path we're
/// already closest to, so we clear it instead of crossing in front of them,
/// and watch them as they pass.
/// </summary>
public class MoveAsideSchedule : BaseSchedule
{
	/// <summary>The player barging into us.</summary>
	public GameObject Blocker { get; set; }

	/// <summary>How far to step out of the way.</summary>
	internal float StepDistance { get; set; } = 80f;

	/// <summary>Speed to step aside at. Null uses the NPC's walk.</summary>
	public float? Speed { get; set; }

	// Above ambient behaviour like following and idling, below fleeing and combat --
	// we step out of the way unless something more important is going on.
	public override int Priority => SchedulePriorities.Alert;

	protected override void OnStart()
	{
		if ( !Blocker.IsValid() )
			return;

		// The direction they're travelling; if they've already stopped, fall back
		// to the line from them to us
		var travel = BaseNpc.GetVelocity( Blocker ).WithZ( 0 );
		if ( travel.Length < 1f )
			travel = (Npc.WorldPosition - Blocker.WorldPosition).WithZ( 0 );

		if ( travel.Length < 1f )
			return;

		// Step perpendicular to their path, to the side we're already offset toward
		var side = travel.Normal.Cross( Vector3.Up );
		var toUs = (Npc.WorldPosition - Blocker.WorldPosition).WithZ( 0 );
		if ( side.Dot( toUs ) < 0f )
			side = -side;

		// Watch them go by while we make room, strafing aside rather than
		// turning our back on them
		Npc.Animation.AddLookTarget( Blocker, 2f );

		AddTask( new MoveToTask( Npc.WorldPosition + side * StepDistance, 8f ) { Speed = Speed, FaceTarget = Blocker } );
		AddTask( new WaitTask( 0.5f ) );
	}

	protected override void OnEnd()
	{
		Npc.Animation.ClearLookTarget();
	}

	protected override bool ShouldCancel() => !Blocker.IsValid();
}

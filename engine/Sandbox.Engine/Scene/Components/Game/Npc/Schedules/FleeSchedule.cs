using Sandbox.Npcs.Tasks;

namespace Sandbox.Npcs.Schedules;

/// <summary>
/// Sprints away from the source, with speed and distance set by panic.
/// </summary>
public class FleeSchedule : BaseSchedule
{
	/// <summary>
	/// What the NPC runs away from.
	/// </summary>
	public GameObject Source { get; set; }

	/// <summary>
	/// 0–1 panic intensity. Higher values mean faster speed and longer flee distance.
	/// </summary>
	public float PanicLevel { get; set; } = 0.5f;

	public override int Priority => SchedulePriorities.Survival;

	// Don't get distracted while panicking -- flee ends when the source is gone or we
	// reach safety, then selection re-evaluates fear.
	public override bool Interruptible => false;

	protected override void OnStart()
	{
		if ( !Source.IsValid() ) return;

		var speed = 200f + 150f * PanicLevel;

		// Don't stare at the player - look where we're running
		Npc.Animation.ClearLookTarget();

		var awayDir = (GameObject.WorldPosition - Source.WorldPosition).WithZ( 0 ).Normal;
		var randomAngle = Game.Random.Float( -40f, 40f );
		awayDir = Rotation.FromAxis( Vector3.Up, randomAngle ) * awayDir;

		var fleeDist = 512f + 1024f * PanicLevel;
		var fleeTarget = GameObject.WorldPosition + awayDir * fleeDist;

		fleeTarget = Npc.Movement?.ProjectPoint( fleeTarget ) ?? fleeTarget;
		AddTask( new MoveToTask( fleeTarget, 15f ) { Speed = speed } );
	}

	protected override bool ShouldCancel()
	{
		return !Source.IsValid();
	}
}

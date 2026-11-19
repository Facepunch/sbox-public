using Sandbox.Npcs.Layers;
using Sandbox.Npcs.Tasks;

namespace Sandbox.Npcs.Schedules;

/// <summary>
/// Panic flee — scream while sprinting away from the source.
/// </summary>
public sealed class ScientistFleeSchedule : ScheduleBase
{
	public GameObject Source { get; set; }

	/// <summary>
	/// 0–1 panic intensity. Higher values mean faster speed and longer flee distance.
	/// </summary>
	public float PanicLevel { get; set; } = 0.5f;

	public override int Priority => SchedulePriority.Survival;

	// Don't get distracted while panicking -- flee ends when the source is gone or we
	// reach safety, then selection re-evaluates fear.
	public override NpcAwareness InterruptedBy => NpcAwareness.None;

	protected override void OnStart()
	{
		if ( !Source.IsValid() ) return;

		// Sprint speed scales with panic (200–350)
		var speed = 200f + 150f * PanicLevel;

		// Don't stare at the player — look where we're running
		Npc.Animation.ClearLookTarget();

		// (The scientist screams via its ScaredVoice when it enters this state.)

		// Flee direction — away from the attacker with some randomness
		var awayDir = (GameObject.WorldPosition - Source.WorldPosition).WithZ( 0 ).Normal;
		var randomAngle = Game.Random.Float( -40f, 40f );
		awayDir = Rotation.FromAxis( Vector3.Up, randomAngle ) * awayDir;

		// Distance scales with panic (200–500)
		var fleeDist = 512f + 1024f * PanicLevel;
		var fleeTarget = GameObject.WorldPosition + awayDir * fleeDist;

		// Snap to navmesh
		if ( Npc.Scene.NavMesh.GetClosestPoint( fleeTarget ) is { } navPoint )
		{
			AddTask( new MoveTo( navPoint, 15f ) { Speed = speed } );
		}
		else
		{
			AddTask( new MoveTo( fleeTarget, 15f ) { Speed = speed } );
		}
	}

	protected override bool ShouldCancel()
	{
		return !Source.IsValid();
	}
}

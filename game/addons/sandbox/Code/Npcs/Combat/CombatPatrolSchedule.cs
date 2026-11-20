using Sandbox.Npcs.Tasks;

namespace Sandbox.Npcs.CombatNpc;

/// <summary>
/// Wanders to random nearby points when no player is known.
/// </summary>
public class CombatPatrolSchedule : ScheduleBase
{
	private static readonly string[] PatrolLines =
	{
		"#npc.speech.combat.patrol.1",
		"#npc.speech.combat.patrol.2",
		"#npc.speech.combat.patrol.3",
		"#npc.speech.combat.patrol.4",
		"#npc.speech.combat.patrol.5",
		"#npc.speech.combat.patrol.6",
		"#npc.speech.combat.patrol.7",
		"#npc.speech.combat.patrol.8",
	};

	/// <summary>
	/// Maximum distance from current position to pick a patrol destination.
	/// </summary>
	public float PatrolRadius { get; set; } = 400f;

	/// <summary>
	/// Walking pace between patrol points.
	/// </summary>
	public float Speed { get; set; } = 85f;

	protected override void OnStart()
	{
		var dest = GetPatrolDestination();
		AddTask( new MoveTo( dest, 15f ) { Speed = Speed } );

		if ( Npc.Speech.CanSpeak && Game.Random.Float() < 0.2f )
			AddTask( new Say( Game.Random.FromArray( PatrolLines ), 2.5f ) );
		else
			AddTask( new Wait( Game.Random.Float( 3f, 7f ) ) );
	}


	private Vector3 GetPatrolDestination()
	{
		var dir = Vector3.Random.WithZ( 0 ).Normal;
		var dist = Game.Random.Float( PatrolRadius * 0.3f, PatrolRadius );
		var candidate = Npc.WorldPosition + dir * dist;

		if ( Npc.Scene.NavMesh.GetClosestPoint( candidate ) is { } nav )
			return nav;

		return candidate;
	}
}

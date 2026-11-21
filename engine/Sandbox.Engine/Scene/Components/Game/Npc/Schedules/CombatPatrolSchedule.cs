using Sandbox.Npcs.Layers;
using Sandbox.Npcs.Tasks;

namespace Sandbox.Npcs.Schedules;

/// <summary>
/// Wanders to random nearby points when no player is known.
/// </summary>
public class CombatPatrolSchedule : BaseSchedule
{
	/// <summary>
	/// Maximum distance from current position to pick a patrol destination.
	/// </summary>
	internal float PatrolRadius { get; set; } = 400f;

	/// <summary>
	/// Walking pace between patrol points.
	/// </summary>
	public float Speed { get; set; } = 85f;

	protected override void OnStart()
	{
		var dest = GetPatrolDestination();
		AddTask( new MoveToTask( dest, 15f ) { Speed = Speed } );

		if ( Npc is BaseCombatNpc { PatrolLines: { Count: > 0 } lines } && Npc.Speech.IsValid() && Npc.Speech.CanSpeak && Game.Random.Float() < 0.2f )
			AddTask( new SayTask( Game.Random.FromList( lines ), new SpeechOptions { Duration = 2.5f } ) );
		else
			AddTask( new WaitTask( Game.Random.Float( 3f, 7f ) ) );
	}


	private Vector3 GetPatrolDestination()
	{
		var dir = Vector3.Random.WithZ( 0 ).Normal;
		var dist = Game.Random.Float( PatrolRadius * 0.3f, PatrolRadius );
		var candidate = Npc.WorldPosition + dir * dist;

		return Npc.Movement?.ProjectPoint( candidate ) ?? candidate;
	}
}

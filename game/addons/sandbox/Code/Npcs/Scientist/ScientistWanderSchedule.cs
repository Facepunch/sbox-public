using Sandbox.Npcs.Layers;
using Sandbox.Npcs.Tasks;

namespace Sandbox.Npcs.Schedules;

/// <summary>
/// Wander to a random nearby point, avoiding player directions.
/// </summary>
public sealed class ScientistWanderSchedule : ScheduleBase
{
	/// <summary>
	/// Walking pace.
	/// </summary>
	public float Speed { get; set; } = 85f;

	protected override void OnStart()
	{
		var randomDir = Vector3.Random.WithZ( 0 ).Normal;

		// Don't wander toward nearby players (a neutral scientist still shies away)
		var nearest = Npc.Senses.GetNearestVisible( "player" );
		if ( nearest.IsValid() )
		{
			var toPlayer = (nearest.WorldPosition - GameObject.WorldPosition).WithZ( 0 ).Normal;
			if ( randomDir.Dot( toPlayer ) > 0.3f )
			{
				// Flip away from the player with some randomness
				randomDir = (-toPlayer + Vector3.Random.WithZ( 0 ).Normal * 0.5f).WithZ( 0 ).Normal;
			}
		}

		var wanderTarget = GameObject.WorldPosition + randomDir * Game.Random.Float( 150f, 350f );

		AddTask( new MoveTo( wanderTarget, 15f ) { Speed = Speed } );
		AddTask( new Wait( Game.Random.Float( 1f, 2f ) ) );
	}
}

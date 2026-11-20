using Sandbox.Npcs.Tasks;

namespace Sandbox.Npcs.Schedules;

/// <summary>
/// Idle in place — glance around at eye level, and watch the people we can see,
/// HL2 style: a visible player gets looked at for a while, then other NPCs, and
/// otherwise we just scan the room.
/// </summary>
public sealed class ScientistIdleSchedule : ScheduleBase
{
	protected override void OnStart()
	{
		// A player we can actually see is the most interesting thing around
		var player = Npc.Senses.GetNearestVisible( "player" );
		if ( player.IsValid() && Game.Random.Float() < 0.5f )
		{
			Watch( player, Game.Random.Float( 2.5f, 5f ) );
			return;
		}

		// Otherwise, watch another NPC we can see for a bit
		var other = NearestVisibleNpc();
		if ( other.IsValid() && Game.Random.Float() < 0.4f )
		{
			Watch( other, Game.Random.Float( 1.5f, 3.5f ) );
			return;
		}

		// Nobody around - pick a direction within comfortable head range of where
		// we're already facing, at eye height, so we scan the room rather than
		// the floor. Kept inside MaxHeadAngle: the body doesn't turn during idle,
		// so a wider glance would pin the head at its limit and never complete.
		var forward = GameObject.WorldRotation.Forward.WithZ( 0 ).Normal;
		var yawOffset = Game.Random.Float( -40f, 40f );
		var lookDir = Rotation.FromAxis( Vector3.Up, yawOffset ) * forward;
		var lookTarget = GameObject.WorldPosition + Vector3.Up * 60f + lookDir * 100f;
		AddTask( new LookAt( lookTarget ) );

		// wait a bit, with random deviation
		AddTask( new Wait( Game.Random.Float( 1f, 3f ) ) );
	}

	protected override void OnEnd()
	{
		// Don't leave the glance point lingering into the next schedule
		Npc.Animation.ClearLookTarget();
	}

	// Look someone in the eyes for a while
	private void Watch( GameObject who, float duration )
	{
		Npc.Animation.AddLookTarget( who, duration );
		AddTask( new Wait( duration ) );
	}

	// The nearest NPC we can actually see (field of view + line of sight)
	private GameObject NearestVisibleNpc()
	{
		GameObject best = null;
		var bestDistance = float.MaxValue;

		foreach ( var npc in Npc.Senses.GetVisible( "npc" ) )
		{
			if ( !npc.IsValid() || npc == Npc.GameObject )
				continue;

			var distance = npc.WorldPosition.Distance( Npc.WorldPosition );
			if ( distance < bestDistance )
			{
				bestDistance = distance;
				best = npc;
			}
		}

		return best;
	}
}

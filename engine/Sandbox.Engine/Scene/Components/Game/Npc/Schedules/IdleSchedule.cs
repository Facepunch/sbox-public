using Sandbox.Npcs.Tasks;

namespace Sandbox.Npcs.Schedules;

/// <summary>
/// Idle in place - glance around at eye level, and watch the people we can see:
/// a visible player gets looked at for a while, then other NPCs, and
/// otherwise we just scan the room.
/// </summary>
public class IdleSchedule : BaseSchedule
{
	protected override void OnStart()
	{
		// A player we can actually see is the most interesting thing around
		var player = Npc.Senses?.FindNearestVisibleOfFaction( Factions.Player );
		if ( player.IsValid() && Game.Random.Float() < 0.5f )
		{
			Watch( player, Game.Random.Float( 2.5f, 5f ) );
			return;
		}

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
		var lookTarget = Npc.EyePosition + lookDir * 100f;
		AddTask( new LookAtTask( lookTarget ) );

		AddTask( new WaitTask( Game.Random.Float( 1f, 3f ) ) );
	}

	protected override void OnEnd()
	{
		Npc.Animation.ClearLookTarget();
	}

	private void Watch( GameObject who, float duration )
	{
		Npc.Animation.AddLookTarget( who, duration );
		AddTask( new WaitTask( duration ) );
	}

	private GameObject NearestVisibleNpc()
	{
		if ( !Npc.Senses.IsValid() )
			return null;

		var nearest = Npc.Senses.FindNearestVisible( "npc" );
		return nearest.IsValid() && nearest != GameObject ? nearest : null;
	}
}

using Sandbox.Npcs.Tasks;

namespace Sandbox.Npcs.Schedules;

/// <summary>
/// Hold a post: stand at ease facing the way we were posted, now and then glance
/// around, and walk back to the post if a fight or search pulled us away from it.
/// </summary>
public class CombatIdleSchedule : BaseSchedule
{
	/// <summary>
	/// Where to stand.
	/// </summary>
	public Vector3 Post { get; set; }

	/// <summary>
	/// Which way to face while standing there.
	/// </summary>
	internal Vector3 PostForward { get; set; } = Vector3.Forward;

	/// <summary>
	/// Walking pace back to the post.
	/// </summary>
	public float WalkSpeed { get; set; } = 85f;

	/// <summary>
	/// Further than this from the post (chased someone a long way), we hold where we are instead.
	/// </summary>
	internal float ReturnRange { get; set; } = 1500f;

	protected override void OnStart()
	{
		var post = Post;
		var forward = PostForward.WithZ( 0 ).Normal;
		var distance = Npc.WorldPosition.Distance( post );

		if ( distance > ReturnRange )
		{
			// Too far to bother going back - hold here, facing the way we are
			post = Npc.WorldPosition;
			forward = Npc.WorldRotation.Forward.WithZ( 0 ).Normal;
		}
		else if ( distance > 48f )
		{
			AddTask( new MoveToTask( post, 24f ) { Speed = WalkSpeed } );
		}

		AddTask( new LookAtTask( post + forward * 400f + Vector3.Up * Npc.EyeHeight ) );
		AddTask( new WaitTask( Game.Random.Float( 4f, 9f ) ) );

		// Now and then, look around - usually just a turn of the head, sometimes the whole body
		if ( Game.Random.Float() < 0.6f )
		{
			var wide = Game.Random.Float() < 0.25f;
			var yaw = Game.Random.Float( wide ? 70f : 15f, wide ? 120f : 40f ) * (Game.Random.Float() < 0.5f ? -1f : 1f);
			var direction = Rotation.FromYaw( yaw ) * forward;

			AddTask( new LookAtTask( post + direction * 400f + Vector3.Up * Npc.EyeHeight ) );
			AddTask( new WaitTask( Game.Random.Float( 2f, 4f ) ) );
		}
	}

	protected override void OnEnd()
	{
		Npc.Animation.ClearLookTarget();
	}
}

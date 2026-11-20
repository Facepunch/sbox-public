using Sandbox.Npcs.Tasks;

namespace Sandbox.Npcs.CombatNpc;

/// <summary>
/// Hold a post, HL2-style: stand at ease facing the way we were posted, now and then glance
/// around, and walk back to the post if a fight or search pulled us away from it.
/// </summary>
public class CombatIdleSchedule : ScheduleBase
{
	/// <summary>
	/// Where to stand.
	/// </summary>
	public Vector3 Post { get; set; }

	/// <summary>
	/// Which way to face while standing there.
	/// </summary>
	public Vector3 PostForward { get; set; } = Vector3.Forward;

	/// <summary>
	/// Walking pace back to the post.
	/// </summary>
	public float WalkSpeed { get; set; } = 85f;

	/// <summary>
	/// Further than this from the post (chased someone a long way), we hold where we are instead.
	/// </summary>
	public float ReturnRange { get; set; } = 1500f;

	private static readonly Vector3 EyeHeight = Vector3.Up * 64f;

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
			// Pulled away from the post - walk back to it
			AddTask( new MoveTo( post, 24f ) { Speed = WalkSpeed } );
		}

		// Face the way we're posted, then stand at ease for a while
		AddTask( new LookAt( post + forward * 400f + EyeHeight ) );
		AddTask( new Wait( Game.Random.Float( 4f, 9f ) ) );

		// Now and then, look around - usually just a turn of the head, sometimes the whole body
		if ( Game.Random.Float() < 0.6f )
		{
			var wide = Game.Random.Float() < 0.25f;
			var yaw = Game.Random.Float( wide ? 70f : 15f, wide ? 120f : 40f ) * (Game.Random.Float() < 0.5f ? -1f : 1f);
			var direction = Rotation.FromYaw( yaw ) * forward;

			AddTask( new LookAt( post + direction * 400f + EyeHeight ) );
			AddTask( new Wait( Game.Random.Float( 2f, 4f ) ) );
		}
	}

	protected override void OnEnd()
	{
		// Let the head settle back rather than holding the last glance
		Npc.Animation.ClearLookTarget();
	}
}

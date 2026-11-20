using Sandbox.Npcs.Tasks;

namespace Sandbox.Npcs.Schedules;

/// <summary>
/// Follow a target around, keeping a respectful distance. Re-evaluates each cycle so it
/// tracks a moving target. Higher-priority schedules (like fleeing danger) preempt it.
/// </summary>
public sealed class FollowSchedule : ScheduleBase
{
	public GameObject Target { get; set; }

	/// <summary>
	/// How close we walk up to the target before stopping.
	/// </summary>
	public float FollowDistance { get; set; } = 200f;

	/// <summary>
	/// How far the target gets before we start walking again. The band between
	/// this and FollowDistance is "close enough" - without it, a crowd of
	/// followers endlessly jockeys and shoves to close the last few units.
	/// </summary>
	public float CatchUpDistance { get; set; } = 320f;

	/// <summary>
	/// If the target walks right up into our face, back off to arm's length --
	/// facing them and walking backwards, not turning our back on them.
	/// </summary>
	public float TooCloseDistance { get; set; } = 80f;

	// Everyone settles at a slightly different range, so a group forms loose
	// layers instead of fighting over the same ring around the target
	private readonly float _personalSpace = Game.Random.Float( 0f, 60f );

	public override int Priority => SchedulePriority.Ambient;

	protected override void OnStart()
	{
		if ( !Target.IsValid() )
		{
			AddTask( new Wait( 0.5f ) );
			return;
		}

		// Keep looking at whoever we're following. While we stand waiting, the body
		// turns to face them too, so we idle fronting our leader rather than
		// wherever we last walked.
		Npc.Animation.SetLookTarget( Target );

		var distance = Npc.WorldPosition.Distance( Target.WorldPosition );

		// They've walked into us - backpedal out of their face without turning away
		if ( distance < TooCloseDistance )
		{
			var away = (Npc.WorldPosition - Target.WorldPosition).WithZ( 0 ).Normal;
			if ( away.Length < 0.5f )
				away = Npc.WorldRotation.Backward.WithZ( 0 ).Normal;

			var backTo = Target.WorldPosition + away * (TooCloseDistance + 50f);
			AddTask( new MoveTo( backTo, 10f ) { FaceTarget = Target } );
			AddTask( new Wait( 0.25f ) );
			return;
		}

		// Close enough - stand and watch them rather than pushing in
		if ( distance <= CatchUpDistance + _personalSpace )
		{
			AddTask( new Wait( 0.5f ) );
			return;
		}

		AddTask( new MoveTo( Target, FollowDistance + _personalSpace ) );
		AddTask( new Wait( 0.5f ) );
	}

	protected override void OnEnd()
	{
		Npc.Animation.ClearLookTarget();
	}

	protected override bool ShouldCancel() => !Target.IsValid();
}

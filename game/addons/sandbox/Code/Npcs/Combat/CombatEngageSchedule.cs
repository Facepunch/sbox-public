using Sandbox.Npcs.Tasks;

namespace Sandbox.Npcs.CombatNpc;

/// <summary>
/// Engages a visible player: close the gap, fire a burst, pause, then reposition to a flanking point.
/// Cancels immediately if the target leaves sight.
/// </summary>
public class CombatEngageSchedule : ScheduleBase
{
	private static readonly string[] SpotLines =
	{
		"#npc.speech.combat.spot.1",
		"#npc.speech.combat.spot.2",
		"#npc.speech.combat.spot.3",
		"#npc.speech.combat.spot.4",
		"#npc.speech.combat.spot.5",
		"#npc.speech.combat.spot.6",
		"#npc.speech.combat.spot.7",
	};

	private static readonly string[] TauntLines =
	{
		"#npc.speech.combat.taunt.1",
		"#npc.speech.combat.taunt.2",
		"#npc.speech.combat.taunt.3",
		"#npc.speech.combat.taunt.4",
		"#npc.speech.combat.taunt.5",
		"#npc.speech.combat.taunt.6",
		"#npc.speech.combat.taunt.7",
		"#npc.speech.combat.taunt.8",
	};

	/// <summary>
	/// The player to engage.
	/// </summary>
	public GameObject Target { get; set; }

	/// <summary>
	/// Weapon to fire. Should be a child component on the NPC's GameObject. Its NPC usage decides
	/// the engagement range, burst length and the rest between bursts.
	/// </summary>
	public BaseSandboxWeapon Weapon { get; set; }

	/// <summary>
	/// Speed the NPC moves when engaging.
	/// </summary>
	public float EngageSpeed { get; set; } = 180f;

	/// <summary>
	/// Speed the NPC strafes at when it repositions without sprinting.
	/// </summary>
	public float StrafeSpeed { get; set; } = 85f;

	/// <summary>
	/// Chance to reposition after a burst. Otherwise the NPC holds its ground and fires again.
	/// </summary>
	public float RepositionChance { get; set; } = 0.35f;

	/// <summary>
	/// Seconds to react before the first burst - set for a fresh contact, zero while a fight is
	/// already going. The NPC calls the contact out, turns to face it and brings its weapon up in
	/// this time, and won't fire before it's up however quickly those finish.
	/// </summary>
	public float ReactionTime { get; set; }

	private TimeUntil _reacted;

	/// <summary>
	/// How long the target can drop out of sight (ducking behind something, a glimpse through a
	/// gap) before we give up on this engagement and go searching instead.
	/// </summary>
	public float LostSightGrace { get; set; } = 0.75f;

	private TimeSince _timeSinceVisible;

	/// <summary>
	/// Radius around the current position to pick a flanking point.
	/// </summary>
	public float FlankRadius { get; set; } = 250f;

	public override int Priority => SchedulePriority.Combat;

	// Stay focused while fighting -- we end via ShouldCancel (lost sight) or when the
	// task sequence finishes, not because of incidental stimuli.
	public override NpcAwareness InterruptedBy => NpcAwareness.None;

	protected override void OnStart()
	{
		if ( !Target.IsValid() || !Weapon.IsValid() )
			return;

		// Set look target now so the NPC tracks the player through all tasks,
		// movement, firing, waiting, and repositioning.
		Npc.Animation.SetLookTarget( Target );

		_timeSinceVisible = 0;

		// A fresh contact gets called out (if the speech system lets us), then we hold fire until
		// the reaction time is up - counted from now, so the callout and the turn eat into it
		var freshContact = ReactionTime > 0f;
		_reacted = ReactionTime;

		if ( freshContact && Npc.Speech.CanSpeak )
			AddTask( new Say( Game.Random.FromArray( SpotLines ), 1.5f, Target )
			{
				Tags = ["combat", "spot"],
				Priority = 2000
			} );

		AddTask( new LookAt( Target ) );

		if ( freshContact )
			AddTask( new AwaitReaction( this ) );

		// Advance to comfortably inside the weapon's reach, not right on the edge of it.
		AddTask( new MoveTo( Target, Weapon.Npc.MaxRange * 0.8f ) { Speed = EngageSpeed, FaceTarget = Target } );
		AddTask( new FireWeapon( Weapon, Target ) );

		// Rest between bursts, as the weapon dictates - with a random combat taunt sometimes
		var rest = Game.Random.Float( Weapon.Npc.RestMin, Weapon.Npc.RestMax );
		if ( Npc.Speech.CanSpeak && Game.Random.Float() < 0.4f )
			AddTask( new Say( Game.Random.FromArray( TauntLines ), rest, Target )
			{
				Tags = ["combat", "taunt"]
			} );
		else
			AddTask( new Wait( rest ) );

		// Now and then, strafe to a new spot - most of the time hold position and keep shooting
		if ( Game.Random.Float() < RepositionChance )
			AddTask( new MoveTo( GetFlankPosition(), 20f ) { Speed = StrafeSpeed, FaceTarget = Target } );
	}

	protected override void OnEnd()
	{
		Npc.Animation.ClearLookTarget();
	}

	protected override bool ShouldCancel()
	{
		if ( !Target.IsValid() || !Weapon.IsValid() )
			return true;

		if ( Npc.Senses.VisibleTargets.Contains( Target ) )
			_timeSinceVisible = 0;

		return _timeSinceVisible > LostSightGrace;
	}

	/// <summary>
	/// Holds until the schedule's reaction time is up, however long the tasks before it took.
	/// </summary>
	private sealed class AwaitReaction : TaskBase
	{
		private readonly CombatEngageSchedule _engage;

		public AwaitReaction( CombatEngageSchedule engage ) => _engage = engage;

		protected override TaskStatus OnUpdate() => _engage._reacted ? TaskStatus.Success : TaskStatus.Running;
	}

	/// <summary>
	/// Pick a random position perpendicular to the NPC→target axis at <see cref="FlankRadius"/>.
	/// Snaps to navmesh if possible.
	/// </summary>
	private Vector3 GetFlankPosition()
	{
		Vector3 toTarget = Target.IsValid()
			? (Target.WorldPosition - Npc.WorldPosition).WithZ( 0 ).Normal
			: Npc.WorldRotation.Forward;

		// Perpendicular + slight forward bias, randomized left/right
		var perp = new Vector3( -toTarget.y, toTarget.x, 0 );
		var side = Game.Random.Float() > 0.5f ? 1f : -1f;
		var flankDir = (perp * side + toTarget * 0.3f).WithZ( 0 ).Normal;
		var candidate = Npc.WorldPosition + flankDir * Game.Random.Float( FlankRadius * 0.5f, FlankRadius );

		if ( Npc.Scene.NavMesh.GetClosestPoint( candidate ) is { } nav )
			return nav;

		return candidate;
	}
}

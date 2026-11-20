using Sandbox.Npcs.Layers;
using Sandbox.Npcs.Schedules;

namespace Sandbox.Npcs.CombatNpc;

/// <summary>
/// A combat NPC that searches for players, advances on them, fires in bursts, and repositions.
/// When friendly, can be recruited to follow a player and engages hostile NPCs instead.
/// </summary>
public class CombatNpc : Npc, Component.IPressable
{
	private static readonly string[] PainLines =
	{
		"#npc.speech.combat.pain.1",
		"#npc.speech.combat.pain.2",
		"#npc.speech.combat.pain.3",
		"#npc.speech.combat.pain.4",
		"#npc.speech.combat.pain.5",
	};

	private static readonly string[] DeathLines =
	{
		"#npc.speech.combat.death.1",
		"#npc.speech.combat.death.2",
		"#npc.speech.combat.death.3",
	};

	/// <summary>
	/// When true, this NPC is friendly to players and will follow them, engaging hostile NPCs.
	/// When false, this NPC targets players and friendly NPCs.
	/// </summary>
	[Property, Title( "#entity.property.npc_friendly" ), Description( "#entity.description.npc_friendly" ), ClientEditable, Sync]
	public bool Friendly { get; set; } = false;

	/// <summary>
	/// The weapon this NPC uses to attack. Its NPC usage decides the engagement range and burst
	/// cadence - the same soldier fights differently with a shotgun than an SMG.
	/// </summary>
	[Property]
	public BaseSandboxWeapon Weapon { get; set; }

	/// <summary>
	/// Weapon prefabs to arm this NPC with - one is picked at random when it spawns, replacing
	/// <see cref="Weapon"/>. Empty keeps the prefab's weapon.
	/// </summary>
	[Property]
	public List<GameObject> WeaponChoices { get; set; } = [];

	/// <summary>
	/// Skill multiplier on the weapon's NPC spread - 1 is a average shot, lower is more accurate.
	/// </summary>
	[Property, Group( "Balance" )]
	public float AimSpreadScale { get; set; } = 1f;

	/// <summary>
	/// Movement speed while engaging an enemy.
	/// </summary>
	[Property, Title( "#entity.property.npc_engagespeed" ), Description( "#entity.description.npc_engagespeed" ), Group( "#entity.group.balance" ), Range( 90, 250f ), Step( 1 ), ClientEditable, Sync]
	public float EngageSpeed { get; set; } = 180f;

	/// <summary>
	/// Walking pace while patrolling.
	/// </summary>
	[Property, Group( "Balance" )]
	public float PatrolSpeed { get; set; } = 85f;

	/// <summary>
	/// Cautious walking pace - sweeping for a hostile we've lost, checking out a noise, strafing to a
	/// new position. Kept to a walk; only fighting and hot pursuit break into a jog.
	/// </summary>
	[Property, Group( "Balance" )]
	public float SearchSpeed { get; set; } = 85f;

	/// <summary>
	/// Each soldier moves up to this much faster or slower than the rest (0.05 = ±5%), so a squad
	/// doesn't move in lockstep. Keep it small - too much pushes a walk into a jog.
	/// </summary>
	[Property, Group( "Balance" )]
	public float SpeedVariation { get; set; } = 0.05f;

	// This soldier's own pace, from SpeedVariation
	private float _speedScale = 1f;

	private float Pace( float speed ) => speed * _speedScale;

	/// <summary>
	/// How long after the last intel on a hostile (seen, heard or called out) to keep searching for it.
	/// </summary>
	[Property, Group( "Balance" )]
	public float SearchTimeout { get; set; } = 8f;

	/// <summary>
	/// Allies of the same faction within this range are told where a spotted hostile is.
	/// </summary>
	[Property, Group( "Balance" )]
	public float CalloutRange { get; set; } = 1500f;

	/// <summary>
	/// Wander between random points when there's nothing going on, instead of holding a post.
	/// </summary>
	[Property, Group( "Balance" )]
	public bool Patrols { get; set; }

	[Property, Group( "Balance" )]
	public float PatrolRadius { get; set; } = 400f;

	/// <summary>
	/// How long the weapon stays up after the last sign of trouble before the NPC relaxes.
	/// </summary>
	[Property, Group( "Balance" )]
	public float RelaxDelay { get; set; } = 5f;

	/// <summary>
	/// Seconds (min, max) to react to a fresh contact when caught at ease - weapon lowered.
	/// </summary>
	[Property, Group( "Balance" )]
	public Vector2 ReactionTimeRelaxed { get; set; } = new( 1.0f, 1.6f );

	/// <summary>
	/// Seconds (min, max) to react to a fresh contact when already alert - weapon up, searching or fighting.
	/// </summary>
	[Property, Group( "Balance" )]
	public Vector2 ReactionTimeAlert { get; set; } = new( 0.5f, 0.9f );

	/// <summary>
	/// Seconds a weapon dropped on death lies around before it's removed, if nobody picks it up.
	/// </summary>
	[Property, Group( "Balance" )]
	public float DroppedWeaponLifetime { get; set; } = 20f;

	/// <summary>
	/// How far a friendly NPC will follow a player before stopping.
	/// </summary>
	[Property, Group( "Balance" )]
	public float FollowDistance { get; set; } = 150f;

	/// <summary>
	/// The player this friendly NPC is following, if any.
	/// </summary>
	[Sync]
	public GameObject Leader { get; set; }

	/// <summary>
	/// Where we last saw, heard or were told a hostile was. Null when we've got nothing to go on.
	/// </summary>
	public Vector3? LastKnownPosition { get; private set; }

	/// <summary>
	/// Which way that hostile was moving, so a search can head where it was going.
	/// </summary>
	public Vector3 LastKnownVelocity { get; private set; }

	/// <summary>
	/// Time since <see cref="LastKnownPosition"/> was last updated.
	/// </summary>
	public TimeSince TimeSinceIntel { get; private set; }

	private TimeSince _timeSinceCallout;

	// The post we hold when nothing's going on - where and which way we were spawned
	private Vector3 _post;
	private Vector3 _postForward;

	private TimeSince _timeSinceAlert;

	// Contact tracking: when we last saw a hostile, and when the current stretch of contact began
	private TimeSince _timeSinceSawHostile;
	private TimeSince _timeSinceContact;
	private float _contactReaction;

	// The last hostile we saw and where, so we notice when it's dead - and where its body is
	private GameObject _lastSeenHostile;
	private Vector3 _lastSeenPosition;
	private Vector3? _bodyPosition;
	private TimeSince _timeSinceKill;

	IPressable.Tooltip? IPressable.GetTooltip( IPressable.Event e )
	{
		if ( !Friendly )
			return null;

		return Leader.IsValid()
			? new IPressable.Tooltip( "#npc.action.stop_following", "person_off", null )
			: new IPressable.Tooltip( "#npc.action.follow", "follow_the_signs", null );
	}

	bool IPressable.CanPress( IPressable.Event e ) => Friendly;

	bool IPressable.Press( IPressable.Event e )
	{
		ToggleFollow( e.Source.GameObject );
		return true;
	}

	[Rpc.Host]
	private void ToggleFollow( GameObject presserObject )
	{
		if ( !Friendly || !presserObject.IsValid() )
			return;

		var player = presserObject.Root.GetComponent<Player>();
		if ( !player.IsValid() )
			return;

		Leader = Leader == player.GameObject ? null : player.GameObject;
		EndCurrentSchedule();
	}

	protected override void OnValidate()
	{
		base.OnValidate();

		// Runs when the NPC is cloned in, before it's network spawned - so the chosen gun goes out in
		// its spawn snapshot and every client sees the same one. Not in the editor or the cached
		// prefab itself, not on clients.
		if ( Game.IsPlaying && Scene.IsValid() && !Scene.IsEditor && Scene is not PrefabScene && Networking.IsHost && !GameObject.Network.Active )
			ArmWithRandomWeapon();
	}

	/// <summary>
	/// Swap the prefab's weapon for a random one from <see cref="WeaponChoices"/>.
	/// </summary>
	private void ArmWithRandomWeapon()
	{
		if ( WeaponChoices is not { Count: > 0 } )
			return;

		ArmWith( WeaponChoices[Game.Random.Int( 0, WeaponChoices.Count - 1 )] );
	}

	/// <summary>
	/// The weapons players can pick for the NPCs they spawn
	/// </summary>
	public static IEnumerable<ScriptedEntity> PickableWeapons => ResourceLibrary.GetAll<ScriptedEntity>().Where( IsPickableWeapon );

	/// <summary>
	/// Installed weapons NPCs can fight with: melee and utility weapons have no NPC use set up
	/// </summary>
	static bool IsPickableWeapon( ScriptedEntity entity )
	{
		if ( entity.IsRemote || entity.Prefab is null || (entity.Developer && !ServerSettings.ShowDeveloperEntities) )
			return false;

		if ( !SpawnMenuCategory.Matches( entity.Category, "Weapon" ) || SpawnMenuCategory.Matches( entity.Category, "Weapon/Melee" ) || SpawnMenuCategory.Matches( entity.Category, "Weapon/Utility" ) )
			return false;

		return SceneUtility.GetPrefabScene( entity.Prefab )?.GetComponent<BaseSandboxWeapon>( true ) is { UsableByNpcs: true };
	}

	/// <summary>
	/// Arm with the weapon a player picked instead of a random one from <see cref="WeaponChoices"/>, NPCs with
	/// a fixed weapon keep it. See <see cref="Player.NpcWeapon"/>. Host only, before the NPC is network spawned.
	/// </summary>
	public void ArmWithPicked( Player player )
	{
		var path = player?.NpcWeapon;
		if ( string.IsNullOrEmpty( path ) || WeaponChoices is not { Count: > 0 } )
			return;

		var weapon = ResourceLibrary.GetAll<ScriptedEntity>().FirstOrDefault( entity => entity.Prefab?.ResourcePath == path && IsPickableWeapon( entity ) );
		if ( weapon is not null )
			ArmWith( SceneUtility.GetPrefabScene( weapon.Prefab ) );
	}

	/// <summary>
	/// Swap the current weapon for one made from a weapon prefab. Host only, before the NPC is network spawned.
	/// </summary>
	private void ArmWith( GameObject prefab )
	{
		if ( !prefab.IsValid() )
			return;

		var go = prefab.Clone( new CloneConfig { Parent = GameObject, Transform = global::Transform.Zero, StartEnabled = true } );
		var weapon = go?.GetComponent<BaseSandboxWeapon>( true );
		if ( !weapon.IsValid() )
		{
			go?.Destroy();
			Log.Warning( $"{DisplayName}: '{prefab.Name}' isn't a weapon prefab, keeping the default weapon." );
			return;
		}

		Weapon?.GameObject.DestroyImmediate();
		Weapon = weapon;
	}

	protected override void OnStart()
	{
		base.OnStart();

		_speedScale = 1f + Game.Random.Float( -SpeedVariation, SpeedVariation );

		_post = WorldPosition;
		_postForward = WorldRotation.Forward;

		if ( Weapon.IsValid() && Renderer.IsValid() )
		{
			Weapon.CreateWorldModel( Renderer );

			// The weapon says how forgiving it is in NPC hands; we say how good we are with it.
			Weapon.SpreadScale = Weapon.Npc.SpreadScale * AimSpreadScale;

			if ( !IsProxy )
				Animation.SetHoldType( Weapon.HoldType );
		}
	}

	// Friendly soldiers fight alongside the player; the rest are enemies.
	public override string Faction => Friendly ? Factions.Ally : Factions.Enemy;

	protected override void SetupRelationships()
	{
		if ( Friendly )
		{
			Likes( Factions.Player );
			Hates( Factions.Enemy, Factions.Monster );
		}
		else
		{
			Hates( Factions.Player, Factions.Ally, Factions.Citizen );
		}
	}

	/// <summary>
	/// Remember where a hostile is (and which way it's heading).
	/// </summary>
	public void RememberHostile( Vector3 position, Vector3 velocity )
	{
		LastKnownPosition = position;
		LastKnownVelocity = velocity;
		TimeSinceIntel = 0;
	}

	/// <summary>
	/// Keep the last known position fresh from everything we sense, every think -- not just when a
	/// fight starts -- and tell nearby allies about anything we can see.
	/// </summary>
	protected override NpcAwareness GatherAwareness()
	{
		var awareness = base.GatherAwareness();

		TrackKill();

		var seen = Senses.GetBestTarget();
		if ( seen.IsValid() )
		{
			// First sight after a gap is a fresh contact - work out how long it takes us to react
			if ( _timeSinceSawHostile > 3f )
			{
				_timeSinceContact = 0;
				_contactReaction = GetReactionTime( seen );
			}

			_timeSinceSawHostile = 0;
			_lastSeenHostile = seen;
			_lastSeenPosition = seen.WorldPosition;
			RememberHostile( seen.WorldPosition, SensesLayer.GetVelocity( seen ) );
			CallOut( seen );
			return awareness;
		}

		if ( Senses.GetHeardHostile() is { } heard && heard.IsValid() )
		{
			RememberHostile( heard.WorldPosition, SensesLayer.GetVelocity( heard ) );
		}
		else if ( Senses.Disturbance is { } noise && noise.Source.IsValid() && GetDisposition( noise.Source ) == Disposition.Hostile )
		{
			// Heard a hostile shoot -- we know roughly where they are, not where they're going
			if ( TimeSinceIntel > 1f )
				RememberHostile( noise.Position, Vector3.Zero );
		}

		// Fresh intel (heard, or an ally's callout) gets an idle NPC moving
		if ( LastKnownPosition.HasValue && TimeSinceIntel < 0.5f )
			awareness |= NpcAwareness.HostileIntel;

		return awareness;
	}

	/// <summary>
	/// The last hostile we saw is gone - dead. If we were in the fight, remember where the body is so
	/// we can walk over and check, and stop hunting it.
	/// </summary>
	private void TrackKill()
	{
		if ( _lastSeenHostile is null || _lastSeenHostile.IsValid() )
			return;

		_lastSeenHostile = null;

		if ( _timeSinceSawHostile > 10f )
			return;

		_bodyPosition = _lastSeenPosition;
		_timeSinceKill = 0;

		// Intel that still points at it is stale - it's not going anywhere
		if ( LastKnownPosition is { } lastKnown && lastKnown.Distance( _lastSeenPosition ) < 200f )
			LastKnownPosition = null;
	}

	/// <summary>
	/// How long it takes to react to spotting a hostile: longer when caught at ease (the weapon has
	/// to come up) than when already alert, plus a little more to pick out something far away.
	/// </summary>
	private float GetReactionTime( GameObject hostile )
	{
		var range = Animation.WeaponLowered ? ReactionTimeRelaxed : ReactionTimeAlert;
		var distance = WorldPosition.Distance( hostile.WorldPosition );

		return Game.Random.Float( range.x, range.y ) + MathF.Min( 0.6f, distance / 2500f );
	}

	/// <summary>
	/// Tell allies of our faction nearby where a hostile is. They'll come and help search, or join
	/// the fight once they see it themselves.
	/// </summary>
	private void CallOut( GameObject hostile )
	{
		if ( _timeSinceCallout < 1f ) return;
		_timeSinceCallout = 0;

		var velocity = SensesLayer.GetVelocity( hostile );

		foreach ( var ally in Scene.GetAll<CombatNpc>() )
		{
			if ( ally == this || ally.Faction != Faction ) continue;
			if ( ally.WorldPosition.Distance( WorldPosition ) > CalloutRange ) continue;

			// They can see something themselves -- don't distract them
			if ( ally.Senses.IsValid() && ally.Senses.VisibleTargets.Count > 0 ) continue;

			ally.RememberHostile( hostile.WorldPosition, velocity );
		}
	}

	public override ScheduleBase GetSchedule()
	{
		var visible = Senses.GetBestTarget();

		if ( visible.IsValid() && Weapon.IsValid() )
		{
			var engage = GetSchedule<CombatEngageSchedule>();
			engage.Target = visible;
			engage.Weapon = Weapon;
			engage.EngageSpeed = Pace( EngageSpeed );

			// Whatever's left of the reaction to a fresh contact - nothing once a fight's under way
			engage.ReactionTime = MathF.Max( 0f, _contactReaction - _timeSinceContact );
			engage.StrafeSpeed = Pace( SearchSpeed );
			return engage;
		}

		// Got them - walk over and check the body. No hurry, the fight's over.
		if ( _bodyPosition is { } body && _timeSinceKill < 20f && WorldPosition.Distance( body ) > 100f )
		{
			var check = GetSchedule<InvestigateSchedule>();
			check.Target = body;
			check.Speed = Pace( PatrolSpeed );
			return check;
		}

		// Lost them, heard them, or been told where they are -- go hunt. Jog after someone we've only
		// just lost sight of; otherwise it's a careful walk.
		if ( LastKnownPosition.HasValue && TimeSinceIntel < SearchTimeout && Weapon.IsValid() )
		{
			var search = GetSchedule<CombatSearchSchedule>();
			var hotPursuit = _lastSeenHostile.IsValid() && _timeSinceSawHostile < 5f;
			search.Speed = hotPursuit ? Pace( EngageSpeed ) : Pace( SearchSpeed );
			search.SweepSpeed = Pace( SearchSpeed );
			return search;
		}

		// Heard gunfire or some other disturbance but can't see the source -- go check it out.
		if ( Senses.Disturbance is { } disturbance )
		{
			var investigate = GetSchedule<InvestigateSchedule>();
			investigate.Target = disturbance.Position;
			investigate.Speed = Pace( SearchSpeed );
			return investigate;
		}

		// Combat and investigation above take priority; once clear, resume following our leader.
		if ( Friendly && Leader.IsValid() )
		{
			var follow = GetSchedule<FollowSchedule>();
			follow.Target = Leader;
			follow.FollowDistance = FollowDistance;
			return follow;
		}

		// Nothing going on - patrol if we're a patroller, otherwise hold our post
		if ( Patrols )
		{
			var patrol = GetSchedule<CombatPatrolSchedule>();
			patrol.PatrolRadius = PatrolRadius;
			patrol.Speed = Pace( PatrolSpeed );
			return patrol;
		}

		var idle = GetSchedule<CombatIdleSchedule>();
		idle.Post = _post;
		idle.PostForward = _postForward;
		idle.WalkSpeed = Pace( PatrolSpeed );
		return idle;
	}

	protected override void OnPutDown()
	{
		base.OnPutDown();

		// Someone put us here and turned us this way - this is our post now
		_post = WorldPosition;
		_postForward = WorldRotation.Forward;
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();

		if ( IsProxy )
			return;

		// Weapon up while anything's going on (searching, fighting) and for a while after - at ease otherwise
		if ( ActiveSchedule is { Priority: >= SchedulePriority.Alert } )
			_timeSinceAlert = 0;

		Animation.WeaponLowered = _timeSinceAlert > RelaxDelay;
	}

	protected override void OnHurt( in DamageInfo damage )
	{
		var attacker = GetAttacker( damage );
		if ( attacker.IsValid() )
		{
			// Turn on whoever hurt us, even a former ally, and prioritise them as a target.
			// This is the runtime "turn nasty" path the crime/aggro systems reuse.
			SetDisposition( attacker, Disposition.Hostile, priority: 10 );

			// You can tell which way the shot came from -- go after whoever fired it.
			RememberHostile( attacker.WorldPosition, SensesLayer.GetVelocity( attacker ) );
		}

		if ( Health >= 1f && Speech.CanSpeak && Game.Random.Float() < 0.5f )
			Speech.TrySay( Game.Random.FromArray( PainLines ), 1.5f, tags: ["reaction", "pain"], priority: 2000 );

		// React immediately.
		EndCurrentSchedule();
	}

	protected override void Die( in DamageInfo damage )
	{
		if ( Speech.CanSpeak )
			Speech.TrySay( Game.Random.FromArray( DeathLines ), 2f, tags: ["reaction", "death"], priority: 3000 );

		DropWeapon();

		base.Die( damage );
	}

	/// <summary>
	/// Drop the held weapon into the world where the hand was, so it survives the NPC's death and can
	/// be picked up. If nobody takes it within <see cref="DroppedWeaponLifetime"/> it's cleaned up.
	/// </summary>
	private void DropWeapon()
	{
		if ( !Weapon.IsValid() )
			return;

		var position = Weapon.WorldModel.IsValid()
			? Weapon.WorldModel.WorldPosition
			: WorldPosition + Vector3.Up * 32f;

		var weapon = Weapon;
		weapon.DropIntoWorld( position, Vector3.Up * 50f );

		// The timer lives on the weapon - this NPC is about to be destroyed
		weapon.Invoke( DroppedWeaponLifetime, () => RemoveIfUnclaimed( weapon ) );
	}

	/// <summary>
	/// Remove a gun we dropped, unless a player has picked it up since. A player who picked it up and
	/// dropped it again owns it now, so that drop is theirs to keep.
	/// </summary>
	private static void RemoveIfUnclaimed( BaseSandboxWeapon weapon )
	{
		if ( !weapon.IsValid() || weapon.HasOwner || !weapon.IsDropped )
			return;

		if ( weapon.GetComponent<Ownable>()?.Owner is not null )
			return;

		weapon.GameObject.Destroy();
	}
}

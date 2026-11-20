using Sandbox.Npcs.Layers;
using Sandbox.Npcs.Schedules;

namespace Sandbox.Npcs;

/// <summary>
/// A combat NPC that searches for whoever its faction is hostile to, advances on them, fires in bursts, and
/// repositions. It is an <see cref="Factions.Enemy"/> unless its <see cref="BaseNpc.Faction"/> says otherwise, and
/// follows its <see cref="Leader"/> between fights when it has one.
/// The senses and speech layers are optional: without senses it never notices anyone, without speech it stays silent.
/// </summary>
public class BaseCombatNpc : BaseNpc, Component.INetworkSpawn
{
	/// <summary>
	/// What a <see cref="BaseCombatNpc"/> does while it has no target.
	/// </summary>
	[Expose]
	public enum IdleBehaviours
	{
		/// <summary>Stands where it was placed, facing the way it was placed, and comes back there.</summary>
		HoldPost,

		/// <summary>Wanders between random points around where it was placed.</summary>
		Patrol,
	}

	public BaseCombatNpc()
	{
		Faction = Factions.Enemy;
	}

	/// <summary>
	/// Said now and then when hurt, one at random. Empty stays silent.
	/// </summary>
	[Property, Group( "Speech" )]
	public List<string> PainLines { get; set; } = [];

	/// <summary>
	/// Said on spotting a fresh contact, one at random. Empty stays silent.
	/// </summary>
	[Property, Group( "Speech" )]
	public List<string> SpotLines { get; set; } = [];

	/// <summary>
	/// Said now and then between bursts, one at random. Empty stays silent.
	/// </summary>
	[Property, Group( "Speech" )]
	public List<string> TauntLines { get; set; } = [];

	/// <summary>
	/// Said now and then while patrolling, one at random. Empty stays silent.
	/// </summary>
	[Property, Group( "Speech" )]
	public List<string> PatrolLines { get; set; } = [];

	private SensesLayer SensesOrNull => Senses.IsValid() ? Senses : null;

	private bool CanSay => Speech.IsValid() && Speech.CanSpeak;

	/// <summary>
	/// The weapon this NPC uses to attack: the one it starts with, then whichever is out of its
	/// <see cref="Inventory"/>. Its NPC usage decides the engagement range and burst cadence - the same
	/// soldier fights differently with a shotgun than an SMG.
	/// </summary>
	[Property]
	public BaseCombatWeapon Weapon { get; set; }

	/// <summary>
	/// Weapon prefabs to arm this NPC with - one is picked at random when it spawns, replacing
	/// <see cref="Weapon"/>. Empty keeps the prefab's weapon.
	/// </summary>
	[Property]
	public List<GameObject> WeaponChoices { get; set; } = [];

	/// <summary>
	/// The inventory this NPC holds its weapons in: the one on its GameObject, or a <see cref="BaseNpcInventory"/>
	/// the host adds when it has a weapon and no inventory. Null until then.
	/// </summary>
	public BaseInventoryComponent Inventory => _inventory.IsValid() ? _inventory : _inventory = GetComponent<BaseInventoryComponent>( true );

	private BaseInventoryComponent _inventory;
	private BaseInventoryComponent _watchedInventory;

	// Set once the host has taken the starting weapon out. From then on arming goes through the inventory.
	private bool _armed;

	private bool _choseWeapon;

	/// <summary>
	/// Skill multiplier on the weapon's NPC spread - 1 is a average shot, lower is more accurate.
	/// </summary>
	[Property, Group( "Balance" )]
	public float AimSpreadScale { get; set; } = 1f;

	/// <summary>
	/// Each soldier moves up to this much faster or slower than the rest (0.05 = ±5%), so a squad
	/// doesn't move in lockstep. Keep it small - too much pushes a walk into a jog.
	/// </summary>
	[Property, Group( "Balance" )]
	public float SpeedVariation { get; set; } = 0.05f;

	private float _speedScale = 1f;

	// The mover's speeds at this soldier's pace: a run to fight and chase, a walk for everything else
	private float RunPace => (Movement.IsValid() ? Movement.RunSpeed : 0f) * _speedScale;
	private float WalkPace => (Movement.IsValid() ? Movement.WalkSpeed : 0f) * _speedScale;

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
	/// What to do when there's nothing going on: hold the post or patrol around it.
	/// </summary>
	[Property, Group( "Balance" )]
	public IdleBehaviours IdleBehaviour { get; set; } = IdleBehaviours.HoldPost;

	/// <summary>
	/// How far from its post the NPC wanders when it patrols.
	/// </summary>
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
	/// How close to its <see cref="Leader"/> the NPC follows before stopping.
	/// </summary>
	[Property, Group( "Balance" )]
	public float FollowDistance { get; set; } = 150f;

	/// <summary>
	/// Who this NPC follows when it is not fighting or investigating, if anyone. Set it on the host.
	/// </summary>
	[Sync( SyncFlags.FromHost )]
	public GameObject Leader
	{
		get;
		set
		{
			if ( field == value )
				return;

			field = value;

			if ( Networking.IsHost )
				EndCurrentSchedule();
		}
	}

	/// <summary>
	/// Where we last saw, heard or were told a hostile was. Null when we've got nothing to go on.
	/// </summary>
	internal Vector3? LastKnownPosition { get; private set; }

	/// <summary>
	/// Which way that hostile was moving, so a search can head where it was going.
	/// </summary>
	internal Vector3 LastKnownVelocity { get; private set; }

	/// <summary>
	/// Time since <see cref="LastKnownPosition"/> was last updated.
	/// </summary>
	private TimeSince TimeSinceIntel { get; set; }

	private TimeSince _timeSinceCallout;

	// The post we hold when nothing's going on - where and which way we were spawned
	private Vector3 _post;
	private bool _putDown;
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

	void INetworkSpawn.OnNetworkSpawn( Connection owner )
	{
		// Before the spawn goes out, so the chosen gun and the inventory that will hold it are in it and every
		// client has the same. An NPC that is only being described, in a prefab or in the editor, is never spawned.
		if ( Networking.IsHost )
			ChooseStartingWeapon();
	}

	/// <summary>
	/// Pick the starting weapon from <see cref="WeaponChoices"/> and add the inventory for it. Once, on the host.
	/// </summary>
	private void ChooseStartingWeapon()
	{
		if ( _choseWeapon )
			return;

		_choseWeapon = true;

		ArmWithRandomWeapon();

		if ( Weapon.IsValid() )
			EnsureInventory( tellClients: false );
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
	/// Swap the current weapon for one made from a weapon prefab. Host only. Before the NPC has started the new
	/// weapon goes out in its spawn snapshot; after that the <see cref="Inventory"/> picks it up and removes the old one.
	/// </summary>
	public void ArmWith( GameObject prefab )
	{
		if ( !prefab.IsValid() )
			return;

		if ( _armed )
		{
			Rearm( prefab );
			return;
		}

		var go = prefab.Clone( new CloneConfig { Parent = GameObject, Transform = global::Transform.Zero, StartEnabled = true } );
		var weapon = go?.GetComponent<BaseCombatWeapon>( true );
		if ( !weapon.IsValid() )
		{
			go?.Destroy();
			Log.Warning( $"{DisplayName}: '{prefab.Name}' isn't a weapon prefab, keeping the default weapon." );
			return;
		}

		Weapon?.GameObject.DestroyImmediate();
		Weapon = weapon;

		// A weapon it was given by hand is its starting weapon: nothing is picked over it
		_choseWeapon = true;
	}

	protected override void OnStart()
	{
		base.OnStart();

		_speedScale = 1f + Game.Random.Float( -SpeedVariation, SpeedVariation );

		_post = WorldPosition;
		_postForward = WorldRotation.Forward;

		if ( Networking.IsHost )
		{
			// Not network spawned, or spawned before this had its say: choose now and tell the clients
			if ( !_choseWeapon )
			{
				var weapon = Weapon;
				var inventory = Inventory;

				ChooseStartingWeapon();

				if ( GameObject.Network.Active && (Weapon != weapon || Inventory != inventory) )
					GameObject.Network.Refresh();
			}

			TakeWeapon();
			_armed = true;
		}

		WatchInventory();
	}

	protected override void OnRefresh()
	{
		base.OnRefresh();

		// The host may have added the inventory after we were spawned.
		WatchInventory();
	}

	protected override void OnDestroy()
	{
		if ( _watchedInventory.IsValid() )
			_watchedInventory.ActiveItemChanged -= OnActiveItemChanged;

		base.OnDestroy();
	}

	/// <summary>
	/// The NPC's inventory on the host, added when its prefab has none. Before the NPC is network spawned where
	/// that's possible, so the component goes out in the spawn snapshot; later, clients learn of it from a refresh.
	/// </summary>
	private BaseInventoryComponent EnsureInventory( bool tellClients = true )
	{
		var inventory = Inventory;

		if ( !inventory.IsValid() )
		{
			inventory = _inventory = GameObject.AddComponent<BaseNpcInventory>();

			if ( tellClients && GameObject.Network.Active )
				GameObject.Network.Refresh();
		}

		// The active item's control reads input, and nobody's input drives an NPC.
		inventory.ManualPumping = true;

		WatchInventory();
		return inventory;
	}

	/// <summary>
	/// Put <see cref="Weapon"/> in the inventory and take it out. The inventory seeds its magazines and deploys
	/// it, its world model included, on every client. Host only.
	/// </summary>
	private void TakeWeapon()
	{
		var weapon = Weapon;
		if ( !weapon.IsValid() )
			return;

		var inventory = EnsureInventory();

		// A weapon under us is already the inventory's by hierarchy, but it only has a slot once it was added.
		if ( (weapon.Slot < 0 || weapon.Inventory != inventory) && !inventory.Add( weapon ) )
		{
			Log.Warning( $"{DisplayName}: its inventory wouldn't take '{weapon.GameObject.Name}'." );
			return;
		}

		inventory.Switch( weapon );
	}

	/// <summary>
	/// Arm with another weapon once started: the inventory picks it up, network spawned like any item it picks
	/// up, and removes the old one. Host only.
	/// </summary>
	private void Rearm( GameObject prefab )
	{
		if ( !Networking.IsHost )
			return;

		var inventory = EnsureInventory();
		var old = Weapon;

		var item = inventory.Pickup( prefab );
		if ( item is not BaseCombatWeapon weapon )
		{
			if ( item.IsValid() )
				inventory.Remove( item );

			Log.Warning( $"{DisplayName}: couldn't be armed with '{prefab.Name}', keeping the current weapon." );
			return;
		}

		if ( old.IsValid() && old != weapon )
		{
			inventory.Remove( old );

			// The starting weapon is part of our own network object, so clients only see it go with a refresh.
			if ( GameObject.Network.Active )
				GameObject.Network.Refresh();
		}

		inventory.Switch( weapon );
	}

	/// <summary>
	/// Follow the inventory's active item on every client, the host included.
	/// </summary>
	private void WatchInventory()
	{
		var inventory = Inventory;
		if ( !inventory.IsValid() || inventory == _watchedInventory )
			return;

		if ( _watchedInventory.IsValid() )
			_watchedInventory.ActiveItemChanged -= OnActiveItemChanged;

		_watchedInventory = inventory;
		inventory.ActiveItemChanged += OnActiveItemChanged;

		// It may have deployed something before we started watching.
		OnActiveItemChanged( null, inventory.ActiveItem );
	}

	private void OnActiveItemChanged( BaseInventoryItem oldItem, BaseInventoryItem newItem )
	{
		// A weapon that leaves our hands shoots as it was made to again.
		if ( oldItem is BaseCombatWeapon old && old.IsValid() )
			old.SpreadScale = 1f;

		if ( newItem is not BaseCombatWeapon weapon || !weapon.IsValid() )
			return;

		Weapon = weapon;

		weapon.SpreadScale = weapon.Npc.SpreadScale * AimSpreadScale;
	}

	/// <summary>
	/// Remember where a hostile is (and which way it's heading).
	/// </summary>
	private void RememberHostile( Vector3 position, Vector3 velocity )
	{
		LastKnownPosition = position;
		LastKnownVelocity = velocity;
		TimeSinceIntel = 0;
	}

	/// <summary>
	/// Keep the last known position fresh from everything we sense, every think -- not just when a
	/// fight starts -- and tell nearby allies about anything we can see.
	/// </summary>
	internal override NpcAwareness GatherAwareness()
	{
		var awareness = base.GatherAwareness();

		TrackKill();

		var senses = SensesOrNull;
		var seen = senses?.FindBestTarget();
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
			RememberHostile( seen.WorldPosition, BaseNpc.GetVelocity( seen ) );
			CallOut( seen );
			return awareness;
		}

		if ( senses?.GetHeardHostile() is { } heard && heard.IsValid() )
		{
			RememberHostile( heard.WorldPosition, BaseNpc.GetVelocity( heard ) );
		}
		else if ( senses?.Disturbance is { } noise && noise.Source.IsValid() && GetDisposition( noise.Source ) == Disposition.Hostile )
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
		var range = Animation.WeaponStance == AnimationLayer.WeaponStances.Lowered ? ReactionTimeRelaxed : ReactionTimeAlert;
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

		var velocity = BaseNpc.GetVelocity( hostile );

		foreach ( var ally in Scene.Query<BaseCombatNpc>() )
		{
			if ( ally == this || ally.Faction != Faction ) continue;
			if ( ally.WorldPosition.Distance( WorldPosition ) > CalloutRange ) continue;

			// They can see something themselves -- don't distract them
			if ( ally.Senses.IsValid() && ally.Senses.VisibleHostiles.Count > 0 ) continue;

			ally.RememberHostile( hostile.WorldPosition, velocity );
		}
	}

	protected override BaseSchedule SelectSchedule()
	{
		var senses = SensesOrNull;
		var visible = senses?.FindBestTarget();

		if ( visible.IsValid() && Weapon.IsValid() && Weapon.IsActive && Weapon.Holder == this )
		{
			var engage = GetSchedule<CombatEngageSchedule>();
			engage.Target = visible;
			engage.Weapon = Weapon;
			engage.EngageSpeed = RunPace;

			// Whatever's left of the reaction to a fresh contact - nothing once a fight's under way
			engage.ReactionTime = MathF.Max( 0f, _contactReaction - _timeSinceContact );
			engage.StrafeSpeed = WalkPace;
			return engage;
		}

		// Got them - walk over and check the body. No hurry, the fight's over.
		if ( _bodyPosition is { } body && _timeSinceKill < 20f && WorldPosition.Distance( body ) > 100f )
		{
			var check = GetSchedule<InvestigateSchedule>();
			check.TargetPosition = body;
			check.Speed = WalkPace;
			return check;
		}

		// Lost them, heard them, or been told where they are -- go hunt. Jog after someone we've only
		// just lost sight of; otherwise it's a careful walk.
		if ( LastKnownPosition.HasValue && TimeSinceIntel < SearchTimeout && Weapon.IsValid() && Weapon.IsActive && Weapon.Holder == this )
		{
			var search = GetSchedule<CombatSearchSchedule>();
			var hotPursuit = _lastSeenHostile.IsValid() && _timeSinceSawHostile < 5f;
			search.Speed = hotPursuit ? RunPace : WalkPace;
			search.SweepSpeed = WalkPace;
			return search;
		}

		if ( senses?.Disturbance is { } disturbance )
		{
			var investigate = GetSchedule<InvestigateSchedule>();
			investigate.TargetPosition = disturbance.Position;
			investigate.Speed = WalkPace;
			return investigate;
		}

		if ( Leader.IsValid() )
		{
			var follow = GetSchedule<FollowSchedule>();
			follow.Target = Leader;
			follow.FollowDistance = FollowDistance;
			return follow;
		}

		if ( IdleBehaviour == IdleBehaviours.Patrol )
		{
			var patrol = GetSchedule<CombatPatrolSchedule>();
			patrol.PatrolRadius = PatrolRadius;
			patrol.Speed = WalkPace;
			return patrol;
		}

		var idle = GetSchedule<CombatIdleSchedule>();
		idle.Post = _post;
		idle.PostForward = _postForward;
		idle.WalkSpeed = WalkPace;
		return idle;
	}

	protected override void OnCarryStopped( GameObject carrier )
	{
		base.OnCarryStopped( carrier );
		_putDown = true;
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();

		if ( !Networking.IsHost )
			return;

		// Someone put us here and turned us this way - this is our post now
		if ( _putDown && IsOnGround )
		{
			_putDown = false;
			_post = WorldPosition;
			_postForward = WorldRotation.Forward;
		}

		// Weapon up while anything's going on (searching, fighting) and for a while after - at ease otherwise
		if ( ActiveSchedule is { Priority: >= SchedulePriorities.Alert } )
			_timeSinceAlert = 0;

		Animation.WeaponStance = _timeSinceAlert > RelaxDelay ? AnimationLayer.WeaponStances.Lowered : AnimationLayer.WeaponStances.Ready;
	}

	protected override void OnDamaged( IDamageable.DamageEvent damage )
	{
		var attacker = damage.Instigator;
		if ( attacker.IsValid() )
		{
			// Turn on whoever hurt us, even a former ally, and prioritise them as a target.
			// This is the runtime "turn nasty" path the crime/aggro systems reuse.
			SetRelationship( attacker, Disposition.Hostile, priority: 10 );

			// You can tell which way the shot came from -- go after whoever fired it.
			RememberHostile( attacker.WorldPosition, BaseNpc.GetVelocity( attacker ) );
		}

		if ( PainLines is { Count: > 0 } && Health >= 1f && CanSay && Game.Random.Float() < 0.5f )
			Speech.TrySay( Game.Random.FromList( PainLines ), new SpeechOptions { Duration = 1.5f, Tags = ["reaction", "pain"], Priority = 2000 } );

		EndCurrentSchedule();
	}

	protected override void OnDeath( IDamageable.DeathEvent death )
	{
		DropWeapon();

		base.OnDeath( death );
	}

	/// <summary>
	/// Drop the held weapon out of the inventory into the world where the hand was, so it survives the NPC's
	/// death and can be picked up.
	/// </summary>
	private void DropWeapon()
	{
		var weapon = Weapon;
		var inventory = Inventory;
		if ( !weapon.IsValid() || !inventory.IsValid() )
			return;

		var position = weapon.WorldModel.IsValid()
			? weapon.WorldModel.WorldPosition
			: WorldPosition + Vector3.Up * 32f;

		// The inventory holsters it, which takes the world model away, and the item places itself from where it is.
		weapon.WorldPosition = position;
		inventory.Drop( weapon );
	}
}

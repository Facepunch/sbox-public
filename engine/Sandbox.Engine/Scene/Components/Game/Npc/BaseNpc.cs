using Sandbox.Npcs.Layers;

namespace Sandbox.Npcs;

/// <summary>
/// A non-player character: health, senses, movement, speech and a schedule of tasks. Host-authoritative: the host
/// thinks, moves and takes damage for it, and clients follow the synced state.
/// </summary>
[Hide]
public partial class BaseNpc : Component, Component.IDamageable, BaseCombatWeapon.IHolder
{
	/// <summary>
	/// Draws this NPC's schedule, awareness and layer state as text above it.
	/// </summary>
	[Property]
	public bool ShowDebugOverlay { get; set; }

	/// <summary>
	/// The model the NPC is drawn with, which its animator drives and its ragdoll copies.
	/// </summary>
	[Property]
	public SkinnedModelRenderer Renderer { get; set; }

	/// <summary>
	/// The name games show for this NPC, such as when it is killed.
	/// </summary>
	[Property]
	public string DisplayName { get; set; } = "NPC";

	/// <summary>
	/// Current health, never negative. Below 1 starts death handling: assigning it on the host follows the same
	/// cancellable death path as damage, with no attacker. Not a number is ignored.
	/// </summary>
	[Property, Title( "Health" ), Description( "Current health. The NPC dies when this drops below 1." ), ClientEditable, Sync( SyncFlags.FromHost )]
	public float Health
	{
		get;
		set
		{
			if ( float.IsNaN( value ) )
				return;

			field = MathF.Max( value, 0f );

			if ( field < 1f && _started && _damageDepth == 0 && Networking.IsHost )
				Kill( null, null );
		}
	} = 100f;

	/// <summary>
	/// The most health this NPC has. Raised to <see cref="Health"/> when the NPC starts if it's lower.
	/// </summary>
	[Property, Sync( SyncFlags.FromHost )]
	public float MaxHealth { get; set; }

	/// <summary>
	/// How far above its origin this NPC sees from.
	/// </summary>
	[Property]
	public float EyeHeight { get; set; } = 64f;

	/// <summary>
	/// Where this NPC sees from, <see cref="EyeHeight"/> above its origin.
	/// </summary>
	public Vector3 EyePosition => WorldPosition + Vector3.Up * EyeHeight;

	/// <summary>
	/// Where a GameObject's eyes are: the camera for the local first person player, the "eyes"
	/// attachment if its model has one, an NPC's <see cref="EyePosition"/>, otherwise its bounds centre.
	/// </summary>
	public static Vector3 CalculateEyePosition( GameObject go )
	{
		if ( !go.IsValid() )
			return default;

		var controller = go.GetComponentInChildren<PlayerController>();
		if ( controller.IsValid() && !controller.IsProxy && !controller.ThirdPerson && go.Scene?.Camera.IsValid() == true )
			return go.Scene.Camera.WorldPosition;

		var renderer = go.GetComponentInChildren<SkinnedModelRenderer>();
		if ( renderer.IsValid() && renderer.GetAttachment( "eyes" ) is { } eyes )
			return eyes.Position;

		if ( go.GetComponent<BaseNpc>() is { } npc )
			return npc.EyePosition;

		return go.GetBounds().Center;
	}

	/// <summary>
	/// How fast a GameObject is moving: a player's controller velocity, an NPC's <see cref="AnimationLayer.MoveVelocity"/>
	/// while it's moving, otherwise its rigidbody's velocity.
	/// </summary>
	public static Vector3 GetVelocity( GameObject go )
	{
		if ( !go.IsValid() )
			return Vector3.Zero;

		if ( go.Root.GetComponent<PlayerController>() is { } controller )
			return controller.Velocity;

		// NPCs walk on the navmesh, their rigidbody sits still
		if ( go.GetComponent<BaseNpc>() is { } npc && npc.Animation.IsValid() && !npc.Animation.MoveVelocity.IsNearlyZero() )
			return npc.Animation.MoveVelocity;

		if ( go.GetComponent<Rigidbody>() is { } rb )
			return rb.Velocity;

		return Vector3.Zero;
	}

	/// <summary>
	/// What this NPC is shooting at, host side. While set, a held weapon fires from its muzzle at
	/// <see cref="CalculateAimPoint"/> instead of along the barrel, which follows the animation and drifts high.
	/// </summary>
	internal GameObject AimTarget { get; set; }

	Ray BaseCombatWeapon.IHolder.CalculateAimRay( BaseCombatWeapon weapon )
	{
		var muzzle = weapon.GetMuzzleTransform();

		if ( AimTarget.IsValid() )
			return new Ray( muzzle.Position, (CalculateAimPoint( AimTarget ) - muzzle.Position).Normal );

		return new Ray( muzzle.Position, muzzle.Rotation.Forward );
	}

	/// <summary>
	/// Where to aim at a target. Roughly chest height by default: most of the way from the feet up to
	/// the eyes, so it follows crouching.
	/// </summary>
	protected virtual Vector3 CalculateAimPoint( GameObject target )
	{
		return Vector3.Lerp( target.WorldPosition, CalculateEyePosition( target ), 0.7f );
	}

	private Rigidbody _rigidbody;
	private bool _started;
	private bool _thinking = true;

	protected override void OnStart()
	{
		GameObject.Tags.Add( "npc" );
		_rigidbody = GetComponent<Rigidbody>();
		Scene.GetSystem<NpcSystem>().Register( this );

		if ( Networking.IsHost )
		{
			MaxHealth = MathF.Max( MaxHealth, Health );

			foreach ( var navigation in GetComponents<NavigationLayer>( true ) )
				navigation.InitializeAgent();

			// One mover at a time: the rest wait their turn
			if ( _activeMover.IsValid() )
			{
				foreach ( var other in GetComponents<MovementLayer>() )
				{
					if ( other != _activeMover )
						other.Enabled = false;
				}
			}
		}

		_started = true;
	}

	protected override void OnDestroy()
	{
		Scene?.GetSystem<NpcSystem>()?.Unregister( this );
		base.OnDestroy();
	}

	protected override void OnUpdate()
	{
		// NPCs are the host's: it thinks, moves and takes damage for them, whoever was given the object
		if ( !Networking.IsHost )
			return;

		if ( IsProxy )
			Network.DropOwnership();

		if ( _dead )
			return;

		UpdateMovement();
		CategorizeGround();
		UpdateAirborne();

		var thinking = NpcConVars.Enabled;
		if ( thinking != _thinking )
		{
			_thinking = thinking;

			if ( !thinking )
				StandDown();
		}

		// No thinking in mid-air - the schedule picks up again once we land
		if ( thinking && !IsAirborne )
			Think();

		if ( ShowDebugOverlay )
		{
			DrawDebugString();
		}
	}

	/// <summary>
	/// Drop everything and stand idle: no schedule, no move, nothing to look or aim at.
	/// </summary>
	private void StandDown()
	{
		EndCurrentSchedule();
		Movement?.Stop();
		Animation.ClearLookTarget();
		AimTarget = null;
	}

	/// <summary>
	/// The NPC's brain, run on the host each update while it's on its feet and AI is enabled.
	/// Ticks the schedule by default; override to drive the NPC some other way.
	/// </summary>
	protected virtual void Think()
	{
		TickSchedule();
	}

	/// <summary>
	/// Spawns a networked ragdoll prop at the NPC's current position, copying the renderer and clothing,
	/// and returns it, or null if there's nothing to ragdoll. Host only. Override to leave something else behind.
	/// </summary>
	protected virtual GameObject CreateRagdoll()
	{
		if ( !Networking.IsHost )
			return null;

		if ( !Renderer.IsValid() || Renderer.Model is null )
			return null;

		var go = new GameObject( false, "Ragdoll" );
		go.Tags.Add( "ragdoll" );
		go.WorldTransform = WorldTransform;

		var prop = go.AddComponent<Prop>();
		prop.Model = Renderer.Model;
		prop.BodyGroups = Renderer.BodyGroups;
		prop.MaterialGroup = Renderer.MaterialGroup;
		prop.Tint = Renderer.Tint;

		// The prop builds its renderer and ModelPhysics when it's enabled, which the batch defers to its end
		using ( Scene.BatchGroup() )
		{
			go.Enabled = true;
		}

		var mainBody = go.GetComponent<SkinnedModelRenderer>();
		var physics = go.GetComponent<ModelPhysics>();

		if ( mainBody.IsValid() )
		{
			mainBody.CopyFrom( Renderer );
			mainBody.UseAnimGraph = false;

			foreach ( var clothing in Renderer.GameObject.Children.SelectMany( x => x.Components.GetAll<SkinnedModelRenderer>() ) )
			{
				if ( !clothing.IsValid() ) continue;

				var newClothing = new GameObject( true, clothing.GameObject.Name );
				newClothing.Parent = go;

				var item = newClothing.Components.Create<SkinnedModelRenderer>();
				item.CopyFrom( clothing );
				item.BoneMergeTarget = mainBody;
			}
		}

		if ( physics.IsValid() )
			physics.CopyBonesFrom( Renderer, true );

		go.NetworkSpawn( true, null );
		return go;
	}

	// Set when the death is committed, before anything is told about it. It never goes back.
	private bool _dead;
	private bool _dying;
	private bool _createRagdoll = true;

	// Above zero while a hit is being worked out, so setting Health in there doesn't start a death of its own.
	private int _damageDepth;

	// Destroyed, or queued to be. Any callback can do that to us.
	private bool IsGone => !IsValid || GameObject.IsDestroyed;

	private bool CanBeHurt => !IsGone && !_dead && !_dying;

	internal bool CanThink => Enabled && Active && CanBeHurt;

	// Raises one stage of a hit to the scene. A broadcast can't be cut short, so listeners that come after
	// one that killed or destroyed the NPC are skipped here: the hit is over for it.
	private void RaiseHit( Action<IDamageable.IEvents> call )
	{
		Scene?.RunEvent<IDamageable.IEvents>( x =>
		{
			if ( CanBeHurt )
				call( x );
		} );
	}

	/// <summary>
	/// Base damage handling, see <see cref="TakeDamage"/>. Subclasses react by overriding OnDamaged and OnDeath -- they don't touch this.
	/// </summary>
	void IDamageable.OnDamage( in DamageInfo damage )
	{
		// A rigidbody we're launched into deals "impact" damage from its side of the collision
		if ( _launched && damage.Tags.Contains( "impact" ) )
			return;

		TakeDamage( damage );
	}

	/// <summary>
	/// One hit, start to finish, on the host: the NPC shapes it in <see cref="OnDamaging"/>, listeners settle it in
	/// <see cref="Component.IDamageable.IEvents.OnDamaging"/>, health drops, <see cref="OnDamaged"/> and
	/// <see cref="Component.IDamageable.IEvents.OnDamaged"/> see the settled hit, and below 1 health the NPC dies. Any of
	/// them may hit the NPC again: that hit runs in full first, and this one carries on
	/// if the NPC is still alive. It stops for good once the NPC is dead or destroyed, and no living NPC
	/// is left below 1 health when the outermost hit is done.
	/// </summary>
	private void TakeDamage( DamageInfo damage )
	{
		if ( !Networking.IsHost || damage is null || !CanBeHurt )
			return;

		var e = new IDamageable.DamageEvent
		{
			Victim = GameObject,
			DamageInfo = damage,
			Instigator = damage.Attacker,
			AcceptedDamage = float.IsNaN( damage.Damage ) ? 0f : MathF.Max( damage.Damage, 0f )
		};

		bool applied;
		GameObject attacker;
		_damageDepth++;

		try
		{
			applied = ApplyHit( e, out attacker );
		}
		finally
		{
			_damageDepth--;
		}

		if ( !CanBeHurt || Health >= 1f )
			return;

		// Below 1 health and alive: this hit did it, or, with no hit left to finish, a Health assignment
		// made while one was worked out did. That death is owed whether the hit was cancelled or not.
		if ( applied )
			Kill( damage, attacker );
		else if ( _damageDepth == 0 )
			Kill( null, null );
	}

	// The stages of one hit. False when it was cancelled, or the NPC died or went away part way.
	private bool ApplyHit( IDamageable.DamageEvent e, out GameObject attacker )
	{
		attacker = e.Instigator;

		try
		{
			OnDamaging( e );
		}
		catch ( Exception exception )
		{
			Log.Error( exception, $"Exception in OnDamaging on {this}" );
		}

		if ( !e.Cancelled && CanBeHurt )
			RaiseHit( x => x.OnDamaging( e ) );

		// The hit is settled here. What anyone changes on it from now on is ignored.
		var amount = float.IsNaN( e.AcceptedDamage ) ? 0f : MathF.Max( e.AcceptedDamage, 0f );
		attacker = e.Instigator;
		e.AcceptedDamage = amount;

		if ( e.Cancelled || !CanBeHurt )
			return false;

		Health -= amount;

		try
		{
			OnDamaged( e );
		}
		catch ( Exception exception )
		{
			Log.Error( exception, $"Exception in OnDamaged on {this}" );
		}

		if ( !CanBeHurt )
			return false;

		RaiseHit( x => x.OnDamaged( e ) );

		return CanBeHurt;
	}

	// The corpse OnDeath made, for the listeners of the death
	private GameObject _ragdoll;

	/// <summary>
	/// Ask listeners before committing the death. Nested damage cannot restart this decision.
	/// </summary>
	private void Kill( DamageInfo damage, GameObject attacker )
	{
		if ( !CanBeHurt )
			return;

		damage ??= new DamageInfo();
		var dying = new IDamageable.DyingEvent { Victim = GameObject, DamageInfo = damage, Instigator = attacker };
		_dying = true;
		try
		{
			Scene?.RunEvent<IDamageable.IEvents>( x =>
			{
				if ( !IsGone ) x.OnDying( dying );
			} );
		}
		finally
		{
			_dying = false;
		}

		if ( IsGone ) return;
		if ( dying.Cancelled )
		{
			Health = MathF.Max( Health, 1f );
			return;
		}
		if ( Health >= 1f ) return;

		_dead = true;
		_createRagdoll = dying.CreateRagdoll;

		try
		{
			Speech?.Stop();
			OnDeath( new IDamageable.DeathEvent { Victim = GameObject, DamageInfo = damage, Instigator = attacker } );
		}
		catch ( Exception exception )
		{
			Log.Error( exception, $"Exception in OnDeath on {this}" );
		}

		var death = new IDamageable.DeathEvent { Victim = GameObject, DamageInfo = damage, Instigator = attacker, Ragdoll = _ragdoll };
		Scene?.RunEvent<IDamageable.IEvents>( x => x.OnDied( death ) );

		if ( IsValid )
			GameObject.Destroy();
	}

	/// <summary>
	/// Called on the host when the NPC is about to take a hit, before anyone else hears of it. Override to shape
	/// the NPC's own incoming damage: change <see cref="Component.IDamageable.DamageEvent.AcceptedDamage"/> or
	/// <see cref="Component.IDamageable.DamageEvent.Instigator"/>, or set <see cref="Component.IDamageable.DamageEvent.Cancelled"/>,
	/// which ends the hit here. A hit goes through, in order: this, <see cref="Component.IDamageable.IEvents.OnDamaging"/>,
	/// the health change, <see cref="OnDamaged"/>, <see cref="Component.IDamageable.IEvents.OnDamaged"/>.
	/// </summary>
	protected virtual void OnDamaging( IDamageable.DamageEvent e ) { }

	/// <summary>
	/// Called on the host when the NPC has taken a hit, before the death check. The hit is settled: its
	/// <see cref="Component.IDamageable.DamageEvent.Instigator"/> and <see cref="Component.IDamageable.DamageEvent.AcceptedDamage"/> are what
	/// counted, and changing them now does nothing. Override to react.
	/// </summary>
	protected virtual void OnDamaged( IDamageable.DamageEvent e ) { }

	/// <summary>
	/// Called on the host, once, when the NPC dies. Spawns a ragdoll with <see cref="CreateRagdoll"/>, which is
	/// the place to change or leave out the corpse: listeners get it in <see cref="Component.IDamageable.IEvents.OnDied"/>,
	/// which is raised after this. The NPC is destroyed once that has run, so listeners can still look at it.
	/// Override to add NPC-specific behaviour before/after death.
	/// </summary>
	protected virtual void OnDeath( IDamageable.DeathEvent e )
	{
		EmitStimulus( StimulusKinds.Death, radius: 1024f, lifetime: 2f );

		if ( _createRagdoll )
			_ragdoll = CreateRagdoll();
	}
}

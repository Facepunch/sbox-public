
using Sandbox.Movement;

/// <summary>
/// Holds player information like health
/// </summary>
public sealed partial class Player : Component, Component.IDamageable, PlayerController.IEvents, Global.ISaveEvents, IKillSource, IHudEvents, IDamageBlame
{
	Player IDamageBlame.Player => this;

	[RequireComponent]
	public PlayerController Controller { get; set; }

	[Property]
	public GameObject Body { get; internal set; }

	[Property, Range( 0, 100 ), Sync( SyncFlags.FromHost )]
	public float Health { get; set; } = 100;

	[Property, Range( 0, 100 ), Sync( SyncFlags.FromHost )]
	public float MaxHealth { get; set; } = 100;

	[Property, Range( 0, 100 ), Sync( SyncFlags.FromHost )]
	public float Armour { get; set; } = 0;

	[Property, Range( 0, 100 ), Sync( SyncFlags.FromHost )]
	public float MaxArmour { get; set; } = 100;

	[Sync( SyncFlags.FromHost )]
	public PlayerData PlayerData { get; internal set; }

	public Transform EyeTransform
	{
		get
		{
			if ( !Controller.IsValid() )
			{
				Log.Warning( $"Invalid Controller for {this.GameObject}" );
				return default;
			}
			return Controller.EyeTransform;
		}
	}

	public bool IsLocalPlayer => !IsProxy;

	string IKillSource.DisplayName => Network.Owner?.DisplayName ?? "Unknown";
	long IKillSource.SteamId => (long)(Network.Owner?.SteamId ?? default);
	void IKillSource.OnKill( GameObject victim )
	{
		PlayerData.Kills++;
		PlayerData.AddStat( victim?.GetComponent<Player>().IsValid() ?? false ? "kills" : "kills.npc" );
	}

	/// <summary>
	/// The HUD elements the player wants hidden right now: everything in freecam, otherwise whatever the held weapon hides
	/// </summary>
	public HudElement HiddenHud
	{
		get
		{
			var freeCam = Scene.Get<FreeCamGameObjectSystem>();
			if ( freeCam.IsActive )
				return HudElement.All;

			var weapon = GetComponent<PlayerInventory>()?.ActiveWeapon;
			var hidden = weapon.IsValid() && weapon.WantsHideHud ? weapon.HiddenHudElements : HudElement.None;

			if ( IsDriving || IsDriveHolding || CameraViewSystem.Get( Scene )?.ActiveCamera.IsValid() == true )
				hidden |= HudElement.Crosshair;

			return hidden;
		}
	}

	/// <summary>
	/// True if the player wants any of the HUD not to draw right now
	/// </summary>
	public bool WantsHideHud => HiddenHud != HudElement.None;

	/// <summary>
	/// What pressing use would do to the thing the player is looking at, or null if there's nothing to show.
	/// The HUD shows it below the crosshair
	/// </summary>
	public IPressable.Tooltip? InteractionHint
	{
		get
		{
			if ( !Controller.IsValid() || Controller.Tooltips.Count == 0 ) return null;

			var hint = Controller.Tooltips[0];
			if ( string.IsNullOrWhiteSpace( hint.Title ) && string.IsNullOrWhiteSpace( hint.Icon ) ) return null;

			return hint;
		}
	}

	/// <summary>
	/// Feeds <see cref="HiddenHud"/> into the HUD visibility event, so the camera weapon and freecam
	/// hide the HUD through the same path addons use. Panels ask through <see cref="Hud.IsVisible"/>.
	/// </summary>
	void IHudEvents.OnHudVisibility( ref HudElement hidden )
	{
		if ( IsLocalPlayer )
			hidden |= HiddenHud;
	}

	protected override void OnStart()
	{
		_timeSinceSpawned = 0;

		if ( IsLocalPlayer )
			LocalPlayer = this;

		if ( ServerSettings.KeepPlayerCorpses )
			return;

		var targets = Scene.GetAllComponents<DeathCameraTarget>()
			.Where( x => x.Connection == Network.Owner );

		// We don't care about spectating corpses once we spawn
		foreach ( var t in targets )
		{
			t.GameObject.Destroy();
		}
	}

	protected override void OnDestroy()
	{
		if ( LocalPlayer == this )
			LocalPlayer = null;
	}

	/// <summary>
	/// Whether this player is currently noclipping. Synced so proxies can animate correctly.
	/// </summary>
	[Sync]
	public bool IsNoclipping { get; internal set; }

	/// <summary>
	/// How far each action is pushed, 0 to 1, when it's on a gamepad's trigger or stick rather than a key. What the
	/// player drives from a seat runs on the host, which only gets whether actions are down, so they sync this with it.
	/// See <see cref="ClientInput.GetAnalog"/>.
	/// </summary>
	[Sync] public NetDictionary<string, float> ActionAmounts { get; set; } = new();

	/// <summary>
	/// The weapon prefab this player picked for the NPCs they spawn
	/// </summary>
	[Sync] public string NpcWeapon { get; private set; }

	/// <summary>Seconds of invulnerability after spawning, to stop spawn-killing. 0 disables it.</summary>
	[Title( "Spawn Protection" ), Group( "Gameplay" ), ServerSettings.Section( "Players" ), Order( 4 ), Range( 0, 30 ), Step( 1 )]
	[ConVar( "sandbox.spawnprotection", ConVarFlags.Replicated | ConVarFlags.Server | ConVarFlags.GameSetting | ConVarFlags.Saved, Help = "Seconds of invulnerability after spawning. 0 disables spawn protection." )]
	public static float SpawnProtection { get; set; } = 5.0f;

	/// <summary>
	/// Time since this player last spawned. Drives <see cref="IsSpawnProtected"/>.
	/// </summary>
	private RealTimeSince _timeSinceSpawned;

	/// <summary>
	/// True while the player is briefly invulnerable just after spawning, per the
	/// <c>sandbox.spawnprotection</c> convar.
	/// </summary>
	public bool IsSpawnProtected => _timeSinceSpawned < SpawnProtection;

	protected override void OnUpdate()
	{
		if ( Controller.IsValid() && Controller.Renderer.IsValid() )
		{
			var seated = GetComponentInParent<ISitTarget>( false ) is not null;
			Controller.Renderer.Set( "b_noclip", IsNoclipping && !seated );

			// Weapons write their pose while active; empty hands need an explicit reset.
			if ( GetComponent<PlayerInventory>()?.ActiveWeapon.IsValid() != true )
			{
				Controller.Renderer.Set( "holdtype", "none" );
				Controller.Renderer.Set( "holdtype_handedness", 0 );
			}
		}

		if ( IsLocalPlayer )
		{
			UpdateSeatedWeapons();
			UpdateSeatedBodyVisibility();
			DrawSeatedWeaponHud();
			UpdatePhysgunColors();
			NpcWeapon = GamePreferences.NpcWeapon;
		}
	}

	/// <summary>
	/// Try to inherit transforms from the player onto its new ragdoll
	/// </summary>
	/// <param name="ragdoll"></param>
	private void CopyBoneScalesToRagdoll( GameObject ragdoll )
	{
		// we are only interested in the bones of the player, not anything that may be attached to it.
		var playerRenderer = Body.GetComponent<SkinnedModelRenderer>();
		var bones = playerRenderer.Model.Bones;

		var ragdollRenderer = ragdoll.GetComponent<SkinnedModelRenderer>();
		ragdollRenderer.CreateBoneObjects = true;

		var ragdollObjects = ragdoll.GetAllObjects( true ).ToLookup( x => x.Name );

		foreach ( var bone in bones.AllBones )
		{
			var boneName = bone.Name;

			if ( !ragdollObjects.Contains( boneName ) )
				continue;

			var boneObject = playerRenderer.GetBoneObject( boneName );
			if ( !boneObject.IsValid() )
			{
				continue;
			}

			var boneOnRagdoll = ragdollObjects[boneName].FirstOrDefault();

			if ( boneOnRagdoll.IsValid() && boneObject.WorldScale != Vector3.One )
			{
				boneOnRagdoll.Flags = boneOnRagdoll.Flags.WithFlag( GameObjectFlags.ProceduralBone, true );
				boneOnRagdoll.WorldScale = boneObject.WorldScale;

				var z = boneOnRagdoll.Parent;
				z.Flags = z.Flags.WithFlag( GameObjectFlags.ProceduralBone, true );
				z.WorldScale = boneObject.WorldScale;
			}
		}
	}

	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Reliable )]
	void CreateRagdoll( Vector3 velocity, Vector3 origin, float propMass )
	{
		if ( !Controller.Renderer.IsValid() || Controller.Renderer.Model is null )
			return;

		var batch = Scene.BatchGroup();

		var go = new GameObject( true, "Ragdoll" );
		go.Tags.Add( "ragdoll" );
		go.WorldTransform = WorldTransform;

		var mainBody = go.Components.Create<SkinnedModelRenderer>();
		mainBody.CopyFrom( Controller.Renderer );
		mainBody.UseAnimGraph = false;

		// copy the clothes
		foreach ( var clothing in Controller.Renderer.GameObject.Children.Where( x => x.Tags.Has( "clothing" ) ).SelectMany( x => x.Components.GetAll<SkinnedModelRenderer>() ) )
		{
			if ( !clothing.IsValid() ) continue;

			var newClothing = new GameObject( true, clothing.GameObject.Name );
			newClothing.Parent = go;

			var item = newClothing.Components.Create<SkinnedModelRenderer>();
			item.CopyFrom( clothing );
			item.BoneMergeTarget = mainBody;
		}

		var physics = go.Components.Create<ModelPhysics>();
		physics.Model = mainBody.Model;
		physics.Renderer = mainBody;
		batch.Dispose();

		physics.CopyBonesFrom( Controller.Renderer, true );

		var corpse = go.AddComponent<DeathCameraTarget>();
		corpse.Connection = Network.Owner;
		corpse.Created = DateTime.Now;

		CopyBoneScalesToRagdoll( go );

		RagdollPush.Apply( physics, velocity, origin, propMass );
	}

	void CreateRagdollAndGhost()
	{
		var go = new GameObject( false, "Observer" );
		go.Components.Create<PlayerObserver>();
		go.NetworkSpawn( Network.Owner );
	}

	/// <summary>
	/// Broadcasts death to other players
	/// </summary>
	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Reliable )]
	void NotifyDeath( PlayerDiedParams args )
	{
		Local.IPlayerEvents.PostToGameObject( GameObject, x => x.OnDied( args ) );
		Global.IPlayerEvents.Post( x => x.OnPlayerDied( this, args ) );

		if ( args.Attacker == GameObject )
		{
			Local.IPlayerEvents.PostToGameObject( GameObject, x => x.OnSuicide() );
			Global.IPlayerEvents.Post( x => x.OnPlayerSuicide( this ) );
		}
	}

	[Rpc.Owner( NetFlags.HostOnly )]
	private void Flatline()
	{
		Sound.Play( "sounds/flatline.sound" );
	}

	private void Ghost()
	{
		CreateRagdollAndGhost();
	}

	/// <summary>
	/// Called on the host when a player dies
	/// </summary>
	void Kill( in DamageInfo d, bool createRagdoll = true )
	{
		//
		// Play the flatline sound on the owner
		//
		if ( IsLocalPlayer )
		{
			Flatline();
		}

		//
		// Let everyone know about the death
		//

		NotifyDeath( new PlayerDiedParams() { Attacker = d.Attacker } );

		// Forced holster: death isn't a voluntary switch, so addons can't veto it
		var inventory = GetComponent<PlayerInventory>();
		if ( inventory.IsValid() )
		{
			inventory.ForceHolster();
		}

		if ( createRagdoll )
		{
			var velocity = RagdollPush.GetVelocity( d, GameObject, 1024, 2048, out var propMass );
			CreateRagdoll( velocity, d.Origin, propMass );
		}

		//
		// Ghost and say goodbye to the player
		//
		Ghost();
		GameObject.Destroy();
	}

	[Rpc.Host]
	internal void EquipBestWeapon()
	{
		var inventory = GetComponent<PlayerInventory>();

		if ( inventory.IsValid() )
			inventory.SwitchWeapon( inventory.GetBestWeapon() );
	}

	void PlayerController.IEvents.PreInput()
	{
		OnControl();
	}

	// Static so the event lambda doesn't capture
	static bool _allowMovement;

	void OnControl()
	{
		_allowMovement = true;
		Local.IPlayerEvents.Post( static x => x.OnMove( ref _allowMovement ) );

		Controller.UseInputControls = _allowMovement;

		if ( Input.Pressed( "die" ) )
		{
			KillSelf();
			return;
		}

		if ( ControlDriving() )
			return;

		var seated = _cachedSeat is not null;

		if ( seated )
			PressSeatedGamepad();
		else if ( ActionAmounts.Count > 0 )
			ActionAmounts.Clear();

		if ( Input.Pressed( "noclip" ) )
		{
			ToggleNoclip();
		}

		// noclip was turned off while we were flying, or we sat down in something
		if ( IsNoclipping && (seated || !ServerSettings.CanNoclip( Network.Owner )) )
		{
			SetNoclip( false );
		}

		if ( Input.Pressed( "undo" ) )
		{
			ConsoleSystem.Run( "undo" );
		}

		// Before the weapon, so carrying something can keep attack from it
		GetComponent<PlayerCarry>()?.OnControl();

		var inventory = GetComponent<PlayerInventory>();
		inventory?.OnControl();

		var weaponSelection = Scene.Get<Inventory>();
		weaponSelection?.HandleInput();

		if ( inventory.IsValid() && inventory.ManualPumping )
			inventory.Pump();

		weaponSelection?.HandleScrollInput();
	}

	/// <summary>
	/// Seated, a gamepad's triggers are forward and backward and its left stick steers: the same actions as the keys, so
	/// whatever we're driving needs nothing new - plus how far each is pushed, in <see cref="ActionAmounts"/>.
	/// </summary>
	private void PressSeatedGamepad()
	{
		var steer = Input.GetAnalog( InputAnalog.LeftStickX );
		PressAnalog( "Forward", Input.GetAnalog( InputAnalog.RightTrigger ), 0.05f );
		PressAnalog( "Backward", Input.GetAnalog( InputAnalog.LeftTrigger ), 0.05f );
		PressAnalog( "Left", -steer, 0.15f );
		PressAnalog( "Right", steer, 0.15f );
	}

	/// <summary>
	/// Press an action from an analog input past its deadzone, and note how far. A key on it counts as all the way.
	/// </summary>
	private void PressAnalog( string action, float value, float deadzone )
	{
		var amount = ((value - deadzone) / (1.0f - deadzone)).Clamp( 0.0f, 1.0f );
		if ( Input.Down( action ) )
			amount = 1.0f;
		else if ( amount > 0.0f )
			Input.SetAction( action, true );

		if ( !ActionAmounts.TryGetValue( action, out var last ) || last != amount )
			ActionAmounts[action] = amount;
	}

	/// <summary>
	/// Runs on the owning client, since it simulates movement.
	/// </summary>
	internal void ToggleNoclip()
	{
		if ( IsProxy ) return;

		// no noclip from a seat
		if ( _cachedSeat is not null ) return;

		if ( !IsNoclipping && !ServerSettings.CanNoclip( Network.Owner ) )
		{
			Sandbox.UI.Notices.AddNotice( "block", Color.Red, Game.Language.GetPhrase( "server.noclip_disabled" ), 3 );
			return;
		}

		SetNoclip( !IsNoclipping );
	}

	void SetNoclip( bool enabled )
	{
		if ( GetComponent<NoclipMoveMode>( true ) is { } noclip )
		{
			noclip.Enabled = enabled;
			IsNoclipping = noclip.Enabled;
		}
	}

	private SoundHandle _dmgSound;

	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Reliable )]
	private void NotifyOnDamage( PlayerDamageParams args )
	{
		Local.IPlayerEvents.PostToGameObject( GameObject, x => x.OnDamage( args ) );
		Global.IPlayerEvents.Post( x => x.OnPlayerDamage( this, args ) );

		if ( IsLocalPlayer )
		{
			Scene.Camera?.AddShake( (args.Damage * 0.05f).Clamp( 0.5f, 3f ), 40f, 0.4f );

			_dmgSound?.Stop();

			if ( args.Tags.Contains( DamageTags.Shock ) )
			{
				_dmgSound = Sound.Play( "damage_taken_shock" );
			}
			else if ( args.Tags.Contains( DamageTags.Fall ) )
			{
				// Only a really hard landing gets the hurt sound - small falls just shake the view
				if ( args.Damage > 40f )
					_dmgSound = Sound.Play( "damage_taken_fall" );
				else
					_dmgSound = Sound.Play( "damage_taken_fall_low" );
			}
			else
			{
				_dmgSound = Sound.Play( "damage_taken_shot" );
			}
		}
	}

	/// <summary>
	/// Launch velocity for a living player caught in a blast: away from the blast origin and upward,
	/// stronger the closer they were.
	/// </summary>
	Vector3 GetExplosionKnockback( in DamageInfo damage )
	{
		var offset = WorldPosition - damage.Origin;
		var strength = MathX.Remap( offset.Length, 0, 512, 900, 300, true );

		return (offset.Normal + Vector3.Up).Normal * strength;
	}

	/// <summary>
	/// Movement is simulated by the owner, so the velocity change has to happen there.
	/// </summary>
	[Rpc.Owner( NetFlags.HostOnly | NetFlags.Reliable )]
	void ApplyKnockback( Vector3 velocity )
	{
		if ( !Controller.IsValid() || !Controller.Body.IsValid() ) return;

		Controller.PreventGrounding( 0.3f );
		Controller.Body.Velocity += velocity;
	}

	void ApplyExplosionKnockback( in DamageInfo dmg )
	{
		if ( dmg.Tags.Contains( DamageTags.Explosion ) && dmg.Origin != Vector3.Zero )
			ApplyKnockback( GetExplosionKnockback( dmg ) );
	}

	public void OnDamage( in DamageInfo dmg )
	{
		// only the host changes health, impacts can also report damage on clients
		if ( !Networking.IsHost ) return;

		if ( Health < 1 ) return;
		if ( !PlayerData.IsValid() ) return;
		if ( IsSpawnProtected ) return;

		var blamed = DamageBlame.Find( dmg );

		// with PvP off, other players can't hurt us or push us with explosions, unless a damage listener allows it
		var pvpBlocked = !ServerSettings.AllowPvp && blamed.IsValid() && blamed != this;

		if ( PlayerData.IsGodMode )
		{
			// Explosions throw the player even when god mode stops the damage.
			if ( ServerSettings.CanUseGodMode( Network.Owner ) )
			{
				if ( !pvpBlocked ) ApplyExplosionKnockback( dmg );
				return;
			}

			// no longer allowed, so turn it off
			PlayerData.IsGodMode = false;
		}

		//
		// Ignore impact damage from the world, for now
		//
		if ( dmg.Tags.Contains( "impact" ) )
		{
			var impactBody = dmg.Attacker?.GetComponentInParent<Rigidbody>();
			if ( !impactBody.IsValid() || !impactBody.MotionEnabled )
				return;

			// Was this fall damage? If so, we can bail out here
			if ( Controller.Velocity.Dot( Vector3.Down ) > 10 )
				return;

			// We were hit by a moving physics object, so lets take that damage.
		}

		// Fire pre-damage events — listeners can modify damage or cancel
		var e = new DamageEvent { Victim = GameObject, DamageInfo = dmg, Attacker = blamed?.GameObject ?? dmg.Attacker, Damage = dmg.Damage, Cancelled = pvpBlocked };
		Scene.RunEvent<Global.IDamageEvents>( x => x.OnDamaging( e ) );

		var damageEvent = new PlayerDamageEvent { Player = this, DamageInfo = dmg, Damage = e.Damage, BlamedPlayer = blamed, Cancelled = e.Cancelled };
		Local.IPlayerEvents.PostToGameObject( GameObject, x => x.OnDamaging( damageEvent ) );
#pragma warning disable CS0618
		Global.IPlayerEvents.Post( x => x.OnPlayerDamaging( damageEvent ) );
#pragma warning restore CS0618

		if ( damageEvent.Cancelled )
			return;

		ApplyExplosionKnockback( dmg );

		var damage = damageEvent.Damage;
		if ( dmg.Tags.Contains( DamageTags.Headshot ) )
			damage *= 2;

		if ( Armour > 0 )
		{
			float remainingDamage = damage - Armour;
			Armour = Math.Max( 0, Armour - damage );
			damage = Math.Max( 0, remainingDamage );
		}

		Health -= damage;

		e.Damage = damageEvent.Damage;
		e.HealthLost = damage;
		Scene.RunEvent<Global.IDamageEvents>( x => x.OnDamaged( e ) );

		NotifyOnDamage( new PlayerDamageParams()
		{
			Damage = damage,
			Attacker = dmg.Attacker,
			Weapon = dmg.Weapon,
			Tags = dmg.Tags,
			Position = dmg.Position,
			Origin = dmg.Origin,
		} );

		// We didn't die
		if ( Health >= 1 ) return;

		var death = new DeathEvent { Victim = GameObject, DamageInfo = dmg, Attacker = e.Attacker };
		Scene.RunEvent<Global.IDeathEvents>( x => x.OnDying( death ) );

		if ( death.Cancelled )
		{
			Health = MathF.Max( Health, 1 );
			return;
		}

		GameManager.Current.OnDeath( this, dmg );

		Health = 0;
		Kill( dmg, death.CreateRagdoll );

		Scene.RunEvent<Global.IDeathEvents>( x => x.OnDied( death ) );
	}

	void PlayerController.IEvents.OnLanded( float distance, Vector3 impactVelocity )
	{
		Local.IPlayerEvents.PostToGameObject( GameObject, x => x.OnLand( distance, impactVelocity ) );
		Global.IPlayerEvents.Post( x => x.OnPlayerLanded( this, distance, impactVelocity ) );

		var player = Components.Get<Player>();
		if ( !player.IsValid() ) return;

		if ( Controller.ThirdPerson || !player.IsLocalPlayer ) return;

		Scene.Camera?.AddPunch( new Angles( 0.105f * distance, Random.Shared.Float( -0.35f, 0.35f ), Random.Shared.Float( -0.35f, 0.35f ) ), 0.5f, 0.75f );
	}

	void PlayerController.IEvents.OnJumped()
	{
		Local.IPlayerEvents.PostToGameObject( GameObject, x => x.OnJump() );
		Global.IPlayerEvents.Post( x => x.OnPlayerJumped( this ) );

		if ( Controller.ThirdPerson || !IsLocalPlayer ) return;

		Scene.Camera?.AddPunch( new Angles( -10f, 0f, 0f ), 0.25f, 1f );
	}

	public T GetWeapon<T>() where T : BaseSandboxWeapon
	{
		return GetComponent<PlayerInventory>().GetWeapon<T>();
	}

	public void SwitchWeapon<T>() where T : BaseSandboxWeapon
	{
		var weapon = GetWeapon<T>();
		if ( weapon == null ) return;

		GetComponent<PlayerInventory>().SwitchWeapon( weapon );
	}

	public override void OnParentDestroy()
	{
		// When parent is destroyed, unparent the player to avoid destroying it
		GameObject.SetParent( null, true );
	}

	void Global.ISaveEvents.AfterLoad( string filename )
	{
		if ( !Body.IsValid() ) return;

		var dresser = Body.GetComponentInChildren<Dresser>( true );
		if ( !dresser.IsValid() ) return;

		// Apply clothing after load
		_ = ReapplyClothingAfterLoad( dresser );
	}

	private async Task ReapplyClothingAfterLoad( Dresser dresser )
	{
		await dresser.Apply();
		GameObject.Network.Refresh();
	}
}

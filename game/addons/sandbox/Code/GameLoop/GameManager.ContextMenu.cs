public sealed partial class GameManager : IContextMenuEvent
{
	/// <summary>
	/// The built-in Inspector right-click options: Ignite/Extinguish, Delete and Break.
	/// Addons add their own by implementing <see cref="IContextMenuEvent"/> the same way.
	/// </summary>
	void IContextMenuEvent.PopulateContextMenu( IContextMenuEvent.Event e )
	{
		var target = e.Target;
		if ( !target.IsValid() ) return;

		var isPlayer = target.Tags.Has( "player" );
		var prop = target.GetComponent<Prop>();
		var physicsProp = prop.IsValid() || target.GetComponent<BuilderProp>().IsValid();

		var localPlayer = Player.FindLocalPlayer();
		if ( localPlayer.IsValid() && !localPlayer.IsSeated && Player.CanDrive( target, Connection.Local ) )
		{
			e.AddOption( "gamepad", Game.Language.GetPhrase( "spawnmenu.inspect.drive" ), () => localPlayer.Drive( target ), 50, !Player.IsHeld( target ) );
		}

		if ( IsOnFire( target ) )
		{
			e.AddOption( "fire_extinguisher", Game.Language.GetPhrase( "spawnmenu.inspect.extinguish" ), () => ExtinguishInspectedObject( target ), 100 );
		}
		else if ( !isPlayer )
		{
			e.AddOption( "local_fire_department", Game.Language.GetPhrase( "spawnmenu.inspect.ignite" ), () => IgniteInspectedObject( target ), 100 );
		}

		if ( physicsProp )
		{
			e.AddSubmenu( "lock", Game.Language.GetPhrase( "spawnmenu.inspect.axis_lock" ), menu => BuildAxisLockMenu( menu, target ), 150 );
			e.AddSubmenu( "layers", Game.Language.GetPhrase( "spawnmenu.inspect.collisions" ), menu => BuildCollisionsMenu( menu, target ), 160 );
		}

		if ( !isPlayer )
		{
			e.AddOption( "delete", Game.Language.GetPhrase( "spawnmenu.inspect.delete" ), () => DeleteInspectedObject( target ), 200 );
		}

		if ( prop.IsValid() && prop.Health > 0 )
		{
			e.AddOption( "flare", Game.Language.GetPhrase( "spawnmenu.inspect.break" ), () => BreakInspectedProp( prop ), 300 );
		}
	}

	/// <summary>
	/// Delete an object from the Inspector context menu.
	/// </summary>
	[Rpc.Host]
	internal static void DeleteInspectedObject( GameObject go )
	{
		if ( !go.IsValid() || go.IsProxy ) return;
		if ( go.Tags.Has( "player" ) ) return;

		go.Destroy();
	}

	/// <summary>
	/// Axis lock menu
	/// </summary>
	private static void BuildAxisLockMenu( Sandbox.UI.Menu menu, GameObject target )
	{
		Sandbox.UI.Menu pitch = null, yaw = null, roll = null, unlock = null;

		pitch = AddAxisLockOption( menu, target, "Pitch", Apply );
		yaw = AddAxisLockOption( menu, target, "Yaw", Apply );
		roll = AddAxisLockOption( menu, target, "Roll", Apply );

		menu.AddSeparator();

		AddAxisLockPreset( menu, "spawnmenu.inspect.axis_lock_full", "lock", () => Set( true, true, true ) );
		AddAxisLockPreset( menu, "spawnmenu.inspect.axis_lock_upright", "vertical_align_top", () => Set( true, false, true ) );
		unlock = AddAxisLockPreset( menu, "spawnmenu.inspect.axis_lock_unlock_all", "lock_open", () => Set( false, false, false ) );

		UpdateUnlock();

		void Set( bool p, bool y, bool r )
		{
			pitch.Checked = p;
			yaw.Checked = y;
			roll.Checked = r;
			Apply();
		}

		void Apply()
		{
			SetAxisLocks( target, pitch.Checked, yaw.Checked, roll.Checked );
			UpdateUnlock();
		}

		void UpdateUnlock() => unlock.Enabled = pitch.Checked || yaw.Checked || roll.Checked;
	}

	private static Sandbox.UI.Menu AddAxisLockOption( Sandbox.UI.Menu menu, GameObject target, string axis, Action changed )
	{
		var text = Game.Language.GetPhrase( $"spawnmenu.inspect.lock_{axis.ToLowerInvariant()}" );
		var option = menu.AddOption( text, _ => changed() );
		option.Checked = IsAxisLocked( target, axis );
		return option;
	}

	private static Sandbox.UI.Menu AddAxisLockPreset( Sandbox.UI.Menu menu, string phrase, string icon, Action action )
	{
		var option = menu.AddOption( Game.Language.GetPhrase( phrase ), icon, action );
		option.StaysOpen = true;
		return option;
	}

	/// <summary>
	/// Set the object's rotation locks from the Inspector context menu
	/// </summary>
	[Rpc.Host]
	internal static void SetAxisLocks( GameObject go, bool pitch, bool yaw, bool roll )
	{
		if ( !go.IsValid() || go.IsProxy ) return;

		PhysicsLock Change( PhysicsLock locking )
		{
			locking.Pitch = pitch;
			locking.Yaw = yaw;
			locking.Roll = roll;
			return locking;
		}

		foreach ( var rb in go.GetComponentsInChildren<Rigidbody>( true ) )
		{
			rb.Locking = Change( rb.Locking );
		}

		foreach ( var mp in go.GetComponentsInChildren<ModelPhysics>( true ) )
		{
			mp.Locking = Change( mp.Locking );
		}

		// Locking isn't synced, so send a snapshot for clients' menu state
		if ( go.Network.Active )
		{
			go.Network.Refresh();
		}
	}

	/// <summary>
	/// Is this rotation axis locked on the object's physics bodies?
	/// </summary>
	internal static bool IsAxisLocked( GameObject go, string axis )
	{
		if ( !go.IsValid() ) return false;

		var rb = go.GetComponentInChildren<Rigidbody>( true );
		if ( !rb.IsValid() ) return false;

		var locking = rb.Locking;
		return axis switch
		{
			"Pitch" => locking.Pitch,
			"Yaw" => locking.Yaw,
			"Roll" => locking.Roll,
			_ => false
		};
	}

	/// <summary>
	/// Tags that turn off a prop's collisions with everything but the world, or with the world.
	/// Their rules live in ProjectSettings/Collision.config.
	/// </summary>
	internal const string NoPropCollisionsTag = "nocollide_props";
	internal const string NoWorldCollisionsTag = "nocollide_world";

	/// <summary>
	/// Collisions menu
	/// </summary>
	private static void BuildCollisionsMenu( Sandbox.UI.Menu menu, GameObject target )
	{
		Sandbox.UI.Menu props = null, world = null;

		props = AddCollisionOption( menu, target, "spawnmenu.inspect.prop_collisions", NoPropCollisionsTag, Apply );
		world = AddCollisionOption( menu, target, "spawnmenu.inspect.world_collisions", NoWorldCollisionsTag, Apply );

		void Apply() => SetCollisions( target, props.Checked, world.Checked );
	}

	private static Sandbox.UI.Menu AddCollisionOption( Sandbox.UI.Menu menu, GameObject target, string phrase, string tag, Action changed )
	{
		var option = menu.AddOption( Game.Language.GetPhrase( phrase ), _ => changed() );
		option.Checked = !target.Tags.Has( tag );
		return option;
	}

	/// <summary>
	/// Turn the object's prop and world collisions on or off from the Inspector context menu
	/// </summary>
	[Rpc.Host]
	internal static void SetCollisions( GameObject go, bool props, bool world )
	{
		if ( !go.IsValid() || go.IsProxy ) return;

		go.Tags.Set( NoPropCollisionsTag, !props );
		go.Tags.Set( NoWorldCollisionsTag, !world );

		if ( go.Network.Active )
		{
			go.Network.Refresh();
		}
	}

	/// <summary>
	/// Break (gib) a prop from the Inspector context menu.
	/// </summary>
	[Rpc.Host]
	internal static void BreakInspectedProp( Prop prop )
	{
		if ( !prop.IsValid() || prop.IsProxy ) return;

		var damageable = prop.GetComponent<Component.IDamageable>();
		if ( damageable is null ) return;

		var dmg = new DamageInfo( 999999, null, null );
		dmg.Tags.Add( DamageTags.GibAlways );
		damageable.OnDamage( in dmg );
	}

	/// <summary>
	/// Set an object on fire from the Inspector context menu. The engine's FireDamage component
	/// damages every IDamageable under the root until it breaks. Objects that can't break (unbreakable
	/// props, non-damageable entities) just keep burning.
	/// </summary>
	[Rpc.Host]
	internal static void IgniteInspectedObject( GameObject go )
	{
		if ( !go.IsValid() || go.IsProxy ) return;
		if ( go.Tags.Has( "player" ) ) return;
		if ( IsOnFire( go ) ) return;

		// Props already know how to burn. Prop.IsOnFire is never reset (protected setter), so a prop
		// that was extinguished and re-ignited falls through to the generic path below.
		if ( go.GetComponent<Prop>() is { IsOnFire: false } prop )
		{
			prop.Ignite();
			return;
		}

		// Everything else gets the same fire prefab Prop.Ignite uses, parented to the root
		var firePrefab = ResourceLibrary.Get<PrefabFile>( "/prefabs/engine/ignite.prefab" );
		if ( firePrefab is null )
		{
			Log.Warning( "Can't find /prefabs/engine/ignite.prefab" );
			return;
		}

		var fire = GameObject.Clone( firePrefab, new CloneConfig { Parent = go, Transform = global::Transform.Zero, StartEnabled = true } );
		if ( !fire.IsValid() ) return;

		fire.RunEvent<ParticleModelEmitter>( x => x.Target = go );

		if ( fire.Network.Active )
		{
			fire.Network.Refresh( fire );
		}
	}

	/// <summary>
	/// Put out a burning object from the Inspector context menu. Removes every fire effect
	/// (anything carrying a FireDamage) under the root; descendant destruction is networked.
	/// </summary>
	[Rpc.Host]
	internal static void ExtinguishInspectedObject( GameObject go )
	{
		if ( !go.IsValid() || go.IsProxy ) return;

		foreach ( var fire in go.GetComponentsInChildren<FireDamage>( true ).ToArray() )
		{
			if ( !fire.IsValid() ) continue;

			var fireObject = fire.GameObject;
			fireObject.Destroy();

			// Destroying a descendant is only broadcast when we refresh it
			if ( go.Network.Active )
			{
				go.Network.Refresh( fireObject );
			}
		}
	}

	/// <summary>
	/// Is this object currently burning? Works on clients too, since the fire prefab is replicated
	/// as a child of the root. Deliberately not Prop.IsOnFire: that flag is never cleared.
	/// </summary>
	internal static bool IsOnFire( GameObject go )
	{
		if ( !go.IsValid() ) return false;
		return go.GetComponentInChildren<FireDamage>( true ).IsValid();
	}
}

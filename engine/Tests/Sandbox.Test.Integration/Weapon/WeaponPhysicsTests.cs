using Sandbox.Network;
using Sandbox.Npcs;
using Sandbox.Npcs.Layers;
using SceneTests;
using System;
using TestHolder = WeaponTests.WeaponHolderTests.TestHolder;
using TestWeapon = WeaponTests.WeaponHolderTests.TestWeapon;

namespace WeaponTests;

/// <summary>
/// A weapon lying in the world is a physics pickup and a held one is not: the weapon switches the body and
/// colliders on its own object, and its network interpolation, from whether something holds it.
/// </summary>
[TestClass]
public class WeaponPhysicsTests
{
	Connection _previousLocalConnection;
	NetworkSystem _previousNetworkSystem;

	[TestInitialize]
	public void PinHostNetworkingState()
	{
		_previousLocalConnection = Connection.Local;
		_previousNetworkSystem = Networking.System;

		Connection.Local = new TestConnection( Guid.NewGuid(), isHost: true );
		Networking.System = null;
	}

	[TestCleanup]
	public void RestoreNetworkingState()
	{
		Connection.Local = _previousLocalConnection;
		Networking.System = _previousNetworkSystem;
	}

	static BaseInventoryComponent CreatePlayer( Scene scene )
	{
		var go = scene.CreateObject();
		var player = go.Components.Create<PlayerController>();
		player.UseInputControls = false;
		player.EnableFootstepSounds = false;
		return go.Components.Create<BaseInventoryComponent>();
	}

	// A pickup: a body and a collider on the weapon's own object, and a collider on a child that is not the weapon's own
	static TestWeapon CreatePickup( Scene scene, bool enabled = true )
	{
		var go = scene.CreateObject( enabled );
		go.Components.Create<Rigidbody>();
		go.Components.Create<BoxCollider>();

		var part = scene.CreateObject();
		part.Parent = go;
		part.Components.Create<SphereCollider>();

		return go.Components.Create<TestWeapon>();
	}

	static void AssertPhysics( bool on, BaseCombatWeapon weapon, string when )
	{
		Assert.AreEqual( on, weapon.GetComponent<Rigidbody>( true ).Enabled, $"Body {when}" );
		Assert.AreEqual( on, weapon.GetComponent<BoxCollider>( true ).Enabled, $"Collider {when}" );
		Assert.AreEqual( on, weapon.GameObject.Network.Interpolation, $"Interpolation {when}" );
		Assert.IsTrue( weapon.GetComponentInChildren<SphereCollider>( true ).Enabled, $"A child's collider is left alone {when}" );
	}

	[TestMethod]
	public void PickedUpDeployedHolsteredAndDropped()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var inventory = CreatePlayer( scene );
		var weapon = CreatePickup( scene );
		scene.GameTick();
		AssertPhysics( true, weapon, "in the world" );

		Assert.IsTrue( inventory.Add( weapon ) );
		Assert.IsTrue( weapon.IsHeld );
		Assert.IsFalse( weapon.GameObject.Enabled );
		AssertPhysics( false, weapon, "picked up" );

		inventory.Switch( weapon );
		Assert.IsTrue( weapon.Active );
		AssertPhysics( false, weapon, "deployed" );

		inventory.Switch( null, allowHolster: true );
		AssertPhysics( false, weapon, "holstered" );

		inventory.Switch( weapon );
		scene.GameTick();
		AssertPhysics( false, weapon, "deployed again" );

		inventory.Drop( weapon );
		Assert.IsFalse( weapon.IsHeld );
		Assert.IsTrue( weapon.Active );
		AssertPhysics( true, weapon, "dropped" );

		Assert.IsTrue( inventory.Add( weapon ) );
		AssertPhysics( false, weapon, "picked up again" );
	}

	[TestMethod]
	public void DropRestoresMixedPhysicsSettingsAcrossPickups()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var inventory = CreatePlayer( scene );
		var weapon = CreatePickup( scene );
		var disabled = weapon.GameObject.AddComponent<SphereCollider>();
		disabled.Enabled = false;
		var body = weapon.GetComponent<Rigidbody>();
		body.Enabled = false;
		weapon.Network.Interpolation = false;

		foreach ( var bodyEnabled in new[] { false, true } )
		{
			body.Enabled = bodyEnabled;
			Assert.IsTrue( inventory.Add( weapon ) );
			inventory.Switch( weapon );
			inventory.Drop( weapon );
			Assert.AreEqual( bodyEnabled, body.Enabled );
			Assert.IsTrue( weapon.GetComponent<BoxCollider>( true ).Enabled );
			Assert.IsFalse( disabled.Enabled );
			Assert.IsFalse( weapon.Network.Interpolation );
		}
	}

	[TestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void LateJoiningPeerRestoresAuthoredSettings( bool stateBeforeDrop )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var holder = scene.CreateObject();
		holder.AddComponent<TestHolder>();
		var weapon = CreatePickup( scene, enabled: false );
		var body = weapon.GetComponent<Rigidbody>( true );
		var collider = weapon.GetComponent<BoxCollider>( true );
		body.Enabled = collider.Enabled = false;
		weapon.Network.Interpolation = false;
		Networking.System = new NetworkSystem( "client", Game.TypeLibrary );
		weapon.Slot = 0;
		weapon.GameObject.Parent = holder;

		void ReceiveState()
		{
			var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
			typeof( BaseCombatWeapon ).GetProperty( "DisabledWorldPhysics", flags ).SetValue( weapon, new[] { collider.Id } );
			typeof( BaseCombatWeapon ).GetProperty( "WorldInterpolation", flags ).SetValue( weapon, false );
		}

		if ( stateBeforeDrop ) ReceiveState();
		weapon.GameObject.Parent = null;
		weapon.Slot = -1;
		if ( !stateBeforeDrop ) ReceiveState();
		Assert.IsTrue( body.Enabled );
		Assert.IsFalse( collider.Enabled );
		Assert.IsFalse( weapon.Network.Interpolation );
	}

	[TestMethod]
	public void DroppedWhileHolstered()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var inventory = CreatePlayer( scene );
		var weapon = CreatePickup( scene );
		scene.GameTick();

		Assert.IsTrue( inventory.Add( weapon ) );
		Assert.IsNull( inventory.ActiveItem );

		inventory.Drop( weapon );

		Assert.IsTrue( weapon.Active );
		AssertPhysics( true, weapon, "dropped without ever being deployed" );
	}

	[TestMethod]
	public void GivenStraightIntoAnInventory()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var inventory = CreatePlayer( scene );
		var prefab = CreatePickup( scene, enabled: false ).GameObject;

		var weapon = inventory.Pickup( prefab ) as BaseCombatWeapon;
		Assert.IsNotNull( weapon );

		inventory.Switch( weapon );
		Assert.IsTrue( weapon.Active );
		AssertPhysics( false, weapon, "given and deployed" );

		inventory.Drop( weapon );
		AssertPhysics( true, weapon, "dropped" );
	}

	[TestMethod]
	public void HeldByAnNpc()
	{
		var scene = new Scene();
		using var scope = scene.Push();

		// Test types aren't in the type library, so the required animation layer is added by hand
		var go = scene.CreateObject();
		var animation = go.Components.Create<AnimationLayer>();
		var npc = go.Components.Create<BaseCombatNpc>();
		npc.Animation ??= animation;

		var weapon = CreatePickup( scene );
		weapon.GameObject.Parent = go;
		npc.Weapon = weapon;
		scene.GameTick();

		Assert.AreEqual( npc, weapon.Holder );
		Assert.AreEqual( weapon, npc.Inventory.ActiveItem );
		AssertPhysics( false, weapon, "held by an NPC" );

		npc.Inventory.Drop( weapon );
		Assert.IsFalse( weapon.IsHeld );
		AssertPhysics( true, weapon, "dropped by an NPC" );
	}

	[TestMethod]
	public void APeerThatNeverSawItDeployedStillLetsItGo()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var holder = scene.CreateObject();
		holder.Components.Create<TestHolder>();

		// As a peer that joined late has it: put away in a slot under its holder, never enabled here, with its
		// physics already off
		var weapon = CreatePickup( scene, enabled: false );
		weapon.GetComponent<Rigidbody>( true ).Enabled = false;
		weapon.GetComponent<BoxCollider>( true ).Enabled = false;
		weapon.GameObject.Network.Interpolation = false;
		Networking.System = new NetworkSystem( "client", Game.TypeLibrary );
		weapon.Slot = 2;
		weapon.GameObject.Parent = holder;
		Assert.IsTrue( weapon.IsHeld );

		weapon.GameObject.Parent = null;
		weapon.Slot = -1;
		weapon.GameObject.Enabled = true;

		AssertPhysics( true, weapon, "dropped" );
	}

	[TestMethod]
	public void APeerThatIsNotTheHostEnablesAWeaponTheHostDropped()
	{
		// The drop reaches a client as synced state, and the two parts of it can land in either order
		foreach ( var slotFirst in new[] { false, true } )
		{
			var scene = new Scene();
			using var scope = scene.Push();
			var inventory = CreatePlayer( scene );
			var weapon = CreatePickup( scene );
			scene.GameTick();

			Assert.IsTrue( inventory.Add( weapon ) );
			inventory.Switch( weapon );
			Assert.IsTrue( weapon.Active );

			Networking.System = new NetworkSystem( "client", Game.TypeLibrary );
			Assert.IsFalse( Networking.IsHost );

			if ( slotFirst )
			{
				weapon.Slot = -1;
				inventory.OnActiveItemChanged( weapon, null );
			}
			else
			{
				inventory.OnActiveItemChanged( weapon, null );
				Assert.IsFalse( weapon.GameObject.Enabled, "Put away on this peer, like on every other" );
				weapon.Slot = -1;
			}

			weapon.GameObject.Parent = null;

			Assert.IsTrue( weapon.GameObject.Enabled, "The host enabled it when it dropped it, which nothing tells this peer" );
			AssertPhysics( true, weapon, "dropped" );

			Networking.System = null;
		}
	}

	[TestMethod]
	public void AWeaponWithoutPhysicsIsHeldAndDroppedTheSame()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var inventory = CreatePlayer( scene );
		var weapon = scene.CreateObject().Components.Create<TestWeapon>();

		Assert.IsTrue( inventory.Add( weapon ) );
		inventory.Switch( weapon );
		Assert.IsFalse( weapon.GameObject.Network.Interpolation );

		inventory.Drop( weapon );
		Assert.IsTrue( weapon.GameObject.Network.Interpolation );
		Assert.IsNull( weapon.GetComponent<Rigidbody>( true ) );
	}

	[TestMethod]
	public void AnInventoryOnSomethingThatIsNotAHolderDoesNotHoldIt()
	{
		var scene = new Scene();
		using var scope = scene.Push();

		// A game's own bot: an inventory and nothing the engine knows as a holder. The game switches the body itself.
		var inventory = scene.CreateObject().Components.Create<BaseInventoryComponent>();
		var weapon = CreatePickup( scene );
		weapon.GetComponent<Rigidbody>().Enabled = false;
		weapon.GameObject.Network.Interpolation = false;
		scene.GameTick();

		Assert.IsTrue( inventory.Add( weapon ) );
		inventory.Switch( weapon );
		scene.GameTick();
		Assert.IsTrue( weapon.IsActive );
		Assert.IsFalse( weapon.IsHeld );

		inventory.Switch( null, allowHolster: true );
		inventory.Switch( weapon );
		inventory.Drop( weapon );
		scene.GameTick();

		Assert.IsNull( weapon.Inventory );
		Assert.IsFalse( weapon.GetComponent<Rigidbody>( true ).Enabled, "The body stays as the game left it" );
		Assert.IsTrue( weapon.GetComponent<BoxCollider>( true ).Enabled );
		Assert.IsFalse( weapon.GameObject.Network.Interpolation );
	}

	[TestMethod]
	public void AWeaponNothingHoldsIsLeftAsItWasMade()
	{
		var scene = new Scene();
		using var scope = scene.Push();

		// Mounted on something that is not a holder, with its body switched off on purpose
		var mount = scene.CreateObject();
		var weapon = CreatePickup( scene );
		weapon.GetComponent<Rigidbody>().Enabled = false;
		weapon.GameObject.Network.Interpolation = false;
		weapon.GameObject.Parent = mount;
		scene.GameTick();

		weapon.GameObject.Enabled = false;
		weapon.GameObject.Enabled = true;
		weapon.GameObject.Parent = null;
		scene.GameTick();

		Assert.IsFalse( weapon.IsHeld );
		Assert.IsFalse( weapon.GetComponent<Rigidbody>( true ).Enabled );
		Assert.IsTrue( weapon.GetComponent<BoxCollider>( true ).Enabled );
		Assert.IsFalse( weapon.GameObject.Network.Interpolation );

		// Parented under a holder without an inventory it is held all the same, and let go when it leaves
		var holder = scene.CreateObject();
		holder.Components.Create<TestHolder>();
		weapon.GameObject.Parent = holder;
		Assert.IsTrue( weapon.IsHeld );
		AssertPhysics( false, weapon, "under a holder" );

		weapon.GameObject.Parent = null;
		Assert.IsFalse( weapon.GetComponent<Rigidbody>( true ).Enabled );
		Assert.IsTrue( weapon.GetComponent<BoxCollider>( true ).Enabled );
		Assert.IsFalse( weapon.GameObject.Network.Interpolation );
	}

	[TestMethod]
	public void AHeldWeaponThatAppearsWithItsHolderIsLeftCleanAndIsDestroyedQuietly()
	{
		var errors = new System.Collections.Generic.List<string>();
		void OnMessage( Sandbox.LogEvent e )
		{
			if ( e.Level == Sandbox.LogLevel.Error )
				errors.Add( e.Message );
		}

		Sandbox.Diagnostics.Logging.OnMessage += OnMessage;

		try
		{
			// A prefab can activate the holder and weapon together in either component order.
			var orders = new[] { "WBC", "WCB", "BWC", "BCW", "CWB", "CBW" };

			foreach ( var cloned in new[] { false, true } )
			{
				foreach ( var order in orders )
				{
					var scene = new Scene();
					using var scope = scene.Push();

					var root = scene.CreateObject( false );
					root.Components.Create<Rigidbody>();
					root.Components.Create<TestHolder>();

					var weaponGo = scene.CreateObject();
					weaponGo.Parent = root;

					foreach ( var part in order )
					{
						if ( part == 'W' ) weaponGo.Components.Create<BaseCombatWeapon>();
						if ( part == 'B' ) weaponGo.Components.Create<Rigidbody>();
						if ( part == 'C' ) weaponGo.Components.Create<BoxCollider>();
					}

					var live = root;

					if ( cloned )
						live = root.Clone( new CloneConfig { StartEnabled = true, Transform = global::Transform.Zero } );
					else
						root.Enabled = true;

					scene.GameTick();

					var weapon = live.Components.Get<BaseCombatWeapon>( FindMode.EverythingInSelfAndDescendants );
					var body = weapon.GetComponent<Rigidbody>( true );
					var collider = weapon.GetComponent<BoxCollider>( true );
					var how = $"{(cloned ? "cloned" : "switched on")} with its parts in the order {order}";

					Assert.IsTrue( weapon.IsHeld, how );
					Assert.IsFalse( body.Enabled, how );
					Assert.IsFalse( collider.Enabled, how );
					Assert.IsNull( collider.Rigidbody, $"The collider is not left connected to a body, {how}" );
					Assert.IsFalse( body.PhysicsBody.IsValid(), $"The weapon has no body left in the world, {how}" );

					live.Destroy();
					scene.GameTick();
					scene.GameTick();

					Assert.AreEqual( 0, errors.Count, $"{how}: {string.Join( " | ", errors )}" );
				}
			}
		}
		finally
		{
			Sandbox.Diagnostics.Logging.OnMessage -= OnMessage;
		}
	}
}

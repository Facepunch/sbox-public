using Sandbox.Citizen;
using Sandbox.Network;
using SceneTests;
using SceneTests.Components;
using System;
using System.Collections.Generic;

namespace WeaponTests;

#pragma warning disable CS0618

/// <summary>
/// The weapon and its holder: how a hold name reaches the holder's graph, who animates the holder and when,
/// which weapon's events count, and what a holder that isn't a player gets.
/// </summary>
[TestClass]
public class WeaponHolderTests
{
	public class TestWeapon : BaseCombatWeapon
	{
		public void Post( string name ) => PostWeaponEvent( name );
		public void Shoot() => OnShootEffects( new ShotEffect() );
	}

	/// <summary>A second kind of weapon: an inventory holds one of each.</summary>
	public sealed class OtherWeapon : TestWeapon;

	/// <summary>A weapon that animates its holder itself, the way a game suppresses the default animation.</summary>
	public sealed class SilentWeapon : TestWeapon
	{
		public int Updates;

		protected override void UpdateBodyAnimations( SkinnedModelRenderer body ) => Updates++;
	}

	public sealed class TestHolder : Component, BaseCombatWeapon.IHolder
	{
		public SkinnedModelRenderer Renderer { get; set; }
		public Ray CalculateAimRay( BaseCombatWeapon weapon ) => new( WorldPosition, WorldRotation.Forward );
	}

	public sealed class Listener : Component, BaseCombatWeapon.IEvents
	{
		public List<string> Log = new();
		public Action<BaseCombatWeapon, string> OnEvent;

		void BaseCombatWeapon.IEvents.OnWeaponEvent( BaseCombatWeapon weapon, string name )
		{
			Log.Add( name );
			OnEvent?.Invoke( weapon, name );
		}
	}

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

	/// <summary>
	/// A player with the human body. The model's own graph is a legacy one; <paramref name="graph"/> swaps in
	/// the current citizen graph, which describes its holdtype options.
	/// </summary>
	static (PlayerController Player, SkinnedModelRenderer Body) CreatePlayer( Scene scene, bool graph )
	{
		var go = scene.CreateObject();
		var player = go.Components.Create<PlayerController>();
		player.UseInputControls = false;
		player.EnableFootstepSounds = false;

		var bodyGo = scene.CreateObject();
		bodyGo.Parent = go;
		var body = bodyGo.Components.Create<SkinnedModelRenderer>();
		body.Model = Model.Load( "models/citizen_human/human.vmdl" );
		player.Renderer = body;

		if ( graph )
			body.Animation.Graph = Sandbox.Animation.Graph.Load( "models/citizen_human/animgraphs/human.ag" );

		scene.GameTick();
		Assert.AreEqual( graph, body.Animation.IsValid, "The body runs the graph the test asked for" );
		return (player, body);
	}

	/// <summary>A weapon parented under <paramref name="holder"/> and enabled, the way a game without the engine inventory holds one.</summary>
	static T Give<T>( Scene scene, GameObject holder, string holdType = "" ) where T : BaseCombatWeapon, new()
	{
		var go = scene.CreateObject();
		go.Parent = holder;
		var weapon = go.Components.Create<T>();
		weapon.HoldType = holdType;
		return weapon;
	}

	/// <summary>A weapon in the inventory on <paramref name="holder"/>, holstered until switched to.</summary>
	static T Add<T>( Scene scene, GameObject holder, string holdType = "" ) where T : BaseCombatWeapon, new()
	{
		var inventory = holder.Components.GetOrCreate<BaseInventoryComponent>();
		var weapon = scene.CreateObject().Components.Create<T>();
		weapon.HoldType = holdType;
		Assert.IsTrue( inventory.Add( weapon ) );
		return weapon;
	}

	static string Hold( SkinnedModelRenderer body )
	{
		if ( body.Animation.Graph is not null )
			return body.Animation.Parameters.TryGetString( "holdtype", out var name ) ? name : null;

		var options = body.SceneModel.AnimationGraph.GetParameter<byte>( "holdtype" ).OptionNames;
		return options[body.GetInt( "holdtype" )];
	}

	static bool Gesture( SkinnedModelRenderer body, string name )
	{
		return body.Animation.Parameters.TryGetBool( name, out var value ) && value;
	}

	static readonly HashSet<string> CitizenOptions = new( StringComparer.OrdinalIgnoreCase )
	{
		"none", "pistol", "rifle", "shotgun", "holditem", "melee_punch", "melee_weapons", "rpg", "physgun"
	};

	static string Resolve( string holdType, string fallback, HashSet<string> options )
	{
		return CitizenHoldTypes.Resolve( holdType, fallback, options, static ( o, name ) => o.Contains( name ) );
	}

	[TestMethod]
	public void HoldResolutionKeepsNamesTheGraphHasAndNamesNobodyKnows()
	{
		Assert.AreEqual( "pistol", Resolve( "pistol", "", CitizenOptions ) );
		Assert.AreEqual( "Pistol", Resolve( "Pistol", "", CitizenOptions ), "The name is passed on as it was written" );
		Assert.AreEqual( "rifle", Resolve( WeaponHolds.Smg, "", CitizenOptions ), "A well-known hold takes the closest citizen option" );
		Assert.AreEqual( "holditem", Resolve( "camera", WeaponHolds.Item, CitizenOptions ), "A game's own hold takes its fallback" );
		Assert.AreEqual( "rifle", Resolve( "", WeaponHolds.Rifle, CitizenOptions ), "A fallback alone is a hold" );
		Assert.AreEqual( "crossbow", Resolve( "crossbow", "", CitizenOptions ), "A name nobody knows is never replaced" );
		Assert.AreEqual( "crossbow", Resolve( "crossbow", "harpoon", CitizenOptions ) );

		var custom = new HashSet<string>( StringComparer.OrdinalIgnoreCase ) { "unarmed", "crossbow", "smg", "melee" };
		Assert.AreEqual( "crossbow", Resolve( "crossbow", "", custom ) );
		Assert.AreEqual( "smg", Resolve( WeaponHolds.Smg, "", custom ), "A graph with its own option of that name gets it untranslated" );
		Assert.AreEqual( "melee", Resolve( WeaponHolds.Melee, "", custom ) );
		Assert.AreEqual( "rifle", Resolve( "rifle", "", custom ), "A well-known hold the graph has no pose for stays as written" );
		Assert.AreEqual( "crossbow", Resolve( "bolt_thrower", "crossbow", custom ), "The fallback is used as written when the graph has it" );
	}

	[TestMethod]
	public void AWeaponParentedWithoutDeployPosesALegacyBody()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (player, body) = CreatePlayer( scene, graph: false );
		Assert.AreEqual( "none", Hold( body ) );

		var weapon = Give<TestWeapon>( scene, player.GameObject, "pistol" );
		weapon.Handedness = BaseCombatWeapon.WeaponHandedness.Right;
		scene.GameTick();

		Assert.AreEqual( "pistol", Hold( body ) );
		Assert.AreEqual( 1, body.GetInt( "holdtype_handedness" ) );

		weapon.HoldType = WeaponHolds.Smg;
		scene.GameTick();
		Assert.AreEqual( "rifle", Hold( body ), "A well-known hold the citizen graph lacks takes its closest option" );
	}

	[TestMethod]
	public void AnUnknownHoldReachesALegacyGraphUnchanged()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (player, body) = CreatePlayer( scene, graph: false );
		var weapon = Give<TestWeapon>( scene, player.GameObject, "shotgun" );
		scene.GameTick();
		Assert.AreEqual( "shotgun", Hold( body ) );

		// The graph has no such option and refuses it, so the pose stays. Replacing the name with "none" would drop it.
		weapon.HoldType = "crossbow";
		scene.GameTick();
		Assert.AreEqual( "shotgun", Hold( body ) );
	}

	[TestMethod]
	public void AnUnknownHoldReachesAGraphUnchanged()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (player, body) = CreatePlayer( scene, graph: true );
		var weapon = Give<TestWeapon>( scene, player.GameObject, "crossbow" );
		scene.GameTick();
		Assert.AreEqual( "crossbow", Hold( body ) );

		weapon.HoldType = WeaponHolds.Smg;
		scene.GameTick();
		Assert.AreEqual( "rifle", Hold( body ) );

		weapon.HoldType = "camera";
		weapon.HoldFallback = WeaponHolds.Item;
		scene.GameTick();
		Assert.AreEqual( "holditem", Hold( body ) );
	}

	[TestMethod]
	public void AWeaponMovedToAnotherHolderPosesThatHolder()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (first, firstBody) = CreatePlayer( scene, graph: true );
		var (second, secondBody) = CreatePlayer( scene, graph: true );
		var weapon = Give<TestWeapon>( scene, first.GameObject, "pistol" );
		scene.GameTick();
		Assert.AreEqual( "pistol", Hold( firstBody ) );

		weapon.GameObject.Parent = second.GameObject;
		weapon.Post( WeaponEvents.Attack );
		scene.GameTick();

		Assert.AreEqual( "pistol", Hold( secondBody ) );
		Assert.IsTrue( Gesture( secondBody, "b_attack" ), "A weapon that was never deployed still plays its attack" );
		Assert.IsFalse( Gesture( firstBody, "b_attack" ) );
	}

	[TestMethod]
	public void OverridingUpdateBodyAnimationsSuppressesTheDefaults()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (player, body) = CreatePlayer( scene, graph: true );
		var before = Hold( body );

		var weapon = Add<SilentWeapon>( scene, player.GameObject, "pistol" );
		player.GetComponent<BaseInventoryComponent>().Switch( weapon );
		weapon.Post( WeaponEvents.Attack );
		weapon.Post( WeaponEvents.Reload );
		scene.GameTick();

		Assert.IsTrue( weapon.Updates > 0, "The weapon's own animation ran" );
		Assert.AreEqual( before, Hold( body ) );
		Assert.IsFalse( Gesture( body, "b_deploy" ) );
		Assert.IsFalse( Gesture( body, "b_attack" ) );
		Assert.IsFalse( Gesture( body, "b_reload" ) );
	}

	[TestMethod]
	public void GesturesPlayForOneUpdateAndTheHoldIsLetGoOnHolster()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (player, body) = CreatePlayer( scene, graph: true );
		var inventory = player.Components.GetOrCreate<BaseInventoryComponent>();
		var weapon = Add<TestWeapon>( scene, player.GameObject, "pistol" );

		inventory.Switch( weapon );
		weapon.Shoot();
		weapon.Post( WeaponEvents.Reload );
		weapon.Post( WeaponEvents.ReloadCancel );
		scene.GameTick();
		Assert.IsTrue( Gesture( body, "b_deploy" ) );
		Assert.IsTrue( Gesture( body, "b_attack" ) );
		Assert.IsFalse( Gesture( body, "b_reload" ) );
		Assert.IsTrue( Gesture( body, "b_reload_cancel" ) );
		Assert.AreEqual( "pistol", Hold( body ) );

		scene.GameTick();
		Assert.IsFalse( Gesture( body, "b_deploy" ) );
		Assert.IsFalse( Gesture( body, "b_attack" ) );
		Assert.IsFalse( Gesture( body, "b_reload_cancel" ) );

		weapon.Shoot();
		scene.GameTick();
		Assert.IsTrue( Gesture( body, "b_attack" ) );

		// Put away the frame after it fired: the gesture left on is switched off with the hold.
		inventory.Switch( null, allowHolster: true );
		Assert.IsFalse( Gesture( body, "b_attack" ) );
		Assert.AreEqual( "none", Hold( body ) );
	}

	[TestMethod]
	public void TheControllerLeavesGestureParametersAloneWithoutAWeapon()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (player, body) = CreatePlayer( scene, graph: true );

		foreach ( var animator in new[] { true, false } )
		{
			player.UseAnimatorControls = animator;
			body.Animation.Parameters.Set( "b_attack", true );
			body.Animation.Parameters.Set( "holdtype", "rifle" );
			scene.GameTick();
			scene.GameTick();

			Assert.IsTrue( Gesture( body, "b_attack" ), "A game's own gesture isn't switched off" );
			Assert.AreEqual( "rifle", Hold( body ), "A game's own hold isn't let go" );
		}

		// A weapon without a hold drives the gestures and still leaves the hold to the game.
		var weapon = Give<TestWeapon>( scene, player.GameObject );
		scene.GameTick();
		Assert.IsFalse( Gesture( body, "b_attack" ) );
		Assert.AreEqual( "rifle", Hold( body ) );
	}

	[TestMethod]
	public void EventsFromAWeaponThatIsNotHeldAreIgnored()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (player, body) = CreatePlayer( scene, graph: true );
		var inventory = player.Components.GetOrCreate<BaseInventoryComponent>();
		var first = Add<TestWeapon>( scene, player.GameObject, "pistol" );
		var second = Add<OtherWeapon>( scene, player.GameObject, "rifle" );

		inventory.Switch( first );
		scene.GameTick();
		inventory.Switch( second );

		// The first weapon's reload is cancelled by the switch, and the news arrives after it.
		first.Post( WeaponEvents.ReloadCancel );
		first.Post( WeaponEvents.Attack );
		first.Post( WeaponEvents.Reload );
		scene.GameTick();

		Assert.IsTrue( Gesture( body, "b_deploy" ), "The late events didn't cost the new weapon its deploy" );
		Assert.IsFalse( Gesture( body, "b_reload_cancel" ) );
		Assert.IsFalse( Gesture( body, "b_attack" ) );
		Assert.IsFalse( Gesture( body, "b_reload" ) );
		Assert.AreEqual( "rifle", Hold( body ) );

		second.Post( WeaponEvents.Reload );
		scene.GameTick();
		Assert.IsTrue( Gesture( body, "b_reload" ) );
	}

	[TestMethod]
	public void ALegacyBodyOnlyTakesTriggersFromTheHeldWeapon()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (player, body) = CreatePlayer( scene, graph: false );
		var inventory = player.Components.GetOrCreate<BaseInventoryComponent>();
		var first = Add<TestWeapon>( scene, player.GameObject, "pistol" );
		var second = Add<OtherWeapon>( scene, player.GameObject, "rifle" );

		inventory.Switch( first );
		inventory.Switch( second );
		first.Post( WeaponEvents.Attack );
		Assert.IsFalse( body.ContainsLegacyParameter( "b_attack" ) );

		second.Post( WeaponEvents.Attack );
		Assert.IsTrue( body.ContainsLegacyParameter( "b_attack" ) );
	}

	[TestMethod]
	public void PendingGesturesSurviveAGraphThatIsNotRunning()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (player, body) = CreatePlayer( scene, graph: true );
		var inventory = player.Components.GetOrCreate<BaseInventoryComponent>();
		var weapon = Add<TestWeapon>( scene, player.GameObject, "pistol" );

		body.Enabled = false;
		Assert.IsFalse( body.Animation.IsValid );

		inventory.Switch( weapon );
		for ( int i = 0; i < 3; i++ ) scene.GameTick();

		body.Enabled = true;

		var deployed = false;
		for ( int i = 0; i < 4 && !deployed; i++ )
		{
			scene.GameTick();
			deployed = Gesture( body, "b_deploy" );
		}

		Assert.IsTrue( deployed, "The deploy waited for the graph" );
	}

	[TestMethod]
	public void DeployIsPostedOnceTheWeaponIsSetUp()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (player, _) = CreatePlayer( scene, graph: true );
		var inventory = player.Components.GetOrCreate<BaseInventoryComponent>();
		var listener = player.Components.Create<Listener>();
		var weapon = Add<TestWeapon>( scene, player.GameObject, "pistol" );
		weapon.ViewModelPrefab = scene.CreateObject( false );
		weapon.DeployTime = 1f;

		var hadViewModel = false;
		listener.OnEvent = ( w, name ) =>
		{
			if ( name == WeaponEvents.Deploy )
				hadViewModel = w.ViewModel.IsValid() && !w.CanPrimaryAttack();
		};

		inventory.Switch( weapon );
		Assert.IsTrue( hadViewModel, "Listeners see a weapon that has its view model and its deploy cooldown" );

		listener.Log.Clear();
		inventory.Switch( null, allowHolster: true );
		CollectionAssert.AreEqual( new[] { WeaponEvents.Holster }, listener.Log );
	}

	[TestMethod]
	public void ADeployListenerMaySwitchAwayOrDestroyTheWeapon()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (player, body) = CreatePlayer( scene, graph: true );
		var inventory = player.Components.GetOrCreate<BaseInventoryComponent>();
		var listener = player.Components.Create<Listener>();
		var first = Add<TestWeapon>( scene, player.GameObject, "pistol" );
		var second = Add<OtherWeapon>( scene, player.GameObject, "rifle" );
		first.ViewModelPrefab = scene.CreateObject( false );

		listener.OnEvent = ( w, name ) =>
		{
			if ( w == first && name == WeaponEvents.Deploy )
				inventory.Switch( second );
		};

		inventory.Switch( first );
		Assert.AreEqual( second, inventory.ActiveItem );
		Assert.IsFalse( first.ViewModel.IsValid(), "Nothing is set up on the weapon after it was put away" );
		Assert.IsFalse( first.GameObject.Enabled );

		scene.GameTick();
		Assert.AreEqual( "rifle", Hold( body ) );

		listener.OnEvent = ( w, name ) =>
		{
			if ( name == WeaponEvents.Deploy )
				w.GameObject.Destroy();
		};

		inventory.Switch( first );
		scene.GameTick();
		Assert.IsFalse( first.IsValid() );
	}

	[TestMethod]
	public void ADeployThatArrivesAfterASwitchLeavesTheNewWeaponItsDeploy()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (player, body) = CreatePlayer( scene, graph: true );
		var inventory = player.Components.GetOrCreate<BaseInventoryComponent>();
		var first = Add<TestWeapon>( scene, player.GameObject, "pistol" );
		var second = Add<OtherWeapon>( scene, player.GameObject, "rifle" );

		// On the weapon itself, so it hears the deploy before the player does and switches while it's still on its way there.
		var listener = first.GameObject.Components.Create<Listener>();
		listener.OnEvent = ( w, name ) =>
		{
			if ( w == first && name == WeaponEvents.Deploy )
				inventory.Switch( second );
		};

		BaseInventoryItem activeWhenThePlayerHeard = null;
		player.Components.Create<Listener>().OnEvent = ( w, name ) =>
		{
			if ( w == first && name == WeaponEvents.Deploy )
				activeWhenThePlayerHeard = inventory.ActiveItem;
		};

		inventory.Switch( first );
		Assert.AreEqual( second, inventory.ActiveItem );
		Assert.AreEqual( second, activeWhenThePlayerHeard, "The first weapon's deploy reached the player after the switch" );

		scene.GameTick();
		Assert.IsTrue( Gesture( body, "b_deploy" ), "The first weapon's late deploy didn't take the second one's" );
		Assert.AreEqual( "rifle", Hold( body ) );
	}

	[TestMethod]
	public void AHolsterOnlyClearsItsOwnWeaponsGestures()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (player, body) = CreatePlayer( scene, graph: true );
		var inventory = player.Components.GetOrCreate<BaseInventoryComponent>();
		var first = Add<TestWeapon>( scene, player.GameObject, "pistol" );
		var second = Add<OtherWeapon>( scene, player.GameObject, "rifle" );

		inventory.Switch( first );
		scene.GameTick();
		inventory.Switch( second );
		scene.GameTick();
		Assert.IsTrue( Gesture( body, "b_deploy" ) );

		// The weapon that was put away holsters once more: the one in hand keeps the gesture it is playing and the one it has pending.
		second.Post( WeaponEvents.Reload );
		first.Post( WeaponEvents.Holster );
		Assert.IsTrue( Gesture( body, "b_deploy" ) );

		scene.GameTick();
		Assert.IsTrue( Gesture( body, "b_reload" ) );
		Assert.AreEqual( "rifle", Hold( body ) );

		// A holster that arrives for the weapon in hand is stale and changes nothing.
		second.Post( WeaponEvents.Holster );
		Assert.IsTrue( Gesture( body, "b_reload" ) );
		Assert.AreEqual( "rifle", Hold( body ) );
	}

	[TestMethod]
	public void AttackIsPostedToTheHolder()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (player, _) = CreatePlayer( scene, graph: true );
		var listener = player.Components.Create<Listener>();
		var weapon = Give<TestWeapon>( scene, player.GameObject, "pistol" );

		weapon.Shoot();
		CollectionAssert.AreEqual( new[] { WeaponEvents.Attack }, listener.Log );
	}

	[TestMethod]
	public void WithoutAnInventoryNothingIsSeededWhoeverHoldsTheWeapon()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (player, _) = CreatePlayer( scene, graph: false );

		var weapon = Give<TestWeapon>( scene, player.GameObject );
		weapon.PrimaryAmmoType = TestAmmoResource.Pistol;
		scene.GameTick();

		Assert.AreEqual( -1, weapon.Clip1 );
		Assert.AreEqual( 0, weapon.Ammo1 );
		Assert.IsFalse( weapon.HasPrimaryAmmo() );

		// Another holder is no different: the magazines and the reserve are its inventory's business.
		var holder = scene.CreateObject().Components.Create<TestHolder>();
		var held = Give<TestWeapon>( scene, holder.GameObject );
		held.PrimaryAmmoType = TestAmmoResource.Pistol;
		scene.GameTick();

		Assert.IsTrue( held.IsHeld );
		Assert.AreEqual( -1, held.Clip1 );
		Assert.AreEqual( 0, held.Ammo1 );
		Assert.IsFalse( held.HasPrimaryAmmo() );
		Assert.IsFalse( held.TakePrimaryAmmo() );
	}

	[TestMethod]
	public void AHolderCarriedByThePlayerGetsNoViewModelAndDoesNotAnimateThePlayer()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (player, body) = CreatePlayer( scene, graph: true );
		var before = Hold( body );

		var carried = scene.CreateObject();
		carried.Parent = player.GameObject;
		var holder = carried.Components.Create<TestHolder>();

		var weapon = Add<TestWeapon>( scene, carried, "pistol" );
		weapon.ViewModelPrefab = scene.CreateObject( false );
		Assert.AreEqual( holder, weapon.Holder );
		Assert.AreEqual( player, weapon.Owner );

		carried.GetComponent<BaseInventoryComponent>().Switch( weapon );
		weapon.Shoot();
		scene.GameTick();

		Assert.IsTrue( weapon.IsActive );
		Assert.IsFalse( weapon.ViewModel.IsValid(), "Only a player holding the weapon itself sees a view model" );
		Assert.AreEqual( before, Hold( body ) );
		Assert.IsFalse( Gesture( body, "b_deploy" ) );
		Assert.IsFalse( Gesture( body, "b_attack" ) );

		var own = Add<TestWeapon>( scene, player.GameObject, "rifle" );
		own.ViewModelPrefab = weapon.ViewModelPrefab;
		player.GetComponent<BaseInventoryComponent>().Switch( own );
		Assert.IsTrue( own.ViewModel.IsValid() );
	}
}

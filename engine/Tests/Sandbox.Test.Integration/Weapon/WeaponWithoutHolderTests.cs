using Sandbox.Network;
using SceneTests;
using System;
using TestWeapon = WeaponTests.WeaponHolderTests.TestWeapon;

namespace WeaponTests;

#pragma warning disable CS0618

/// <summary>
/// Games hold weapons their own way: under a bot or an NPC of theirs that is neither a player nor a holder the
/// engine knows. Such a weapon is unheld to the engine, fires from its own muzzle for itself, gets no models, and
/// animates only the body its own code hands to it.
/// </summary>
[TestClass]
public class WeaponWithoutHolderTests
{
	public sealed class AttributedWeapon : BaseCombatWeapon
	{
		public GameObject CurrentAttacker => Attacker;
		public Action Primary;
		public Action Secondary;
		public override void PrimaryAttack() => Primary?.Invoke();
		public override void SecondaryAttack() => Secondary?.Invoke();
	}

	[TestMethod]
	public void RemoteAttackerIsScopedWithoutCreatingAHolder()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var weapon = scene.CreateObject().Components.Create<AttributedWeapon>();
		var first = scene.CreateObject();
		var second = scene.CreateObject();
		weapon.Secondary = () => Assert.AreEqual( second, weapon.CurrentAttacker );
		weapon.Primary = () =>
		{
			Assert.AreEqual( first, weapon.CurrentAttacker );
			Assert.IsFalse( weapon.IsHeld );
			Assert.IsTrue( weapon.FireSecondary( second ) );
			Assert.AreEqual( first, weapon.CurrentAttacker );
		};

		Assert.IsTrue( weapon.FirePrimary( first ) );
		Assert.AreEqual( weapon.GameObject, weapon.CurrentAttacker );
		Assert.IsFalse( weapon.FirePrimary( second ), "The explicit overload still enforces cooldown" );
		Assert.AreEqual( weapon.GameObject, weapon.CurrentAttacker );
	}

	[TestMethod]
	public void ThrowingAttackDoesNotKeepItsRemoteAttacker()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var weapon = scene.CreateObject().Components.Create<AttributedWeapon>();
		weapon.Primary = () => throw new InvalidOperationException();
		Assert.ThrowsException<InvalidOperationException>( () => weapon.FirePrimary( scene.CreateObject() ) );
		Assert.AreEqual( weapon.GameObject, weapon.CurrentAttacker );
	}

	/// <summary>A game's weapon for its own bots: it poses the bot's body and puts the world model on its skeleton itself.</summary>
	public sealed class BotWeapon : TestWeapon
	{
		public SkinnedModelRenderer Body;

		protected override void OnUpdate()
		{
			base.OnUpdate();

			if ( Body.IsValid() )
				UpdateBodyAnimations( Body );
		}

		protected override void CreateWorldModel()
		{
			DestroyWorldModel();

			if ( WorldModelPrefab is null || !Body.IsValid() )
				return;

			WorldModel = WorldModelPrefab.Clone( new CloneConfig
			{
				Parent = Body.GetBoneObject( HoldBone ) ?? Body.GameObject,
				StartEnabled = true,
				Transform = global::Transform.Zero
			} );
		}
	}

	/// <summary>Something to shoot that remembers what hit it.</summary>
	public sealed class Target : Component, Component.IDamageable
	{
		public int Hits;
		public GameObject Attacker;
		public GameObject Weapon;

		public void OnDamage( in DamageInfo damage )
		{
			Hits++;
			Attacker = damage.Attacker;
			Weapon = damage.Weapon;
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

	/// <summary>A game's own bot: a body, no player controller and nothing that is a holder.</summary>
	static (GameObject Bot, SkinnedModelRenderer Body) CreateBot( Scene scene, bool graph )
	{
		var go = scene.CreateObject();

		var bodyGo = scene.CreateObject();
		bodyGo.Parent = go;
		var body = bodyGo.Components.Create<SkinnedModelRenderer>();
		body.Model = Model.Load( "models/citizen_human/human.vmdl" );

		if ( graph )
			body.Animation.Graph = Sandbox.Animation.Graph.Load( "models/citizen_human/animgraphs/human.ag" );

		scene.GameTick();
		Assert.AreEqual( graph, body.Animation.IsValid, "The body runs the graph the test asked for" );
		return (go, body);
	}

	/// <summary>A weapon under <paramref name="bot"/>: in an inventory on it and deployed, or just parented and enabled.</summary>
	static T Arm<T>( Scene scene, GameObject bot, bool inventory, Action<T> setup = null ) where T : BaseCombatWeapon, new()
	{
		var weapon = scene.CreateObject().Components.Create<T>();
		weapon.HoldType = "pistol";
		weapon.Ballistics = weapon.Ballistics with { SpreadBase = Vector2.Zero, SpreadGrowth = Vector2.Zero };
		weapon.WorldModelPrefab = scene.CreateObject( false );
		weapon.ViewModelPrefab = scene.CreateObject( false );
		setup?.Invoke( weapon );

		if ( !inventory )
		{
			weapon.GameObject.Parent = bot;
			weapon.GameObject.LocalTransform = global::Transform.Zero;
			return weapon;
		}

		var items = bot.Components.GetOrCreate<BaseInventoryComponent>();
		Assert.IsTrue( items.Add( weapon ) );
		items.Switch( weapon );
		Assert.IsTrue( weapon.IsActive );
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

	[TestMethod]
	public void TheEngineLeavesTheBotAndItsBodyAlone()
	{
		foreach ( var inventory in new[] { false, true } )
		{
			var scene = new Scene();
			using var scope = scene.Push();
			var (bot, body) = CreateBot( scene, graph: true );
			body.Animation.Parameters.Set( "holdtype", "rifle" );

			var weapon = Arm<TestWeapon>( scene, bot, inventory );
			weapon.Shoot();
			scene.GameTick();
			scene.GameTick();

			Assert.IsNull( weapon.Owner );
			Assert.IsNull( weapon.Holder );
			Assert.IsFalse( weapon.IsHeld, "A weapon is held by a player or a holder, not by whatever it sits under" );
			Assert.IsNull( weapon.HolderRenderer );
			Assert.IsFalse( weapon.WorldModel.IsValid(), "A world model only comes from a holder's body" );
			Assert.IsFalse( weapon.ViewModel.IsValid() );
			Assert.AreEqual( "rifle", Hold( body ), "The body is the game's to pose" );
			Assert.IsFalse( Gesture( body, "b_deploy" ) );
			Assert.IsFalse( Gesture( body, "b_attack" ) );
		}
	}

	[TestMethod]
	public void AGamesOwnWeaponPosesTheBodyItHandsIn()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (bot, body) = CreateBot( scene, graph: true );

		var weapon = Arm<BotWeapon>( scene, bot, inventory: true, x =>
		{
			x.Body = body;
			x.Handedness = BaseCombatWeapon.WeaponHandedness.Right;
		} );
		scene.GameTick();

		Assert.AreEqual( "pistol", Hold( body ) );
		Assert.IsTrue( Gesture( body, "b_deploy" ) );
		Assert.IsTrue( weapon.WorldModel.IsValid(), "The game's own world model spawn runs when the weapon is deployed" );

		scene.GameTick();
		Assert.IsFalse( Gesture( body, "b_deploy" ), "A gesture plays for one update" );

		weapon.Shoot();
		scene.GameTick();
		Assert.IsTrue( Gesture( body, "b_attack" ) );
		scene.GameTick();
		Assert.IsFalse( Gesture( body, "b_attack" ) );

		weapon.Post( WeaponEvents.Reload );
		scene.GameTick();
		Assert.IsTrue( Gesture( body, "b_reload" ) );

		weapon.Post( WeaponEvents.Reload );
		weapon.Post( WeaponEvents.ReloadCancel );
		weapon.Shoot();
		scene.GameTick();
		Assert.IsFalse( Gesture( body, "b_reload" ) );
		Assert.IsTrue( Gesture( body, "b_reload_cancel" ) );
		Assert.IsTrue( Gesture( body, "b_attack" ) );

		scene.GameTick();
		Assert.IsFalse( Gesture( body, "b_reload_cancel" ) );
		Assert.IsFalse( Gesture( body, "b_attack" ) );

		// Put away: the model goes, the hold stays the game's to clear
		bot.GetComponent<BaseInventoryComponent>().Switch( null, allowHolster: true );
		Assert.IsFalse( weapon.WorldModel.IsValid() );
		Assert.AreEqual( "pistol", Hold( body ) );
	}

	[TestMethod]
	public void AGamesOwnWeaponPosesALegacyBody()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (bot, body) = CreateBot( scene, graph: false );
		Assert.AreEqual( "none", Hold( body ) );

		Arm<BotWeapon>( scene, bot, inventory: false, x =>
		{
			x.Body = body;
			x.Handedness = BaseCombatWeapon.WeaponHandedness.Right;
		} );
		scene.GameTick();

		Assert.AreEqual( "pistol", Hold( body ) );
		Assert.AreEqual( 1, body.GetInt( "holdtype_handedness" ) );
	}

	[TestMethod]
	public void ItFiresFromItsOwnMuzzleForItselfAndSpendsNothing()
	{
		foreach ( var inventory in new[] { false, true } )
		{
			var scene = new Scene();
			using var scope = scene.Push();
			var (bot, _) = CreateBot( scene, graph: false );

			var targetGo = scene.CreateObject();
			targetGo.WorldPosition = new Vector3( 200, 0, 0 );
			targetGo.Components.Create<BoxCollider>().Scale = new Vector3( 50 );
			var target = targetGo.Components.Create<Target>();

			var weapon = Arm<TestWeapon>( scene, bot, inventory );
			var clip = weapon.Clip1;

			weapon.PrimaryAttack();

			Assert.AreEqual( 1, target.Hits, "The shot goes along the weapon's own forward" );
			Assert.AreEqual( weapon.GameObject, target.Attacker, "Nothing holds it, so the weapon is credited itself" );
			Assert.AreEqual( weapon.GameObject, target.Weapon );
			Assert.AreEqual( clip, weapon.Clip1, "An unheld weapon has no ammunition to spend" );

			// The trigger works the same once the deploy delay is over
			if ( !inventory )
			{
				Assert.IsTrue( weapon.FirePrimary() );
				Assert.AreEqual( 2, target.Hits );
			}
		}
	}
}

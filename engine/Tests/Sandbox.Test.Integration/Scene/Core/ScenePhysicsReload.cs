using Sandbox.Engine;
using System;

namespace SceneTests.Core;

[TestClass]
[DoNotParallelize]
public class ScenePhysicsReloadTest
{
	static SceneLoadOptions MakeOptions( bool deleteEverything = false )
	{
		var file = new SceneFile { GameObjects = [] };
		file.RegisterWeakResourceId( $"physics_reload_{Guid.NewGuid():N}.scene" );

		var options = new SceneLoadOptions
		{
			ShowLoadingScreen = false,
			DeleteEverything = deleteEverything
		};
		options.SetScene( file );
		return options;
	}

	static Rigidbody CreateBody( Scene scene )
	{
		var go = scene.CreateObject();
		var body = go.Components.Create<Rigidbody>();
		body.Gravity = false;
		go.Components.Create<BoxCollider>().Scale = new Vector3( 10 );
		return body;
	}

	[DataTestMethod]
	[DataRow( ScenePhysicsMode.Physics3D )]
	[DataRow( ScenePhysicsMode.Physics2D )]
	public void ChangeSceneRecreatesWorldAndKeepsSimulating( ScenePhysicsMode mode )
	{
		var previousStandalone = Application.IsStandalone;
		var previousInstance = IGameInstance.Current;
		var previousSystem = Networking.System;
		var previousScene = Game.ActiveScene;
		var scene = new Scene { PhysicsMode = mode, WantsSystemScene = false };
		using var scope = scene.Push();
		try
		{
			Application.IsStandalone = true;
			IGameInstance.Current = new GameInstance( "test.game", GameLoadingFlags.Host );
			Networking.System = null;
			Game.ActiveScene = scene;

			var survivor = scene.CreateObject();
			survivor.Flags |= GameObjectFlags.DontDestroyOnLoad;
			var oldBody = CreateBody( scene );
			oldBody.Velocity = new Vector3( 10, 0, 0 );
			scene.GameTick();
			Assert.IsTrue( oldBody.WorldPosition.x > 0 );
			var oldWorld = scene.PhysicsWorld;

			Assert.IsTrue( Game.ChangeScene( MakeOptions() ) );
			Assert.AreSame( scene, Game.ActiveScene );
			Assert.IsTrue( survivor.IsValid() );
			Assert.IsFalse( oldBody.IsValid() );
			Assert.IsFalse( oldWorld.IsValid(), "A full reload must destroy the cleared physics world" );
			Assert.IsFalse( scene.HasPhysicsWorld, "The replacement world should be created on demand" );

			var newBody = CreateBody( scene );
			newBody.Velocity = new Vector3( 10, 0, 0 );
			Assert.AreNotSame( oldWorld, scene.PhysicsWorld );
			for ( int i = 0; i < 10; i++ ) scene.GameTick();
			Assert.IsTrue( newBody.WorldPosition.x > 1, "The physics system must step the replacement world" );
		}
		finally
		{
			scene.Destroy();
			Game.ActiveScene = previousScene;
			Application.IsStandalone = previousStandalone;
			IGameInstance.Current = previousInstance;
			Networking.System = previousSystem;
		}
	}

	[DataTestMethod]
	[DataRow( ScenePhysicsMode.Physics3D )]
	[DataRow( ScenePhysicsMode.Physics2D )]
	public void PersistentPhysicsPreventsResetUntilDeleted( ScenePhysicsMode mode )
	{
		var scene = new Scene { PhysicsMode = mode, WantsSystemScene = false };
		using var scope = scene.Push();
		try
		{
			var rb = CreateBody( scene );
			rb.GameObject.Flags |= GameObjectFlags.DontDestroyOnLoad;
			rb.Velocity = new Vector3( 12, 34, 0 );
			var body = rb.PhysicsBody;
			var world = scene.PhysicsWorld;

			Assert.IsTrue( scene.Load( MakeOptions() ) );
			Assert.AreSame( world, scene.PhysicsWorld );
			Assert.IsTrue( world.IsValid() );
			Assert.AreSame( body, rb.PhysicsBody );
			Assert.AreEqual( new Vector3( 12, 34, 0 ), rb.Velocity );

			Assert.IsTrue( scene.Load( MakeOptions( deleteEverything: true ) ) );
			Assert.IsFalse( rb.IsValid() );
			Assert.IsFalse( body.IsValid() );
			Assert.IsFalse( world.IsValid(), "Deleting the surviving physics must allow a fresh world" );
			Assert.AreNotSame( world, scene.PhysicsWorld );
		}
		finally
		{
			scene.Destroy();
		}
	}

	[DataTestMethod]
	[DataRow( ScenePhysicsMode.Physics3D, ReloadStage.PrePhysics )]
	[DataRow( ScenePhysicsMode.Physics2D, ReloadStage.PrePhysics )]
	[DataRow( ScenePhysicsMode.Physics3D, ReloadStage.Collision )]
	[DataRow( ScenePhysicsMode.Physics2D, ReloadStage.Collision )]
	[DataRow( ScenePhysicsMode.Physics3D, ReloadStage.Trigger )]
	[DataRow( ScenePhysicsMode.Physics2D, ReloadStage.Trigger )]
	public void ReloadDuringPhysicsCallbacks( ScenePhysicsMode mode, ReloadStage stage )
	{
		var scene = new Scene { PhysicsMode = mode, WantsSystemScene = false };
		using var scope = scene.Push();
		try
		{
			var first = CreateBody( scene );
			var second = scene.CreateObject();
			second.WorldPosition = new Vector3( 5, 0, 0 );
			var collider = second.Components.Create<BoxCollider>();
			collider.Scale = new Vector3( 10 );
			collider.IsTrigger = stage == ReloadStage.Trigger;

			var oldWorld = scene.PhysicsWorld;
			var probeObject = stage == ReloadStage.Trigger ? second : first.GameObject;
			var probe = probeObject.Components.Create<ReloadProbe>();
			probe.Stage = stage;
			bool loaded = false;
			Rigidbody newBody = null;
			probe.Reload = () =>
			{
				loaded = scene.Load( MakeOptions() );
				newBody = CreateBody( scene );
				newBody.Velocity = new Vector3( 10, 0, 0 );
			};

			for ( int i = 0; i < 10; i++ ) scene.GameTick();
			Assert.IsTrue( loaded, "The physics callback must execute and load the scene" );
			Assert.IsFalse( oldWorld.IsValid(), "The callback must replace the old world" );
			Assert.IsTrue( newBody.IsValid() );
			Assert.IsTrue( newBody.WorldPosition.x > 1, "Simulation must resume on the replacement world" );
		}
		finally
		{
			scene.Destroy();
		}
	}

	[TestMethod]
	public void ReloadRepeatsPhysicsSimulationBitForBit()
	{
		var scene = new Scene { WantsSystemScene = false };
		using var scope = scene.Push();
		try
		{
			var controller = scene.CreateObject();
			controller.Flags |= GameObjectFlags.DontDestroyOnLoad;
			var options = MakeOptions();
			var expected = new uint[250][];

			for ( int run = 0; run < 5; run++ )
			{
				Assert.IsTrue( scene.Load( options ) );
				var bodies = CreateDeterminismRig( scene );
				scene.PhysicsWorld.SubSteps = 1;

				for ( int step = 0; step < expected.Length; step++ )
				{
					using var time = Time.PushScope( (step + 1) * 0.02, 0.02 );
					scene.InternalFixedUpdate();
					var actual = CapturePhysicsState( bodies );

					if ( run == 0 )
					{
						expected[step] = actual;
						continue;
					}

					for ( int i = 0; i < actual.Length; i++ )
					{
						if ( expected[step][i] == actual[i] ) continue;

						Assert.Fail( $"Run {run}, step {step}, {bodies[i / PhysicsStateFields.Length].GameObject.Name}.{PhysicsStateFields[i % PhysicsStateFields.Length]}: expected 0x{expected[step][i]:x8}, got 0x{actual[i]:x8}" );
					}
				}

				Assert.IsTrue( bodies[0].WorldPosition.z < 700, "The rig must actually simulate, not just repeat an unchanged state" );
			}
		}
		finally
		{
			scene.Destroy();
		}
	}

	static Rigidbody[] CreateDeterminismRig( Scene scene )
	{
		var floor = scene.CreateObject();
		floor.WorldPosition = new Vector3( 0, 0, -16 );
		var floorCollider = floor.Components.Create<BoxCollider>();
		floorCollider.Scale = new Vector3( 1200, 1200, 32 );
		floorCollider.Static = true;

		var bodies = new Rigidbody[43];
		for ( int i = 0; i < bodies.Length; i++ )
		{
			var go = scene.CreateObject();
			if ( i < 3 )
			{
				go.Name = $"Sphere{i}";
				go.WorldPosition = new Vector3( 105, 0, 700 + i * 90 );
				go.Components.Create<SphereCollider>().Radius = 40;
				go.WorldScale = 3.2f;
			}
			else
			{
				int crate = i - 3;
				go.Name = $"Crate{crate}";
				go.WorldPosition = new Vector3( (crate / 10) * 70, 0, 40 + (crate % 10) * 62 );
				go.Components.Create<BoxCollider>().Scale = new Vector3( 60 );
			}

			bodies[i] = go.Components.Create<Rigidbody>();
			bodies[i].EnableImpactDamage = false;
		}
		return bodies;
	}

	static readonly string[] PhysicsStateFields =
	[
		"position.x", "position.y", "position.z",
		"rotation.x", "rotation.y", "rotation.z", "rotation.w",
		"velocity.x", "velocity.y", "velocity.z",
		"angularVelocity.x", "angularVelocity.y", "angularVelocity.z",
		"sleeping"
	];

	static uint[] CapturePhysicsState( Rigidbody[] bodies )
	{
		var result = new uint[bodies.Length * PhysicsStateFields.Length];
		int index = 0;
		foreach ( var rb in bodies )
		{
			var body = rb.PhysicsBody;
			var position = body.Position;
			var rotation = body.Rotation;
			var velocity = body.Velocity;
			var angularVelocity = body.AngularVelocity;
			ReadOnlySpan<float> values =
			[
				position.x, position.y, position.z,
				rotation.x, rotation.y, rotation.z, rotation.w,
				velocity.x, velocity.y, velocity.z,
				angularVelocity.x, angularVelocity.y, angularVelocity.z,
				body.Sleeping ? 1f : 0f
			];
			foreach ( var value in values )
				result[index++] = BitConverter.SingleToUInt32Bits( value );
		}
		return result;
	}

	public enum ReloadStage
	{
		PrePhysics,
		Collision,
		Trigger
	}

	public sealed class ReloadProbe : Component, IScenePhysicsEvents, Component.ICollisionListener, Component.ITriggerListener
	{
		public Action Reload;
		public ReloadStage Stage;

		public void PrePhysicsStep() => Invoke( ReloadStage.PrePhysics );
		public void OnCollisionStart( Collision collision ) => Invoke( ReloadStage.Collision );
		public void OnTriggerEnter( Collider self, Collider other ) => Invoke( ReloadStage.Trigger );

		void Invoke( ReloadStage stage )
		{
			if ( Stage != stage ) return;

			var action = Reload;
			Reload = null;
			action?.Invoke();
		}
	}
}

using System;

namespace SceneTests.GameObjects;

[TestClass]
public class GameObjectSystemSerializationTest
{
	private Sandbox.Internal.TypeLibrary _oldTypeLibrary;

	[TestInitialize]
	public void TestInitialize()
	{
		_oldTypeLibrary = Game.TypeLibrary;

		var typeLibrary = new Sandbox.Internal.TypeLibrary();
		typeLibrary.AddAssembly( typeof( Sandbox.Scene ).Assembly, false );
		typeLibrary.AddAssembly( typeof( GameObjectSystemDefaultTestSystem ).Assembly, false );

		Game.TypeLibrary = typeLibrary;
	}

	[TestCleanup]
	public void Cleanup()
	{
		Game.TypeLibrary = _oldTypeLibrary;
	}

	[TestMethod]
	public void DefaultValue_IsNotSerializedAsSceneOverride()
	{
		var scene = new Sandbox.Scene();

		try
		{
			var system = scene.GetSystem<GameObjectSystemDefaultTestSystem>();
			Assert.IsNotNull( system );
			Assert.AreEqual( 123, system.MyInt );

			var serialized = scene.Serialize();
			var properties = serialized["Properties"].AsObject();

			if ( properties.TryGetPropertyValue( "GameObjectSystems", out var systemsNode ) )
			{
				Assert.IsFalse( systemsNode.AsObject().ContainsKey( typeof( GameObjectSystemDefaultTestSystem ).FullName ) );
			}

			system.MyInt = 456;

			serialized = scene.Serialize();
			properties = serialized["Properties"].AsObject();
			var systems = properties["GameObjectSystems"].AsObject();
			var overrides = systems[typeof( GameObjectSystemDefaultTestSystem ).FullName].AsObject();

			Assert.AreEqual( 456, overrides[nameof( GameObjectSystemDefaultTestSystem.MyInt )].GetValue<int>() );
		}
		finally
		{
			scene.Destroy();
		}
	}

	/// <summary>
	/// The serialized GameObjectSystems block should be ordered by type name so it
	/// doesn't reorder on every save and produce noisy scene diffs.
	/// </summary>
	[TestMethod]
	public void SystemsAreSerializedInStableOrder()
	{
		var scene = new Sandbox.Scene();

		try
		{
			// Set a non-default value so each system appears in the block.
			scene.GetSystem<ZebraOrderTestSystem>().Value = 1;
			scene.GetSystem<AppleOrderTestSystem>().Value = 1;
			scene.GetSystem<MangoOrderTestSystem>().Value = 1;

			var serialized = scene.Serialize();
			var systems = serialized["Properties"].AsObject()["GameObjectSystems"].AsObject();

			var keys = systems.Select( x => x.Key ).ToArray();
			var expected = keys.OrderBy( x => x, StringComparer.Ordinal ).ToArray();

			CollectionAssert.AreEqual( expected, keys,
				"GameObjectSystems should be serialized in ordinal order, was: " + string.Join( ", ", keys ) );

			var keysAgain = scene.Serialize()["Properties"].AsObject()["GameObjectSystems"]
				.AsObject().Select( x => x.Key ).ToArray();

			CollectionAssert.AreEqual( keys, keysAgain, "system order should be stable across saves" );
		}
		finally
		{
			scene.Destroy();
		}
	}
}

[Expose]
public sealed class GameObjectSystemDefaultTestSystem : GameObjectSystem
{
	public GameObjectSystemDefaultTestSystem( Sandbox.Scene scene ) : base( scene )
	{
	}

	[Property, DefaultValue( 123 )]
	public int MyInt { get; set; } = 123;
}

[Expose]
public sealed class ZebraOrderTestSystem : GameObjectSystem
{
	public ZebraOrderTestSystem( Sandbox.Scene scene ) : base( scene ) { }

	[Property] public int Value { get; set; }
}

[Expose]
public sealed class AppleOrderTestSystem : GameObjectSystem
{
	public AppleOrderTestSystem( Sandbox.Scene scene ) : base( scene ) { }

	[Property] public int Value { get; set; }
}

[Expose]
public sealed class MangoOrderTestSystem : GameObjectSystem
{
	public MangoOrderTestSystem( Sandbox.Scene scene ) : base( scene ) { }

	[Property] public int Value { get; set; }
}

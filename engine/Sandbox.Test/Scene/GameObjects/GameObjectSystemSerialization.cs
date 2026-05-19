namespace GameObjects;

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

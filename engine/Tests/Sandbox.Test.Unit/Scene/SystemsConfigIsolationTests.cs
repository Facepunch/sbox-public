namespace SceneTests;

[TestClass]
[DoNotParallelize]
public class SystemsConfigIsolationTests
{
	static (TypeDescription System, PropertyDescription Property) GetDataProperty()
	{
		var typeLibrary = new Sandbox.Internal.TypeLibrary();
		typeLibrary.AddAssembly( typeof( IsolationProbeSystem ).Assembly, false );

		var systemType = typeLibrary.GetType<IsolationProbeSystem>();
		return (systemType, systemType.GetProperty( nameof( IsolationProbeSystem.Data ) ));
	}

	[TestMethod]
	public void SharedValuesStaySerialized()
	{
		var (systemType, property) = GetDataProperty();
		var typeLibrary = new Sandbox.Internal.TypeLibrary();
		typeLibrary.AddAssembly( typeof( RehydrateProbeSystem ).Assembly, false );
		var planting = typeLibrary.GetType<RehydrateProbeSystem>().GetProperty( nameof( RehydrateProbeSystem.Data ) );
		var config = new SystemsConfig();

		config.SetPropertyValue( systemType, property, new PlantedProbeData { Value = "hello" } );
		config.TryGetPropertyValue( systemType, planting, out _ );
		PlantedProbeData.GetterCalls = 0;

		Assert.IsTrue( config.TryGetPropertyValue( systemType, property, out var value ) );
		Assert.AreEqual( typeof( IsolationProbeData ), value.GetType(), "The shared config handed out a live game type" );
		Assert.AreEqual( "hello", ((IsolationProbeData)value).Value );
		Assert.AreEqual( 0, PlantedProbeData.GetterCalls );

		config.SetPropertyValue( systemType, property, new UnrelatedProbeValue() );
		UnrelatedProbeValue.GetterCalls = 0;
		config.TryGetPropertyValue( systemType, property, out _ );
		Assert.AreEqual( 0, UnrelatedProbeValue.GetterCalls, "A stored game object was serialized again later" );

		config.SetPropertyValue( systemType, property, new IsolationProbeData { Value = "saved" } );
		var loaded = new SystemsConfig();
		loaded.Deserialize( config.Serialize().ToJsonString() );
		Assert.IsTrue( loaded.TryGetPropertyValue( systemType, property, out var reloaded ), "Systems didn't survive a save and load" );
		Assert.AreEqual( "saved", ((IsolationProbeData)reloaded).Value );
	}

	[TestMethod]
	public void GameTypesCannotReachTheSharedDictionary()
	{
		var typeLibrary = new Sandbox.Internal.TypeLibrary();
		typeLibrary.AddAssembly( typeof( PlantedSystemsConfig ).Assembly, true );

		Assert.IsNull( typeLibrary.GetType<PlantedSystemsConfig>().GetProperty( "Systems" ) );
	}

	[TestMethod]
	public void GetDoesNotHandOutCachedSubtype()
	{
		ProjectSettings.ClearCache();

		try
		{
			ProjectSettings.Get<PlantedSystemsConfig>( "Systems.config" );

			Assert.AreEqual( typeof( SystemsConfig ), ProjectSettings.Systems.GetType() );
		}
		finally
		{
			ProjectSettings.ClearCache();
		}
	}
}

public class PlantedSystemsConfig : SystemsConfig
{
}


public class IsolationProbeData
{
	public virtual string Value { get; set; }
}

public sealed class PlantedProbeData : IsolationProbeData
{
	public static int GetterCalls;

	string _value;

	public override string Value
	{
		get
		{
			GetterCalls++;
			return _value;
		}
		set => _value = value;
	}
}

public sealed class UnrelatedProbeValue
{
	public static int GetterCalls;

	public string Value
	{
		get
		{
			GetterCalls++;
			return "planted";
		}
	}
}

[Expose]
public sealed class IsolationProbeSystem : GameObjectSystem
{
	public IsolationProbeSystem( Sandbox.Scene scene ) : base( scene )
	{
	}

	[Property]
	public IsolationProbeData Data { get; set; }
}

[Expose]
public sealed class RehydrateProbeSystem : GameObjectSystem
{
	public RehydrateProbeSystem( Sandbox.Scene scene ) : base( scene )
	{
	}

	[Property]
	public PlantedProbeData Data { get; set; }
}

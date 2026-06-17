using System;

namespace SceneTests.Core;

/// <summary>
/// Component with a serialized property so load/save tests can prove component data
/// survives a round trip.
/// </summary>
public class LoadSavePersistComponent : Component
{
	[Property] public int Number { get; set; }
}

/// <summary>
/// Component recording the <see cref="ISceneLoadingEvents"/> callbacks it receives
/// during a scene load.
/// </summary>
public class LoadEventsProbe : Component, ISceneLoadingEvents
{
	public int BeforeLoadCalls;
	public int OnLoadCalls;
	public SceneLoadOptions SeenOptions;

	void ISceneLoadingEvents.BeforeLoad( Scene scene, SceneLoadOptions options )
	{
		BeforeLoadCalls++;
		SeenOptions = options;
	}

	Task ISceneLoadingEvents.OnLoad( Scene scene, SceneLoadOptions options, LoadingContext context )
	{
		OnLoadCalls++;
		context.Title = "probe loading";
		return Task.CompletedTask;
	}
}

/// <summary>
/// Pins scene loading and saving: the Load overloads and their failure modes, what
/// survives a non-additive load, additive loads, scene id adoption, loading events,
/// and serialization round trips through both JSON and SceneFile.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SceneLoadSaveTest : SceneTest
{
	/// <summary>
	/// Builds a SceneFile containing one plain game object per name, registered under
	/// the given resource path.
	/// </summary>
	static SceneFile MakeSceneFile( string resourcePath, params string[] objectNames )
	{
		var file = new SceneFile
		{
			GameObjects = objectNames
				.Select( name => Sandbox.Json.ParseToJsonObject( $"{{ \"__guid\": \"{Guid.NewGuid()}\", \"Name\": \"{name}\", \"Enabled\": true }}" ) )
				.ToArray()
		};

		file.RegisterWeakResourceId( resourcePath );
		return file;
	}

	/// <summary>
	/// Builds load options that never show a loading screen, so headless tests don't
	/// kick off the async loading task.
	/// </summary>
	static SceneLoadOptions MakeOptions( SceneFile file, bool additive = false, bool deleteEverything = false )
	{
		var options = new SceneLoadOptions
		{
			ShowLoadingScreen = false,
			IsAdditive = additive,
			DeleteEverything = deleteEverything
		};

		options.SetScene( file );
		return options;
	}

	/// <summary>
	/// The Load( GameResource ) overload loads scene files and rejects any other
	/// resource type by returning false.
	/// </summary>
	[TestMethod]
	public void LoadFromGameResourceOverload()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var sceneFile = MakeSceneFile( "loadsave_resource_overload.scene", "Loaded Object" );

		Assert.IsTrue( scene.Load( (GameResource)sceneFile ) );
		Assert.AreEqual( 1, scene.Directory.FindByName( "Loaded Object" ).Count() );

		Assert.IsFalse( scene.Load( new PrefabFile() ) );

		scene.Destroy();
	}

	/// <summary>
	/// Loading with options that never had a scene set fails cleanly with false
	/// instead of throwing or wiping the scene.
	/// </summary>
	[TestMethod]
	public void LoadWithoutSceneFails()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var existing = scene.CreateObject();

		Assert.IsFalse( scene.Load( new SceneLoadOptions() ) );
		Assert.IsTrue( existing.IsValid );

		scene.Destroy();
	}

	/// <summary>
	/// A non-additive load destroys the existing objects, but objects flagged
	/// DontDestroyOnLoad survive and are re-parented to the scene root. The scene also
	/// records the loaded file as its Source.
	/// </summary>
	[TestMethod]
	public void NonAdditiveLoadKeepsSurvivors()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var doomed = scene.CreateObject();
		doomed.Name = "Doomed";

		var parent = scene.CreateObject();
		var survivor = new GameObject( parent, name: "Survivor" );
		survivor.Flags |= GameObjectFlags.DontDestroyOnLoad;

		var sceneFile = MakeSceneFile( "loadsave_survivors.scene", "Loaded Object" );

		Assert.IsTrue( scene.Load( MakeOptions( sceneFile ) ) );

		Assert.IsFalse( doomed.IsValid );
		Assert.IsFalse( parent.IsValid );
		Assert.IsTrue( survivor.IsValid );
		Assert.AreEqual( scene, survivor.Parent );
		Assert.AreEqual( 1, scene.Directory.FindByName( "Loaded Object" ).Count() );
		Assert.AreEqual( sceneFile, scene.Source );

		scene.Destroy();
	}

	/// <summary>
	/// DeleteEverything overrides DontDestroyOnLoad - nothing survives the load.
	/// </summary>
	[TestMethod]
	public void DeleteEverythingRemovesSurvivors()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var survivor = scene.CreateObject();
		survivor.Flags |= GameObjectFlags.DontDestroyOnLoad;

		var sceneFile = MakeSceneFile( "loadsave_delete_everything.scene", "Loaded Object" );

		Assert.IsTrue( scene.Load( MakeOptions( sceneFile, deleteEverything: true ) ) );

		Assert.IsFalse( survivor.IsValid );

		scene.Destroy();
	}

	/// <summary>
	/// An additive load keeps all existing objects, adds the new ones alongside them
	/// and doesn't claim the file as the scene's Source.
	/// </summary>
	[TestMethod]
	public void AdditiveLoadKeepsExistingObjects()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var existing = scene.CreateObject();
		existing.Name = "Existing";

		var sceneFile = MakeSceneFile( "loadsave_additive.scene", "Loaded Object" );

		Assert.IsTrue( scene.Load( MakeOptions( sceneFile, additive: true ) ) );

		Assert.IsTrue( existing.IsValid );
		Assert.AreEqual( 1, scene.Directory.FindByName( "Loaded Object" ).Count() );
		Assert.IsNull( scene.Source );

		scene.Destroy();
	}

	/// <summary>
	/// When the scene file carries an id, the scene adopts it on load and stays
	/// addressable through its directory under the new guid.
	/// </summary>
	[TestMethod]
	public void SceneAdoptsSceneFileId()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var sceneFile = MakeSceneFile( "loadsave_scene_id.scene", "Loaded Object" );
		sceneFile.Id = Guid.NewGuid();

		Assert.IsTrue( scene.Load( MakeOptions( sceneFile ) ) );

		Assert.AreEqual( sceneFile.Id, scene.Id );
		Assert.AreEqual( scene, scene.Directory.FindByGuid( sceneFile.Id ) );

		scene.Destroy();
	}

	/// <summary>
	/// Scene.Serialize produces a "Scene" typed json document whose objects - names,
	/// transforms and component properties - survive Deserialize into a fresh scene.
	/// </summary>
	[TestMethod]
	public void SerializeRoundtrip()
	{
		var scene = new Scene();

		using ( scene.Push() )
		{
			var go = scene.CreateObject();
			go.Name = "Saved Object";
			go.LocalPosition = new Vector3( 1, 2, 3 );
			go.Components.Create<LoadSavePersistComponent>().Number = 42;
		}

		var json = scene.Serialize();

		Assert.AreEqual( "Scene", (string)json["Type"] );
		Assert.AreEqual( 1, json["GameObjects"].AsArray().Count );

		var restored = new Scene();
		restored.Deserialize( json );

		using ( restored.Push() )
		{
			var loaded = restored.Directory.FindByName( "Saved Object" ).Single();

			Assert.IsTrue( loaded.LocalPosition.AlmostEqual( new Vector3( 1, 2, 3 ) ) );
			Assert.AreEqual( 42, loaded.Components.Get<LoadSavePersistComponent>().Number );
		}

		scene.Destroy();
		restored.Destroy();
	}

	/// <summary>
	/// CreateSceneFile captures the scene - id, objects, component data and scene
	/// properties like WantsSystemScene - and loading that file restores all of it.
	/// </summary>
	[TestMethod]
	public void SceneFileRoundtrip()
	{
		var scene = new Scene();
		scene.WantsSystemScene = false;

		using ( scene.Push() )
		{
			var go = scene.CreateObject();
			go.Name = "Saved Object";
			go.LocalPosition = new Vector3( 4, 5, 6 );
			go.Components.Create<LoadSavePersistComponent>().Number = 7;
		}

		var sceneFile = scene.CreateSceneFile();
		sceneFile.RegisterWeakResourceId( "loadsave_scenefile_roundtrip.scene" );

		Assert.AreEqual( scene.Id, sceneFile.Id );
		Assert.AreEqual( 1, sceneFile.GameObjects.Length );
		Assert.IsNotNull( sceneFile.SceneProperties );

		var restored = new Scene();

		using ( restored.Push() )
		{
			Assert.IsTrue( restored.Load( MakeOptions( sceneFile ) ) );

			Assert.IsFalse( restored.WantsSystemScene );

			var loaded = restored.Directory.FindByName( "Saved Object" ).Single();
			Assert.IsTrue( loaded.LocalPosition.AlmostEqual( new Vector3( 4, 5, 6 ) ) );
			Assert.AreEqual( 7, loaded.Components.Get<LoadSavePersistComponent>().Number );
		}

		scene.Destroy();
		restored.Destroy();
	}

	/// <summary>
	/// Components implementing ISceneMetadata contribute their entries to the
	/// "Metadata" block of the serialized scene properties.
	/// </summary>
	[TestMethod]
	public void MetadataComponentsAreSerialized()
	{
		var scene = new Scene();

		using ( scene.Push() )
		{
			var info = scene.CreateObject().Components.Create<SceneInformation>();
			info.Title = "Metadata Test Scene";
		}

		var json = scene.Serialize();
		var metadata = json["Properties"]?["Metadata"];

		Assert.IsNotNull( metadata );
		Assert.AreEqual( "Metadata Test Scene", (string)metadata["Title"] );

		scene.Destroy();
	}

	/// <summary>
	/// During a load, components already in the scene receive BeforeLoad and OnLoad
	/// with the options that drove the load, and a load whose tasks complete
	/// synchronously leaves the scene not loading.
	/// </summary>
	[TestMethod]
	public void LoadingEventsFire()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var probe = scene.CreateObject().Components.Create<LoadEventsProbe>();

		var sceneFile = MakeSceneFile( "loadsave_loading_events.scene", "Loaded Object" );
		var options = MakeOptions( sceneFile, additive: true );

		Assert.IsTrue( scene.Load( options ) );

		Assert.AreEqual( 1, probe.BeforeLoadCalls );
		Assert.AreEqual( 1, probe.OnLoadCalls );
		Assert.AreEqual( options, probe.SeenOptions );
		Assert.IsFalse( scene.IsLoading );

		scene.Destroy();
	}

	/// <summary>
	/// A freshly constructed scene is not in a loading phase, and SceneLoadOptions
	/// defaults match what gameplay code relies on: loading screen shown, replace (not
	/// additive), keep DontDestroyOnLoad objects, no offset.
	/// </summary>
	[TestMethod]
	public void LoadingDefaults()
	{
		var scene = new Scene();

		Assert.IsFalse( scene.IsLoading );

		var options = new SceneLoadOptions();

		Assert.IsTrue( options.ShowLoadingScreen );
		Assert.IsFalse( options.IsAdditive );
		Assert.IsFalse( options.DeleteEverything );
		Assert.AreEqual( Transform.Zero, options.Offset );
		Assert.IsNull( options.GetSceneFile() );

		var sceneFile = MakeSceneFile( "loadsave_options_defaults.scene" );
		Assert.IsTrue( options.SetScene( sceneFile ) );
		Assert.AreEqual( sceneFile, options.GetSceneFile() );

		scene.Destroy();
	}
}

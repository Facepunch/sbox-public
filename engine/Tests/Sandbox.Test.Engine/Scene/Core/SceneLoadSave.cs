using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using Sandbox.Resources;
using Sandbox.Engine;

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

	[DataTestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void ChangeScenePreservesGameAndPersistentObjects( bool standalone )
	{
		var previousStandalone = Application.IsStandalone;
		var previousInstance = IGameInstance.Current;
		var previousSystem = Networking.System;
		var previousScene = Game.ActiveScene;
		var scene = new Scene();
		using var sceneScope = scene.Push();
		try
		{
			Application.IsStandalone = standalone;
			var instance = new GameInstance( "test.game", GameLoadingFlags.Host );
			IGameInstance.Current = instance;
			Networking.System = null;
			Game.ActiveScene = scene;
			var library = Game.TypeLibrary;
			var doomed = scene.CreateObject();
			var survivor = scene.CreateObject();
			survivor.Flags |= GameObjectFlags.DontDestroyOnLoad;

			var file = MakeSceneFile( "change_scene_preserves_game.scene", "New Scene Object" );
			Assert.IsTrue( Game.ChangeScene( MakeOptions( file ) ) );

			Assert.AreSame( instance, IGameInstance.Current );
			Assert.AreSame( library, Game.TypeLibrary );
			Assert.AreSame( scene, Game.ActiveScene );
			Assert.IsTrue( survivor.IsValid );
			Assert.IsFalse( doomed.IsValid );
			Assert.AreEqual( 1, scene.Directory.FindByName( "New Scene Object" ).Count() );
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
	/// When both parent and child are flagged DontDestroyOnLoad, the hierarchy is
	/// preserved across a non-additive scene load instead of flattening both objects
	/// to the scene root.
	/// </summary>
	[TestMethod]
	public void NonAdditiveLoadPreservesSurvivorHierarchy()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var survivorParent = scene.CreateObject();
		survivorParent.Name = "Survivor Parent";
		survivorParent.Flags |= GameObjectFlags.DontDestroyOnLoad;

		var survivorChild = new GameObject( survivorParent, name: "Survivor Child" );
		survivorChild.Flags |= GameObjectFlags.DontDestroyOnLoad;

		var sceneFile = MakeSceneFile( "loadsave_survivor_hierarchy.scene", "Loaded Object" );

		Assert.IsTrue( scene.Load( MakeOptions( sceneFile ) ) );

		Assert.IsTrue( survivorParent.IsValid );
		Assert.IsTrue( survivorChild.IsValid );
		Assert.AreEqual( scene, survivorParent.Parent );
		Assert.AreEqual( survivorParent, survivorChild.Parent );
		Assert.AreEqual( 1, scene.Directory.FindByName( "Loaded Object" ).Count() );

		scene.Destroy();
	}

	/// <summary>
	/// A DontDestroyOnLoad object that already has a DontDestroyOnLoad ancestor keeps its hierarchy intact.
	/// </summary>
	[TestMethod]
	public void NonAdditiveLoadPreservesNestedSurvivorHierarchy()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var survivorParent = scene.CreateObject();
		survivorParent.Name = "Survivor Parent";
		survivorParent.Flags |= GameObjectFlags.DontDestroyOnLoad;

		var middle = new GameObject( survivorParent, name: "Middle" );
		var survivorChild = new GameObject( middle, name: "Survivor Child" );
		survivorChild.Flags |= GameObjectFlags.DontDestroyOnLoad;

		var sceneFile = MakeSceneFile( "loadsave_nested_survivor_hierarchy.scene", "Loaded Object" );

		Assert.IsTrue( scene.Load( MakeOptions( sceneFile ) ) );

		Assert.IsTrue( survivorParent.IsValid );
		Assert.IsTrue( survivorChild.IsValid );
		Assert.AreEqual( scene, survivorParent.Parent );
		Assert.AreEqual( survivorParent, middle.Parent );
		Assert.AreEqual( middle, survivorChild.Parent );
		Assert.AreEqual( 1, scene.Directory.FindByName( "Loaded Object" ).Count() );

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
	public unsafe void SceneFileRoundtrip()
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

		var sourceSnapshot = SceneFile.FromSource( sceneFile.ResourcePath, sceneFile.Guid, sceneFile.Serialize().ToJsonString(), sceneFile.BinaryData );
		Assert.AreEqual( sceneFile.Id, sourceSnapshot.Id );
		Assert.AreEqual( sceneFile.ResourcePath, sourceSnapshot.ResourcePath );
		Assert.AreSame( sceneFile, ResourceLibrary.Get<SceneFile>( sceneFile.ResourcePath ), "Editing source must not replace the runtime resource" );
		var compiledJson = sceneFile.Serialize();
		var writer = new ResourceWriter();
		writer.SetDataBlock( Encoding.UTF8.GetBytes( compiledJson.ToJsonString() ) );
		var compiled = SceneFile.FromCompiled( sceneFile.ResourcePath, sceneFile.Guid, writer.ToArray() );
		var compiledData = writer.ToArray();
		Assert.AreEqual( sceneFile.Id, compiled.Id );

		foreach ( var length in new[] { 0, 1, 15, 20, compiledData.Length - 1 } )
		{
			Assert.ThrowsException<InvalidDataException>( () => Game.Resources.ReadCompiledResourceJson( compiledData[..length] ) );
			fixed ( byte* header = compiledData )
			{
				var context = new ResourceLoadContext( "truncated.scene", (IntPtr)header, length );
				Assert.IsTrue( context.ReadData( "DATA" ).IsEmpty );
			}
		}

		fixed ( byte* header = compiledData )
		{
			var context = new ResourceLoadContext( "valid.scene", (IntPtr)header, compiledData.Length );
			CollectionAssert.AreEqual( Game.Resources.ReadCompiledResourceBlock( "DATA", compiledData ), context.ReadData( "DATA" ).ToArray() );
			Assert.IsTrue( context.ReadData( "NONE" ).IsEmpty );
		}

		foreach ( var (offset, value) in new[] { (8, int.MaxValue), (8, -16), (12, int.MaxValue), (20, int.MinValue), (24, int.MaxValue) } )
		{
			var corrupt = (byte[])compiledData.Clone();
			BinaryPrimitives.WriteInt32LittleEndian( corrupt.AsSpan( offset ), value );
			Assert.ThrowsException<InvalidDataException>( () => Game.Resources.ReadCompiledResourceJson( corrupt ) );
			Assert.IsNull( Game.Resources.ReadCompiledResourceBlock( "DATA", corrupt ) );
			fixed ( byte* header = corrupt )
			{
				var context = new ResourceLoadContext( "corrupt.scene", (IntPtr)header, corrupt.Length );
				Assert.IsTrue( context.ReadData( "DATA" ).IsEmpty );
			}
		}

		sceneFile.GameObjects[0]["Name"] = "Runtime Object";

		var restored = new Scene();

		using ( restored.Push() )
		{
			Assert.IsTrue( restored.Load( MakeOptions( sourceSnapshot ) ) );

			Assert.IsFalse( restored.WantsSystemScene );

			var loaded = restored.Directory.FindByName( "Saved Object" ).Single();
			Assert.IsTrue( loaded.LocalPosition.AlmostEqual( new Vector3( 4, 5, 6 ) ) );
			Assert.AreEqual( 7, loaded.Components.Get<LoadSavePersistComponent>().Number );

			Assert.IsTrue( restored.Load( MakeOptions( sceneFile ) ) );
			Assert.AreEqual( 1, restored.Directory.FindByName( "Runtime Object" ).Count() );
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

		options.GetSceneFile().GameObjects[0]["Name"] = "Changed Before Initialize";
		Assert.IsTrue( scene.Load( options ) );
		Assert.AreEqual( 1, scene.Directory.FindByName( "Changed Before Initialize" ).Count() );

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

using Sandbox.Internal;
using System;
using System.Text.Json;

namespace Prefab;

[TestClass]
public class Prefabs
{
	TypeLibrary TypeLibrary;

	private TypeLibrary _oldTypeLibrary;

	[TestInitialize]
	public void TestInitialize()
	{
		// Replace TypeLibrary / NodeLibrary with mocked ones, store the originals

		_oldTypeLibrary = Game.TypeLibrary;

		TypeLibrary = new Sandbox.Internal.TypeLibrary();
		TypeLibrary.AddAssembly( typeof( ModelRenderer ).Assembly, false );
		TypeLibrary.AddAssembly( typeof( ComponentWithPrefabScene ).Assembly, false );
		TypeLibrary.AddAssembly( typeof( PrefabFile ).Assembly, false );
		JsonUpgrader.UpdateUpgraders( TypeLibrary );

		Game.TypeLibrary = TypeLibrary;
	}

	[TestCleanup]
	public void Cleanup()
	{
		// Make sure our mocked TypeLibrary doesn't leak out, restore old ones
		Game.TypeLibrary = _oldTypeLibrary;
	}

	public static PrefabScene GetPrefab( string resourceName, string json )
	{
		// get prefab
		var pfile = new PrefabFile();
		pfile.SetIdFromResourcePath( resourceName );
		pfile.LoadFromJson( json );

		return SceneUtility.GetPrefabScene( pfile );
	}

	[TestMethod]
	public void Basic()
	{
		var prefabBasic = BasicPrefab;

		Assert.IsTrue( prefabBasic.Components.Get<ModelRenderer>() is not null );

		// spawn in scene
		var scene = new Scene();
		using var sceneScope = scene.Push();

		Assert.IsNull( scene.Camera );
		Assert.AreEqual( scene.Directory.Count, 0 );

		var clone1 = prefabBasic.Clone( Vector3.Right * -100 );
		Assert.AreEqual( scene.Directory.Count, 1 );
		Assert.IsTrue( clone1.Components.Get<ModelRenderer>() is not null );
		Assert.AreEqual( clone1.Components.Get<ModelRenderer>().Tint, Color.Red );

		var clone2 = prefabBasic.Clone( Vector3.Right * 100 );
		Assert.AreEqual( scene.Directory.Count, 2 );
		Assert.IsTrue( clone2.Components.Get<ModelRenderer>() is not null );
		Assert.AreEqual( clone2.Components.Get<ModelRenderer>().Tint, Color.Red );
	}

	public static GameObject BasicPrefab => GetPrefab( "basic.prefab", _basicPrefabSource );

	static readonly string _basicPrefabSource = """"

		{
		  "RootObject": {
		    "Id": "fab370f8-2e2c-48cf-a523-e4be49723490",
		    "Name": "Object",
		    "Position": "788.8395,-1793.604,-1218.092",
		    "Enabled": true,
		    "Components": [
		      {
		        "__type": "ModelRenderer",
		        "BodyGroups": 18446744073709551615,
		        "MaterialGroup": null,
		        "MaterialOverride": null,
		        "Model": null,
		        "RenderType": "On",
		        "Tint": "1,0,0,1"
		      }
		    ]
		  },
		  "ShowInMenu": false,
		  "MenuPath": null,
		  "MenuIcon": null,
		  "__references": []
		}

		"""";


	[TestMethod]
	public void Variables()
	{
		var prefabScene = PrefabScene.CreateForEditing();

		//
		// Create a prefab with a variable
		//
		using ( prefabScene.Push() )
		{
			// create a child 
			var go = new GameObject();
			go.Parent = prefabScene;

			// create a model renderer with a red tint
			var modelRender = go.Components.Create<ModelRenderer>();
			modelRender.Tint = Color.Red;

			var colorVar = prefabScene.Variables.Create( "ModelColor" );
			colorVar.AddTarget( modelRender.Id, nameof( modelRender.Tint ) );

			Assert.AreEqual( 1, prefabScene.Variables.Count() );
		}

		using var tempPrefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( "runtime_variable_test.prefab", prefabScene.Serialize().ToJsonString() );
		var prefabFile = ResourceLibrary.Get<PrefabFile>( "runtime_variable_test.prefab" );

		// Make sure serializing variables works
		{
			var json = prefabFile.Serialize().ToJsonString();

			var imported = new PrefabFile();
			imported.LoadFromJson( json );

			var importedScene = SceneUtility.GetPrefabScene( prefabFile );

			Assert.AreEqual( 1, importedScene.Variables.Count() );
		}

		var newPrefabScene = SceneUtility.GetPrefabScene( prefabFile );
		var gameScene = new Scene();

		Assert.AreEqual( 1, newPrefabScene.Variables.Count() );

		using ( gameScene.Push() )
		{
			CloneConfig config = new();
			config.StartEnabled = true;
			config.PrefabVariables = new();
			config.PrefabVariables["ModelColor"] = Color.Blue;

			var instance = newPrefabScene.Clone( config );
			Assert.AreEqual( 1, gameScene.Children.Count() );

			var child = instance.Children.First();

			var modelRender = child.Components.Get<ModelRenderer>();
			Assert.AreEqual( Color.Blue, modelRender.Tint );
		}

	}

	private (PrefabScene Scene, T Component) CreatePrefabSceneWithComponent<T>() where T : Component, new()
	{
		var scene = new PrefabScene( false );

		// Pretend we have source
		scene.Source = new PrefabFile();
		scene.Source.SetIdFromResourcePath( "test.prefab" );

		T comp;

		using ( scene.Push() )
		{
			comp = scene.Components.Create<T>();
		}

		return (scene, comp);
	}

	/// <summary>
	/// When using PrefabScene as property type the property should always be serialized to the prefab path.
	/// Even when the reference points to the prefab the component resides in.
	/// </summary>
	[TestMethod]
	public void SerializeComponentWithPrefabSceneProperty()
	{
		var (scene, comp) = CreatePrefabSceneWithComponent<ComponentWithPrefabScene>();
		using var sceneScope = scene.Push();

		comp.PrefabSceneRef = scene;

		var serializedData = scene.Serialize();

		var prefabSceneProp = serializedData["Components"][0]["PrefabSceneRef"];
		// Get the serialized property
		Assert.IsNotNull( prefabSceneProp );
		Assert.AreEqual( scene.Source.ResourcePath, prefabSceneProp["prefab"].Deserialize<string>() );
	}

	public class ComponentWithPrefabScene : Component
	{
		[Sandbox.Property]
		public PrefabScene PrefabSceneRef { get; set; }
	}

	/// <summary>
	/// When using GameObject as property type the property should be serialized to the id when referencing the root.
	/// </summary>
	[TestMethod]
	public void SerializeComponentWithGameObjectProperty()
	{
		var (scene, comp) = CreatePrefabSceneWithComponent<ComponentWithGameObject>();
		using var sceneScope = scene.Push();

		comp.GameObjectRef = scene;

		var serializedData = scene.Serialize();

		var gameObjectProp = serializedData["Components"][0]["GameObjectRef"];
		// Get the serialized property
		Assert.IsNotNull( gameObjectProp );
		Assert.AreEqual( scene.Id, gameObjectProp["go"].Deserialize<Guid>() );
	}

	public class ComponentWithGameObject : Component
	{
		[Sandbox.Property]
		public GameObject GameObjectRef { get; set; }
	}
}

namespace GameObjects;

[TestClass]
public class GameObjectPrefabSourceTests
{
	[TestMethod]
	public void PrefabSourceRetainedThroughClone()
	{
		var prefabLocation = "___prefab_source_clone.prefab";

		using var prefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( prefabLocation, _basicPrefabSource );
		var prefabFile = ResourceLibrary.Get<PrefabFile>( prefabLocation );
		var prefabScene = SceneUtility.GetPrefabScene( prefabFile );

		var scene = new Scene();
		using var sceneScope = scene.Push();

		// Create a prefab instance in the scene
		var instance = prefabScene.Clone( Vector3.Zero );

		// PrefabSource should be set on prefab instance
		Assert.AreEqual( prefabLocation, instance.PrefabSource );

		// We're looking to retain PrefabSource for GameObjects specifically, make sure instance isn't a prefab instance.
		instance.BreakFromPrefab();

		Assert.IsFalse( instance.IsPrefabInstance, "Instance should no longer be a prefab instance after BreakFromPrefab." );

		// Clone the instance and ensure PrefabSource is retained on the clone
		var cloned = instance.Clone();
		Assert.AreEqual( prefabLocation, cloned.PrefabSource );
	}

	[TestMethod]
	public void PrefabSourceRetainedThroughNetworkSpawn()
	{
		var prefabLocation = "___prefab_source_networkspawn.prefab";

		using var prefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( prefabLocation, _basicPrefabSource );
		var prefabFile = ResourceLibrary.Get<PrefabFile>( prefabLocation );
		var prefabScene = SceneUtility.GetPrefabScene( prefabFile );

		var scene = new Scene();
		using var sceneScope = scene.Push();

		// Create a prefab instance in the scene
		var instance = prefabScene.Clone( Vector3.Zero );

		// Ensure PrefabSource initially set on prefab instance
		Assert.AreEqual( prefabLocation, instance.PrefabSource );

		// Ensure NetworkSpawn does not clear PrefabSource
		var spawned = instance.NetworkSpawn();

		Assert.IsFalse( instance.IsPrefabInstance, "Instance should no longer be a prefab instance after NetworkSpawn." );
		Assert.IsTrue( spawned, "NetworkSpawn should succeed." );
		Assert.AreEqual( prefabLocation, instance.PrefabSource );
	}

	[TestMethod]
	public void PrefabSourceRetainedThroughBreakFromPrefab()
	{
		var prefabLocation = "___prefab_source_break.prefab";

		using var prefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( prefabLocation, _basicPrefabSource );
		var prefabFile = ResourceLibrary.Get<PrefabFile>( prefabLocation );
		var prefabScene = SceneUtility.GetPrefabScene( prefabFile );

		var scene = new Scene();
		using var sceneScope = scene.Push();

		var instance = prefabScene.Clone( Vector3.Zero );

		// Ensure PrefabSource initially set on prefab instance
		Assert.AreEqual( prefabLocation, instance.PrefabSource );

		// Break the instance from its prefab - PrefabSource should remain set (we only clear the instance data)
		instance.BreakFromPrefab();

		// Ensure PrefabSource set after breaking from prefab
		Assert.AreEqual( prefabLocation, instance.PrefabSource );
	}

	[TestMethod]
	public void PrefabSourceAvailableForNestedGameObjects()
	{
		var nestedPrefabLocation = "___nested_for_source_test.prefab";
		var outerPrefabLocation = "___outer_for_source_test.prefab";

		// Register nested prefab
		using var nestedPrefabFile = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( nestedPrefabLocation, _basicPrefabSource );

		// Build outer prefab
		var outerJson = """" 
		{
		  "__guid": "16a942f3-0000-0000-0000-000000000000",
		  "Name": "OuterObject",
		  "Position": "0,0,0",
		  "Enabled": true,
		  "Components": [],
		  "Children": [
		    {
		      "__guid": "f1482e7a-1111-1111-1111-111111111111",
		      "__version": 1,
		      "__Prefab": "___nested_for_source_test.prefab",
		      "__PrefabInstancePatch": {
		        "AddedObjects": [],
		        "RemovedObjects": [],
		        "PropertyOverrides": [],
		        "MovedObjects": []
		      },
		      "__PrefabIdToInstanceId": {}
		    }
		  ]
		}
		"""";

		// Register outer prefab
		using var outerPrefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( outerPrefabLocation, outerJson );
		var outerPrefabFile = ResourceLibrary.Get<PrefabFile>( outerPrefabLocation );
		var outerPrefabScene = SceneUtility.GetPrefabScene( outerPrefabFile );

		var scene = new Scene();
		using var sceneScope = scene.Push();

		// Instantiate the outer prefab in the scene
		var outerInstance = outerPrefabScene.Clone( Vector3.Zero );

		// Ensure the nested instance is present 
		Assert.AreEqual( 1, outerInstance.Children.Count, "Outer instance should have one child (nested prefab instance)." );
		var nestedInstance = outerInstance.Children[0];

		// Ensure PrefabSource set correctly on both outer and nested prefab instances
		Assert.AreEqual( outerPrefabLocation, outerInstance.PrefabSource );
		Assert.AreEqual( nestedPrefabLocation, nestedInstance.PrefabSource );

		// Break the prefab instances
		outerInstance.BreakFromPrefab();
		nestedInstance.BreakFromPrefab();

		// Both should no longer be prefab instances
		Assert.IsFalse( outerInstance.IsPrefabInstance, "Outer instance should no longer be a prefab instance after BreakFromPrefab." );
		Assert.IsFalse( nestedInstance.IsPrefabInstance, "Nested instance should no longer be a prefab instance after BreakFromPrefab." );

		// PrefabSource should remain available for both the outer and inner gameobjects
		Assert.AreEqual( outerPrefabLocation, outerInstance.PrefabSource );
		Assert.AreEqual( nestedPrefabLocation, nestedInstance.PrefabSource );
	}

	private readonly string _basicPrefabSource = """" 

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
}

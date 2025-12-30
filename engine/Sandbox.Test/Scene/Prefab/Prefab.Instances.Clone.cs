namespace Prefab;

[TestClass]
/// <summary>
/// Tests for cloning prefab instances in different scenarios.
/// </summary>
public class InstanceCloneTests
{
	[TestMethod]
	public void CloneOutermostPrefabInstanceInRegularScene()
	{
		var prefabLocation = "___clone_prefab_test.prefab";

		// Create our base prefab
		using var prefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( prefabLocation, _basicPrefabSource );
		var pfile = ResourceLibrary.Get<PrefabFile>( prefabLocation );
		var prefabScene = SceneUtility.GetPrefabScene( pfile );

		// Verify the prefab has the expected component
		Assert.IsTrue( prefabScene.Components.Get<ModelRenderer>() is not null );

		// Create a regular scene and instantiate the prefab
		var scene = new Scene();
		using var sceneScope = scene.Push();

		// Create an instance in the scene
		var instance = prefabScene.Clone( Vector3.Zero );
		Assert.IsTrue( instance.IsOutermostPrefabInstanceRoot );
		Assert.IsFalse( instance is PrefabScene );

		// Modify the instance to make it unique
		instance.Components.Get<ModelRenderer>().Tint = Color.Blue;
		instance.PrefabInstance.RefreshPatch();

		// Store some information from the original instance
		var originalId = instance.Id;
		var originalTint = instance.Components.Get<ModelRenderer>().Tint;
		var originalMapping = instance.PrefabInstance.InstanceToPrefabLookup;

		// Clone the instance
		var clonedInstance = instance.Clone();

		// Verify that:
		// 1. The cloned instance is also an outermost prefab instance root
		Assert.IsTrue( clonedInstance.IsOutermostPrefabInstanceRoot );
		Assert.IsFalse( clonedInstance is PrefabScene );

		// 2. The cloned instance has a different ID but the same prefab reference
		Assert.AreNotEqual( originalId, clonedInstance.Id );
		Assert.AreEqual( instance.PrefabInstance.PrefabSource, clonedInstance.PrefabInstance.PrefabSource );

		// 3. The local modifications are preserved in the clone
		Assert.AreEqual( originalTint, clonedInstance.Components.Get<ModelRenderer>().Tint );

		// 4. The new instance has correct prefab lookups
		var clonedMapping = clonedInstance.PrefabInstance.InstanceToPrefabLookup;
		Assert.AreNotEqual( 0, clonedMapping.Count );

		// 5. The mapping keys (prefab references) should be the same in both mappings
		foreach ( var prefabGuid in originalMapping.Values )
		{
			Assert.IsTrue( clonedMapping.Values.Contains( prefabGuid ),
				"Clone's mapping should contain all prefab GUIDs from original mapping" );
		}

		// 6. But the mapped values (instance GUIDs) should be different
		foreach ( var originalInstanceGuid in originalMapping.Keys )
		{
			Assert.IsFalse( clonedMapping.ContainsKey( originalInstanceGuid ),
				"Clone's mapping should not contain instance GUIDs from original mapping" );
		}
	}

	[TestMethod]
	public void ClonePrefabInstanceInPrefabScene()
	{
		var outerPrefabLocation = "___outer_prefab_for_clone.prefab";
		var nestedPrefabLocation = "___nested_prefab_for_clone.prefab";

		// First create a nested prefab
		using var nestedPrefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( nestedPrefabLocation, _basicPrefabSource );
		var nestedPrefabFile = ResourceLibrary.Get<PrefabFile>( nestedPrefabLocation );
		var nestedPrefabScene = SceneUtility.GetPrefabScene( nestedPrefabFile );

		// Then create an outer prefab that includes an instance of the nested prefab
		using var outerPrefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( outerPrefabLocation, _outerPrefabSource );
		var outerPrefabFile = ResourceLibrary.Get<PrefabFile>( outerPrefabLocation );
		var outerPrefabScene = SceneUtility.GetPrefabScene( outerPrefabFile );

		// Verify the outer prefab contains an instance of the nested prefab
		Assert.AreEqual( 1, outerPrefabScene.Children.Count );
		var nestedInstance = outerPrefabScene.Children[0];
		Assert.IsTrue( nestedInstance.IsPrefabInstanceRoot );
		Assert.IsFalse( nestedInstance.IsNestedPrefabInstanceRoot );
		Assert.IsTrue( nestedInstance.IsOutermostPrefabInstanceRoot );

		// Store some information about the nested instance
		var originalNestedId = nestedInstance.Id;
		var originalNestedMapping = nestedInstance.PrefabInstance.InstanceToPrefabLookup;

		using var sceneScope = outerPrefabScene.Push();

		// Clone the nested instance within the prefab scene
		var clonedNested = nestedInstance.Clone();

		// Verify that:
		// 1. The cloned instance is properly set up as a prefab instance
		Assert.IsTrue( clonedNested.IsPrefabInstanceRoot );
		Assert.IsFalse( clonedNested.IsNestedPrefabInstanceRoot );
		Assert.IsTrue( clonedNested.IsOutermostPrefabInstanceRoot );

		// 2. The cloned instance has a different ID but the same prefab source
		Assert.AreNotEqual( originalNestedId, clonedNested.Id );
		Assert.AreEqual( nestedInstance.PrefabInstance.PrefabSource, clonedNested.PrefabInstance.PrefabSource );

		// 3. The new instance has correct prefab lookups
		var clonedMapping = clonedNested.PrefabInstance.InstanceToPrefabLookup;
		Assert.AreNotEqual( 0, clonedMapping.Count );

		// 4. The mapping keys (prefab references) should be the same in both mappings
		foreach ( var prefabGuid in originalNestedMapping.Values )
		{
			Assert.IsTrue( clonedMapping.Values.Contains( prefabGuid ),
				"Clone's mapping should contain all prefab GUIDs from original mapping" );
		}

		// 5. But the mapped values (instance GUIDs) should be different
		foreach ( var originalInstanceGuid in originalNestedMapping.Keys )
		{
			Assert.IsFalse( clonedMapping.ContainsKey( originalInstanceGuid ),
				"Clone's mapping should not contain instance GUIDs from original mapping" );
		}

		// 6. The cloned instance should be a sibling of the original instance
		Assert.AreEqual( nestedInstance.Parent, clonedNested.Parent );
		Assert.AreEqual( 2, outerPrefabScene.Children.Count );
	}

	[TestMethod]
	public void CloneDeeplyNestedPrefabInstanceInRegularScene()
	{
		var outerPrefabLocation = "___outer_prefab_for_clone.prefab";
		var nestedPrefabLocation = "___nested_prefab_for_clone.prefab";

		// First create a nested prefab
		using var nestedPrefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( nestedPrefabLocation, _basicPrefabSource );
		var nestedPrefabFile = ResourceLibrary.Get<PrefabFile>( nestedPrefabLocation );
		var nestedPrefabScene = SceneUtility.GetPrefabScene( nestedPrefabFile );

		// Then create an outer prefab that includes an instance of the nested prefab
		using var outerPrefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( outerPrefabLocation, _outerPrefabSource );
		var outerPrefabFile = ResourceLibrary.Get<PrefabFile>( outerPrefabLocation );
		var outerPrefabScene = SceneUtility.GetPrefabScene( outerPrefabFile );

		// Verify the outer prefab contains an instance of the nested prefab
		Assert.AreEqual( 1, outerPrefabScene.Children.Count );
		var nestedInstanceInPrefab = outerPrefabScene.Children[0];
		Assert.IsTrue( nestedInstanceInPrefab.IsPrefabInstanceRoot );
		Assert.IsTrue( nestedInstanceInPrefab.IsOutermostPrefabInstanceRoot );
		Assert.IsFalse( nestedInstanceInPrefab.IsNestedPrefabInstanceRoot );

		// Create a regular scene
		var scene = new Scene();
		using var sceneScope = scene.Push();

		// Create an instance of the outer prefab in the scene
		var outerInstance = outerPrefabScene.Clone( Vector3.Zero );

		// Verify the outer instance is a prefab instance root
		Assert.IsTrue( outerInstance.IsPrefabInstanceRoot );
		Assert.IsTrue( outerInstance.IsOutermostPrefabInstanceRoot );
		Assert.IsFalse( outerInstance is PrefabScene );

		// Verify the nested structure in the regular scene
		Assert.AreEqual( 1, outerInstance.Children.Count );
		var nestedInstanceInScene = outerInstance.Children[0];
		Assert.IsTrue( nestedInstanceInScene.IsPrefabInstanceRoot );
		Assert.IsTrue( nestedInstanceInScene.IsNestedPrefabInstanceRoot );
		Assert.IsFalse( nestedInstanceInScene.IsOutermostPrefabInstanceRoot );
		Assert.AreEqual( outerInstance, nestedInstanceInScene.OutermostPrefabInstanceRoot );
		// Nested instance should have a non empy mapping
		Assert.AreNotEqual( nestedInstanceInScene.PrefabInstance.PrefabToInstanceLookup.Count, 0 );

		// Modify the outer instance to make it unique
		outerInstance.Components.Get<ModelRenderer>().Tint = Color.Blue;
		var nestedComponent = nestedInstanceInScene.Components.Get<ModelRenderer>();
		nestedComponent.Tint = Color.Yellow;
		outerInstance.PrefabInstance.RefreshPatch();

		// Store mappings for comparison
		var outerMapping = outerInstance.PrefabInstance.InstanceToPrefabLookup;
		var nestedMapping = nestedInstanceInScene.PrefabInstance.InstanceToPrefabLookup;

		// Record original IDs for comparison
		var outerInstanceId = outerInstance.Id;
		var nestedInstanceId = nestedInstanceInScene.Id;
		var outerComponentId = outerInstance.Components.Get<ModelRenderer>().Id;
		var nestedComponentId = nestedComponent.Id;

		// Clone the entire outer instance with its nested structure
		var clonedOuter = outerInstance.Clone();

		// Verify the cloned outer instance is properly set up
		Assert.IsTrue( clonedOuter.IsPrefabInstanceRoot );
		Assert.IsTrue( clonedOuter.IsOutermostPrefabInstanceRoot );
		Assert.IsFalse( clonedOuter is PrefabScene );

		// IDs should be different, but prefab source should be the same
		Assert.AreNotEqual( outerInstanceId, clonedOuter.Id );
		Assert.AreEqual( outerInstance.PrefabInstance.PrefabSource, clonedOuter.PrefabInstance.PrefabSource );

		// The modified color should be preserved in the clone
		Assert.AreEqual( Color.Blue, clonedOuter.Components.Get<ModelRenderer>().Tint );
		Assert.AreNotEqual( outerComponentId, clonedOuter.Components.Get<ModelRenderer>().Id );

		// Verify the nested structure in the clone
		Assert.AreEqual( 1, clonedOuter.Children.Count );
		var clonedNested = clonedOuter.Children[0];
		Assert.IsTrue( clonedNested.IsPrefabInstanceRoot );
		Assert.IsTrue( clonedNested.IsNestedPrefabInstanceRoot );
		Assert.IsFalse( clonedNested.IsOutermostPrefabInstanceRoot );

		// The nested instance should have a new ID but same prefab source
		Assert.AreNotEqual( nestedInstanceId, clonedNested.Id );
		Assert.AreEqual( nestedInstanceInScene.PrefabInstance.PrefabSource, clonedNested.PrefabInstance.PrefabSource );

		// The nested modification should be preserved
		var clonedNestedComponent = clonedNested.Components.Get<ModelRenderer>();
		Assert.AreEqual( Color.Yellow, clonedNestedComponent.Tint );
		Assert.AreNotEqual( nestedComponentId, clonedNestedComponent.Id );

		// Verify the outermost prefab root reference is correct
		Assert.AreEqual( clonedOuter, clonedNested.OutermostPrefabInstanceRoot );

		// Verify prefab lookups for the outer instance
		var clonedOuterMapping = clonedOuter.PrefabInstance.InstanceToPrefabLookup;
		Assert.AreEqual( outerMapping.Count, clonedOuterMapping.Count );

		// The prefab GUIDs should be the same
		foreach ( var prefabGuid in outerMapping.Values )
		{
			Assert.IsTrue( clonedOuterMapping.Values.Contains( prefabGuid ),
				"Clone's mapping should contain all prefab GUIDs from original mapping" );
		}

		// But the instance GUIDs should be different
		foreach ( var originalInstanceGuid in outerMapping.Keys )
		{
			Assert.IsFalse( clonedOuterMapping.ContainsKey( originalInstanceGuid ),
				"Clone's mapping should not contain instance GUIDs from original mapping" );
		}

		// Verify prefab lookups for the nested instance
		var clonedNestedMapping = clonedNested.PrefabInstance.InstanceToPrefabLookup;
		Assert.AreEqual( nestedMapping.Count, clonedNestedMapping.Count );

		foreach ( var prefabGuid in nestedMapping.Values )
		{
			Assert.IsTrue( clonedNestedMapping.Values.Contains( prefabGuid ),
				"Nested clone's mapping should contain all prefab GUIDs from original nested mapping" );
		}

		foreach ( var originalInstanceGuid in nestedMapping.Keys )
		{
			Assert.IsFalse( clonedNestedMapping.ContainsKey( originalInstanceGuid ),
				"Nested clone's mapping should not contain instance GUIDs from original nested mapping" );
		}
	}

	[TestMethod]
	public void CloneRegularGameObjectContainingNestedPrefabInstance()
	{
		var outerPrefabLocation = "___outer_prefab_with_regular_go.prefab";
		var innerPrefabLocation = "___inner_prefab_for_clone.prefab";

		// Create inner prefab
		using var innerPrefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( innerPrefabLocation, _basicPrefabSource );
		var innerPrefabFile = ResourceLibrary.Get<PrefabFile>( innerPrefabLocation );
		var innerPrefabScene = SceneUtility.GetPrefabScene( innerPrefabFile );

		// Create outer prefab
		using var outerPrefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( outerPrefabLocation, _outerPrefabWithRegularGOSource );
		var outerPrefabFile = ResourceLibrary.Get<PrefabFile>( outerPrefabLocation );
		var outerPrefabScene = SceneUtility.GetPrefabScene( outerPrefabFile );

		// Create a regular scene
		var scene = new Scene();
		using var sceneScope = scene.Push();

		// Instantiate the outer prefab
		var outerInstance = outerPrefabScene.Clone( Vector3.Zero );

		// Get the interesting gameobjects
		var regularGo = outerInstance.Children[0];
		var nestedInstance = regularGo.Children[0];

		// Verify initial state
		Assert.IsFalse( regularGo.IsPrefabInstanceRoot );
		Assert.IsTrue( nestedInstance.IsPrefabInstanceRoot );
		Assert.IsTrue( nestedInstance.IsNestedPrefabInstanceRoot );
		Assert.IsFalse( nestedInstance.IsOutermostPrefabInstanceRoot );

		// Clone the regular GameObject, which is part of a prefab instance, but not an instance itself.
		var clonedRegularGo = regularGo.Clone();

		// The cloned GO should not be a prefab instance
		Assert.IsFalse( clonedRegularGo.IsPrefabInstanceRoot );
		Assert.AreEqual( 1, clonedRegularGo.Children.Count );

		// Its child, which was a nested prefab instance, should now be a full, outermost prefab instance.
		var clonedNestedInstance = clonedRegularGo.Children[0];
		Assert.IsTrue( clonedNestedInstance.IsPrefabInstanceRoot );
		Assert.IsFalse( clonedNestedInstance.IsNestedPrefabInstanceRoot );
		Assert.IsTrue( clonedNestedInstance.IsOutermostPrefabInstanceRoot );

		// It should have the same prefab source as the original nested instance
		Assert.AreEqual( nestedInstance.PrefabInstance.PrefabSource, clonedNestedInstance.PrefabInstance.PrefabSource );
	}

	[TestMethod]
	public void CloneNestedPrefabInstanceDirectly()
	{
		var outerPrefabLocation = "___outer_prefab_for_clone.prefab";
		var nestedPrefabLocation = "___nested_prefab_for_clone.prefab";

		// Create nested prefab
		using var nestedPrefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( nestedPrefabLocation, _basicPrefabSource );
		var nestedPrefabFile = ResourceLibrary.Get<PrefabFile>( nestedPrefabLocation );
		var nestedPrefabScene = SceneUtility.GetPrefabScene( nestedPrefabFile );

		// Create outer prefab containing the nested one
		using var outerPrefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( outerPrefabLocation, _outerPrefabSource );
		var outerPrefabFile = ResourceLibrary.Get<PrefabFile>( outerPrefabLocation );
		var outerPrefabScene = SceneUtility.GetPrefabScene( outerPrefabFile );

		// Create a regular scene
		var scene = new Scene();
		using var sceneScope = scene.Push();

		// Instantiate the outer prefab
		var outerInstance = outerPrefabScene.Clone( Vector3.Zero );
		var nestedInstance = outerInstance.Children[0];

		// Verify initial state
		Assert.IsTrue( nestedInstance.IsPrefabInstanceRoot );
		Assert.IsTrue( nestedInstance.IsNestedPrefabInstanceRoot );
		Assert.IsFalse( nestedInstance.IsOutermostPrefabInstanceRoot );

		// Clone the nested instance directly
		var clonedNested = nestedInstance.Clone();

		// The cloned instance should be converted to a full, outermost prefab instance.
		Assert.IsTrue( clonedNested.IsPrefabInstanceRoot );
		Assert.IsFalse( clonedNested.IsNestedPrefabInstanceRoot );
		Assert.IsTrue( clonedNested.IsOutermostPrefabInstanceRoot );
		Assert.AreEqual( clonedNested, clonedNested.OutermostPrefabInstanceRoot );

		// It should have a new ID but the same prefab source
		Assert.AreNotEqual( nestedInstance.Id, clonedNested.Id );
		Assert.AreEqual( nestedInstance.PrefabInstance.PrefabSource, clonedNested.PrefabInstance.PrefabSource );
	}

	static readonly string _basicPrefabSource = """"
    {
        "__guid": "fab370f8-2e2c-48cf-a523-e4be49723490",
        "Name": "Object",
        "Position": "788.8395,-1793.604,-1218.092",
        "Scale": "10, 10, 10",
        "Enabled": true,
        "Components": [
            {
                "__type": "ModelRenderer",
                "__guid": "230b45c1-a446-42b4-af39-f7195135e31f",
                "BodyGroups": 18446744073709551615,
                "MaterialGroup": null,
                "MaterialOverride": null,
                "Model": null,
                "RenderType": "On",
                "Tint": "1,0,0,1"
            }
        ],
        "Children": []
    }
    """";

	static readonly string _outerPrefabSource = """"
    {
        "__guid": "16a942f3-8b7c-4b6e-a14b-5854d568e256",
        "Name": "OuterPrefab",
        "Position": "0,0,0",
        "Enabled": true,
        "Components": [
            {
                "__type": "ModelRenderer",
                "__guid": "b34c25d6-22cd-4e4a-9fb4-71c12dce2efd",
                "BodyGroups": 18446744073709551615,
                "MaterialGroup": null,
                "MaterialOverride": null,
                "Model": null,
                "RenderType": "On",
                "Tint": "0,1,0,1"
            }
        ],
        "Children": [
            {
                "__guid": "f1482e7a-a10c-4c5b-b0fa-3dc07ef5f7e9",
                "__version": 1,
                "__Prefab": "___nested_prefab_for_clone.prefab",
                "__PrefabInstancePatch": {
                    "AddedObjects": [],
                    "RemovedObjects": [],
                    "PropertyOverrides": [],
                    "MovedObjects": []
                },
                "__PrefabIdToInstanceId": {
                    "fab370f8-2e2c-48cf-a523-e4be49723490": "f1482e7a-a10c-4c5b-b0fa-3dc07ef5f7e9",
                    "230b45c1-a446-42b4-af39-f7195135e31f": "aa721c3b-9d6c-48d5-81a9-f72ef5c5b12e"
                }
            }
        ]
    }
    """";

	static readonly string _outerPrefabWithRegularGOSource = """"
    {
        "__guid": "a1b2c3d4-e5f6-4a1b-a1b2-c3d4e5f6a1b2",
        "Name": "OuterPrefabWithRegularGO",
        "Position": "0,0,0",
        "Enabled": true,
        "Components": [],
        "Children": [
            {
                "__guid": "b2c3d4e5-f6a1-4b1a-b2c3-d4e5f6a1b2c3",
                "Name": "RegularGO",
                "Position": "0,0,0",
                "Enabled": true,
                "Components": [],
                "Children": [
                    {
                        "__guid": "c3d4e5f6-a1b2-4c1b-c3d4-e5f6a1b2c3d4",
                        "__version": 1,
                        "__Prefab": "___inner_prefab_for_clone.prefab",
                        "__PrefabInstancePatch": {
                            "AddedObjects": [],
                            "RemovedObjects": [],
                            "PropertyOverrides": [],
                            "MovedObjects": []
                        },
                        "__PrefabIdToInstanceId": {
                            "fab370f8-2e2c-48cf-a523-e4be49723490": "c3d4e5f6-a1b2-4c1b-c3d4-e5f6a1b2c3d4",
                            "230b45c1-a446-42b4-af39-f7195135e31f": "d4e5f6a1-b2c3-4d1b-d4e5-f6a1b2c3d4e5"
                        }
                    }
                ]
            }
        ]
    }
    """";
}

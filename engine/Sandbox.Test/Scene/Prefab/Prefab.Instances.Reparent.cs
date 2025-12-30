namespace Prefab;

[TestClass]
/// <summary>
/// Tests for reparenting prefab instances in different scenarios.
/// </summary>
public class InstanceReparentTests
{
	[TestMethod]
	public void ReparentRegularGameObjectContainingNestedPrefabInstance()
	{
		var outerPrefabLocation = "___outer_prefab_with_regular_go.prefab";
		var innerPrefabLocation = "___inner_prefab_for_reparent.prefab";

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

		// Create a neutral GameObject outside the prefab hierarchy
		var neutralParent = new GameObject( true, "NeutralParent" );

		// Instantiate the outer prefab
		var outerInstance = outerPrefabScene.Clone( Vector3.Zero );

		// Get the interesting gameobjects
		var regularGo = outerInstance.Children[0];
		var nestedInstance = regularGo.Children[0];

		// Store the nested instance GUID for later verification
		var nestedInstanceGuid = nestedInstance.Id;

		// Verify initial state
		Assert.IsFalse( regularGo.IsPrefabInstanceRoot );
		Assert.IsTrue( nestedInstance.IsPrefabInstanceRoot );
		Assert.IsTrue( nestedInstance.IsNestedPrefabInstanceRoot );
		Assert.IsFalse( nestedInstance.IsOutermostPrefabInstanceRoot );

		// Verify the nested instance is initially in the outerInstance's mapping
		Assert.IsTrue( outerInstance.PrefabInstance.InstanceToPrefabLookup.ContainsKey( nestedInstanceGuid ) );

		// Reparent the regular GameObject to the neutral parent
		regularGo.SetParent( neutralParent );

		// The regular GO should still not be a prefab instance
		Assert.IsFalse( regularGo.IsPrefabInstanceRoot );
		Assert.AreEqual( 1, regularGo.Children.Count );

		// Its child, which was a nested prefab instance, should now be a full, outermost prefab instance
		Assert.IsTrue( nestedInstance.IsPrefabInstanceRoot );
		Assert.IsFalse( nestedInstance.IsNestedPrefabInstanceRoot );
		Assert.IsTrue( nestedInstance.IsOutermostPrefabInstanceRoot );
		Assert.AreEqual( nestedInstance, nestedInstance.OutermostPrefabInstanceRoot );

		// Verify the nested instance is no longer in the outerInstance's mapping
		Assert.IsFalse( outerInstance.PrefabInstance.InstanceToPrefabLookup.ContainsKey( nestedInstanceGuid ) );
	}

	[TestMethod]
	public void ReparentNestedPrefabInstanceDirectly()
	{
		var outerPrefabLocation = "___outer_prefab_for_reparent.prefab";
		var nestedPrefabLocation = "___nested_prefab_for_reparent.prefab";

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

		// Create a neutral GameObject outside the prefab hierarchy
		var neutralParent = new GameObject( true, "NeutralParent" );

		// Instantiate the outer prefab
		var outerInstance = outerPrefabScene.Clone( Vector3.Zero );
		var nestedInstance = outerInstance.Children[0];

		// Store the nested instance GUID for later verification
		var nestedInstanceGuid = nestedInstance.Id;

		// Verify initial state
		Assert.IsTrue( nestedInstance.IsPrefabInstanceRoot );
		Assert.IsTrue( nestedInstance.IsNestedPrefabInstanceRoot );
		Assert.IsFalse( nestedInstance.IsOutermostPrefabInstanceRoot );
		Assert.AreEqual( outerInstance, nestedInstance.OutermostPrefabInstanceRoot );

		// Verify the nested instance is initially in the outerInstance's mapping
		Assert.IsTrue( outerInstance.PrefabInstance.InstanceToPrefabLookup.ContainsKey( nestedInstanceGuid ) );

		// Reparent the nested instance directly to the neutral parent
		nestedInstance.SetParent( neutralParent );

		// The reparented instance should be converted to a full, outermost prefab instance
		Assert.IsTrue( nestedInstance.IsPrefabInstanceRoot );
		Assert.IsFalse( nestedInstance.IsNestedPrefabInstanceRoot );
		Assert.IsTrue( nestedInstance.IsOutermostPrefabInstanceRoot );
		Assert.AreEqual( nestedInstance, nestedInstance.OutermostPrefabInstanceRoot );

		// It should maintain the same prefab source
		Assert.IsNotNull( nestedInstance.PrefabInstance );
		Assert.IsNotNull( nestedInstance.PrefabInstance.PrefabSource );

		// Verify the nested instance is no longer in the outerInstance's mapping
		Assert.IsFalse( outerInstance.PrefabInstance.InstanceToPrefabLookup.ContainsKey( nestedInstanceGuid ) );
	}

	// Reuse the same JSON data sources from the original tests
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
                "__Prefab": "___nested_prefab_for_reparent.prefab",
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
                        "__Prefab": "___inner_prefab_for_reparent.prefab",
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

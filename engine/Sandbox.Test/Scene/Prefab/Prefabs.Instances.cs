using Editor;
using Sandbox;
using Sandbox.Internal;
using System;

namespace Prefab;

/// <summary>
/// Various prefab instance tests. No specific category.
/// </summary>
[TestClass]
public partial class Instances
{
	[TestMethod]
	public void WriteBackInstanceToPrefab()
	{
		var saveLocation = "___writeback.prefab";

		using var prefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( saveLocation, _basicPrefabSource );

		var pfile = ResourceLibrary.Get<PrefabFile>( saveLocation );

		var prefabScene = SceneUtility.GetPrefabScene( pfile );

		Assert.IsTrue( prefabScene.Components.Get<ModelRenderer>() is not null );

		// spawn in scene
		var scene = new Scene();
		using var sceneScope = scene.Push();

		Assert.IsNull( scene.Camera );
		Assert.AreEqual( scene.Directory.Count, 0 );

		var instance = prefabScene.Clone( Vector3.Right * -100 );
		Assert.AreEqual( scene.Directory.Count, 1 );
		Assert.IsTrue( instance.Components.Get<ModelRenderer>() is not null );

		instance.AddComponent<SkinnedModelRenderer>();
		Assert.IsTrue( instance.Components.Get<SkinnedModelRenderer>() is not null );

		instance.AddComponent<ModelHitboxes>();
		Assert.IsTrue( instance.Components.Get<ModelHitboxes>() is not null );
		instance.Components.Get<ModelHitboxes>().Renderer = instance.Components.Get<SkinnedModelRenderer>();

		EditorUtility.Prefabs.WriteInstanceToPrefab( instance, true );

		Assert.AreEqual( 3, prefabScene.Components.Count );
		Assert.IsTrue( prefabScene.Components.Get<SkinnedModelRenderer>() is not null );
		Assert.AreNotEqual( instance.Components.Get<ModelRenderer>().Id, prefabScene.Components.Get<ModelRenderer>().Id );
		// Added component should also not share an id with the prefab to avoid conflicts
		Assert.AreNotEqual( instance.Components.Get<SkinnedModelRenderer>().Id, prefabScene.Components.Get<SkinnedModelRenderer>().Id );

		// Reference to added objects should be maintaned, when writing back to prefab
		Assert.AreEqual( instance.Components.Get<ModelHitboxes>().Renderer.Id, instance.Components.Get<SkinnedModelRenderer>().Id );
		Assert.AreNotEqual( instance.Components.Get<ModelHitboxes>().Renderer.Id, prefabScene.Components.Get<SkinnedModelRenderer>().Id );
		Assert.AreEqual( prefabScene.Components.Get<ModelHitboxes>().Renderer.Id, prefabScene.Components.Get<SkinnedModelRenderer>().Id );
	}

	[TestMethod]
	public void UpstreamChangesFromPrefabToInstance()
	{
		var saveLocation = "___upstream_changes.prefab";

		// Create our base prefab
		using var prefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( saveLocation, _basicPrefabSource );
		var pfile = ResourceLibrary.Get<PrefabFile>( saveLocation );
		var prefabScene = SceneUtility.GetPrefabScene( pfile );

		Assert.IsTrue( prefabScene.Components.Get<ModelRenderer>() is not null );

		// Create an instance in a scene
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var instance = prefabScene.Clone( Vector3.Zero );

		// Verify the instance has the expected component from prefab
		Assert.IsTrue( instance.Components.Get<ModelRenderer>() is not null );

		// Make a local modification to the instance (this should be preserved)
		instance.Components.Get<ModelRenderer>().Tint = Color.Blue;
		// The editor would run this after a modification
		instance.PrefabInstance.RefreshPatch();

		// Now modify the original prefab by adding a new component
		prefabScene.AddComponent<BoxCollider>();
		Assert.IsTrue( prefabScene.Components.Get<BoxCollider>() is not null );

		// Save the changes to the prefab file
		prefabScene.ToPrefabFile();

		// Update the instance from the modified prefab
		instance.UpdateFromPrefab();

		// Verify that:
		// 1. The instance now has the new component from the prefab
		Assert.IsTrue( instance.Components.Get<BoxCollider>() is not null );

		// 2. The instance's local modifications are preserved
		Assert.AreEqual( Color.Blue, instance.Components.Get<ModelRenderer>().Tint );

		// 3. The original prefab's component values remain unchanged
		prefabScene.Load( pfile ); // Refresh to be sure we have the latest data
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), prefabScene.Components.Get<ModelRenderer>().Tint );
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

	[TestMethod]
	public void UpstreamChangesWithHierarchiesThatContainTheSamePrefabMoreThanOnce_Nested()
	{
		var saveLocation = "___upstream_changes_self_nested.prefab";

		// Create our base prefab
		using var prefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( saveLocation, _basicPrefabSource );
		var pfile = ResourceLibrary.Get<PrefabFile>( saveLocation );
		var prefabScene = SceneUtility.GetPrefabScene( pfile );

		Assert.IsTrue( prefabScene.Components.Get<ModelRenderer>() is not null );

		// Create an instance in a scene
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var hierarchy = scene.CreateObject();
		hierarchy.Deserialize( Json.ParseToJsonObject( _gameObjectWithNestedPrefabInstance ) );

		// Verify the instance has the expected component from prefab
		Assert.AreEqual( 1, hierarchy.Children.Count );
		GameObject outerPrefabInstance = hierarchy.Children[0];
		Assert.IsTrue( outerPrefabInstance.Components.Get<ModelRenderer>() is not null );

		// Make a local modification to the instance (this should be preserved)
		outerPrefabInstance.Components.Get<ModelRenderer>().Tint = Color.Blue;
		// The editor would run this after a modification
		outerPrefabInstance.PrefabInstance.RefreshPatch();

		// Now modify the original prefab by adding a new component
		prefabScene.AddComponent<BoxCollider>();
		Assert.IsTrue( prefabScene.Components.Get<BoxCollider>() is not null );

		// Save the changes to the prefab file
		prefabScene.ToPrefabFile();

		// Update the instance from the modified prefab
		// This should call RefreshCachedPatch with refreshFromUpstream=true internally
		outerPrefabInstance.UpdateFromPrefab();

		// Verify that:
		// 1. The instance now has the new component from the prefab
		Assert.IsTrue( outerPrefabInstance.Components.Get<BoxCollider>() is not null );

		// 2. The instance's local modifications are preserved
		Assert.AreEqual( Color.Blue, outerPrefabInstance.Components.Get<ModelRenderer>().Tint );

		// 3. The original prefab's component values remain unchanged
		prefabScene.Load( pfile ); // Refresh to be sure we have the latest data
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), prefabScene.Components.Get<ModelRenderer>().Tint );
	}

	static readonly string _gameObjectWithNestedPrefabInstance = """"
	{
		"__guid": "d03c7897-8558-48ee-990f-547b61de7f9a",
		"Name": "Object",
		"Position": "788.8395,-1793.604,-1218.092",
		"Enabled": true,
		"Children": [
			{
				"__guid": "c07c9e05-a64f-4354-a98f-11bbdbec71fa",
				"__version": 1,
				"__Prefab": "___upstream_changes_self_nested.prefab",
				"__PrefabInstancePatch": {
					"AddedObjects": [
						{
							"Id": {
								"Type": "GameObject",
								"IdValue": "e5156285-adb9-4986-bd03-e7899750ffe1"
							},
							"Parent": {
								"Type": "GameObject",
								"IdValue": "fab370f8-2e2c-48cf-a523-e4be49723490"
							},
							"PreviousElement": null,
							"ContainerProperty": "Children",
							"IsContainerArray": true,
							"Data": {
								"__guid": "e5156285-adb9-4986-bd03-e7899750ffe1",
								"__version": 1,
								"__Prefab": "___upstream_changes_self_nested.prefab",
								"__PrefabInstancePatch": {
									"AddedObjects": [
										{
											"Id": {
												"Type": "GameObject",
												"IdValue": "3508f8c0-9cb0-421b-a040-9d340c5611c1"
											},
											"Parent": {
												"Type": "GameObject",
												"IdValue": "fab370f8-2e2c-48cf-a523-e4be49723490"
											},
											"PreviousElement": null,
											"ContainerProperty": "Children",
											"IsContainerArray": true,
											"Data": {
												"__guid": "3508f8c0-9cb0-421b-a040-9d340c5611c1",
												"__version": 1,
												"__Prefab": "___upstream_changes_self_nested.prefab",
												"__PrefabInstancePatch": {
													"AddedObjects": [],
													"RemovedObjects": [],
													"PropertyOverrides": [],
													"MovedObjects": []
												},
												"__PrefabIdToInstanceId": {
													"fab370f8-2e2c-48cf-a523-e4be49723490": "3508f8c0-9cb0-421b-a040-9d340c5611c1",
													"230b45c1-a446-42b4-af39-f7195135e31f": "d2679da4-2b84-4aa5-95ad-32e47a1bbed5"
												}
											}
										}
									],
									"RemovedObjects": [],
									"PropertyOverrides": [],
									"MovedObjects": []
								},
								"__PrefabIdToInstanceId": {
									"fab370f8-2e2c-48cf-a523-e4be49723490": "e5156285-adb9-4986-bd03-e7899750ffe1",
									"230b45c1-a446-42b4-af39-f7195135e31f": "65886a24-2a05-41c7-a5dc-da429ea1aeff"
								}
							}
						}
					],
					"RemovedObjects": [],
					"PropertyOverrides": [],
					"MovedObjects": []
				},
				"__PrefabIdToInstanceId": {
					"fab370f8-2e2c-48cf-a523-e4be49723490": "c07c9e05-a64f-4354-a98f-11bbdbec71fa",
					"230b45c1-a446-42b4-af39-f7195135e31f": "19e18ce9-aba4-465c-b07b-7e63f5f05cef"
				}
			}
		]
	}

	"""";

	[TestMethod]
	public void UpstreamChangesWithHierarchiesThatContainTheSamePrefabMoreThanOnce()
	{
		var saveLocation = "___upstream_changes_multiple.prefab";

		// Create our base prefab
		using var prefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( saveLocation, _basicPrefabSource );
		var pfile = ResourceLibrary.Get<PrefabFile>( saveLocation );
		var prefabScene = SceneUtility.GetPrefabScene( pfile );

		Assert.IsTrue( prefabScene.Components.Get<ModelRenderer>() is not null );

		// Create an instance in a scene
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var hierarchy = scene.CreateObject();
		hierarchy.Deserialize( Json.ParseToJsonObject( _gameObjectWithPrefabInstances ) );

		// Verify the instance has the expected component from prefab
		Assert.AreEqual( 2, hierarchy.Children.Count );

		foreach ( var prefabInstance in hierarchy.Children )
		{
			Assert.IsTrue( prefabInstance.Components.Get<ModelRenderer>() is not null );

			// Make a local modification to the instance (this should be preserved)
			prefabInstance.Components.Get<ModelRenderer>().Tint = Color.Blue;
			// The editor would run this after a modification
			prefabInstance.PrefabInstance.RefreshPatch();
		}

		// Now modify the original prefab by adding a new go and component
		var newGo = prefabScene.CreateObject();
		newGo.AddComponent<BoxCollider>();
		Assert.IsTrue( newGo.Components.Get<BoxCollider>() is not null );

		prefabScene.ToPrefabFile();

		foreach ( var prefabInstance in hierarchy.Children )
		{
			// Update the instance from the modified prefab
			prefabInstance.UpdateFromPrefab();

			Assert.AreEqual( 1, prefabInstance.Children.Count );
			Assert.IsNotNull( prefabInstance.Children[0].Components.Get<BoxCollider>() );

			Assert.AreEqual( Color.Blue, prefabInstance.Components.Get<ModelRenderer>().Tint );
		}

		prefabScene.Load( pfile ); // Refresh to be sure we have the latest data
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), prefabScene.Components.Get<ModelRenderer>().Tint );
	}

	static readonly string _gameObjectWithPrefabInstances = """"
	{
		"__guid": "d03c7897-8558-48ee-990f-547b61de7f9a",
		"Name": "Object",
		"Position": "788.8395,-1793.604,-1218.092",
		"Enabled": true,
		"Children": [
			{
				"__guid": "c07c9e05-a64f-4354-a98f-11bbdbec71fa",
				"__version": 1,
				"__Prefab": "___upstream_changes_multiple.prefab",
				"__PrefabInstancePatch": {
					"AddedObjects": [],
					"RemovedObjects": [],
					"PropertyOverrides": [],
					"MovedObjects": []
				},
				"__PrefabIdToInstanceId": {
					"fab370f8-2e2c-48cf-a523-e4be49723490": "c07c9e05-a64f-4354-a98f-11bbdbec71fa",
					"230b45c1-a446-42b4-af39-f7195135e31f": "19e18ce9-aba4-465c-b07b-7e63f5f05cef"
				}
			},
			{
				"__guid": "3508f8c0-9cb0-421b-a040-9d340c5611c1",
				"__version": 1,
				"__Prefab": "___upstream_changes_multiple.prefab",
				"__PrefabInstancePatch": {
					"AddedObjects": [],
					"RemovedObjects": [],
					"PropertyOverrides": [],
					"MovedObjects": []
				},
				"__PrefabIdToInstanceId": {
					"fab370f8-2e2c-48cf-a523-e4be49723490": "3508f8c0-9cb0-421b-a040-9d340c5611c1",
					"230b45c1-a446-42b4-af39-f7195135e31f": "d2679da4-2b84-4aa5-95ad-32e47a1bbed5"
				}
			}
		]
	}

	"""";

	[TestMethod]
	public void SelfNestedPrefabInstanceWithAddedGameObjectInNestedInstanceShouldCopyCorrectly()
	{
		var saveLocation = "___upstream_changes_self_nested.prefab";

		// Create our base prefab
		using var prefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( saveLocation, _basicPrefabSource );
		var pfile = ResourceLibrary.Get<PrefabFile>( saveLocation );
		var prefabScene = SceneUtility.GetPrefabScene( pfile );

		Assert.IsTrue( prefabScene.Components.Get<ModelRenderer>() is not null );

		// Create an instance in a scene
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var hierarchy = scene.CreateObject();
		hierarchy.Deserialize( Json.ParseToJsonObject( _gameObjectWithNestedPrefabInstance ) );

		// Add a new gameobject to the nested prefab instance
		var nestedPrefabInstance = hierarchy.Children[0].Children[0];

		Assert.IsNotNull( nestedPrefabInstance.Components.Get<ModelRenderer>() );
		Assert.AreEqual( 1, nestedPrefabInstance.Children.Count );

		var addedObject = new GameObject( nestedPrefabInstance );

		// Editor would make sure this is called
		nestedPrefabInstance.PrefabInstance.RefreshPatch();

		// Verify that the added object is now part of the prefab instance
		Assert.AreEqual( 2, nestedPrefabInstance.Children.Count );

		// Emulate copy paste
		var s = hierarchy.Serialize();
		var newHierarchy = scene.CreateObject();
		SceneUtility.MakeIdGuidsUnique( s );
		newHierarchy.Deserialize( s );

		// Verify that the new hierarchy has the same structure
		Assert.AreEqual( 1, newHierarchy.Children.Count );
		var newNestedPrefabInstance = newHierarchy.Children[0].Children[0];
		Assert.AreEqual( 2, newNestedPrefabInstance.Children.Count );
		Assert.IsNotNull( newNestedPrefabInstance.Components.Get<ModelRenderer>() );
	}

	[TestMethod]
	public void SelfNestedPrefabInstanceWithReferenceToOuterInstanceInNestedInstanceShouldCopyCorrectly()
	{
		var saveLocation = "___upstream_changes_self_nested.prefab";

		// Create our base prefab
		using var prefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( saveLocation, _basicPrefabSource );
		var pfile = ResourceLibrary.Get<PrefabFile>( saveLocation );
		var prefabScene = SceneUtility.GetPrefabScene( pfile );

		Assert.IsTrue( prefabScene.Components.Get<ModelRenderer>() is not null );

		// Create an instance in a scene
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var hierarchy = scene.CreateObject();
		hierarchy.Deserialize( Json.ParseToJsonObject( _gameObjectWithNestedPrefabInstance ) );

		// Add a new gameobject to the nested prefab instance
		var nestedPrefabInstance = hierarchy.Children[0].Children[0];

		Assert.IsNotNull( nestedPrefabInstance.Components.Get<ModelRenderer>() );
		Assert.AreEqual( 1, nestedPrefabInstance.Children.Count );

		var lineRenderer = nestedPrefabInstance.AddComponent<LineRenderer>();
		lineRenderer.Points = [hierarchy.Children[0]];

		// Editor would make sure this is called
		nestedPrefabInstance.PrefabInstance.RefreshPatch();

		// Emulate copy paste
		var s = hierarchy.Serialize();
		var newHierarchy = scene.CreateObject();
		SceneUtility.MakeIdGuidsUnique( s );
		newHierarchy.Deserialize( s );

		// Verify that the copied line renderer is referencing the outer instance of the new hierarchy
		var newNestedPrefabInstance = newHierarchy.Children[0].Children[0];
		Assert.AreEqual( 1, newNestedPrefabInstance.Children.Count );
		Assert.IsNotNull( newNestedPrefabInstance.Components.Get<LineRenderer>() );

		var newLineRenderer = newNestedPrefabInstance.Components.Get<LineRenderer>();
		Assert.AreEqual( 1, newLineRenderer.Points.Count );
		Assert.AreEqual( newLineRenderer.Points[0], newHierarchy.Children[0] );
	}

	[TestMethod]
	public void UpdatesToNestedPrefabFileShouldPropagateToOuterPrefabFile()
	{
		var nestedPrefabLocation = "__nestedPrefab.prefab";
		var outerPrefabLocation = "__outerPrefab.prefab";

		// Step 1: Create the nested prefab
		using var nestedPrefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( nestedPrefabLocation, _basicPrefabSource );
		var nestedPrefabFile = ResourceLibrary.Get<PrefabFile>( nestedPrefabLocation );
		var nestedPrefabScene = SceneUtility.GetPrefabScene( nestedPrefabFile );

		Assert.IsTrue( nestedPrefabScene.Components.Get<ModelRenderer>() is not null );

		// Step 2: Create the outer prefab containing the nested prefab
		using var outerPrefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( outerPrefabLocation, _outerPrefabWithNestedPrefabSource );
		var outerPrefabFile = ResourceLibrary.Get<PrefabFile>( outerPrefabLocation );
		var outerPrefabScene = SceneUtility.GetPrefabScene( outerPrefabFile );

		// Verify the outer prefab structure and its nested prefab instance
		Assert.AreEqual( 1, outerPrefabScene.Children.Count );
		var nestedPrefabInstance = outerPrefabScene.Children[0];
		Assert.IsTrue( nestedPrefabInstance.PrefabInstance != null );
		Assert.IsTrue( nestedPrefabInstance.Components.Get<ModelRenderer>() is not null );

		// Step 3: Modify the nested prefab by adding a new component
		nestedPrefabScene.AddComponent<BoxCollider>();
		Assert.IsTrue( nestedPrefabScene.Components.Get<BoxCollider>() is not null );

		// Step 4: Save the changes to the nested prefab file
		nestedPrefabScene.ToPrefabFile();

		// Step 5: Verify that the changes in the nested prefab are reflected in the outer prefab
		// The nested prefab instance should now have the BoxCollider
		Assert.AreEqual( 1, outerPrefabScene.Children.Count );
		var updatedNestedPrefabInstance = outerPrefabScene.Children[0];

		// This is the key assertion - the BoxCollider from the nested prefab should now appear 
		// in the instance within the outer prefab
		Assert.IsTrue( updatedNestedPrefabInstance.Components.Get<BoxCollider>() is not null );
	}

	[TestMethod]
	public void OutermostPrefabInstanceRoot_FromNestedPrefabShouldReturnCorrectRoot()
	{
		// Create the nested structure:
		// - Inner prefab (basic prefab)
		// - Outer prefab (contains an instance of inner prefab)

		var innerPrefabLocation = "__nestedPrefab.prefab";
		var outerPrefabLocation = "__outer_prefab.prefab";

		// Step 1: Create the inner prefab
		using var innerPrefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( innerPrefabLocation, _basicPrefabSource );
		var innerPrefabFile = ResourceLibrary.Get<PrefabFile>( innerPrefabLocation );
		var innerPrefabScene = SceneUtility.GetPrefabScene( innerPrefabFile );

		// Step 2: Create the outer prefab containing the inner prefab
		using var outerPrefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( outerPrefabLocation, _outerPrefabWithNestedPrefabSource );
		var outerPrefabFile = ResourceLibrary.Get<PrefabFile>( outerPrefabLocation );
		var outerPrefabScene = SceneUtility.GetPrefabScene( outerPrefabFile );

		// Step 3: Create a scene and instantiate the outer prefab
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var outerInstance = outerPrefabScene.Clone( Vector3.Zero );
		Assert.IsTrue( outerInstance.IsPrefabInstanceRoot );

		// Step 4: Get the inner prefab instance within the outer prefab
		var innerInstance = outerInstance.Children[0];
		Assert.IsTrue( innerInstance.IsPrefabInstanceRoot );

		// Get a component inside the inner instance to test from
		var modelRenderer = innerInstance.Components.Get<ModelRenderer>();
		Assert.IsNotNull( modelRenderer );

		// Step 5: Test OutermostPrefabInstanceRoot from different levels

		// Test from innerInstance - should return outerInstance
		Assert.AreEqual( outerInstance, innerInstance.OutermostPrefabInstanceRoot );

		// Test from outerInstance - should return itself
		Assert.AreEqual( outerInstance, outerInstance.OutermostPrefabInstanceRoot );

		// Test IsOutermostPrefabInstanceRoot property
		Assert.IsTrue( outerInstance.IsOutermostPrefabInstanceRoot );
		Assert.IsFalse( innerInstance.IsOutermostPrefabInstanceRoot );
	}

	[TestMethod]
	public void OutermostPrefabInstanceRoot_RuntimeAddedPrefabShouldReturnItself()
	{
		// Step 1: Create two prefabs
		var prefabALocation = "__prefab_a.prefab";
		var prefabBLocation = "__prefab_b.prefab";

		using var prefabA = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( prefabALocation, _basicPrefabSource );
		var prefabAFile = ResourceLibrary.Get<PrefabFile>( prefabALocation );
		var prefabAScene = SceneUtility.GetPrefabScene( prefabAFile );

		// Create prefab B with a different tint color to distinguish it
		string prefabBSource = _basicPrefabSource.Replace( "1,0,0,1", "0,1,0,1" );
		using var prefabB = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( prefabBLocation, prefabBSource );
		var prefabBFile = ResourceLibrary.Get<PrefabFile>( prefabBLocation );
		var prefabBScene = SceneUtility.GetPrefabScene( prefabBFile );

		// Step 2: Create a scene and instantiate prefab A
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var instanceA = prefabAScene.Clone( Vector3.Zero );
		Assert.IsTrue( instanceA.IsPrefabInstanceRoot );
		Assert.IsTrue( instanceA.IsOutermostPrefabInstanceRoot );

		// Step 3: At runtime, instantiate prefab B and parent it to instanceA
		var instanceB = prefabBScene.Clone( Vector3.Zero );
		instanceB.SetParent( instanceA );

		// Editor would run this
		instanceA.PrefabInstance.RefreshPatch();

		// Even though instanceB is now a child of instanceA,
		// it should still be its own outermost prefab instance root
		// because it was added at runtime and not part of the original prefab

		// Test from instanceB
		Assert.AreEqual( instanceB, instanceB.OutermostPrefabInstanceRoot );
		Assert.IsTrue( instanceB.IsOutermostPrefabInstanceRoot );

		// Step 4: Create a new GameObject inside instanceB
		var childOfInstanceB = scene.CreateObject();
		childOfInstanceB.SetParent( instanceB );

		// This child is part of instanceB, so its outermost prefab instance root should be instanceB
		Assert.AreEqual( instanceB, childOfInstanceB.OutermostPrefabInstanceRoot );

		// Step 5: Create a new GameObject inside instanceA (but not part of instanceB)
		var childOfInstanceA = scene.CreateObject();
		childOfInstanceA.SetParent( instanceA );

		// This child is part of instanceA, so its outermost prefab instance root should be instanceA
		Assert.AreEqual( instanceA, childOfInstanceA.OutermostPrefabInstanceRoot );

		// Verify the hierarchy
		Assert.AreEqual( 2, instanceA.Children.Count ); // instanceB and childOfInstanceA
		Assert.AreEqual( 1, instanceB.Children.Count ); // childOfInstanceB
	}

	static readonly string _outerPrefabWithNestedPrefabSource = """"
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
				"__Prefab": "__nestedPrefab.prefab",
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

	private void TestOverriddenPropertyOnPrefabInstance(
		   string saveLocation,
		   string prefabSource,
		   Action<GameObject> assertInitialState,
		   Action<GameObject> setOverrideAction,
		   Action<GameObject> assertOverrideSet,
		   Action<GameObject> assertOverridePreserved
	   )
	{
		// Create the prefab from JSON source
		using var prefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( saveLocation, prefabSource );
		var pfile = ResourceLibrary.Get<PrefabFile>( saveLocation );
		var prefabScene = SceneUtility.GetPrefabScene( pfile );

		// Assert initial state
		assertInitialState( prefabScene );

		// Create a new scene and instantiate the prefab
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var instance = prefabScene.Clone( Vector3.Zero );

		// Override the property in the instance
		setOverrideAction( instance );

		// Assert that the override was set
		assertOverrideSet( instance );

		// Simulate the editor updating the patch after modification
		instance.PrefabInstance.RefreshPatch();

		// Ensure that when updating from the prefab the override is preserved
		instance.UpdateFromPrefab();

		// Assert that the override was preserved
		assertOverridePreserved( instance );
	}

	[TestMethod]
	public void OverriddenEnabledStateOnPrefabInstance()
	{
		TestOverriddenPropertyOnPrefabInstance(
			"___prefab_enabled_override.prefab",
			_basicPrefabSource,
			prefab => Assert.IsTrue( prefab.Enabled, "Prefab should be enabled initially." ),
			instance => instance.Enabled = false,
			instance => Assert.IsFalse( instance.Enabled, "Prefab instance Enabled should be overridden to false." ),
			instance => Assert.IsFalse( instance.Enabled, "Prefab instance override should be preserved after UpdateFromPrefab." )
		);
	}

	[TestMethod]
	public void OverriddenLocalTransformOnPrefabInstance()
	{
		var newTransform = new Transform( new Vector3( 1, 2, 3 ), Rotation.FromYaw( 45 ), new Vector3( 1, 2, 3 ) );
		TestOverriddenPropertyOnPrefabInstance(
			"___prefab_localtransform_override.prefab",
			_basicPrefabSource,
			prefab => Assert.AreEqual( new Transform( new Vector3( 788.8395f, -1793.604f, -1218.092f ), Rotation.Identity, new Vector3( 10, 10, 10 ) ), prefab.LocalTransform, "Prefab should have default LocalTransform." ),
			instance => instance.LocalTransform = newTransform,
			instance => Assert.AreEqual( newTransform, instance.LocalTransform, "Prefab instance LocalTransform should be overridden to new value." ),
			instance => Assert.AreEqual( newTransform, instance.LocalTransform, "Prefab instance override should be preserved after UpdateFromPrefab." )
		);
	}

	[TestMethod]
	public void OverriddenTagsOnPrefabInstance()
	{
		TestOverriddenPropertyOnPrefabInstance(
			"___prefab_tags_override.prefab",
			_basicPrefabSource,
			prefab => Assert.IsFalse( prefab.Tags.Has( "TestTag" ), "Prefab should not contain 'TestTag' initially." ),
			instance => instance.Tags.Add( "TestTag" ),
			instance => Assert.IsTrue( instance.Tags.Has( "TestTag" ), "Prefab instance should contain 'TestTag' after override." ),
			instance => Assert.IsTrue( instance.Tags.Has( "TestTag" ), "Prefab instance override of Tags should be preserved after UpdateFromPrefab." )
		);
	}

	[TestMethod]
	public void RevertInstanceToPrefab_DoesNotRevertTransformNameFlagsOnRoot()
	{
		var saveLocation = "___revert_instance_test.prefab";

		// Create our base prefab
		using var prefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( saveLocation, _basicPrefabSource );
		var pfile = ResourceLibrary.Get<PrefabFile>( saveLocation );
		var prefabScene = SceneUtility.GetPrefabScene( pfile );

		// Verify initial prefab state
		Assert.AreEqual( "Object", prefabScene.Name );
		Assert.AreEqual( new Transform( new Vector3( 788.8395f, -1793.604f, -1218.092f ), Rotation.Identity, new Vector3( 10, 10, 10 ) ), prefabScene.LocalTransform );
		Assert.IsTrue( prefabScene.Enabled );

		// Create a scene and instantiate the prefab
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var instance = prefabScene.Clone( Vector3.Zero );

		// Modify instance's transform, name, and flags
		instance.LocalTransform = new Transform( new Vector3( 100, 200, 300 ), Rotation.FromYaw( 45 ), Vector3.One );
		instance.Name = "ModifiedInstance";
		instance.Enabled = false;
		instance.Flags = GameObjectFlags.DontDestroyOnLoad;

		// Modify a component property
		instance.Components.Get<ModelRenderer>( true ).Tint = Color.Blue;

		// The editor would run this after a modification
		instance.PrefabInstance.RefreshPatch();

		// Revert instance to prefab
		EditorUtility.Prefabs.RevertInstanceToPrefab( instance );

		// Assert that transform, name, and flags are not reverted
		Assert.AreEqual( new Transform( new Vector3( 100, 200, 300 ), Rotation.FromYaw( 45 ), Vector3.One ), instance.LocalTransform );
		Assert.AreEqual( "ModifiedInstance", instance.Name );
		Assert.IsFalse( instance.Enabled );
		Assert.IsTrue( instance.Flags.HasFlag( GameObjectFlags.DontDestroyOnLoad ) );

		// Assert that component overrides are reverted
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), instance.Components.Get<ModelRenderer>( true ).Tint );
	}

	[TestMethod]
	public void NestedPrefabInstanceRefreshDeserialization()
	{
		// Setup prefab file paths
		var nestedNestedPrefabLocation = "__nested_nested.prefab";
		var nestedPrefabLocation = "__nested.prefab";
		var basePrefabLocation = "__base.prefab";

		// Step 1: Create the innermost prefab
		using var nestedNestedPrefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( nestedNestedPrefabLocation, _nestedNestedPrefabSource );
		var nestedNestedPrefabFile = ResourceLibrary.Get<PrefabFile>( nestedNestedPrefabLocation );
		var nestedNestedPrefabScene = SceneUtility.GetPrefabScene( nestedNestedPrefabFile );

		// Step 2: Create the middle prefab that contains an instance of the innermost prefab
		using var nestedPrefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( nestedPrefabLocation, _nestedPrefabSource );
		var nestedPrefabFile = ResourceLibrary.Get<PrefabFile>( nestedPrefabLocation );
		var nestedPrefabScene = SceneUtility.GetPrefabScene( nestedPrefabFile );

		// Step 3: Create the outermost prefab that contains an instance of the middle prefab
		using var basePrefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( basePrefabLocation, _basePrefabSource );
		var basePrefabFile = ResourceLibrary.Get<PrefabFile>( basePrefabLocation );
		var basePrefabScene = SceneUtility.GetPrefabScene( basePrefabFile );

		// Step 4: Create a test scene and instantiate the outermost prefab
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var baseInstance = basePrefabScene.Clone( Vector3.Zero );

		// Step 5: Verify the initial structure is correct
		Assert.IsTrue( baseInstance.IsPrefabInstanceRoot );
		Assert.AreEqual( "BasePrefab", baseInstance.Name );
		Assert.IsNotNull( baseInstance.Components.Get<ModelRenderer>() );

		Assert.AreEqual( 1, baseInstance.Children.Count );
		var nestedInstanceInScene = baseInstance.Children[0];
		Assert.IsTrue( nestedInstanceInScene.IsPrefabInstanceRoot );
		Assert.AreEqual( "NestedPrefab", nestedInstanceInScene.Name );
		Assert.IsNotNull( nestedInstanceInScene.Components.Get<BoxCollider>() );

		Assert.AreEqual( 1, nestedInstanceInScene.Children.Count );
		var nestedNestedInstanceInScene = nestedInstanceInScene.Children[0];
		Assert.IsTrue( nestedNestedInstanceInScene.IsPrefabInstanceRoot );
		Assert.AreEqual( "NestedNestedPrefab", nestedNestedInstanceInScene.Name );
		Assert.IsNotNull( nestedNestedInstanceInScene.Components.Get<NavMeshArea>() );

		// Step 6: Serialize and then deserialize the nested_nested instance
		var serializedData = baseInstance.Serialize();

		// Store some IDs for comparison after refresh
		var originalId = nestedNestedInstanceInScene.Id;
		var originalComponentId = nestedNestedInstanceInScene.Components.Get<NavMeshArea>().Id;
		var originalPrefabSource = nestedNestedInstanceInScene.PrefabInstance.PrefabSource;

		// Refresh through deserialization
		baseInstance.Deserialize( serializedData, new GameObject.DeserializeOptions { IsRefreshing = true } );

		// Step 7: Assert that the instance maintains proper identity and prefab relationships
		Assert.AreEqual( originalId, nestedNestedInstanceInScene.Id, "GameObject ID should be preserved after refresh" );
		Assert.AreEqual( originalComponentId, nestedNestedInstanceInScene.Components.Get<NavMeshArea>().Id, "Component ID should be preserved after refresh" );

		// Verify the prefab instance relationships are still intact
		Assert.IsTrue( nestedNestedInstanceInScene.IsPrefabInstanceRoot, "Should still be a prefab instance root after refresh" );
		Assert.IsTrue( nestedNestedInstanceInScene.IsNestedPrefabInstanceRoot, "Should still be a nested prefab instance root after refresh" );
		Assert.IsNotNull( nestedNestedInstanceInScene.PrefabInstance, "Should still have prefab instance data after refresh" );
		Assert.AreEqual( originalPrefabSource, nestedNestedInstanceInScene.PrefabInstance.PrefabSource, "Prefab source should be preserved" );

		// Verify the full hierarchy is still intact and properly connected
		Assert.AreEqual( nestedInstanceInScene, nestedNestedInstanceInScene.Parent, "Parent relationship should be preserved" );
		Assert.IsTrue( nestedInstanceInScene.IsPrefabInstanceRoot, "Should still be a prefab instance root after refresh" );
		Assert.IsTrue( nestedInstanceInScene.IsNestedPrefabInstanceRoot, "Should still be a nested prefab instance root after refresh" );
		Assert.IsTrue( nestedInstanceInScene.Children.Contains( nestedNestedInstanceInScene ), "Child relationship should be preserved" );

		// Verify the outermost prefab instance root reference is still correct
		Assert.AreEqual( baseInstance, nestedNestedInstanceInScene.OutermostPrefabInstanceRoot, "Outermost prefab root reference should be correct" );
	}

	[TestMethod]
	public void BreakFromPrefabConvertsNestedInstancesToFullPrefabInstances()
	{
		// 1. Create the nested prefab (innermost)
		var innerPrefabLocation = "__nestedPrefab.prefab";
		using var innerPrefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( innerPrefabLocation, _basicPrefabSource );
		var innerPrefabFile = ResourceLibrary.Get<PrefabFile>( innerPrefabLocation );
		var innerPrefabScene = SceneUtility.GetPrefabScene( innerPrefabFile );

		// 2. Create the outer prefab that contains an instance of the nested prefab
		var outerPrefabLocation = "__outer_for_break.prefab";
		using var outerPrefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( outerPrefabLocation, _outerPrefabWithNestedPrefabSource );
		var outerPrefabFile = ResourceLibrary.Get<PrefabFile>( outerPrefabLocation );
		var outerPrefabScene = SceneUtility.GetPrefabScene( outerPrefabFile );

		// 3. Create a scene and instantiate the outer prefab
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var outerInstance = outerPrefabScene.Clone( Vector3.Zero );

		// Verify initial state
		Assert.IsTrue( outerInstance.IsPrefabInstanceRoot, "Outer instance should be a prefab instance root" );
		Assert.AreEqual( 1, outerInstance.Children.Count, "Outer instance should have one child" );

		var nestedInstance = outerInstance.Children[0];
		Assert.IsTrue( nestedInstance.IsPrefabInstanceRoot, "Nested instance should be a prefab instance root" );
		Assert.IsTrue( nestedInstance.IsNestedPrefabInstanceRoot, "Nested instance should be a nested prefab instance root" );
		Assert.IsFalse( nestedInstance.IsOutermostPrefabInstanceRoot, "Nested instance should not be an outermost prefab instance root" );
		Assert.AreEqual( outerInstance, nestedInstance.OutermostPrefabInstanceRoot, "Outer instance should be the outermost prefab instance root" );

		// 4. Break the outer instance from its prefab
		outerInstance.BreakFromPrefab();

		// 5. Verify that outer instance is no longer a prefab instance
		Assert.IsFalse( outerInstance.IsPrefabInstanceRoot, "Outer instance should no longer be a prefab instance root after breaking" );
		Assert.IsFalse( outerInstance.IsPrefabInstance, "Outer instance should no longer be a prefab instance after breaking" );

		// 6. Verify that nested instance is still a prefab instance but now a full instance
		Assert.IsTrue( nestedInstance.IsPrefabInstanceRoot, "Nested instance should still be a prefab instance root after breaking parent" );
		Assert.IsTrue( nestedInstance.IsPrefabInstance, "Nested instance should still be a prefab instance after breaking parent" );
		Assert.IsFalse( nestedInstance.IsNestedPrefabInstanceRoot, "Nested instance should no longer be a nested prefab instance root" );
		Assert.IsTrue( nestedInstance.IsOutermostPrefabInstanceRoot, "Nested instance should now be an outermost prefab instance root" );
		Assert.AreEqual( nestedInstance, nestedInstance.OutermostPrefabInstanceRoot, "Nested instance should be its own outermost prefab instance root" );

		// 7. Verify that the nested instance can still be updated from its prefab
		// First, modify the original prefab by adding a new component
		innerPrefabScene.AddComponent<BoxCollider>();
		innerPrefabScene.ToPrefabFile();

		// Update the nested instance and verify it received the changes
		nestedInstance.UpdateFromPrefab();
		Assert.IsTrue( nestedInstance.Components.Get<BoxCollider>() is not null,
			"Nested instance should receive updates from its prefab after parent instance broke from prefab" );
	}

	// https://github.com/Facepunch/sbox-public/issues/930
	[TestMethod]
	public void WriteGameObjectToPrefab_WithNestedInstance()
	{
		// 1. Setup base prefab
		var basePrefabLocation = "__writeback_nested_base.prefab";
		using var basePrefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( basePrefabLocation, _basicPrefabSource );
		var basePrefabFile = ResourceLibrary.Get<PrefabFile>( basePrefabLocation );
		var basePrefabScene = SceneUtility.GetPrefabScene( basePrefabFile );

		// 2. Create another prefab to use as nested
		var nestedPrefabLocation = "__writeback_nested_inner.prefab";
		string nestedPrefabSource = _basicPrefabSource.Replace( "1,0,0,1", "0,1,0,1" ); // Change color to green
		using var nestedPrefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( nestedPrefabLocation, nestedPrefabSource );
		var nestedPrefabFile = ResourceLibrary.Get<PrefabFile>( nestedPrefabLocation );
		var nestedPrefabScene = SceneUtility.GetPrefabScene( nestedPrefabFile );

		// 3. Create a scene and instantiate the base prefab
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var baseInstance = basePrefabScene.Clone( Vector3.Zero );

		// Verify initial state
		Assert.IsTrue( baseInstance.IsPrefabInstanceRoot );
		Assert.AreEqual( 0, baseInstance.Children.Count );

		// 4. Add a nested prefab instance to the base instance
		var nestedInstance = nestedPrefabScene.Clone( Vector3.Zero );
		nestedInstance.SetParent( baseInstance );

		var nestedOriginalId = nestedInstance.Id;

		// The editor would call this after modification
		baseInstance.PrefabInstance.RefreshPatch();

		// Verify the nested instance was added
		Assert.AreEqual( 1, baseInstance.Children.Count );
		Assert.IsTrue( baseInstance.Children[0].IsPrefabInstanceRoot );
		Assert.IsTrue( baseInstance.PrefabInstance.IsAddedGameObject( nestedInstance ) );
		Assert.IsTrue( baseInstance.PrefabInstance.IsModified() );

		// 5. Write the modified instance back to its prefab
		EditorUtility.Prefabs.WriteInstanceToPrefab( baseInstance, true );

		// Need to call this manually since we skip file write
		EditorScene.UpdatePrefabInstances( basePrefabFile );

		// 6. Verify that the prefab now contains the nested instance
		basePrefabScene = SceneUtility.GetPrefabScene( basePrefabFile );
		Assert.AreEqual( 1, basePrefabScene.Children.Count );
		Assert.IsTrue( basePrefabScene.Children[0].IsPrefabInstanceRoot );

		// 7. Verify that the instance patch is now empty since it matches the prefab
		Assert.IsFalse( baseInstance.PrefabInstance.IsModified() );
		Assert.IsFalse( baseInstance.PrefabInstance.IsAddedGameObject( nestedInstance ) );

		// 8. Verify id mapping is intact
		Assert.IsTrue( baseInstance.PrefabInstance.InstanceToPrefabLookup.ContainsKey( nestedOriginalId ) );
	}

	// Innermost prefab definition
	static readonly string _nestedNestedPrefabSource = """"
	{
		"__guid": "1e26fa20-684e-44af-8a52-30df3a45ea28",
		"Name": "NestedNestedPrefab",
		"Position": "0,0,0",
		"Scale": "1,1,1",
		"Enabled": true,
		"Components": [
			{
				"__type": "NavMeshArea",
				"__guid": "ccefdfc7-aa08-444d-b5db-b2433aeb235e",
				"Bounds": "64,64,64",
				"Enabled": true
			}
		],
		"Children": []
	}
	"""";

	// Middle prefab definition with instance of innermost prefab
	static readonly string _nestedPrefabSource = """"
	{
		"__guid": "def12345-6789-0abc-def1-23456789abcd",
		"Name": "NestedPrefab",
		"Position": "0,0,0",
		"Scale": "1,1,1",
		"Enabled": true,
		"Components": [
			{
				"__type": "BoxCollider",
				"__guid": "98765432-fedc-ba98-7654-321012345678",
				"Size": "50,50,50",
				"IsTrigger": false,
				"Enabled": true
			}
		],
		"Children": [
			{
				"__guid": "4515d718-070a-4de3-bbda-9835c6e118c9",
				"__version": 1,
				"__Prefab": "__nested_nested.prefab",
				"__PrefabInstancePatch": {
					"AddedObjects": [],
					"RemovedObjects": [],
					"PropertyOverrides": [],
					"MovedObjects": []
				},
				"__PrefabIdToInstanceId": {
					"1e26fa20-684e-44af-8a52-30df3a45ea28": "4515d718-070a-4de3-bbda-9835c6e118c9",
					"ccefdfc7-aa08-444d-b5db-b2433aeb235e": "3bfd4ead-0d39-4ad1-8707-e45f47045776"
				}
			}
		]
	}
	"""";

	// Outermost prefab definition with instance of middle prefab
	static readonly string _basePrefabSource = """"
	{
		"__guid": "01234567-89ab-cdef-0123-456789abcdef",
		"Name": "BasePrefab",
		"Position": "0,0,0",
		"Scale": "1,1,1",
		"Enabled": true,
		"Components": [
			{
				"__type": "ModelRenderer",
				"__guid": "fedcba98-7654-3210-fedc-ba9876543210",
				"BodyGroups": 18446744073709551615,
				"MaterialGroup": null,
				"MaterialOverride": null,
				"Model": null,
				"RenderType": "On",
				"Tint": "1,0,0,1"
			}
		],
		"Children": [
			{
				"__guid": "2fe84d07-0e73-48f7-a8c8-e6d90c58082d",
				"__version": 1,
				"__Prefab": "__nested.prefab",
				"__PrefabInstancePatch": {
					"AddedObjects": [],
					"RemovedObjects": [],
					"PropertyOverrides": [],
					"MovedObjects": []
				},
				"__PrefabIdToInstanceId": {
					"def12345-6789-0abc-def1-23456789abcd": "2fe84d07-0e73-48f7-a8c8-e6d90c58082d",
					"4515d718-070a-4de3-bbda-9835c6e118c9": "a5c72647-dc84-4557-b8fd-112e8b16b3b5",
					"3bfd4ead-0d39-4ad1-8707-e45f47045776": "34567890-abcd-ef12-3456-789012345678",
					"98765432-fedc-ba98-7654-321012345678": "abcdef01-2345-6789-abcd-ef0123456789"
				}
			}
		]
	}
	"""";
}

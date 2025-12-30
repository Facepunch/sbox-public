namespace Prefab;

[TestClass]
/// <summary>
/// Tests for PrefabInstanceData methods in GameObject.Prefab.cs
/// </summary>
public class InstanceDataMethods
{
	[TestMethod]
	public void IsAddedGameObject_ReturnsTrue_ForAddedObjects()
	{
		var saveLocation = "___added_object_test.prefab";
		using var prefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( saveLocation, _basicPrefabSource );
		var prefabScene = SceneUtility.GetPrefabScene( ResourceLibrary.Get<PrefabFile>( saveLocation ) );

		// Create instance in scene
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var instance = prefabScene.Clone( Vector3.Zero );

		// Add a new child GameObject to the instance
		var addedChild = new GameObject( instance );
		addedChild.Name = "AddedChild";

		// The editor would call this after modification
		instance.PrefabInstance.RefreshPatch();

		// Verify the object is recognized as added
		Assert.IsTrue( instance.PrefabInstance.IsAddedGameObject( addedChild ) );
		Assert.IsFalse( instance.PrefabInstance.IsAddedGameObject( instance ) );
	}

	[TestMethod]
	public void IsAddedComponent_ReturnsTrue_ForAddedComponents()
	{
		var saveLocation = "___added_component_test.prefab";
		using var prefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( saveLocation, _basicPrefabSource );
		var prefabScene = SceneUtility.GetPrefabScene( ResourceLibrary.Get<PrefabFile>( saveLocation ) );

		// Create instance in scene
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var instance = prefabScene.Clone( Vector3.Zero );

		// Verify initial state - should have only ModelRenderer
		Assert.IsTrue( instance.Components.Get<ModelRenderer>() is not null );
		Assert.IsTrue( instance.Components.Get<BoxCollider>() is null );

		// Add a new component
		var boxCollider = instance.AddComponent<BoxCollider>();

		// The editor would call this after modification
		instance.PrefabInstance.RefreshPatch();

		// Verify the component is recognized as added
		Assert.IsTrue( instance.PrefabInstance.IsAddedComponent( boxCollider ) );
		Assert.IsFalse( instance.PrefabInstance.IsAddedComponent( instance.Components.Get<ModelRenderer>() ) );
	}

	[TestMethod]
	public void IsPropertyOverridden_ReturnsTrue_ForModifiedProperties()
	{
		var saveLocation = "___property_override_test.prefab";
		using var prefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( saveLocation, _basicPrefabSource );
		var prefabScene = SceneUtility.GetPrefabScene( ResourceLibrary.Get<PrefabFile>( saveLocation ) );

		// Create instance in scene
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var instance = prefabScene.Clone( Vector3.Zero );

		// Original value is red (1,0,0,1)
		var modelRenderer = instance.Components.Get<ModelRenderer>();
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), modelRenderer.Tint );

		// Override property value
		modelRenderer.Tint = Color.Blue;

		// The editor would call this after modification
		instance.PrefabInstance.RefreshPatch();

		// Verify the property is recognized as overridden
		Assert.IsTrue( instance.PrefabInstance.IsPropertyOverridden( modelRenderer, "Tint" ) );
		Assert.IsFalse( instance.PrefabInstance.IsPropertyOverridden( modelRenderer, "Model" ) );
	}

	[TestMethod]
	public void IsGameObjectModified_ReturnsTrueForModifiedGameObjects()
	{
		var saveLocation = "___go_modified_test.prefab";
		using var prefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( saveLocation, _basicPrefabSource );
		var prefabScene = SceneUtility.GetPrefabScene( ResourceLibrary.Get<PrefabFile>( saveLocation ) );

		// Create instance in scene
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var instance = prefabScene.Clone( Vector3.Zero );

		// Not modified initially
		Assert.IsFalse( instance.PrefabInstance.IsGameObjectModified( instance, true ) );

		// Modify component property
		instance.Components.Get<ModelRenderer>().Tint = Color.Blue;
		instance.PrefabInstance.RefreshPatch();

		// Should now be modified
		Assert.IsTrue( instance.PrefabInstance.IsGameObjectModified( instance, true ) );

		// Add a component to further modify
		instance.AddComponent<BoxCollider>();
		instance.PrefabInstance.RefreshPatch();

		// Should still be modified
		Assert.IsTrue( instance.PrefabInstance.IsGameObjectModified( instance, true ) );
	}

	[TestMethod]
	public void IsComponentModified_ReturnsTrueForModifiedComponents()
	{
		var saveLocation = "___component_modified_test.prefab";
		using var prefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( saveLocation, _basicPrefabSource );
		var prefabScene = SceneUtility.GetPrefabScene( ResourceLibrary.Get<PrefabFile>( saveLocation ) );

		// Create instance in scene
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var instance = prefabScene.Clone( Vector3.Zero );

		var modelRenderer = instance.Components.Get<ModelRenderer>();

		// Not modified initially
		Assert.IsFalse( instance.PrefabInstance.IsComponentModified( modelRenderer ) );

		// Modify component property
		modelRenderer.Tint = Color.Blue;
		instance.PrefabInstance.RefreshPatch();

		// Should now be modified
		Assert.IsTrue( instance.PrefabInstance.IsComponentModified( modelRenderer ) );
	}

	[TestMethod]
	public void RevertPropertyChange_ResetsPropertyToOriginalValue()
	{
		var saveLocation = "___revert_property_test.prefab";
		using var prefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( saveLocation, _basicPrefabSource );
		var prefabScene = SceneUtility.GetPrefabScene( ResourceLibrary.Get<PrefabFile>( saveLocation ) );

		// Create instance in scene
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var instance = prefabScene.Clone( Vector3.Zero );

		var modelRenderer = instance.Components.Get<ModelRenderer>();
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), modelRenderer.Tint );

		// Modify component property
		modelRenderer.Tint = Color.Blue;
		modelRenderer.CreateAttachments = true;
		instance.PrefabInstance.RefreshPatch();

		// Verify it's modified
		Assert.AreEqual( Color.Blue, modelRenderer.Tint );
		Assert.IsTrue( instance.PrefabInstance.IsPropertyOverridden( modelRenderer, "Tint" ) );

		// Revert the property change
		instance.PrefabInstance.RevertPropertyChange( modelRenderer, "Tint" );

		// Verify it's reverted
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), modelRenderer.Tint );
		Assert.IsFalse( instance.PrefabInstance.IsPropertyOverridden( modelRenderer, "Tint" ) );

		// Verify other properties are unchanged
		Assert.IsTrue( modelRenderer.CreateAttachments );
	}

	[TestMethod]
	public void RevertComponentChanges_ResetsComponentToOriginalState()
	{
		var saveLocation = "___revert_component_test.prefab";
		using var prefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( saveLocation, _basicPrefabSource );
		var prefabScene = SceneUtility.GetPrefabScene( ResourceLibrary.Get<PrefabFile>( saveLocation ) );

		// Create instance in scene
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var instance = prefabScene.Clone( Vector3.Zero );

		var modelRenderer = instance.Components.Get<ModelRenderer>();
		instance.AddComponent<BoxCollider>();

		// Make multiple property changes
		modelRenderer.Tint = Color.Blue;
		modelRenderer.RenderType = ModelRenderer.ShadowRenderType.ShadowsOnly;
		instance.PrefabInstance.RefreshPatch();

		// Verify changes
		Assert.AreEqual( Color.Blue, modelRenderer.Tint );
		Assert.AreEqual( ModelRenderer.ShadowRenderType.ShadowsOnly, modelRenderer.RenderType );

		// Revert all component changes
		instance.PrefabInstance.RevertComponentChanges( modelRenderer );

		// Verify all properties are reset
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), modelRenderer.Tint );
		Assert.AreEqual( ModelRenderer.ShadowRenderType.On, modelRenderer.RenderType );
		Assert.IsFalse( instance.PrefabInstance.IsComponentModified( modelRenderer ) );

		// Make sure the added object still exists
		Assert.IsNotNull( instance.Components.Get<BoxCollider>() );
	}

	[TestMethod]
	public void RevertGameObjectChanges_ResetsGameObjectToOriginalState()
	{
		var saveLocation = "___revert_gameobject_test.prefab";
		using var prefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( saveLocation, _basicPrefabSource );
		var prefabScene = SceneUtility.GetPrefabScene( ResourceLibrary.Get<PrefabFile>( saveLocation ) );

		// Create instance in scene
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var instance = prefabScene.Clone( Vector3.Zero );

		// Original state
		var originalComponentCount = instance.Components.Count;
		var originalChildCount = instance.Children.Count;

		// Modify properties, add a component, and add a child
		instance.Components.Get<ModelRenderer>().Tint = Color.Blue;
		instance.AddComponent<BoxCollider>();
		var addedChild = new GameObject( instance );
		addedChild.Name = "AddedChild";
		instance.PrefabInstance.RefreshPatch();

		// Verify modifications
		Assert.AreEqual( originalComponentCount + 1, instance.Components.Count );
		Assert.AreEqual( originalChildCount + 1, instance.Children.Count );
		Assert.AreEqual( Color.Blue, instance.Components.Get<ModelRenderer>().Tint );

		// Revert all GameObject changes
		instance.PrefabInstance.RevertGameObjectChanges( instance );

		// Process deletes to finalize the revert, in an editor or game this would be called end of frame
		scene.ProcessDeletes();

		// Verify everything is reset
		Assert.AreEqual( originalComponentCount, instance.Components.Count );
		Assert.AreEqual( originalChildCount, instance.Children.Count );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), instance.Components.Get<ModelRenderer>().Tint );
		Assert.IsNull( instance.Components.Get<BoxCollider>() );
		Assert.IsFalse( instance.Children.Any( c => c.Name == "AddedChild" ) );
		Assert.IsFalse( instance.PrefabInstance.IsGameObjectModified( instance, true ) );
	}

	[TestMethod]
	public void ApplyPropertyChangeToPrefab_UpdatesPrefabWithInstanceProperty()
	{
		var saveLocation = "___apply_property_test.prefab";
		using var prefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( saveLocation, _basicPrefabSource );
		var prefabFile = ResourceLibrary.Get<PrefabFile>( saveLocation );
		var prefabScene = SceneUtility.GetPrefabScene( prefabFile );

		// Create instance in scene
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var instance = prefabScene.Clone( Vector3.Zero );

		var modelRenderer = instance.Components.Get<ModelRenderer>();

		// Modify property
		modelRenderer.Tint = Color.Blue;
		instance.PrefabInstance.RefreshPatch();

		// Apply the property change back to the prefab
		instance.PrefabInstance.ApplyPropertyChangeToPrefab( modelRenderer, "Tint" );

		// Reload prefab scene to verify change
		prefabScene = SceneUtility.GetPrefabScene( prefabFile );

		// Verify prefab was updated
		Assert.AreEqual( Color.Blue, prefabScene.Components.Get<ModelRenderer>().Tint );

		// Create a new instance - should have the updated property
		var newInstance = prefabScene.Clone( Vector3.Right * 100 );
		Assert.AreEqual( Color.Blue, newInstance.Components.Get<ModelRenderer>().Tint );
	}

	[TestMethod]
	public void ApplyComponentChangesToPrefab_UpdatesPrefabWithInstanceComponent()
	{
		var saveLocation = "___apply_component_test.prefab";
		using var prefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( saveLocation, _basicPrefabSource );
		var prefabFile = ResourceLibrary.Get<PrefabFile>( saveLocation );
		var prefabScene = SceneUtility.GetPrefabScene( prefabFile );

		// Create instance in scene
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var instance = prefabScene.Clone( Vector3.Zero );

		var modelRenderer = instance.Components.Get<ModelRenderer>();

		// Modify multiple properties
		modelRenderer.Tint = Color.Blue;
		modelRenderer.RenderType = ModelRenderer.ShadowRenderType.ShadowsOnly;
		instance.PrefabInstance.RefreshPatch();

		// Apply all component changes back to the prefab
		instance.PrefabInstance.ApplyComponentChangesToPrefab( modelRenderer );

		// Reload prefab scene to verify changes
		prefabScene = SceneUtility.GetPrefabScene( prefabFile );

		// Verify prefab was updated with all properties
		Assert.AreEqual( Color.Blue, prefabScene.Components.Get<ModelRenderer>().Tint );
		Assert.AreEqual( ModelRenderer.ShadowRenderType.ShadowsOnly, prefabScene.Components.Get<ModelRenderer>().RenderType );

		// Create a new instance - should have all the updated properties
		var newInstance = prefabScene.Clone( Vector3.Right * 100 );
		Assert.AreEqual( Color.Blue, newInstance.Components.Get<ModelRenderer>().Tint );
		Assert.AreEqual( ModelRenderer.ShadowRenderType.ShadowsOnly, newInstance.Components.Get<ModelRenderer>().RenderType );
	}

	[TestMethod]
	public void ApplyGameObjectChangesToPrefab_UpdatesPrefabWithAllInstanceChanges()
	{
		var saveLocation = "___apply_gameobject_test.prefab";
		using var prefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( saveLocation, _basicPrefabSource );
		var prefabFile = ResourceLibrary.Get<PrefabFile>( saveLocation );
		var prefabScene = SceneUtility.GetPrefabScene( prefabFile );

		// Create instance in scene
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var instance = prefabScene.Clone( Vector3.Zero );

		// Original state
		Assert.IsNull( prefabScene.Components.Get<BoxCollider>() );
		Assert.AreEqual( 0, prefabScene.Children.Count );

		// Make various changes to the instance
		instance.Components.Get<ModelRenderer>().Tint = Color.Blue;
		var boxCollider = instance.AddComponent<BoxCollider>();
		boxCollider.Scale = new Vector3( 5, 5, 5 );
		var childObject = new GameObject( instance );
		childObject.Name = "ChildObject";
		childObject.AddComponent<NavMeshArea>();
		instance.PrefabInstance.RefreshPatch();

		// Apply all GameObject changes back to the prefab
		instance.PrefabInstance.ApplyGameObjectChangesToPrefab( instance );

		// Reload prefab scene to verify changes
		prefabScene = SceneUtility.GetPrefabScene( prefabFile );

		// Verify prefab was updated with all changes
		Assert.AreEqual( Color.Blue, prefabScene.Components.Get<ModelRenderer>().Tint );
		Assert.IsNotNull( prefabScene.Components.Get<BoxCollider>() );
		Assert.AreEqual( new Vector3( 5, 5, 5 ), prefabScene.Components.Get<BoxCollider>().Scale );
		Assert.AreEqual( 1, prefabScene.Children.Count );
		Assert.AreEqual( "ChildObject", prefabScene.Children[0].Name );
		Assert.IsNotNull( prefabScene.Children[0].Components.Get<NavMeshArea>() );

		// Create a new instance - should have all the updates
		var newInstance = prefabScene.Clone( Vector3.Right * 100 );
		Assert.AreEqual( Color.Blue, newInstance.Components.Get<ModelRenderer>().Tint );
		Assert.IsNotNull( newInstance.Components.Get<BoxCollider>() );
		Assert.AreEqual( 1, newInstance.Children.Count );
		Assert.AreEqual( "ChildObject", newInstance.Children[0].Name );
	}

	[TestMethod]
	public void AddGameObjectToPrefab_UpdatesPrefabWithNewObject()
	{
		var saveLocation = "___apply_added_gameobject_test.prefab";
		using var prefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( saveLocation, _basicPrefabSource );
		var prefabFile = ResourceLibrary.Get<PrefabFile>( saveLocation );
		var prefabScene = SceneUtility.GetPrefabScene( prefabFile );

		// Original state - prefab has no children
		Assert.AreEqual( 0, prefabScene.Children.Count );

		// Create instance in scene
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var instance = prefabScene.Clone( Vector3.Zero );

		// Add a new child GameObject to the instance
		var addedChild = new GameObject( instance );
		addedChild.Name = "NewAddedObject";
		addedChild.LocalPosition = new Vector3( 10, 20, 30 );
		var navArea = addedChild.AddComponent<NavMeshArea>();
		navArea.Enabled = false;

		// The editor would call this after modification
		instance.PrefabInstance.RefreshPatch();

		// Verify it's recognized as added
		Assert.IsTrue( instance.PrefabInstance.IsAddedGameObject( addedChild ) );

		// Apply the added GameObject back to the prefab
		instance.PrefabInstance.AddGameObjectToPrefab( addedChild );

		// Reload prefab scene to verify changes
		prefabScene = SceneUtility.GetPrefabScene( prefabFile );

		// Verify prefab was updated with the new GameObject
		Assert.AreEqual( 1, prefabScene.Children.Count );
		var childInPrefab = prefabScene.Children[0];
		Assert.AreEqual( "NewAddedObject", childInPrefab.Name );
		Assert.AreEqual( new Vector3( 10, 20, 30 ), childInPrefab.LocalPosition );
		Assert.IsNotNull( childInPrefab.Components.Get<NavMeshArea>( true ) );
		Assert.IsFalse( childInPrefab.Components.Get<NavMeshArea>( true ).Enabled );

		// Create a new instance - should have the added GameObject
		var newInstance = prefabScene.Clone( Vector3.Right * 100 );
		Assert.AreEqual( 1, newInstance.Children.Count );
		Assert.AreEqual( "NewAddedObject", newInstance.Children[0].Name );
		Assert.IsNotNull( newInstance.Children[0].Components.Get<NavMeshArea>( true ) );
	}

	[TestMethod]
	public void AddGameObjectToPrefab_HandlesPrefabInstanceAsAddedObject()
	{
		// Create two separate prefabs
		var childPrefabLocation = "___nested_child.prefab";
		var mainPrefabLocation = "___main_with_added_prefab.prefab";

		// Create child prefab first - will be added to the main prefab instance
		using var childPrefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( childPrefabLocation, _basicPrefabSource );
		var childPrefabFile = ResourceLibrary.Get<PrefabFile>( childPrefabLocation );
		var childPrefabScene = SceneUtility.GetPrefabScene( childPrefabFile );

		// Create main prefab - initially empty
		using var mainPrefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( mainPrefabLocation, """"
		{
			"__guid": "58a842f3-8b7c-4b6e-a14b-5854d568e256",
			"Name": "MainPrefab",
			"Position": "0,0,0",
			"Enabled": true,
			"Components": [],
			"Children": []
		}
		"""" );
		var mainPrefabFile = ResourceLibrary.Get<PrefabFile>( mainPrefabLocation );
		var mainPrefabScene = SceneUtility.GetPrefabScene( mainPrefabFile );

		// Original state - main prefab has no children
		Assert.AreEqual( 0, mainPrefabScene.Children.Count );

		// Create main instance in scene
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var mainInstance = mainPrefabScene.Clone( Vector3.Zero );

		// Add child prefab instance to main instance
		var childInstance = childPrefabScene.Clone( Vector3.Zero );
		childInstance.Name = "ChildPrefabInstance";
		childInstance.LocalPosition = new Vector3( 5, 10, 15 );
		childInstance.Components.Get<ModelRenderer>().Tint = Color.Green; // Override a property
		childInstance.SetParent( mainInstance );

		// The editor would call this after modification
		mainInstance.PrefabInstance.RefreshPatch();

		// Verify child is recognized as added
		Assert.IsTrue( mainInstance.PrefabInstance.IsAddedGameObject( childInstance ) );

		// Apply the added child prefab instance back to the main prefab
		mainInstance.PrefabInstance.AddGameObjectToPrefab( childInstance );

		// Reload main prefab scene to verify changes
		mainPrefabScene = SceneUtility.GetPrefabScene( mainPrefabFile );

		// Verify main prefab was updated with the nested child prefab instance
		Assert.AreEqual( 1, mainPrefabScene.Children.Count );
		var childInPrefab = mainPrefabScene.Children[0];

		// Check basic properties
		Assert.AreEqual( "ChildPrefabInstance", childInPrefab.Name );
		Assert.AreEqual( new Vector3( 5, 10, 15 ), childInPrefab.LocalPosition );

		// Verify it's actually a prefab instance, not just a regular GameObject
		Assert.IsTrue( childInPrefab.IsPrefabInstanceRoot );

		// Check the nested prefab instance retains property overrides
		Assert.AreEqual( Color.Green, childInPrefab.Components.Get<ModelRenderer>().Tint );

		// Create a new instance of the main prefab
		var newMainInstance = mainPrefabScene.Clone( Vector3.Right * 100 );

		// Verify the new instance also has the child prefab instance
		Assert.AreEqual( 1, newMainInstance.Children.Count );
		var newChildInstance = newMainInstance.Children[0];
		Assert.IsTrue( newChildInstance.IsPrefabInstanceRoot );
		Assert.AreEqual( "ChildPrefabInstance", newChildInstance.Name );
		Assert.AreEqual( Color.Green, newChildInstance.Components.Get<ModelRenderer>().Tint );
	}

	[TestMethod]
	public void AddGameObjectToPrefab_UpdatesInstanceToPrefabMapping()
	{
		// Create two separate prefabs
		var childPrefabLocation = "___mapping_update_child.prefab";
		var mainPrefabLocation = "___mapping_update_main.prefab";

		// Create child prefab first
		using var childPrefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( childPrefabLocation, _basicPrefabSource );
		var childPrefabFile = ResourceLibrary.Get<PrefabFile>( childPrefabLocation );
		var childPrefabScene = SceneUtility.GetPrefabScene( childPrefabFile );
		var childPrefabRootGuid = childPrefabScene.Id;

		// Create main prefab - initially empty
		using var mainPrefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( mainPrefabLocation, """"
		{
			"__guid": "a9be5f4a-2c88-4a8b-9492-f82c499487ac",
			"Name": "MainPrefab",
			"Position": "0,0,0",
			"Enabled": true,
			"Components": [],
			"Children": []
		}
		"""" );
		var mainPrefabFile = ResourceLibrary.Get<PrefabFile>( mainPrefabLocation );
		var mainPrefabScene = SceneUtility.GetPrefabScene( mainPrefabFile );

		// Create main instance in scene
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var mainInstance = mainPrefabScene.Clone( Vector3.Zero );

		// Add child prefab instance to main instance
		var childInstance = childPrefabScene.Clone( Vector3.Zero );
		childInstance.SetParent( mainInstance );
		var childInstanceRootGuid = childInstance.Id;

		// Verify initially there's no mapping for the child in the main instance
		Assert.IsFalse( mainInstance.PrefabInstance.InstanceToPrefabLookup.ContainsKey( childInstanceRootGuid ) );

		// The editor would call this after modification
		mainInstance.PrefabInstance.RefreshPatch();

		// Apply the added child prefab instance back to the main prefab
		mainInstance.PrefabInstance.AddGameObjectToPrefab( childInstance );

		// Reload main prefab scene to verify changes
		mainPrefabScene = SceneUtility.GetPrefabScene( mainPrefabFile );
		var childInPrefab = mainPrefabScene.Children[0];
		var childInPrefabId = childInPrefab.Id;

		// Create a new instance of the main prefab
		var newMainInstance = mainPrefabScene.Clone( Vector3.Right * 100 );

		// Now check if the mapping has been updated - main instance should know the 
		// relationship between the original child instance ID and the prefab's child ID
		Assert.IsTrue( mainInstance.PrefabInstance.InstanceToPrefabLookup.ContainsKey( childInstanceRootGuid ) );
		Assert.AreEqual( childInPrefabId, mainInstance.PrefabInstance.InstanceToPrefabLookup[childInstanceRootGuid] );
	}

	[TestMethod]
	public void NestedPrefabInstance_IsModified_DetectsChangesInNestedInstance()
	{
		// Setup nested prefab structure
		var innerPrefabLocation = "___nested_inner.prefab";
		var outerPrefabLocation = "___nested_outer.prefab";

		// Create inner prefab
		using var innerPrefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( innerPrefabLocation, _basicPrefabSource );
		var innerPrefabFile = ResourceLibrary.Get<PrefabFile>( innerPrefabLocation );

		// Create outer prefab with inner prefab
		using var outerPrefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( outerPrefabLocation, _outerPrefabWithNestedPrefabSource );
		var outerPrefabFile = ResourceLibrary.Get<PrefabFile>( outerPrefabLocation );
		var outerPrefabScene = (PrefabCacheScene)SceneUtility.GetPrefabScene( outerPrefabFile );

		// Create a scene and instantiate the outer prefab
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var outerInstance = outerPrefabScene.Clone( Vector3.Zero );

		// Verify the nested structure
		Assert.AreEqual( 1, outerInstance.Children.Count );
		var innerInstance = outerInstance.Children[0];
		Assert.IsTrue( innerInstance.IsPrefabInstanceRoot );
		Assert.IsTrue( innerInstance.IsNestedPrefabInstanceRoot );

		// Initially not modified
		Assert.IsFalse( outerInstance.PrefabInstance.IsModified() );
		Assert.IsFalse( innerInstance.PrefabInstance.IsModified() );

		// Modify inner instance
		var innerModelRenderer = innerInstance.Components.Get<ModelRenderer>();
		innerModelRenderer.Tint = Color.Blue;
		innerInstance.PrefabInstance.RefreshPatch();

		// Inner instance should be modified, outer instance should not
		Assert.IsTrue( innerInstance.PrefabInstance.IsModified() );
		Assert.IsFalse( outerInstance.PrefabInstance.IsModified() );

		// Modify outer instance
		var outerModelRenderer = outerInstance.Components.Get<ModelRenderer>();
		outerModelRenderer.Tint = Color.Green;
		outerInstance.PrefabInstance.RefreshPatch();

		// Both instances should now be modified
		Assert.IsTrue( innerInstance.PrefabInstance.IsModified() );
		Assert.IsTrue( outerInstance.PrefabInstance.IsModified() );
	}

	[TestMethod]
	public void NestedPrefabInstance_ApplyGameObjectChangesToPrefab_UpdatesCorrectPrefab()
	{
		// Setup nested prefab structure
		var innerPrefabLocation = "___nested_inner.prefab";
		var outerPrefabLocation = "___nested_outer.prefab";

		// Create inner prefab
		using var innerPrefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( innerPrefabLocation, _basicPrefabSource );
		var innerPrefabFile = ResourceLibrary.Get<PrefabFile>( innerPrefabLocation );

		// Create outer prefab with inner prefab
		using var outerPrefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( outerPrefabLocation, _outerPrefabWithNestedPrefabSource );
		var outerPrefabFile = ResourceLibrary.Get<PrefabFile>( outerPrefabLocation );

		var outerPrefabScene = (PrefabCacheScene)SceneUtility.GetPrefabScene( outerPrefabFile );

		// Create a scene and instantiate the outer prefab
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var outerInstance = outerPrefabScene.Clone( Vector3.Zero );
		var innerInstance = outerInstance.Children[0];

		// Modify the inner instance by adding a component
		var boxCollider = innerInstance.AddComponent<BoxCollider>();
		boxCollider.Scale = new Vector3( 3, 3, 3 );
		innerInstance.PrefabInstance.RefreshPatch();

		// Apply changes to the inner prefab
		innerInstance.OutermostPrefabInstanceRoot.PrefabInstance.ApplyGameObjectChangesToPrefab( innerInstance );

		outerPrefabScene.Refresh( outerPrefabFile );

		var innerInstanceInPrefabScene = outerPrefabScene.Children[0];

		// Inner prefab should have the BoxCollider
		Assert.IsNotNull( innerInstanceInPrefabScene.Components.Get<BoxCollider>() );
		Assert.AreEqual( new Vector3( 3, 3, 3 ), innerInstanceInPrefabScene.Components.Get<BoxCollider>().Scale );

		// But the BoxCollider should not be directly on the outer prefab
		Assert.IsNull( outerPrefabScene.Components.Get<BoxCollider>() );

		// However, the inner prefab instance in the outer prefab should now have the BoxCollider
		Assert.IsNotNull( outerPrefabScene.Children[0].Components.Get<BoxCollider>() );
		Assert.AreEqual( new Vector3( 3, 3, 3 ), outerPrefabScene.Children[0].Components.Get<BoxCollider>().Scale );
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
				"__Prefab": "___nested_inner.prefab",
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
}

using Sandbox.Internal;
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace SceneTests.GameObjects;

[TestClass]
[DoNotParallelize]
public class RefreshTest : SceneTest
{
	TypeLibrary TypeLibrary;

	private TypeLibrary _oldTypeLibrary;

	[TestInitialize]
	public void TestInitialize()
	{
		_oldTypeLibrary = Game.TypeLibrary;

		TypeLibrary = new Sandbox.Internal.TypeLibrary();
		TypeLibrary.AddAssembly( typeof( ModelRenderer ).Assembly, false );
		TypeLibrary.AddAssembly( typeof( RefreshTestComponent ).Assembly, false );
		TypeLibrary.AddAssembly( typeof( ComponentIdTest ).Assembly, false );

		Game.TypeLibrary = TypeLibrary;
	}

	[TestCleanup]
	public void Cleanup()
	{
		Game.TypeLibrary = _oldTypeLibrary;
	}

	[TestMethod]
	public void RegularRefreshPrunesAllMissingObjects()
	{
		using var scope = new Scene().Push();

		// Create a hierarchy with parent and children
		var parent = new GameObject( true, "Parent" );

		// Child objects with various configurations
		var child1 = new GameObject( parent, true, "Child1" );
		var child2 = new GameObject( parent, true, "Child2" );
		var child3 = new GameObject( parent, true, "Child3" );

		// Serialize the parent to get the whole hierarchy
		var originalJson = parent.Serialize();

		// Remove child2 from the serialized data
		var childrenArray = originalJson["Children"].AsArray();
		JsonNode removedChild = null;

		for ( int i = 0; i < childrenArray.Count; i++ )
		{
			var childJson = childrenArray[i].AsObject();
			if ( childJson["Name"].GetValue<string>() == "Child2" )
			{
				removedChild = childrenArray[i];
				childrenArray.RemoveAt( i );
				break;
			}
		}

		Assert.IsNotNull( removedChild, "Failed to find Child2 in JSON to remove" );

		// Now deserialize with IsRefreshing=true, IsNetworkRefresh=false
		var deserializeOptions = new GameObject.DeserializeOptions
		{
			IsRefreshing = true,
			IsNetworkRefresh = false
		};

		parent.Deserialize( originalJson, deserializeOptions );

		// Verify child1 and child3 still exist, but child2 was removed
		Assert.AreEqual( 2, parent.Children.Count, "Parent should have 2 children after refresh" );

		var remainingNames = parent.Children.Select( c => c.Name ).ToArray();
		Assert.IsTrue( remainingNames.Contains( "Child1" ), "Child1 should still exist" );
		Assert.IsFalse( remainingNames.Contains( "Child2" ), "Child2 should have been removed" );
		Assert.IsTrue( remainingNames.Contains( "Child3" ), "Child3 should still exist" );
	}

	[TestMethod]
	public void NetworkRefreshPreservesNonSnapshotObjects()
	{
		using var scope = new Scene().Push();

		// Create a hierarchy with parent as a network object
		var parent = new GameObject( true, "Parent" );
		parent.NetworkMode = NetworkMode.Object;

		// Child with NetworkMode.Snapshot - should be pruned if not in JSON
		var snapshotChild = new GameObject( parent, true, "SnapshotChild" );
		snapshotChild.NetworkMode = NetworkMode.Snapshot;

		// Child with NetworkMode.Object - should be preserved
		var networkObjectChild = new GameObject( parent, true, "NetworkObjectChild" );
		networkObjectChild.NetworkMode = NetworkMode.Object;

		// Child with NetworkMode.Snapshot but NotNetworked flag - should be preserved
		var notNetworkedChild = new GameObject( parent, true, "NotNetworkedChild" );
		notNetworkedChild.NetworkMode = NetworkMode.Snapshot;
		notNetworkedChild.Flags |= GameObjectFlags.NotNetworked;

		// Child with NetworkMode.Never - should be preserved
		var neverNetworkedChild = new GameObject( parent, true, "NeverNetworkedChild" );
		neverNetworkedChild.NetworkMode = NetworkMode.Never;

		// Serialize the parent with SingleNetworkObject option
		var serializeOptions = new GameObject.SerializeOptions
		{
			SingleNetworkObject = true
		};
		var originalJson = parent.Serialize( serializeOptions );

		// Remove all children from the serialized data
		var childrenArray = originalJson["Children"].AsArray();
		childrenArray.Clear();

		// Now deserialize with IsRefreshing=true and IsNetworkRefresh=true
		var deserializeOptions = new GameObject.DeserializeOptions
		{
			IsRefreshing = true,
			IsNetworkRefresh = true
		};

		parent.Deserialize( originalJson, deserializeOptions );

		// Verify only Snapshot objects without NotNetworked flag are removed
		Assert.AreEqual( 3, parent.Children.Count, "Parent should have 3 children after network refresh" );

		var remainingNames = parent.Children.Select( c => c.Name ).ToArray();
		Assert.IsFalse( remainingNames.Contains( "SnapshotChild" ), "SnapshotChild should have been removed" );
		Assert.IsTrue( remainingNames.Contains( "NetworkObjectChild" ), "NetworkObjectChild should still exist" );
		Assert.IsTrue( remainingNames.Contains( "NotNetworkedChild" ), "NotNetworkedChild should still exist" );
		Assert.IsTrue( remainingNames.Contains( "NeverNetworkedChild" ), "NeverNetworkedChild should still exist" );
	}

	[TestMethod]
	public void NetworkRefreshPrunesComponentsInRemainingObjects()
	{
		using var scope = new Scene().Push();

		// Create a GameObject with NetworkMode.Object for network refresh
		var go = new GameObject( true, "TestObject" );
		go.NetworkMode = NetworkMode.Object;

		// Add multiple components
		var comp1 = go.Components.Create<RefreshTestComponent>();
		var comp2 = go.Components.Create<ComponentIdTest>();

		// Save component IDs for verification
		var comp1Id = comp1.Id;
		var comp2Id = comp2.Id;

		// Serialize the object with SingleNetworkObject option
		var serializeOptions = new GameObject.SerializeOptions
		{
			SingleNetworkObject = true
		};
		var originalJson = go.Serialize( serializeOptions );

		// Remove comp2 from the serialized data
		var componentsArray = originalJson["Components"].AsArray();
		for ( int i = 0; i < componentsArray.Count; i++ )
		{
			var componentJson = componentsArray[i].AsObject();
			if ( componentJson[Component.JsonKeys.Id].GetValue<Guid>() == comp2Id )
			{
				componentsArray.RemoveAt( i );
				break;
			}
		}

		// Deserialize with IsRefreshing=true and IsNetworkRefresh=true
		var deserializeOptions = new GameObject.DeserializeOptions
		{
			IsRefreshing = true,
			IsNetworkRefresh = true
		};

		go.Deserialize( originalJson, deserializeOptions );

		// Verify that comp1 still exists but comp2 was removed
		Assert.AreEqual( 1, go.Components.Count, "Should have only one component after refresh" );

		var remainingComponents = go.Components.GetAll().ToList();
		Assert.AreEqual( comp1Id, remainingComponents[0].Id, "comp1 should still exist" );
	}

	[TestMethod]
	public void NetworkRefreshPreservesHierarchyWithNestedSnapshots()
	{
		using var scope = new Scene().Push();

		// Create a hierarchy with network objects at different levels
		var parent = new GameObject( true, "Parent" );
		parent.NetworkMode = NetworkMode.Object;

		// Child with NetworkMode.Snapshot - should be preserved if in JSON, pruned if not
		var child1 = new GameObject( parent, true, "Child1" );
		child1.NetworkMode = NetworkMode.Snapshot;

		// Grandchild with NetworkMode.Snapshot - should be pruned if not in JSON
		var grandchild1 = new GameObject( child1, true, "GrandChild1" );
		grandchild1.NetworkMode = NetworkMode.Snapshot;

		// Child with NetworkMode.Object - should be preserved
		var child2 = new GameObject( parent, true, "Child2" );
		child2.NetworkMode = NetworkMode.Object;

		// Serialize the parent with SingleNetworkObject option
		var serializeOptions = new GameObject.SerializeOptions
		{
			SingleNetworkObject = true
		};
		var originalJson = parent.Serialize( serializeOptions );

		// Find Child1 in the JSON
		var childrenArray = originalJson["Children"].AsArray();
		JsonObject child1Json = FindChildByName( originalJson, "Child1" );

		Assert.IsNotNull( child1Json, "Failed to find Child1 in JSON" );

		// Remove GrandChild1 from Child1's children
		var child1ChildrenArray = child1Json["Children"].AsArray();
		child1ChildrenArray.Clear();

		// Deserialize with IsRefreshing=true and IsNetworkRefresh=true
		var deserializeOptions = new GameObject.DeserializeOptions
		{
			IsRefreshing = true,
			IsNetworkRefresh = true
		};

		parent.Deserialize( originalJson, deserializeOptions );

		// Verify child1 still exists (it was in the JSON)
		var child1After = parent.Children.FirstOrDefault( c => c.Name == "Child1" );
		Assert.IsNotNull( child1After, "Child1 should still exist" );

		// Verify grandchild1 was pruned (it's NetworkMode.Snapshot and wasn't in the JSON)
		Assert.AreEqual( 0, child1After.Children.Count, "Child1 should have no children after refresh" );
	}

	[TestMethod]
	public void NetworkRefreshPreservesNotNetworkedComponents()
	{
		using var scope = new Scene().Push();

		// Create a GameObject with NetworkMode.Object for network refresh
		var go = new GameObject( true, "TestObject" );
		go.NetworkMode = NetworkMode.Object;

		// Add multiple components
		var comp1 = go.Components.Create<RefreshTestComponent>();
		var comp2 = go.Components.Create<ComponentIdTest>();

		// Mark comp2 as not networked
		comp2.Flags |= ComponentFlags.NotNetworked;

		// Save component IDs for verification
		var comp1Id = comp1.Id;
		var comp2Id = comp2.Id;

		// Serialize the object with SingleNetworkObject option
		var serializeOptions = new GameObject.SerializeOptions
		{
			SingleNetworkObject = true
		};
		var originalJson = go.Serialize( serializeOptions );

		// Remove all components from the serialized data
		originalJson["Components"] = new JsonArray();

		// Deserialize with IsRefreshing=true and IsNetworkRefresh=true
		var deserializeOptions = new GameObject.DeserializeOptions
		{
			IsRefreshing = true,
			IsNetworkRefresh = true
		};

		go.Deserialize( originalJson, deserializeOptions );

		// Verify that comp1 was removed (it was networked and not in JSON)
		// but comp2 was preserved (it had NotNetworked flag)
		Assert.AreEqual( 1, go.Components.Count, "Should have only one component after refresh" );

		var remainingComponents = go.Components.GetAll().ToList();
		Assert.AreEqual( comp2Id, remainingComponents[0].Id, "NotNetworked component should be preserved" );
		Assert.IsTrue( remainingComponents[0] is ComponentIdTest, "Remaining component should be ComponentIdTest" );
	}

	[TestMethod]
	public void RegularRefreshAddsNewGameObjects()
	{
		using var scope = new Scene().Push();

		// Create a hierarchy with parent
		var parent = new GameObject( true, "Parent" );

		// Serialize the parent to get the initial hierarchy
		var originalJson = parent.Serialize();

		// Add a new child to the JSON (not in the actual hierarchy)
		var childrenArray = originalJson["Children"].AsArray();
		var newChildJson = new JsonObject
		{
			["__guid"] = Guid.NewGuid(),
			["Name"] = "NewChild",
			["Position"] = JsonValue.Create( Vector3.Zero ),
			["Rotation"] = JsonValue.Create( Rotation.Identity ),
			["Scale"] = JsonValue.Create( Vector3.One ),
			["Enabled"] = true
		};

		childrenArray.Add( newChildJson );

		// Now deserialize with IsRefreshing=true
		var deserializeOptions = new GameObject.DeserializeOptions
		{
			IsRefreshing = true
		};

		parent.Deserialize( originalJson, deserializeOptions );

		// Verify the new child was added
		Assert.AreEqual( 1, parent.Children.Count, "Parent should have 1 child after refresh" );
		Assert.AreEqual( "NewChild", parent.Children[0].Name, "New child should have been added" );
	}

	[TestMethod]
	public void RegularRefreshAddsNewComponents()
	{
		using var scope = new Scene().Push();

		// Create a GameObject
		var go = new GameObject( true, "TestObject" );

		// Add an initial component
		var comp1 = go.Components.Create<RefreshTestComponent>();

		// Serialize the object
		var originalJson = go.Serialize();

		// Add a new component to the JSON
		var componentsArray = originalJson["Components"].AsArray();
		var newComponentJson = new JsonObject
		{
			["__guid"] = Guid.NewGuid(),
			["__type"] = "ComponentIdTest",
			["__enabled"] = true
		};

		componentsArray.Add( newComponentJson );

		// Deserialize with IsRefreshing=true
		var deserializeOptions = new GameObject.DeserializeOptions
		{
			IsRefreshing = true
		};

		go.Deserialize( originalJson, deserializeOptions );

		// Verify both components exist
		Assert.AreEqual( 2, go.Components.Count, "GameObject should have 2 components after refresh" );
		Assert.IsNotNull( go.Components.Get<RefreshTestComponent>(), "Original component should still exist" );
		Assert.IsNotNull( go.Components.Get<ComponentIdTest>(), "New component should have been added" );
	}

	[TestMethod]
	public void RegularRefreshMaintainsHierarchyRelationships()
	{
		using var scope = new Scene().Push();

		// Create a complex hierarchy
		var parent = new GameObject( true, "Parent" );

		var child1 = new GameObject( parent, true, "Child1" );
		var grandChild1A = new GameObject( child1, true, "GrandChild1A" );
		var grandChild1B = new GameObject( child1, true, "GrandChild1B" );

		var child2 = new GameObject( parent, true, "Child2" );
		var grandChild2A = new GameObject( child2, true, "GrandChild2A" );

		// Keep track of original hierarchy relationships and IDs
		var originalHierarchy = new Dictionary<string, List<string>>();
		originalHierarchy["Parent"] = parent.Children.Select( c => c.Name ).ToList();
		originalHierarchy["Child1"] = child1.Children.Select( c => c.Name ).ToList();
		originalHierarchy["Child2"] = child2.Children.Select( c => c.Name ).ToList();

		var originalIds = new Dictionary<string, Guid>();
		originalIds["Parent"] = parent.Id;
		originalIds["Child1"] = child1.Id;
		originalIds["Child2"] = child2.Id;
		originalIds["GrandChild1A"] = grandChild1A.Id;
		originalIds["GrandChild1B"] = grandChild1B.Id;
		originalIds["GrandChild2A"] = grandChild2A.Id;

		// Serialize the parent
		var originalJson = parent.Serialize();

		// Modify the JSON: remove GrandChild1B and add a new GrandChild2B
		var child1Json = FindChildByName( originalJson, "Child1" );
		var child1Children = child1Json["Children"].AsArray();

		// Remove GrandChild1B
		for ( int i = 0; i < child1Children.Count; i++ )
		{
			if ( child1Children[i]["Name"].GetValue<string>() == "GrandChild1B" )
			{
				child1Children.RemoveAt( i );
				break;
			}
		}

		// Add GrandChild2B to Child2
		var child2Json = FindChildByName( originalJson, "Child2" );
		var child2Children = child2Json["Children"].AsArray();

		var newGrandChildJson = new JsonObject
		{
			["__guid"] = Guid.NewGuid(),
			["Name"] = "GrandChild2B",
			["Position"] = JsonValue.Create( Vector3.Zero ),
			["Rotation"] = JsonValue.Create( Rotation.Identity ),
			["Scale"] = JsonValue.Create( Vector3.One ),
			["Enabled"] = true
		};

		child2Children.Add( newGrandChildJson );

		// Deserialize with IsRefreshing=true
		var deserializeOptions = new GameObject.DeserializeOptions
		{
			IsRefreshing = true
		};

		parent.Deserialize( originalJson, deserializeOptions );

		// Verify the hierarchy was updated correctly
		Assert.AreEqual( 2, parent.Children.Count, "Parent should still have 2 children" );

		var refreshedChild1 = parent.Children.FirstOrDefault( c => c.Name == "Child1" );
		var refreshedChild2 = parent.Children.FirstOrDefault( c => c.Name == "Child2" );

		Assert.IsNotNull( refreshedChild1, "Child1 should still exist" );
		Assert.IsNotNull( refreshedChild2, "Child2 should still exist" );

		// Child1 should have lost GrandChild1B
		Assert.AreEqual( 1, refreshedChild1.Children.Count, "Child1 should have 1 child after refresh" );
		Assert.AreEqual( "GrandChild1A", refreshedChild1.Children[0].Name, "GrandChild1A should still exist" );

		// Child2 should have gained GrandChild2B
		Assert.AreEqual( 2, refreshedChild2.Children.Count, "Child2 should have 2 children after refresh" );

		var grandChildNames = refreshedChild2.Children.Select( c => c.Name ).ToArray();
		Assert.IsTrue( grandChildNames.Contains( "GrandChild2A" ), "GrandChild2A should still exist" );
		Assert.IsTrue( grandChildNames.Contains( "GrandChild2B" ), "GrandChild2B should have been added" );

		// IDs should be preserved for existing objects
		Assert.AreEqual( originalIds["Parent"], parent.Id, "Parent ID should be preserved" );
		Assert.AreEqual( originalIds["Child1"], refreshedChild1.Id, "Child1 ID should be preserved" );
		Assert.AreEqual( originalIds["Child2"], refreshedChild2.Id, "Child2 ID should be preserved" );

		var refreshedGrandChild1A = refreshedChild1.Children.FirstOrDefault( c => c.Name == "GrandChild1A" );
		Assert.AreEqual( originalIds["GrandChild1A"], refreshedGrandChild1A.Id, "GrandChild1A ID should be preserved" );

		var refreshedGrandChild2A = refreshedChild2.Children.FirstOrDefault( c => c.Name == "GrandChild2A" );
		Assert.AreEqual( originalIds["GrandChild2A"], refreshedGrandChild2A.Id, "GrandChild2A ID should be preserved" );
	}

	// A refresh applies flags verbatim. A plain (disk-style) snapshot has runtime flags stripped, so a refresh
	// from it keeps the persisted flags and drops the runtime ones (which then get re-derived at runtime).
	[TestMethod]
	public void RefreshFromPlainSnapshotAppliesFlagsVerbatim()
	{
		using var scope = new Scene().Push();

		var go = new GameObject( true, "Object" );
		go.Flags |= GameObjectFlags.Hidden;

		var json = go.Serialize(); // plain: strips runtime flags

		// Runtime flag set on the live object after the snapshot was taken.
		go.Flags |= GameObjectFlags.Loading;

		go.Deserialize( json, new GameObject.DeserializeOptions { IsRefreshing = true } );

		Assert.IsTrue( go.Flags.Contains( GameObjectFlags.Hidden ), "persisted flag should be applied" );
		Assert.IsFalse( go.Flags.Contains( GameObjectFlags.Loading ), "verbatim refresh from a plain snapshot clears runtime flags" );
	}

	// Helper method to find a child object in the JSON by name
	private JsonObject FindChildByName( JsonObject parentJson, string childName )
	{
		var childrenArray = parentJson["Children"].AsArray();

		foreach ( var child in childrenArray )
		{
			if ( child["Name"].GetValue<string>() == childName )
			{
				return child.AsObject();
			}
		}

		return null;
	}
}

/// <summary>
/// A do-nothing component for refresh tests - just something to create and prune
/// without loading any resources.
/// </summary>
public class RefreshTestComponent : Component
{
}

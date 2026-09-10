using Editor;
using System.Linq;
using System.Text.Json.Nodes;

namespace SceneTests.GameObjects;

public partial class PrefabInstanceTest
{
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void PrefabReparentUndoRestoresMappingsAndNestedOwnership( bool nested )
	{
		using var basic = RegisterBasicPrefab( out var prefab );
		using var outer = Helpers.RegisterPrefabFromJson( "undo_move_outer.prefab",
			MakeContainingPrefab( System.Guid.NewGuid(), "Outer", BasicPrefabPath, System.Guid.Parse( RootGuid ) ) );
		var scene = new Scene();
		using var scope = scene.Push();
		var instance = (nested ? GameObject.GetPrefab( "undo_move_outer.prefab" ) : prefab).Clone();
		var target = GetChild( instance, nested ? "BasicRoot" : "ChildA" );
		var id = target.Id;
		var mappings = instance.PrefabInstance.PrefabToInstanceLookup.ToArray();
		var session = new GameEditorSession( null, scene );
		try
		{
			using ( session.UndoScope( "Move out" ).WithGameObjectChanges( target, GameObjectUndoFlags.Properties ).Push() )
				target.SetParent( scene );
			for ( var i = 0; i < 2; i++ )
			{
				Assert.IsTrue( session.UndoSystem.Undo() );
				Assert.AreSame( instance, target.Parent );
				Assert.AreEqual( nested, target.IsNestedPrefabInstanceRoot );
				CollectionAssert.AreEquivalent( mappings, instance.PrefabInstance.PrefabToInstanceLookup.ToArray() );
				instance.UpdateFromPrefab();
				Assert.AreSame( target, scene.Directory.FindByGuid( id ) );
				Assert.IsTrue( session.UndoSystem.Redo() );
				Assert.AreSame( scene, target.Parent );
				Assert.IsFalse( target.IsNestedPrefabInstanceRoot );
				instance.UpdateFromPrefab();
				Assert.AreSame( target, scene.Directory.FindByGuid( id ) );
			}
		}
		finally { session.Destroy(); }
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void PrefabPropertyUndoPreservesOwnershipAndUncapturedChildren( bool nested )
	{
		using var basic = RegisterBasicPrefab( out var prefab );
		using var outer = Helpers.RegisterPrefabFromJson( "undo_outer.prefab",
			MakeContainingPrefab( System.Guid.NewGuid(), "Outer", BasicPrefabPath, System.Guid.Parse( RootGuid ) ) );
		var scene = new Scene();
		using var scope = scene.Push();
		var instance = (nested ? GameObject.GetPrefab( "undo_outer.prefab" ) : prefab).Clone();
		var target = nested ? GetChild( instance, "BasicRoot" ) : instance;
		var ownership = target.PrefabInstance;
		var mappings = ownership.PrefabToInstanceLookup.ToArray();
		var session = new GameEditorSession( null, scene );
		try
		{
			using ( session.UndoScope( "Rename" ).WithGameObjectChanges( target, GameObjectUndoFlags.Properties ).Push() )
				target.Name = "Renamed";

			// This change is outside the scope and must not be rolled back with the root property.
			var child = GetChild( target, "ChildA" );
			child.Components.Get<PrefabInstanceStatComponent>().Number = 71;
			Assert.IsTrue( session.UndoSystem.Undo() );
			Assert.AreEqual( "BasicRoot", target.Name );
			Assert.AreSame( ownership, target.PrefabInstance );
			CollectionAssert.AreEquivalent( mappings, ownership.PrefabToInstanceLookup.ToArray() );
			Assert.AreEqual( 71, child.Components.Get<PrefabInstanceStatComponent>().Number );
			Assert.IsTrue( session.UndoSystem.Redo() );
			Assert.AreEqual( "Renamed", target.Name );
			Assert.AreSame( ownership, target.PrefabInstance );
			instance.UpdateFromPrefab();
			Assert.AreEqual( "Renamed", target.Name );
			Assert.AreEqual( 71, child.Components.Get<PrefabInstanceStatComponent>().Number );
		}
		finally
		{
			session.Destroy();
		}
	}

	[TestMethod]
	public void PrefabComponentUndoRefreshesPatchWithoutCapturingSiblings()
	{
		using var registration = RegisterBasicPrefab( out var prefab );
		var scene = new Scene();
		using var scope = scene.Push();
		var instance = prefab.Clone();
		var component = GetChild( instance, "ChildA" ).Components.Get<PrefabInstanceStatComponent>();
		var original = component.Number;
		var session = new GameEditorSession( null, scene );
		try
		{
			using ( session.UndoScope( "Change component" ).WithComponentChanges( component ).Push() )
				component.Number = 42;
			instance.Name = "Uncaptured";
			Assert.IsTrue( session.UndoSystem.Undo() );
			instance.UpdateFromPrefab();
			Assert.AreEqual( original, component.Number );
			Assert.AreEqual( "Uncaptured", instance.Name );
			Assert.IsTrue( session.UndoSystem.Redo() );
			instance.UpdateFromPrefab();
			Assert.AreEqual( 42, component.Number );
			Assert.AreEqual( "Uncaptured", instance.Name );
		}
		finally
		{
			session.Destroy();
		}
	}

	[TestMethod]
	public void PrefabDifferenceCalculatorRefreshesWithSource()
	{
		using var registration = RegisterBasicPrefab( out var prefab );
		var scene = new Scene();
		using var scope = scene.Push();
		var instance = prefab.Clone();
		var cache = (PrefabCacheScene)prefab;
		for ( var value = 20; value < 23; value++ )
		{
			var file = ResourceLibrary.Get<PrefabFile>( BasicPrefabPath );
			file.RootObject["Components"][0]["Number"] = value;
			cache.Refresh( file );
			instance.UpdateFromPrefab();
			instance.PrefabInstance.RefreshPatch();
			Assert.AreEqual( value, instance.Components.Get<PrefabInstanceStatComponent>().Number );
			var json = instance.SerializeStandard( new GameObject.SerializeOptions() );
			instance.PrefabInstance.RemapInstanceIdsToPrefabIds( ref json );
			Assert.IsTrue( JsonNode.DeepEquals(
				Json.ToNode( Json.CalculateDifferences( cache.FullPrefabInstanceJson, json, GameObject.DiffObjectDefinitions ) ),
				Json.ToNode( instance.PrefabInstance.Patch ) ) );
		}
	}
}

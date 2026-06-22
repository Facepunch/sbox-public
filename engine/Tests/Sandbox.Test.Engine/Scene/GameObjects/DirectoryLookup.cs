using System;

namespace SceneTests.GameObjects;

/// <summary>
/// Pins GameObjectDirectory bookkeeping not covered by the core directory tests:
/// the object/component counts, re-registration when an id changes, and guid
/// collision handling between objects and components.
/// </summary>
[TestClass]
[DoNotParallelize]
public class DirectoryLookupTest : SceneTest
{
	/// <summary>
	/// GameObjectCount and ComponentCount track creations and destructions, and
	/// the scene itself is not counted as a game object.
	/// </summary>
	[TestMethod]
	public void CountsTrackAddAndRemove()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		Assert.AreEqual( 0, scene.Directory.GameObjectCount );
		Assert.AreEqual( 0, scene.Directory.ComponentCount );

		var go = scene.CreateObject();
		var child = new GameObject( go );
		var comp = go.Components.Create<DirectoryLookupProbe>();

		Assert.AreEqual( 2, scene.Directory.GameObjectCount );
		Assert.AreEqual( 1, scene.Directory.ComponentCount );

		comp.Destroy();
		scene.ProcessDeletes();

		Assert.AreEqual( 2, scene.Directory.GameObjectCount );
		Assert.AreEqual( 0, scene.Directory.ComponentCount );

		go.Destroy();
		scene.ProcessDeletes();

		Assert.AreEqual( 0, scene.Directory.GameObjectCount, "destroying a parent must unregister its children too" );
	}

	/// <summary>
	/// An unknown component guid returns null rather than throwing.
	/// </summary>
	[TestMethod]
	public void UnknownComponentGuidReturnsNull()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		Assert.IsNull( scene.Directory.FindComponentByGuid( Guid.NewGuid() ) );
	}

	/// <summary>
	/// Changing an object's id re-registers it under the new guid: the old
	/// lookup stops resolving, the new one resolves, and the count is unchanged.
	/// </summary>
	[TestMethod]
	public void ChangingIdRebooksDirectory()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var oldId = go.Id;
		var newId = Guid.NewGuid();

		go.SetDeterministicId( newId );

		Assert.AreEqual( newId, go.Id );
		Assert.IsNull( scene.Directory.FindByGuid( oldId ) );
		Assert.AreEqual( go, scene.Directory.FindByGuid( newId ) );
		Assert.AreEqual( 1, scene.Directory.GameObjectCount );
	}

	/// <summary>
	/// When an object tries to claim a guid already owned by another object,
	/// the newcomer is reassigned a fresh guid and the incumbent keeps its id.
	/// </summary>
	[TestMethod]
	public void GameObjectGuidCollisionReassignsNewcomer()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var incumbent = scene.CreateObject();
		var newcomer = scene.CreateObject();

		newcomer.SetDeterministicId( incumbent.Id );

		Assert.AreNotEqual( incumbent.Id, newcomer.Id );
		Assert.AreEqual( incumbent, scene.Directory.FindByGuid( incumbent.Id ) );
		Assert.AreEqual( newcomer, scene.Directory.FindByGuid( newcomer.Id ) );
	}

	/// <summary>
	/// Component guids collide with other component guids the same way - the
	/// newcomer gets a fresh id, the incumbent is untouched.
	/// </summary>
	[TestMethod]
	public void ComponentGuidCollisionReassignsNewcomer()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var incumbent = go.Components.Create<DirectoryLookupProbe>();
		var newcomer = go.Components.Create<DirectoryLookupProbe>();

		newcomer.SetDeterministicId( incumbent.Id );

		Assert.AreNotEqual( incumbent.Id, newcomer.Id );
		Assert.AreEqual( incumbent, scene.Directory.FindComponentByGuid( incumbent.Id ) );
		Assert.AreEqual( newcomer, scene.Directory.FindComponentByGuid( newcomer.Id ) );
	}

	/// <summary>
	/// Guid uniqueness is enforced across the object/component divide: a
	/// component cannot take a game object's guid.
	/// </summary>
	[TestMethod]
	public void ComponentCannotTakeGameObjectGuid()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<DirectoryLookupProbe>();

		comp.SetDeterministicId( go.Id );

		Assert.AreNotEqual( go.Id, comp.Id );
		Assert.AreEqual( go, scene.Directory.FindByGuid( go.Id ) );
		Assert.AreEqual( comp, scene.Directory.FindComponentByGuid( comp.Id ) );
	}

	/// <summary>
	/// And the reverse: a game object cannot take a component's guid.
	/// </summary>
	[TestMethod]
	public void GameObjectCannotTakeComponentGuid()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<DirectoryLookupProbe>();

		var other = scene.CreateObject();
		other.SetDeterministicId( comp.Id );

		Assert.AreNotEqual( comp.Id, other.Id );
		Assert.AreEqual( comp, scene.Directory.FindComponentByGuid( comp.Id ) );
		Assert.AreEqual( other, scene.Directory.FindByGuid( other.Id ) );
	}
}

/// <summary>
/// Bare component used to populate the scene directory in these tests.
/// </summary>
public class DirectoryLookupProbe : Component
{
}

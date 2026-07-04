using System;
using System.Threading;

namespace SceneTests.GameObjects;

/// <summary>
/// Pins core GameObject behaviour that isn't covered elsewhere: naming (null names,
/// ToString, MakeNameUnique), the enabled cancellation token lifecycle, root/ancestry
/// queries, GetAllObjects enumeration, sibling traversal and the scene-level guards
/// on SetParent/AddSibling.
/// </summary>
[TestClass]
[DoNotParallelize]
public class GameObjectCoreTest : SceneTest
{
	/// <summary>
	/// Assigning a null name must fall back to "Untitled Object" rather than
	/// storing null - lots of code assumes Name is never null.
	/// </summary>
	[TestMethod]
	public void NullNameBecomesUntitled()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.Name = null;

		Assert.AreEqual( "Untitled Object", go.Name );
	}

	/// <summary>
	/// ToString includes the object's name, which debugging and log output rely on.
	/// </summary>
	[TestMethod]
	public void ToStringIncludesName()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = new GameObject( true, "Snail" );

		Assert.AreEqual( "GameObject:Snail", go.ToString() );
	}

	/// <summary>
	/// MakeNameUnique appends a "(n)" suffix when a sibling already has the same
	/// name, and counts upwards for each further duplicate.
	/// </summary>
	[TestMethod]
	public void MakeNameUniqueRenamesDuplicates()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();

		var first = new GameObject( parent, true, "Item" );
		Assert.AreEqual( "Item", first.Name );

		var second = new GameObject( parent, true, "Item" );
		second.MakeNameUnique();
		Assert.AreEqual( "Item (1)", second.Name );

		var third = new GameObject( parent, true, "Item" );
		third.MakeNameUnique();
		Assert.AreEqual( "Item (2)", third.Name );
	}

	/// <summary>
	/// When numbered siblings already exist, MakeNameUnique picks the highest
	/// existing number plus one rather than filling gaps.
	/// </summary>
	[TestMethod]
	public void MakeNameUniquePicksHighestNumberPlusOne()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();
		var a = new GameObject( parent, true, "Item" );
		var b = new GameObject( parent, true, "Item (7)" );

		var c = new GameObject( parent, true, "Item" );
		c.MakeNameUnique();

		Assert.AreEqual( "Item (8)", c.Name );
	}

	/// <summary>
	/// MakeNameUnique leaves the name alone when there's no duplicate sibling.
	/// </summary>
	[TestMethod]
	public void MakeNameUniqueKeepsUniqueName()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();
		var a = new GameObject( parent, true, "Unique" );
		var b = new GameObject( parent, true, "Other" );

		a.MakeNameUnique();

		Assert.AreEqual( "Unique", a.Name );
	}

	/// <summary>
	/// An object that already carries a number suffix still gets renamed when a
	/// sibling has the exact same numbered name.
	/// </summary>
	[TestMethod]
	public void MakeNameUniqueHandlesNumberedSelf()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();
		var existing = new GameObject( parent, true, "Item (2)" );

		var dupe = new GameObject( parent, true, "Item (2)" );
		dupe.MakeNameUnique();

		Assert.AreEqual( "Item (3)", dupe.Name );
	}

	/// <summary>
	/// Outside of the editor MakeNameUnique is skipped when the parent has more
	/// than 100 children, because the sibling scan would become too expensive.
	/// </summary>
	[TestMethod]
	public void MakeNameUniqueSkipsHugeSiblingCounts()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();

		for ( int i = 0; i < 100; i++ )
		{
			_ = new GameObject( parent, true, "Mass" );
		}

		var newcomer = new GameObject( parent, true, "Mass" );
		Assert.IsTrue( parent.Children.Count > 100 );

		newcomer.MakeNameUnique();

		Assert.AreEqual( "Mass", newcomer.Name, "MakeNameUnique should be a no-op with >100 siblings outside the editor" );
	}

	/// <summary>
	/// The EnabledToken is cancelled when the object is disabled, after which a
	/// fresh (non-cancelled) token is issued on re-enable. While disabled there is
	/// no token source, so EnabledToken returns CancellationToken.None.
	/// </summary>
	[TestMethod]
	public void EnabledTokenFollowsEnabledState()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		var token = go.EnabledToken;
		Assert.IsFalse( token.IsCancellationRequested );

		go.Enabled = false;

		Assert.IsTrue( token.IsCancellationRequested, "disabling must cancel the previous token" );
		Assert.AreEqual( CancellationToken.None, go.EnabledToken, "a disabled object has no token source" );

		go.Enabled = true;

		var newToken = go.EnabledToken;
		Assert.IsFalse( newToken.IsCancellationRequested );
		Assert.AreNotEqual( CancellationToken.None, newToken );
	}

	/// <summary>
	/// Destroying an object cancels its enabled token, so any tasks waiting on it
	/// get torn down with the object.
	/// </summary>
	[TestMethod]
	public void EnabledTokenCancelledByDestroy()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var token = go.EnabledToken;

		go.DestroyImmediate();

		Assert.IsTrue( token.IsCancellationRequested );
	}

	/// <summary>
	/// An object created disabled never creates a token source, so EnabledToken
	/// is CancellationToken.None until it is enabled.
	/// </summary>
	[TestMethod]
	public void DisabledObjectHasNoEnabledToken()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = new GameObject( false );

		Assert.AreEqual( CancellationToken.None, go.EnabledToken );
	}

	/// <summary>
	/// IsRoot is true for direct children of the scene, and Root walks up the
	/// hierarchy to the scene-parented object (which may be the object itself).
	/// </summary>
	[TestMethod]
	public void RootAndIsRoot()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var root = scene.CreateObject();
		var child = new GameObject( root );
		var grandchild = new GameObject( child );

		Assert.IsTrue( root.IsRoot );
		Assert.IsFalse( child.IsRoot );
		Assert.IsFalse( grandchild.IsRoot );

		Assert.AreEqual( root, root.Root );
		Assert.AreEqual( root, child.Root );
		Assert.AreEqual( root, grandchild.Root );
	}

	/// <summary>
	/// IsDescendant mirrors IsAncestor: parents report their children (and an
	/// object reports itself) as descendants, but not the other way around.
	/// </summary>
	[TestMethod]
	public void IsDescendantSemantics()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();
		var child = new GameObject( parent );
		var grandchild = new GameObject( child );

		Assert.IsTrue( parent.IsDescendant( child ) );
		Assert.IsTrue( parent.IsDescendant( grandchild ) );
		Assert.IsTrue( parent.IsDescendant( parent ), "an object counts as its own descendant" );
		Assert.IsFalse( child.IsDescendant( parent ) );
	}

	/// <summary>
	/// GetAllObjects yields the object and all its descendants. With enabled=true
	/// it skips disabled subtrees entirely - even enabled grandchildren under a
	/// disabled child are excluded.
	/// </summary>
	[TestMethod]
	public void GetAllObjectsRespectsEnabledFilter()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var root = scene.CreateObject();
		var enabledChild = new GameObject( root, true, "EnabledChild" );
		var disabledChild = new GameObject( root, false, "DisabledChild" );
		var grandchild = new GameObject( disabledChild, true, "Grandchild" );

		var all = root.GetAllObjects( false ).ToList();
		Assert.AreEqual( 4, all.Count );
		CollectionAssert.Contains( all, grandchild );

		var enabledOnly = root.GetAllObjects( true ).ToList();
		Assert.AreEqual( 2, enabledOnly.Count );
		CollectionAssert.Contains( enabledOnly, root );
		CollectionAssert.Contains( enabledOnly, enabledChild );
	}

	/// <summary>
	/// GetAllObjects with enabled=true on a disabled root yields nothing at all.
	/// </summary>
	[TestMethod]
	public void GetAllObjectsOnDisabledRootIsEmpty()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var root = scene.CreateObject( false );
		_ = new GameObject( root, true, "Child" );

		Assert.AreEqual( 0, root.GetAllObjects( true ).Count() );
	}

	/// <summary>
	/// GetNextSibling returns null when there is no later sibling, including when
	/// the only later siblings are disabled and enabledOnly is requested.
	/// </summary>
	[TestMethod]
	public void GetNextSiblingReturnsNullAtEnd()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();
		var a = new GameObject( parent, true, "A" );
		var b = new GameObject( parent, true, "B" );

		Assert.IsNull( b.GetNextSibling( false ) );

		b.Enabled = false;
		Assert.IsNull( a.GetNextSibling( true ) );
		Assert.AreEqual( b, a.GetNextSibling( false ) );
	}

	/// <summary>
	/// The SetParent overload treats null like the Parent property does - the
	/// object is reparented to the scene root instead of being orphaned.
	/// </summary>
	[TestMethod]
	public void SetParentNullReparentsToScene()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();
		var child = new GameObject( parent );

		child.SetParent( null );

		Assert.AreEqual( scene, child.Parent );
		Assert.IsTrue( child.IsRoot );
	}

	/// <summary>
	/// Scenes cannot take part in the sibling/parent API: SetParent and AddSibling
	/// on a scene throw rather than corrupting the hierarchy.
	/// </summary>
	[TestMethod]
	public void SceneRejectsParentAndSiblingOperations()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		Assert.ThrowsException<InvalidOperationException>( () => scene.SetParent( go ) );
		Assert.ThrowsException<InvalidOperationException>( () => scene.AddSibling( go, true ) );
	}

	/// <summary>
	/// GetBounds transforms each IHasBounds component's local bounds into world
	/// space and merges them, including bounds contributed by descendants.
	/// </summary>
	[TestMethod]
	public void GetBoundsMergesComponentAndChildBounds()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 100, 0, 0 );
		go.Components.Create<BoundsProbeComponent>();

		var bounds = go.GetBounds();
		Assert.IsTrue( bounds.Mins.AlmostEqual( new Vector3( 90, -10, -10 ) ), $"{bounds.Mins}" );
		Assert.IsTrue( bounds.Maxs.AlmostEqual( new Vector3( 110, 10, 10 ) ), $"{bounds.Maxs}" );

		var child = new GameObject( go );
		child.WorldPosition = new Vector3( 200, 0, 0 );
		child.Components.Create<BoundsProbeComponent>();

		var merged = go.GetBounds();
		Assert.IsTrue( merged.Mins.AlmostEqual( new Vector3( 90, -10, -10 ) ), $"{merged.Mins}" );
		Assert.IsTrue( merged.Maxs.AlmostEqual( new Vector3( 210, 10, 10 ) ), $"{merged.Maxs}" );
	}

	/// <summary>
	/// With no IHasBounds components at all, GetBounds falls back to a zero-size
	/// box at the object's world position.
	/// </summary>
	[TestMethod]
	public void GetBoundsFallsBackToWorldPosition()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 5, 0, 0 );

		var bounds = go.GetBounds();

		Assert.IsTrue( bounds.Mins.AlmostEqual( new Vector3( 5, 0, 0 ) ), $"{bounds.Mins}" );
		Assert.IsTrue( bounds.Maxs.AlmostEqual( new Vector3( 5, 0, 0 ) ), $"{bounds.Maxs}" );
	}

	/// <summary>
	/// GetLocalBounds merges component bounds in local space, always including
	/// the origin point in the result.
	/// </summary>
	[TestMethod]
	public void GetLocalBoundsIncludesOrigin()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var probe = go.Components.Create<BoundsProbeComponent>();
		probe.LocalBounds = BBox.FromPositionAndSize( new Vector3( 50, 0, 0 ), 10f );

		var bounds = go.GetLocalBounds();

		Assert.IsTrue( bounds.Mins.AlmostEqual( new Vector3( 0, -5, -5 ) ), $"{bounds.Mins}" );
		Assert.IsTrue( bounds.Maxs.AlmostEqual( new Vector3( 55, 5, 5 ) ), $"{bounds.Maxs}" );
	}
}

/// <summary>
/// A headless-safe component that reports bounds via <see cref="Component.IHasBounds"/>,
/// used to exercise <see cref="GameObject.GetBounds"/> without any renderers.
/// </summary>
public class BoundsProbeComponent : Component, Component.IHasBounds
{
	/// <summary>
	/// Bounds reported to the bounds queries, in local space.
	/// </summary>
	public BBox LocalBounds { get; set; } = BBox.FromPositionAndSize( Vector3.Zero, 20f );
}

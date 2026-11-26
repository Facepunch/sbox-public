using System.Collections.Generic;

namespace SceneTests.GameObjects;

/// <summary>
/// ComponentList.Query must find exactly what GetAll finds, in the same order, without allocating.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ComponentQueryTests : SceneTest
{
	const FindMode AllFlags = FindMode.Enabled | FindMode.Disabled | FindMode.InSelf | FindMode.InParent | FindMode.InAncestors | FindMode.InChildren | FindMode.InDescendants;

	static List<T> Iterate<T>( ComponentQuery<T> query )
	{
		var list = new List<T>();
		foreach ( var c in query ) list.Add( c );
		return list;
	}

	static List<T> Collect<T>( GameObject go, FindMode find )
	{
		var list = new List<T>();
		go.Components.GetAll( list, find );
		return list;
	}

	static void AssertMatchesGetAll<T>( GameObject go, string name )
	{
		for ( var mode = 0; mode <= (int)AllFlags; mode++ )
		{
			var find = (FindMode)mode;
			var expected = Collect<T>( go, find );

			CollectionAssert.AreEqual( expected, go.Components.GetAll<T>( find ).ToList(), $"GetAll {name}, {typeof( T ).Name}, {find}" );
			CollectionAssert.AreEqual( expected, Iterate( go.Components.Query<T>( find ) ), $"Query {name}, {typeof( T ).Name}, {find}" );
		}
	}

	[TestMethod]
	public void MatchesGetAllForEveryFindMode()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var grandparent = scene.CreateObject();
		grandparent.AddComponent<QueryProbeComponent>();

		var parent = new GameObject( grandparent );
		parent.AddComponent<QueryProbeComponent>( false );
		parent.AddComponent<QueryOtherComponent>();

		var root = new GameObject( parent );
		root.AddComponent<QueryProbeComponent>();
		root.AddComponent<QueryOtherComponent>( false );
		root.AddComponent<QueryProbeComponent>( false );

		var child = new GameObject( root );
		child.AddComponent<QueryProbeComponent>();

		var grandchild = new GameObject( child );
		grandchild.AddComponent<QueryProbeComponent>();
		grandchild.AddComponent<QueryProbeComponent>( false );

		var greatGrandchild = new GameObject( grandchild );
		greatGrandchild.AddComponent<QueryOtherComponent>();
		greatGrandchild.AddComponent<QueryProbeComponent>();

		var disabledChild = new GameObject( root ) { Enabled = false };
		disabledChild.AddComponent<QueryProbeComponent>();
		new GameObject( disabledChild ).AddComponent<QueryProbeComponent>();

		var emptyChild = new GameObject( root );
		new GameObject( emptyChild ).AddComponent<QueryOtherComponent>();

		var disabledParent = new GameObject( root ) { Enabled = false };
		var underDisabled = new GameObject( disabledParent );
		underDisabled.AddComponent<QueryProbeComponent>();

		foreach ( var (go, name) in new[] { (grandparent, "grandparent"), (root, "root"), (child, "child"), (greatGrandchild, "greatGrandchild"), (disabledChild, "disabledChild"), (underDisabled, "underDisabled") } )
		{
			AssertMatchesGetAll<Component>( go, name );
			AssertMatchesGetAll<QueryProbeComponent>( go, name );
			AssertMatchesGetAll<IQueryProbe>( go, name );
		}
	}

	[TestMethod]
	public void DeepHierarchyMatchesGetAll()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var root = scene.CreateObject();
		var go = root;

		for ( var i = 0; i < 70; i++ )
		{
			go.AddComponent<QueryProbeComponent>();
			new GameObject( go ).AddComponent<QueryOtherComponent>();
			go = new GameObject( go );
		}

		foreach ( var (from, find) in new[] { (root, FindMode.InSelf | FindMode.Enabled | FindMode.InDescendants), (go, FindMode.EverythingInSelfAndAncestors) } )
		{
			var expected = Collect<Component>( from, find );
			Assert.IsTrue( expected.Count > 16 );
			CollectionAssert.AreEqual( expected, from.Components.GetAll<Component>( find ).ToList() );
			CollectionAssert.AreEqual( expected, Iterate( from.Components.Query<Component>( find ) ) );
		}
	}

	[TestMethod]
	public void DestroyedObjectFindsNothing()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.AddComponent<QueryProbeComponent>();
		go.Destroy();

		Assert.AreEqual( 0, Iterate( go.Components.Query<Component>( FindMode.EverythingInSelfAndDescendants ) ).Count );
	}

	[TestMethod]
	public void WorksThroughLinq()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var a = go.AddComponent<QueryProbeComponent>();
		var b = new GameObject( go ).AddComponent<QueryProbeComponent>();

		CollectionAssert.AreEqual( new[] { a, b }, go.Components.Query<QueryProbeComponent>().ToArray() );
		Assert.AreEqual( b, go.Components.Query<QueryProbeComponent>().Last() );
	}

	[TestMethod, TestCategory( Allocations.Category )]
	public void IteratingDoesNotAllocate()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var root = scene.CreateObject();
		root.AddComponent<QueryProbeComponent>();

		for ( var i = 0; i < 4; i++ )
		{
			var child = new GameObject( root );
			child.AddComponent<QueryProbeComponent>();
			new GameObject( new GameObject( child ) ).AddComponent<QueryProbeComponent>();
		}

		var found = 0;
		Allocations.AssertSettles( "Querying descendants", () =>
		{
			foreach ( var c in root.Components.Query<QueryProbeComponent>( FindMode.EverythingInSelfAndDescendants ) )
				found++;

			foreach ( var c in root.Children[0].Components.Query<Component>( FindMode.EverythingInSelfAndAncestors ) )
				found++;
		} );

		Assert.IsTrue( found > 0 );
	}

	[TestMethod, TestCategory( Allocations.Category )]
	public void GetAllFindingNothingDoesNotAllocate()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var root = scene.CreateObject();
		root.AddComponent<QueryProbeComponent>();
		new GameObject( root ).AddComponent<QueryProbeComponent>();

		var found = 0;
		Allocations.AssertSettles( "GetAll with no matches", () =>
		{
			foreach ( var c in root.Components.GetAll<QueryOtherComponent>( FindMode.EverythingInSelfAndDescendants ) )
				found++;
		} );

		Assert.AreEqual( 0, found );
	}

	[TestMethod]
	public void GetAllAroundTheInlineSize()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		foreach ( var count in new[] { 1, 15, 16, 17, 32, 33 } )
		{
			var root = scene.CreateObject();
			for ( var i = 0; i < count; i++ )
				new GameObject( root ).AddComponent<QueryProbeComponent>();

			var all = root.Components.GetAll<QueryProbeComponent>( FindMode.EverythingInSelfAndDescendants ).ToList();
			Assert.AreEqual( count, all.Count );
			CollectionAssert.AreEqual( Collect<QueryProbeComponent>( root, FindMode.EverythingInSelfAndDescendants ), all );
		}
	}

	[TestMethod]
	public void GetAllResultsAreSnapshots()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var root = scene.CreateObject();
		var a = root.AddComponent<QueryProbeComponent>();
		var first = root.Components.GetAll<QueryProbeComponent>( FindMode.EverythingInSelfAndDescendants );
		var b = new GameObject( root ).AddComponent<QueryProbeComponent>();
		var second = root.Components.GetAll<QueryProbeComponent>( FindMode.EverythingInSelfAndDescendants );

		CollectionAssert.AreEqual( new[] { a }, first.ToArray() );
		CollectionAssert.AreEqual( new[] { a, b }, second.ToArray() );
	}
}

public interface IQueryProbe
{
}

public class QueryProbeComponent : Component, IQueryProbe
{
}

public class QueryOtherComponent : Component
{
}

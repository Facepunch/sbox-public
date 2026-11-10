namespace SceneTests.GameObjects;

/// <summary>
/// Pins the structural rules of the game object hierarchy: what parentings are legal,
/// how sibling order behaves, and that child order survives serialization and cloning.
/// </summary>
[TestClass]
[DoNotParallelize]
public class HierarchyTest : SceneTest
{
	static GameObject Named( Scene scene, string name, GameObject parent = null )
	{
		var go = scene.CreateObject();
		go.Name = name;
		if ( parent is not null ) go.Parent = parent;
		return go;
	}

	static string ChildNames( GameObject parent ) => string.Join( ",", parent.Children.Select( x => x.Name ) );

	/// <summary>
	/// Setting Parent to null must reparent to the scene root, not orphan the object.
	/// </summary>
	[TestMethod]
	public void ParentNullBecomesSceneRoot()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = Named( scene, "Parent" );
		var child = Named( scene, "Child", parent );

		Assert.IsFalse( child.IsRoot );

		child.Parent = null;

		Assert.AreEqual( scene, child.Parent );
		Assert.IsTrue( child.IsRoot );
		Assert.IsFalse( parent.Children.Contains( child ) );
	}

	/// <summary>
	/// An object can never become its own parent - the assignment is ignored and the
	/// hierarchy is left untouched.
	/// </summary>
	[TestMethod]
	public void ParentToSelfIsRejected()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = Named( scene, "Loner" );

		go.Parent = go;

		Assert.AreEqual( scene, go.Parent );
		Assert.IsFalse( go.Children.Contains( go ) );
	}

	/// <summary>
	/// Parenting to a direct child or any deeper descendant would create a cycle and
	/// must be rejected, leaving both objects where they were.
	/// </summary>
	[TestMethod]
	public void ParentToDescendantIsRejected()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = Named( scene, "Parent" );
		var child = Named( scene, "Child", parent );
		var grandchild = Named( scene, "Grandchild", child );

		parent.Parent = child;

		Assert.AreEqual( scene, parent.Parent );
		Assert.AreEqual( parent, child.Parent );

		parent.Parent = grandchild;

		Assert.AreEqual( scene, parent.Parent );
		Assert.AreEqual( child, grandchild.Parent );
	}

	/// <summary>
	/// Reparenting must remove the object from the old parent's child list and append
	/// it to the new parent's.
	/// </summary>
	[TestMethod]
	public void ReparentMovesBetweenChildLists()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var a = Named( scene, "A" );
		var b = Named( scene, "B" );
		var child = Named( scene, "Child", a );

		Assert.AreEqual( 1, a.Children.Count );

		child.Parent = b;

		Assert.AreEqual( 0, a.Children.Count );
		Assert.AreEqual( 1, b.Children.Count );
		Assert.AreEqual( child, b.Children[0] );
	}

	/// <summary>
	/// By default reparenting keeps the world position, recalculating the local
	/// transform. With keepWorldPosition false the local transform is kept instead,
	/// so the object moves in world space.
	/// </summary>
	[TestMethod]
	public void ReparentWorldPositionModes()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = Named( scene, "Parent" );
		parent.WorldPosition = new Vector3( 100, 0, 0 );

		var keep = Named( scene, "Keep" );
		keep.WorldPosition = new Vector3( 150, 0, 0 );

		keep.SetParent( parent );

		Assert.IsTrue( keep.WorldPosition.AlmostEqual( new Vector3( 150, 0, 0 ) ), $"{keep.WorldPosition}" );
		Assert.IsTrue( keep.LocalPosition.AlmostEqual( new Vector3( 50, 0, 0 ) ), $"{keep.LocalPosition}" );

		var move = Named( scene, "Move" );
		move.WorldPosition = new Vector3( 10, 0, 0 );

		move.SetParent( parent, keepWorldPosition: false );

		Assert.IsTrue( move.LocalPosition.AlmostEqual( new Vector3( 10, 0, 0 ) ), $"{move.LocalPosition}" );
		Assert.IsTrue( move.WorldPosition.AlmostEqual( new Vector3( 110, 0, 0 ) ), $"{move.WorldPosition}" );
	}

	/// <summary>
	/// AddSibling must insert the object directly before or after this one in the
	/// parent's child list.
	/// </summary>
	[TestMethod]
	public void AddSiblingOrdering()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = Named( scene, "Parent" );
		var a = Named( scene, "A", parent );
		var b = Named( scene, "B", parent );
		var c = Named( scene, "C", parent );

		Assert.AreEqual( "A,B,C", ChildNames( parent ) );

		var d = Named( scene, "D" );
		b.AddSibling( d, before: true );
		Assert.AreEqual( "A,D,B,C", ChildNames( parent ) );

		var e = Named( scene, "E" );
		b.AddSibling( e, before: false );
		Assert.AreEqual( "A,D,B,E,C", ChildNames( parent ) );

		// Moving an existing sibling reorders rather than duplicates
		c.AddSibling( a, before: true );
		Assert.AreEqual( "D,B,E,A,C", ChildNames( parent ) );
		Assert.AreEqual( 5, parent.Children.Count );
	}

	/// <summary>
	/// Child order is part of the scene - it must survive a serialize/deserialize
	/// round trip exactly.
	/// </summary>
	[TestMethod]
	public void ChildOrderSurvivesSerialize()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = Named( scene, "Parent" );
		Named( scene, "First", parent );
		Named( scene, "Second", parent );
		Named( scene, "Third", parent );

		var node = parent.Serialize();

		var restored = new GameObject();
		restored.Deserialize( node );

		Assert.AreEqual( "First,Second,Third", ChildNames( restored ) );
	}

	/// <summary>
	/// Child order must survive cloning.
	/// </summary>
	[TestMethod]
	public void ChildOrderSurvivesClone()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = Named( scene, "Parent" );
		Named( scene, "First", parent );
		Named( scene, "Second", parent );
		Named( scene, "Third", parent );

		var clone = parent.Clone();

		Assert.AreEqual( "First,Second,Third", ChildNames( clone ) );
	}

	/// <summary>
	/// IsAncestor: an object is its own ancestor, parents and grandparents are
	/// ancestors, children are not.
	/// </summary>
	[TestMethod]
	public void IsAncestorSemantics()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = Named( scene, "Parent" );
		var child = Named( scene, "Child", parent );
		var grandchild = Named( scene, "Grandchild", child );

		Assert.IsTrue( child.IsAncestor( child ) );
		Assert.IsTrue( child.IsAncestor( parent ) );
		Assert.IsTrue( grandchild.IsAncestor( parent ) );
		Assert.IsFalse( parent.IsAncestor( child ) );
	}

	/// <summary>
	/// GetNextSibling walks the parent's child list in order, optionally skipping
	/// disabled siblings, and returns null at the end.
	/// </summary>
	[TestMethod]
	public void GetNextSibling()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = Named( scene, "Parent" );
		var a = Named( scene, "A", parent );
		var b = Named( scene, "B", parent );
		var c = Named( scene, "C", parent );

		Assert.AreEqual( b, a.GetNextSibling( false ) );
		Assert.AreEqual( c, b.GetNextSibling( false ) );

		b.Enabled = false;
		Assert.AreEqual( c, a.GetNextSibling( true ) );
		Assert.AreEqual( b, a.GetNextSibling( false ) );
	}
}

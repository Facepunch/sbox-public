using Sandbox.Engine;
using Sandbox.UI;
using System;

namespace UITests.Panels;

[TestClass]
[DoNotParallelize] // Modifies UI System Global
public class PanelHierarchyTest
{
	[TestCleanup]
	public void Cleanup()
	{
		GlobalContext.Current.UISystem.Clear();
	}

	/// <summary>
	/// Setting Parent adds the panel to the parent's children list, gives it a
	/// sibling index, and resetting Parent back to null detaches it again.
	/// </summary>
	[TestMethod]
	public void ParentAssignAndReset()
	{
		var parent = new Panel();
		var child = new Panel();

		Assert.IsFalse( parent.HasChildren );
		Assert.AreEqual( 0, parent.ChildrenCount );

		child.Parent = parent;

		Assert.IsTrue( parent.HasChildren );
		Assert.AreEqual( 1, parent.ChildrenCount );
		Assert.AreSame( parent, child.Parent );
		Assert.AreSame( child, parent.Children.First() );
		Assert.AreEqual( 0, child.SiblingIndex );
		Assert.AreEqual( 0, parent.GetChildIndex( child ) );

		child.Parent = null;

		Assert.IsFalse( parent.HasChildren );
		Assert.AreEqual( 0, parent.ChildrenCount );
		Assert.IsNull( child.Parent );
		Assert.AreEqual( -1, parent.GetChildIndex( child ) );
	}

	/// <summary>
	/// A panel can never be parented to itself, and a RootPanel can never be
	/// given a parent - both throw immediately.
	/// </summary>
	[TestMethod]
	public void ParentGuardsThrow()
	{
		var p = new Panel();
		Assert.ThrowsException<Exception>( () => p.Parent = p );

		var root = new RootPanel();
		var other = new Panel();
		Assert.ThrowsException<Exception>( () => root.Parent = other );
	}

	/// <summary>
	/// The AddChild overloads create or attach children, apply the optional
	/// class names and keep children in the order they were added.
	/// </summary>
	[TestMethod]
	public void AddChildOverloads()
	{
		var parent = new Panel();

		var a = parent.AddChild<Panel>( "first" );
		Assert.AreSame( parent, a.Parent );
		Assert.IsTrue( a.HasClass( "first" ) );

		Assert.IsTrue( parent.AddChild( out Panel b, "second" ) );
		Assert.AreSame( parent, b.Parent );
		Assert.IsTrue( b.HasClass( "second" ) );

		var c = parent.AddChild( new Panel() );
		Assert.AreSame( parent, c.Parent );

		CollectionAssert.AreEqual( new[] { a, b, c }, parent.Children.ToArray() );
		Assert.AreEqual( 0, a.SiblingIndex );
		Assert.AreEqual( 1, b.SiblingIndex );
		Assert.AreEqual( 2, c.SiblingIndex );
	}

	/// <summary>
	/// GetChild returns children by index, null when out of bounds, and wraps
	/// the index around in both directions when looping is requested.
	/// </summary>
	[TestMethod]
	public void GetChildIndexing()
	{
		var parent = new Panel();
		var a = parent.AddChild<Panel>();
		var b = parent.AddChild<Panel>();
		var c = parent.AddChild<Panel>();

		Assert.AreSame( a, parent.GetChild( 0 ) );
		Assert.AreSame( c, parent.GetChild( 2 ) );
		Assert.IsNull( parent.GetChild( 3 ) );
		Assert.IsNull( parent.GetChild( -1 ) );

		Assert.AreSame( c, parent.GetChild( -1, true ) );
		Assert.AreSame( a, parent.GetChild( 3, true ) );
		Assert.AreSame( b, parent.GetChild( 4, true ) );
	}

	/// <summary>
	/// SetChildIndex reorders the child list, refreshes every sibling index,
	/// clamps an out of range index and refuses panels that aren't children.
	/// </summary>
	[TestMethod]
	public void SetChildIndexReorders()
	{
		var parent = new Panel();
		var a = parent.AddChild<Panel>();
		var b = parent.AddChild<Panel>();
		var c = parent.AddChild<Panel>();

		parent.SetChildIndex( c, 0 );

		CollectionAssert.AreEqual( new[] { c, a, b }, parent.Children.ToArray() );
		Assert.AreEqual( 0, c.SiblingIndex );
		Assert.AreEqual( 1, a.SiblingIndex );
		Assert.AreEqual( 2, b.SiblingIndex );

		// Out of range indexes get clamped to the last slot
		parent.SetChildIndex( c, 99 );
		CollectionAssert.AreEqual( new[] { a, b, c }, parent.Children.ToArray() );

		var stranger = new Panel();
		Assert.ThrowsException<ArgumentException>( () => parent.SetChildIndex( stranger, 0 ) );
	}

	/// <summary>
	/// MoveAfterSibling places a panel directly after the given sibling when
	/// moving forwards, and throws when the panel has no parent at all.
	/// </summary>
	[TestMethod]
	public void MoveAfterSiblingForward()
	{
		var parent = new Panel();
		var a = parent.AddChild<Panel>();
		var b = parent.AddChild<Panel>();
		var c = parent.AddChild<Panel>();

		b.MoveAfterSibling( c );

		CollectionAssert.AreEqual( new[] { a, c, b }, parent.Children.ToArray() );

		var orphan = new Panel();
		Assert.ThrowsException<ArgumentException>( () => orphan.MoveAfterSibling( a ) );
	}

	/// <summary>
	/// ChildrenOfType walks the child list backwards, so matching children are
	/// returned in reverse order of how they were added.
	/// </summary>
	[TestMethod]
	public void ChildrenOfTypeReturnsReverseOrder()
	{
		var parent = new Panel();
		var m1 = parent.AddChild( new HierarchyMarkerPanel() );
		parent.AddChild<Panel>();
		var m2 = parent.AddChild( new HierarchyMarkerPanel() );

		CollectionAssert.AreEqual( new[] { m2, m1 }, parent.ChildrenOfType<HierarchyMarkerPanel>().ToArray() );
	}

	/// <summary>
	/// AncestorsAndSelf starts at the panel itself and walks up to the root,
	/// Ancestors skips the panel itself, and Descendants enumerates the whole
	/// subtree depth-first in pre-order.
	/// </summary>
	[TestMethod]
	public void AncestorAndDescendantEnumeration()
	{
		var top = new Panel();
		var mid = top.AddChild<Panel>();
		var leafa = mid.AddChild<Panel>();
		var leafb = mid.AddChild<Panel>();
		var sibling = top.AddChild<Panel>();

		CollectionAssert.AreEqual( new[] { leafa, mid, top }, leafa.AncestorsAndSelf.ToArray() );
		CollectionAssert.AreEqual( new[] { mid, top }, leafa.Ancestors.ToArray() );
		CollectionAssert.AreEqual( new[] { mid, leafa, leafb, sibling }, top.Descendants.ToArray() );
	}

	/// <summary>
	/// IsAncestor returns true for the panel itself and anything above it in
	/// the tree, and false for siblings and children.
	/// </summary>
	[TestMethod]
	public void IsAncestorChecks()
	{
		var top = new Panel();
		var mid = top.AddChild<Panel>();
		var leaf = mid.AddChild<Panel>();
		var sibling = top.AddChild<Panel>();

		Assert.IsTrue( leaf.IsAncestor( leaf ) );
		Assert.IsTrue( leaf.IsAncestor( mid ) );
		Assert.IsTrue( leaf.IsAncestor( top ) );
		Assert.IsFalse( leaf.IsAncestor( sibling ) );
		Assert.IsFalse( top.IsAncestor( leaf ) );
	}

	/// <summary>
	/// Reparenting a panel between two root panels removes it from the first
	/// root, attaches it to the second and updates FindRootPanel, and the new
	/// root lays it out on its next layout pass.
	/// </summary>
	[TestMethod]
	public void ReparentBetweenRoots()
	{
		var rootA = new RootPanel();
		rootA.PanelBounds = new Rect( 0, 0, 1000, 1000 );
		var rootB = new RootPanel();
		rootB.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var p = new Panel { Parent = rootA };
		Assert.AreSame( rootA, p.FindRootPanel() );

		p.Parent = rootB;

		Assert.AreEqual( 0, rootA.ChildrenCount );
		Assert.AreEqual( 1, rootB.ChildrenCount );
		Assert.AreSame( rootB, p.FindRootPanel() );

		p.Style.Set( "position: absolute; left: 10px; top: 20px; width: 100px; height: 50px;" );
		rootB.Layout();

		Assert.AreEqual( 10, p.Box.Rect.Left );
		Assert.AreEqual( 20, p.Box.Rect.Top );
		Assert.AreEqual( 100, p.Box.Rect.Width );
	}

	/// <summary>
	/// Adding a child after a completed layout dirties the parent chain, so
	/// the next layout pass lays the new child out without any manual
	/// invalidation.
	/// </summary>
	[TestMethod]
	public void NewChildIsLaidOutAfterLayout()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );
		root.Layout();

		var late = new Panel { Parent = root };
		late.Style.Set( "position: absolute; left: 5px; top: 6px; width: 50px; height: 40px;" );
		root.Layout();

		Assert.AreEqual( 5, late.Box.Rect.Left );
		Assert.AreEqual( 6, late.Box.Rect.Top );
		Assert.AreEqual( 50, late.Box.Rect.Width );
		Assert.AreEqual( 40, late.Box.Rect.Height );
	}

	/// <summary>
	/// SortChildren reorders the children with the given comparison and
	/// immediately refreshes their sibling indexes.
	/// </summary>
	[TestMethod]
	public void SortChildrenReorders()
	{
		var parent = new Panel();
		var c = parent.AddChild<Panel>( "c" );
		var a = parent.AddChild<Panel>( "a" );
		var b = parent.AddChild<Panel>( "b" );

		parent.SortChildren( ( x, y ) => string.CompareOrdinal( x.Classes, y.Classes ) );

		CollectionAssert.AreEqual( new[] { a, b, c }, parent.Children.ToArray() );
		Assert.AreEqual( 0, a.SiblingIndex );
		Assert.AreEqual( 1, b.SiblingIndex );
		Assert.AreEqual( 2, c.SiblingIndex );
	}

	/// <summary>
	/// DeleteChildren deletes every child but leaves the parent itself alive.
	/// </summary>
	[TestMethod]
	public void DeleteChildrenEmptiesPanel()
	{
		var parent = new Panel();
		parent.AddChild<Panel>();
		parent.AddChild<Panel>();

		parent.DeleteChildren( immediate: true );

		Assert.AreEqual( 0, parent.ChildrenCount );
		Assert.IsTrue( parent.IsValid );
	}
}

/// <summary>
/// Marker subclass so ChildrenOfType has a distinct panel type to filter on.
/// </summary>
public class HierarchyMarkerPanel : Panel
{
}

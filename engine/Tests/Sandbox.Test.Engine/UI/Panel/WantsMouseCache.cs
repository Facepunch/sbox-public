using Sandbox.UI;

namespace UITests.Panels;

/// <summary>
/// A panel keeps its answer to whether it wants the mouse while nothing in its subtree changes, and every change that can
/// move the answer still moves it: pointer-events, visibility, children added or deleted, and panels that answer themselves.
/// </summary>
[TestClass]
[DoNotParallelize] // The shared panel clock
public class WantsMouseCacheTest
{
	[TestCleanup]
	public void Cleanup()
	{
		Panel.CacheWantsMouse = true;
	}

	sealed class Asks : Panel
	{
		public bool Wants;
		public override bool WantsMouseInput() => Wants;
	}

	/// <summary>
	/// A frame as the UI system runs one: tick, input, styles, layout, then the roots record (which moves the paint clock on).
	/// </summary>
	static bool Frame( RootPanel root )
	{
		root.TickInternal();
		root.TickInputInternal();
		root.BuildStyleRules();
		root.Layout();
		Panel.PaintClock++;
		return root.ChildrenWantMouseInput;
	}

	static (RootPanel Root, Panel Menu, Panel Leaf) Tree()
	{
		var root = UiTesting.CreateRoot();
		var menu = root.AddChild<Panel>();
		Panel leaf = null;
		for ( int i = 0; i < 8; i++ )
		{
			var cell = menu.AddChild<Panel>();
			cell.Style.Set( "width: 20px; height: 20px;" );
			leaf = cell.AddChild<Panel>();
		}
		for ( int i = 0; i < 3; i++ ) Frame( root );
		return (root, menu, leaf);
	}

	[TestMethod]
	public void UnchangedTreeIsNotWalkedAgain()
	{
		var (root, _, _) = Tree();
		var before = Panel.WantsMouseWorkedOut;
		Assert.IsFalse( Frame( root ) );
		Assert.AreEqual( before, Panel.WantsMouseWorkedOut, "nothing changed, so the root's answer is kept" );
	}

	[TestMethod]
	public void PointerEventsOnALeafIsSeen()
	{
		var (root, _, leaf) = Tree();
		leaf.Style.Set( "pointer-events: all;" );
		Frame( root );
		Assert.IsTrue( Frame( root ) );

		leaf.Style.Set( "pointer-events: none;" );
		Frame( root );
		Assert.IsFalse( Frame( root ) );
	}

	[TestMethod]
	public void HidingTheWantingPanelIsSeen()
	{
		var (root, _, leaf) = Tree();
		leaf.Style.Set( "pointer-events: all;" );
		Frame( root );
		Assert.IsTrue( Frame( root ) );

		leaf.Parent.Style.Set( "display: none;" );
		Frame( root );
		Assert.IsFalse( Frame( root ) );

		leaf.Parent.Style.Set( "display: flex;" );
		Frame( root );
		Assert.IsTrue( Frame( root ) );
	}

	[TestMethod]
	public void AddedAndDeletedChildrenAreSeen()
	{
		var (root, menu, _) = Tree();
		var added = menu.AddChild<Panel>();
		added.Style.Set( "pointer-events: all;" );
		Frame( root );
		Assert.IsTrue( Frame( root ) );

		added.Delete( true );
		Frame( root );
		Assert.IsFalse( Frame( root ) );
	}

	[TestMethod]
	public void APanelThatAnswersItselfIsAskedEveryFrame()
	{
		var (root, menu, _) = Tree();
		var asks = menu.AddChild<Asks>();
		Frame( root );
		Assert.IsFalse( Frame( root ) );

		asks.Wants = true;
		Assert.IsTrue( Frame( root ), "nothing stamps its answer, so it's asked again" );
	}

	[TestMethod]
	public void TurnedOffItWalksEveryFrame()
	{
		var (root, _, _) = Tree();
		Panel.CacheWantsMouse = false;
		var before = Panel.WantsMouseWorkedOut;
		Frame( root );
		Assert.IsTrue( Panel.WantsMouseWorkedOut > before );
	}
}

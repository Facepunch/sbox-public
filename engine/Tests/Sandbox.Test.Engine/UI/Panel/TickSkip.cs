using Sandbox.UI;

namespace UITests.Panels;

/// <summary>
/// A subtree whose last tick had nothing to do is skipped until something in it changes, and nothing it would have done
/// is lost: changes that stamp it, queued events, class binds and panels that tick themselves all still tick.
/// </summary>
[TestClass]
[DoNotParallelize] // The shared panel clock
public class TickSkipTest
{
	[TestCleanup]
	public void Cleanup()
	{
		Panel.SkipQuietTicks = true;
	}

	sealed class Counter : Panel
	{
		public int Ticks;
		public override void Tick() => Ticks++;
	}

	/// <summary>
	/// A frame as the UI system runs one: tick, styles, layout, then the roots record (which moves the paint clock on).
	/// </summary>
	static void Frame( RootPanel root )
	{
		root.TickInternal();
		root.BuildStyleRules();
		root.Layout();
		Panel.PaintClock++;
	}

	static (RootPanel Root, Panel Leaf) Tree()
	{
		var root = UiTesting.CreateRoot();
		var menu = root.AddChild<Panel>();
		Panel leaf = null;
		for ( int i = 0; i < 4; i++ )
		{
			var cell = menu.AddChild<Panel>();
			cell.Style.Set( "width: 20px; height: 20px;" );
			leaf = cell.AddChild<Panel>();
		}
		for ( int i = 0; i < 3; i++ ) Frame( root );
		return (root, leaf);
	}

	[TestMethod]
	public void QuietSubtreeIsSkipped()
	{
		var (root, _) = Tree();
		var skipped = Panel.SkippedTicks;
		Frame( root );
		Assert.IsTrue( Panel.SkippedTicks > skipped, "the menu should be skipped" );
	}

	[TestMethod]
	public void StyleChangeInASkippedSubtreeTicksIt()
	{
		var (root, leaf) = Tree();
		root.StyleSheet.Parse( ".x::before { content: 'a'; }" );
		Frame( root );

		leaf.AddClass( "x" );
		for ( int i = 0; i < 3; i++ ) Frame( root );
		Assert.AreEqual( 1, leaf.ChildrenCount, "the ::before element is made in the tick" );
	}

	[TestMethod]
	public void EventQueuedOnASkippedPanelRuns()
	{
		var (root, leaf) = Tree();
		int calls = 0;
		leaf.AddEventListener( "custom", () => calls++ );
		Frame( root );

		leaf.CreateEvent( "custom" );
		Frame( root );
		Assert.AreEqual( 1, calls );
	}

	[TestMethod]
	public void ClassBoundOnASkippedPanelApplies()
	{
		var (root, leaf) = Tree();
		var on = false;
		leaf.BindClass( "on", () => on );
		Frame( root );
		Assert.IsFalse( leaf.HasClass( "on" ) );

		on = true;
		Frame( root );
		Assert.IsTrue( leaf.HasClass( "on" ), "a panel with class binds ticks every frame" );
	}

	[TestMethod]
	public void PanelThatTicksItselfTicksEveryFrame()
	{
		var root = UiTesting.CreateRoot();
		var counter = root.AddChild<Panel>().AddChild<Panel>().AddChild<Counter>();
		for ( int i = 0; i < 3; i++ ) Frame( root );

		var ticks = counter.Ticks;
		Frame( root );
		Assert.IsTrue( counter.Ticks > ticks );
		ticks = counter.Ticks;
		Frame( root );
		Assert.IsTrue( counter.Ticks > ticks );
	}

	[TestMethod]
	public void DisabledNeverSkips()
	{
		Panel.SkipQuietTicks = false;
		var (root, _) = Tree();
		var skipped = Panel.SkippedTicks;
		Frame( root );
		Assert.AreEqual( skipped, Panel.SkippedTicks );
	}
}

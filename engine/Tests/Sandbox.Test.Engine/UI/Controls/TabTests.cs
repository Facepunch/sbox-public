using Sandbox.UI;
using static UITests.UiTesting;

namespace UITests.Controls;

[TestClass]
[DoNotParallelize]
public class TabTests
{
	bool previousRenderText;
	UISurface surface;
	TabPanel pages;

	[TestInitialize]
	public void Setup()
	{
		ThreadSafe.MarkMainThread();
		previousRenderText = DisableTextRendering();
		surface = new UISurface { Size = new Vector2( 800, 600 ), DpiScale = 1, MouseInside = true };
		pages = new TabPanel { Parent = surface.Root };
	}

	[TestCleanup]
	public void Cleanup()
	{
		try { surface.Dispose(); }
		finally { TextBlock.ui_rendertext = previousRenderText; }
	}

	void Frame() { for ( int i = 0; i < 3; i++ ) UiTesting.Frame( surface ); }

	[TestMethod]
	public void AddingAndSelectingTabDoesNotScrollEnclosingPage()
	{
		var scroller = new Panel { Parent = surface.Root };
		scroller.Style.Width = 400;
		scroller.Style.Height = 200;
		scroller.Style.Overflow = OverflowMode.Scroll;
		pages.Parent = scroller;
		pages.Style.MarginTop = 150;
		pages.Style.Height = 500;
		pages.Style.FlexShrink = 0;
		pages.AddTab( "First", new Panel() );
		Frame();
		scroller.ScrollTo( new Vector2( 0, 80 ) );
		Frame();
		var before = scroller.ScrollOffset;
		var added = pages.AddTab( "New document", new Panel(), "description", true );
		pages.TabBar.SelectTab( added );
		Assert.AreEqual( before, scroller.ScrollOffset, "Unlaid-out tab bounds must not scroll ancestors." );
		for ( int i = 0; i < 8; i++ )
		{
			Frame();
			Assert.AreEqual( before, scroller.ScrollOffset );
		}
	}

	[TestMethod]
	public void NewlySelectedOverflowTabIsRevealedAfterLayout()
	{
		var bar = pages.TabBar;
		bar.AutoHideText = false;
		bar.Style.Width = 150;
		pages.AddTab( "First document", new Panel() );
		Frame();
		Tab last = null;
		for ( int i = 0; i < 6; i++ ) last = pages.AddTab( $"Document number {i}", new Panel() );
		bar.SelectTab( last );
		Frame();
		Frame();
		Assert.IsTrue( bar.ScrollOffset.x > 0 );
		Assert.IsTrue( last.Box.Rect.Right <= bar.Box.RectInner.Right + 1 );
	}

	[TestMethod]
	public void BadgesUpdateAndNullHidesThem()
	{
		var tab = pages.AddTab( "Messages", new Panel(), "mail", count: 0 );
		var badge = tab.Children.OfType<Label>().Single( x => x.HasClass( "tab-count" ) );
		Frame();
		Assert.IsTrue( badge.IsVisible );
		Assert.AreEqual( "0", badge.Text );
		tab.Count = 1234;
		Frame();
		Assert.AreEqual( "1234", badge.Text );
		tab.Count = null;
		Frame();
		Assert.IsFalse( badge.IsVisible );
	}

	[TestMethod]
	public void NarrowTabsKeepBadgesAndRestoreTitlesWhenWidened()
	{
		var bar = pages.TabBar;
		var a = pages.AddTab( "Messages", new Panel(), "mail", count: 12 );
		var b = pages.AddTab( "History", new Panel(), "history" );
		var plain = pages.AddTab( "No icon", new Panel() );
		bar.Style.Width = 240;
		Frame();
		Frame();
		Assert.IsTrue( a.IsIconOnly );
		Assert.IsTrue( b.IsIconOnly );
		Assert.IsFalse( plain.IsIconOnly );
		Assert.AreEqual( "Messages", a.Tooltip );
		Assert.IsTrue( a.Children.Single( x => x.HasClass( "tab-count" ) ).IsVisible );
		Frame();
		Assert.IsTrue( a.IsIconOnly, "Compact layout must not oscillate." );
		bar.Style.Width = 600;
		Frame();
		Frame();
		Assert.IsFalse( a.IsIconOnly );
		Assert.IsFalse( b.IsIconOnly );
		bar.Style.Width = 240;
		bar.AutoHideText = false;
		Frame();
		Assert.IsFalse( a.IsIconOnly );
	}

	[TestMethod]
	public void SwitchingAndReorderingRetainsPageState()
	{
		var entry = new TextEntry { Text = "unsaved" };
		var a = pages.AddTab( "A", entry, canClose: true );
		var b = pages.AddTab( "B", new Panel(), canClose: true );
		Frame();
		Assert.IsTrue( entry.IsVisible );
		pages.TabBar.SelectTab( b );
		Frame();
		Assert.IsFalse( entry.IsVisible );
		Assert.IsTrue( entry.IsValid );
		pages.TabBar.MoveTab( a, 1 );
		Assert.AreSame( b, pages.TabBar.SelectedTab );
		CollectionAssert.AreEqual( new[] { b, a }, pages.TabBar.Tabs.ToArray() );
		pages.TabBar.SelectTab( a );
		Frame();
		Assert.IsTrue( entry.IsVisible );
		Assert.AreEqual( "unsaved", entry.Text );
	}

	[TestMethod]
	public void ClosingRespectsPolicyAndSelectsAdjacentPage()
	{
		var locked = pages.AddTab( "Locked", new Panel() );
		var content = new Panel();
		var a = pages.AddTab( "A", content, canClose: true );
		var b = pages.AddTab( "B", new Panel(), canClose: true );
		pages.TabBar.SelectTab( a );
		Assert.IsFalse( pages.TabBar.CloseTab( locked ) );
		pages.TabBar.CanCloseTab = _ => false;
		Assert.IsFalse( pages.TabBar.CloseTab( a ) );
		Assert.IsTrue( content.IsValid );
		pages.TabBar.CanCloseTab = null;
		Assert.IsTrue( pages.TabBar.CloseTab( a ) );
		Assert.IsFalse( content.IsValid );
		Assert.AreSame( b, pages.TabBar.SelectedTab );
		pages.TabBar.CloseTab( b );
		Assert.AreSame( locked, pages.TabBar.SelectedTab );
		pages.TabBar.RemoveTab( locked );
		Assert.IsNull( pages.TabBar.SelectedTab );
		Assert.AreEqual( 0, pages.Body.ChildrenCount );
	}

	[TestMethod]
	public void DragReorderCanBeDisabledCancelledOrCommitted()
	{
		var bar = pages.TabBar;
		var a = pages.AddTab( "First tab", new Panel() );
		var b = pages.AddTab( "Second tab", new Panel() );
		Frame();
		var end = new Vector2( b.Box.Rect.Right - 1, b.Box.Rect.Center.y );
		bar.BeginReorder( a );
		bar.EndReorder( end );
		Assert.AreSame( a, bar.Tabs[0] );
		bar.AllowReorder = true;
		bar.BeginReorder( a );
		bar.UpdateReorder( end );
		bar.CancelReorder();
		Assert.AreSame( a, bar.Tabs[0] );
		bar.BeginReorder( a );
		bar.EndReorder( end );
		Assert.AreSame( b, bar.Tabs[0] );
		Assert.AreSame( a, bar.SelectedTab );
		Assert.IsFalse( a.HasClass( "dragging" ) );
	}

	[TestMethod]
	public void CloseButtonDoesNotSelectInactivePage()
	{
		var a = pages.AddTab( "A", new Panel() );
		var b = pages.AddTab( "B", new Panel(), canClose: true );
		int changes = 0;
		pages.TabBar.SelectionChanged += _ => changes++;
		b.Children.OfType<Button>().Single().Click();
		Frame();
		Assert.AreSame( a, pages.TabBar.SelectedTab );
		Assert.AreEqual( 0, changes );
		Assert.IsFalse( b.IsValid );
	}
}

using Sandbox.UI;

namespace UITests.Panels;

[TestClass]
[DoNotParallelize] // Modifies UI System Global + RealTime.SmoothDelta
public partial class PanelScrollingTest
{
	float savedSmoothDelta;

	/// <summary>
	/// Remembers the global smooth delta so tests that drive the scroll pump can restore it.
	/// </summary>
	[TestInitialize]
	public void Initialize()
	{
		savedSmoothDelta = RealTime.SmoothDelta;
	}

	/// <summary>
	/// Restores the global smooth delta so other test classes see the value they expect.
	/// </summary>
	[TestCleanup]
	public void Cleanup()
	{
		RealTime.SmoothDelta = savedSmoothDelta;
	}

	/// <summary>
	/// Builds a 200x200 panel with overflow-y: scroll containing a single 1000px tall child,
	/// laid out once so ScrollSize and ComputedStyle are populated.
	/// </summary>
	static (RootPanel Root, Panel Scroller) CreateVerticalScroller()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var scroller = root.Add.Panel();
		scroller.Style.Set( "width: 200px; height: 200px; overflow-y: scroll; flex-direction: column;" );

		var content = scroller.Add.Panel();
		content.Style.Set( "width: 100px; height: 1000px; flex-shrink: 0;" );

		root.Layout();

		return (root, scroller);
	}

	/// <summary>
	/// Builds a 200x200 padded scroll panel containing a single child of the given size.
	/// </summary>
	static (RootPanel Root, Panel Scroller) CreatePaddedScroller( int contentWidth, int contentHeight, string overflow = "overflow: scroll;" )
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var scroller = root.Add.Panel();
		scroller.Style.Set( $"width: 200px; height: 200px; padding: 20px; {overflow} flex-direction: column;" );

		var content = scroller.Add.Panel();
		content.Style.Set( $"width: {contentWidth}px; height: {contentHeight}px; flex-shrink: 0;" );

		root.Layout();

		return (root, scroller);
	}

	/// <summary>
	/// A scroll panel whose content fits inside it has nothing to scroll - on either axis. Its own
	/// padding must not count as overflow, otherwise every padded scroll panel is scrollable by
	/// its padding even when the content is nowhere near the edge.
	/// </summary>
	[TestMethod]
	public void PaddingAloneDoesNotMakePanelScrollable()
	{
		var (_, scroller) = CreatePaddedScroller( 100, 100 );

		Assert.AreEqual( 0, scroller.ScrollSize.x, 0.001f );
		Assert.AreEqual( 0, scroller.ScrollSize.y, 0.001f );
		Assert.IsFalse( scroller.HasScrollX );
		Assert.IsFalse( scroller.HasScrollY );
		Assert.IsFalse( scroller.TryScroll( new Vector2( 1, 0 ) ) );
		Assert.IsFalse( scroller.TryScroll( new Vector2( 0, 1 ) ) );
	}

	/// <summary>
	/// overflow-y: scroll on its own leaves the X axis unset, which the engine fills to scroll as
	/// well (like CSS computing the other axis to auto). That must not make a panel horizontally
	/// scrollable when its content is narrower than the box.
	/// </summary>
	[TestMethod]
	public void VerticalScrollerWithPaddingHasNoHorizontalScroll()
	{
		var (_, scroller) = CreatePaddedScroller( 100, 1000, "overflow-y: scroll;" );

		Assert.IsFalse( scroller.HasScrollX );
		Assert.AreEqual( 0, scroller.ScrollSize.x, 0.001f );
		Assert.IsFalse( scroller.TryScroll( new Vector2( 1, 0 ) ) );
		Assert.IsTrue( scroller.HasScrollY );
	}

	/// <summary>
	/// When the content does overflow, the padding after it is still part of the scrollable area:
	/// 20 (top pad) + 1000 (content) + 20 (bottom pad) - 200 (box) = 840.
	/// </summary>
	[TestMethod]
	public void OverflowingContentIncludesTrailingPadding()
	{
		var (_, scroller) = CreatePaddedScroller( 100, 1000 );

		Assert.AreEqual( 840, scroller.ScrollSize.y, 0.001f );
		Assert.AreEqual( 0, scroller.ScrollSize.x, 0.001f );
	}

	/// <summary>
	/// A panel with overflow-y: scroll whose content is taller than its box becomes scrollable on
	/// the Y axis only, and ScrollSize reports the content overhang (1000 - 200 = 800).
	/// </summary>
	[TestMethod]
	public void ScrollAxesDetectedAfterLayout()
	{
		var (_, scroller) = CreateVerticalScroller();

		Assert.IsTrue( scroller.HasScrollY );
		Assert.IsFalse( scroller.HasScrollX );
		Assert.AreEqual( 0, scroller.ScrollSize.x, 0.001f );
		Assert.AreEqual( 800, scroller.ScrollSize.y, 0.001f );
		Assert.AreEqual( 0, scroller.ScrollOffset.y, 0.001f );
	}

	/// <summary>
	/// TryScroll converts wheel deltas into scroll velocity: 20 units per wheel step, with each
	/// further step amplified by the current velocity (1 + length/100). A horizontal wheel is
	/// refused because the panel has no horizontal overflow.
	/// </summary>
	[TestMethod]
	public void MouseWheelAddsScrollVelocity()
	{
		var (_, scroller) = CreateVerticalScroller();

		Assert.IsTrue( scroller.TryScroll( new Vector2( 0, 1 ) ) );
		Assert.AreEqual( 20, scroller.ScrollVelocity.y, 0.001f );

		Assert.IsTrue( scroller.TryScroll( new Vector2( 0, 0.125f ) ) );
		Assert.AreEqual( 23, scroller.ScrollVelocity.y, 0.001f );

		// Velocity compounds - 20 * (1 + 23/100) = 24.6 gets added on top
		Assert.IsTrue( scroller.TryScroll( new Vector2( 0, 1 ) ) );
		Assert.AreEqual( 47.6f, scroller.ScrollVelocity.y, 0.001f );

		Assert.IsFalse( scroller.TryScroll( new Vector2( 1, 0 ) ) );
		Assert.AreEqual( 0, scroller.ScrollVelocity.x, 0.001f );
	}

	/// <summary>
	/// OnMouseWheel on a non-scrollable child bubbles up the hierarchy until it finds the
	/// scrollable ancestor, which receives the scroll velocity.
	/// </summary>
	[TestMethod]
	public void MouseWheelPropagatesToScrollableAncestor()
	{
		var (_, scroller) = CreateVerticalScroller();
		var inner = scroller.Children.First();

		inner.OnMouseWheel( new Vector2( 0, 1 ) );

		Assert.AreEqual( 20, scroller.ScrollVelocity.y, 0.001f );
	}

	/// <summary>
	/// Scroll velocity added by the mouse wheel is integrated into ScrollOffset by the layout
	/// pass (ConstrainScrolling), moving the content within the scrollable extents.
	/// </summary>
	[TestMethod]
	public void WheelVelocityMovesScrollOffset()
	{
		var (root, scroller) = CreateVerticalScroller();

		// The layout integration step scales by RealTime.SmoothDelta, which is never
		// ticked in the test host - give it a fixed 60fps frame time.
		RealTime.SmoothDelta = 1.0f / 60.0f;

		Assert.IsTrue( scroller.TryScroll( new Vector2( 0, 5 ) ) );

		for ( int i = 0; i < 10; i++ )
		{
			root.Layout();
		}

		Assert.IsTrue( scroller.ScrollOffset.y > 0 );
		Assert.IsTrue( scroller.ScrollOffset.y <= scroller.ScrollSize.y );
	}

	/// <summary>
	/// ScrollOffset written past the content extents is pulled back by the layout pass: values
	/// beyond the bottom clamp to ScrollSize and values above the top clamp back to zero.
	/// </summary>
	[TestMethod]
	public void ScrollOffsetClampsToContentExtents()
	{
		var (root, scroller) = CreateVerticalScroller();

		RealTime.SmoothDelta = 1.0f / 60.0f;

		scroller.ScrollOffset = new Vector2( 0, 5000 );
		scroller.SetNeedsPreLayout();
		root.Layout();

		Assert.AreEqual( scroller.ScrollSize.y, scroller.ScrollOffset.y, 0.001f );

		scroller.ScrollOffset = new Vector2( 0, -500 );
		scroller.ScrollVelocity = 0;
		scroller.SetNeedsPreLayout();
		root.Layout();

		Assert.AreEqual( 0, scroller.ScrollOffset.y, 0.001f );
	}

	/// <summary>
	/// A ScrollOffset within the valid extents survives repeated layouts unchanged - the
	/// constrain step only rewrites the offset when it's out of bounds or has velocity.
	/// </summary>
	[TestMethod]
	public void ScrollOffsetPersistsAcrossLayout()
	{
		var (root, scroller) = CreateVerticalScroller();

		RealTime.SmoothDelta = 1.0f / 60.0f;

		scroller.ScrollOffset = new Vector2( 0, 300 );

		for ( int i = 0; i < 5; i++ )
		{
			scroller.SetNeedsPreLayout();
			root.Layout();
		}

		Assert.AreEqual( 300, scroller.ScrollOffset.y, 0.001f );
	}
}

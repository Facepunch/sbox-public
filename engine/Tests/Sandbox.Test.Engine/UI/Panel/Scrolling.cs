using Sandbox.UI;
using System;

namespace UITests.Panels;

[TestClass]
[DoNotParallelize] // Modifies UI System Global + RealTime deltas
public partial class PanelScrollingTest
{
	float savedSmoothDelta;
	float savedDelta;

	/// <summary>
	/// Remembers the global deltas so tests that drive the scroll pump can restore them.
	/// </summary>
	[TestInitialize]
	public void Initialize()
	{
		savedSmoothDelta = RealTime.SmoothDelta;
		savedDelta = RealTime.Delta;
	}

	/// <summary>
	/// Restores the global deltas so other test classes see the values they expect.
	/// </summary>
	[TestCleanup]
	public void Cleanup()
	{
		RealTime.SmoothDelta = savedSmoothDelta;
		RealTime.Delta = savedDelta;
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
		RealTime.Delta = RealTime.SmoothDelta = 1.0f / 60.0f;

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

		RealTime.Delta = RealTime.SmoothDelta = 1.0f / 60.0f;

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

	[TestMethod]
	[DataRow( 30, false, false )]
	[DataRow( 60, false, false )]
	[DataRow( 144, false, false )]
	[DataRow( 240, false, false )]
	[DataRow( 2000, false, false )]
	[DataRow( 60, true, false )]
	[DataRow( 144, true, false )]
	[DataRow( 240, true, false )]
	[DataRow( 2000, true, false )]
	[DataRow( 60, false, true )]
	[DataRow( 144, false, true )]
	[DataRow( 240, false, true )]
	[DataRow( 2000, false, true )]
	[DataRow( 60, true, true )]
	[DataRow( 144, true, true )]
	[DataRow( 240, true, true )]
	[DataRow( 2000, true, true )]
	public void RapidWheelInputHasBoundedBounceAtBothBoundaries( int frameRate, bool reversed, bool horizontal )
	{
		var (root, scroller) = CreatePaddedScroller( 1000, 1000 );
		if ( reversed )
		{
			scroller.Style.Set( "flex-direction: column-reverse;" );
			root.Layout();
		}

		RealTime.Delta = RealTime.SmoothDelta = 1.0f / frameRate;
		var axis = horizontal ? new Vector2( 1, 0 ) : new Vector2( 0, 1 );
		var wheel = horizontal ? -axis : axis;
		var min = reversed ? -scroller.ScrollSize : Vector2.Zero;
		var max = reversed ? Vector2.Zero : scroller.ScrollSize;

		foreach ( var direction in new[] { 1, -1 } )
		{
			var boundary = Vector2.Dot( direction > 0 ? max : min, axis );
			scroller.ScrollOffset = axis * boundary;
			for ( int frame = 0; frame < 30; frame++ )
			{
				for ( int i = 0; i < 1000; i++ )
					scroller.TryScroll( wheel * direction );
				scroller.SetNeedsPreLayout();
				root.Layout();
				var overshoot = (Vector2.Dot( scroller.ScrollOffset, axis ) - boundary) * direction;
				Assert.IsTrue( overshoot >= 0 && overshoot <= 40 * scroller.ScaleToScreen, $"Overshoot: {overshoot}" );
				if ( frameRate > 100 ) Assert.AreEqual( Vector2.Zero, scroller.ScrollVelocity );
			}

			var beforeReverse = Vector2.Dot( scroller.ScrollOffset, axis );
			scroller.TryScroll( wheel * -direction );
			if ( frameRate > 100 ) Assert.AreEqual( -direction * 20, Vector2.Dot( scroller.ScrollVelocity, axis ), 0.001f, "The hard limit must discard outward momentum" );
			root.Layout();
			if ( frameRate > 100 ) Assert.IsTrue( (Vector2.Dot( scroller.ScrollOffset, axis ) - beforeReverse) * direction < 0 );

			// Hit the same edge again, then release without any more input.
			for ( int i = 0; i < 1000; i++ ) scroller.TryScroll( wheel * direction );
			root.Layout();
			var previous = MathF.Abs( Vector2.Dot( scroller.ScrollOffset, axis ) - boundary );
			for ( int frame = 0; frame < frameRate; frame++ )
			{
				root.Layout();
				var overshoot = MathF.Abs( Vector2.Dot( scroller.ScrollOffset, axis ) - boundary );
				Assert.IsTrue( overshoot <= previous, "Released bounce should return monotonically" );
				previous = overshoot;
			}
			Assert.AreEqual( boundary, Vector2.Dot( scroller.ScrollOffset, axis ), 0.01f );

			scroller.TryScroll( wheel * -direction );
			scroller.ScrollTo( scroller.ScrollOffset );
		}
	}

	class DragScroller : Panel
	{
		public void Start( DragEvent e ) => OnDragStart( e );
		public void Drag( DragEvent e ) => OnDrag( e );
		public void End( DragEvent e ) => OnDragEnd( e );
		public void Step( Vector2 size )
		{
			AddScrollVelocity();
			ConstrainScrolling( size + Box.Rect.Size );
		}
	}

	[TestMethod]
	public void DragReleaseSpringsBackAt240Fps()
	{
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 1000, 1000 ) };
		var scroller = root.AddChild<DragScroller>();
		scroller.Style.Set( "width: 200px; height: 200px; overflow: scroll;" );
		root.Layout();
		RealTime.Delta = RealTime.SmoothDelta = 1.0f / 240.0f;
		scroller.Step( new Vector2( 800, 800 ) );
		var e = new DragEvent( "ondrag", scroller, 0, 0 ) { LocalPosition = new Vector2( 0, 100 ) };
		scroller.Start( e );
		scroller.Drag( e );
		var overshoot = scroller.ScrollOffset.y;
		Assert.IsTrue( overshoot < 0 && overshoot >= -20 );
		scroller.End( e );
		Assert.IsFalse( scroller.IsDragScrolling );
		// Isolate springback from the test host's cursor velocity.
		scroller.ScrollVelocity = 0;
		scroller.Step( new Vector2( 800, 800 ) );
		Assert.AreEqual( overshoot * (1 - RealTime.SmoothDelta * 100), scroller.ScrollOffset.y, 0.001f );
		Assert.IsTrue( scroller.ScrollOffset.y < 0, "Release should spring back, not snap" );
		for ( int i = 0; i < 72; i++ ) scroller.Step( new Vector2( 800, 800 ) );
		Assert.AreEqual( 0, scroller.ScrollOffset.y, 0.001f );
	}

	[TestMethod]
	public void DiagonalWheelRetainsInertiaBelowHardLimit()
	{
		var (root, scroller) = CreatePaddedScroller( 1000, 1000 );
		RealTime.Delta = RealTime.SmoothDelta = 1.0f / 240.0f;
		scroller.ScrollOffset = new Vector2( scroller.ScrollSize.x, 100 );
		scroller.TryScroll( new Vector2( -1, 1 ) );
		// RootPanel.Layout ticks first, damping velocity before integrating the offset.
		var derivative = Vector2.Zero;
		var velocity = Vector2.SmoothDamp( new Vector2( 20, 20 ), 0, ref derivative, 0.5f, RealTime.SmoothDelta );
		root.Layout();
		Assert.AreEqual( velocity, scroller.ScrollVelocity );
		Assert.IsTrue( scroller.ScrollOffset.x > scroller.ScrollSize.x && scroller.ScrollOffset.x <= scroller.ScrollSize.x + 20 );
		Assert.AreEqual( 100 + velocity.y * RealTime.SmoothDelta * 60, scroller.ScrollOffset.y, 0.001f );
	}

	[TestMethod]
	[DataRow( 60, 15 )]
	[DataRow( 60, 25 )]
	[DataRow( 240, 15 )]
	[DataRow( 240, 25 )]
	[DataRow( 2000, 15 )]
	[DataRow( 2000, 25 )]
	public void SingleWheelImpulseBouncesThenSettles( int fps, int distance )
	{
		var scroller = new DragScroller();
		scroller.Style.Set( "width: 200px; height: 200px; overflow: scroll;" );
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 1000, 1000 ) };
		root.AddChild( scroller );
		root.Layout();
		RealTime.Delta = RealTime.SmoothDelta = 1.0f / fps;
		var size = new Vector2( 800, 800 );
		scroller.Step( size );
		scroller.ScrollOffset = new Vector2( 0, 800 - distance );
		scroller.TryScroll( new Vector2( 0, 1 ) );
		Assert.AreEqual( 800 - distance, scroller.ScrollOffset.y, 0.001f, "Input must not predict a frame or move the offset" );
		var derivative = Vector2.Zero;
		var velocity = Vector2.SmoothDamp( new Vector2( 0, 20 ), 0, ref derivative, 0.5f, RealTime.SmoothDelta );
		var expected = 800 - distance + velocity.y * RealTime.SmoothDelta * 60;
		scroller.Step( size );
		if ( expected <= 800 ) Assert.AreEqual( expected, scroller.ScrollOffset.y, 0.001f );
		var bounced = scroller.ScrollOffset.y > 800;
		for ( int i = 0; i < fps * 4; i++ )
		{
			scroller.Step( size );
			bounced |= scroller.ScrollOffset.y > 800;
			Assert.IsTrue( scroller.ScrollOffset.y <= 820 );
		}
		if ( fps > 100 ) Assert.IsTrue( bounced, "A single wheel impulse should still overshoot" );
		Assert.AreEqual( 800, scroller.ScrollOffset.y, 0.01f );
		Assert.AreEqual( 0, scroller.ScrollVelocity.y );
	}

	[TestMethod]
	public void WheelAndProgrammaticInertiaBothSpringBack()
	{
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 1000, 1000 ) };
		var scroller = root.AddChild<DragScroller>();
		scroller.Style.Set( "width: 200px; height: 200px; overflow: scroll;" );
		root.Layout();
		RealTime.Delta = RealTime.SmoothDelta = 1.0f / 240;
		var size = new Vector2( 800, 800 );
		scroller.Step( size );
		scroller.TryScroll( new Vector2( -1, 1 ) );
		var e = new DragEvent( "ondrag", scroller, 0, 0 );
		scroller.Start( e );
		scroller.End( e );
		scroller.ScrollOffset = new Vector2( 800, 800 );
		scroller.ScrollVelocity = new Vector2( 20, 20 );
		scroller.Step( size );
		Assert.IsTrue( scroller.ScrollOffset.x > 800 && scroller.ScrollOffset.y > 800 );

		scroller.TryScrollToBottom();
		scroller.ScrollOffset = new Vector2( 800, 800 );
		scroller.TryScroll( new Vector2( -1, 1 ) );
		scroller.ScrollVelocity.x = 30;
		scroller.Step( size );
		Assert.IsTrue( scroller.ScrollOffset.x > 800 );
		Assert.IsTrue( scroller.ScrollOffset.y > 800 && scroller.ScrollOffset.y <= 820 );
		Assert.IsTrue( scroller.ScrollVelocity.y > 0 );
	}

	[TestMethod]
	public void WheelInertiaHasBoundedBounceAfterContentShrinks()
	{
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 1000, 1000 ) };
		var scroller = root.AddChild<DragScroller>();
		scroller.Style.Set( "width: 200px; height: 200px; overflow: scroll;" );
		root.Layout();
		RealTime.Delta = RealTime.SmoothDelta = 1.0f / 240;
		scroller.Step( new Vector2( 800, 800 ) );
		scroller.ScrollOffset = new Vector2( 600, 600 );
		scroller.TryScroll( new Vector2( -1, 0 ) );
		scroller.Step( new Vector2( 200, 200 ) );
		Assert.AreEqual( 240, scroller.ScrollOffset.x, 0.001f );
		Assert.AreEqual( 0, scroller.ScrollVelocity.x );
		Assert.AreEqual( 240, scroller.ScrollOffset.y, 0.001f, "The outer limit also applies to the non-wheel axis" );
	}

	[TestMethod]
	public void ShrinkingContentDuringInertiaSpringsToNewBoundary()
	{
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 1000, 1000 ) };
		var scroller = root.AddChild<DragScroller>();
		scroller.Style.Set( "width: 200px; height: 200px; overflow: scroll;" );
		root.Layout();
		RealTime.Delta = RealTime.SmoothDelta = 1.0f / 240.0f;
		scroller.Step( new Vector2( 800, 800 ) );
		scroller.ScrollOffset = new Vector2( 600, 600 );
		scroller.ScrollVelocity = new Vector2( 20, 0 );
		scroller.Step( new Vector2( 200, 200 ) );
		Assert.IsTrue( scroller.ScrollOffset.x > 200 && scroller.ScrollOffset.x < 600 );
		Assert.AreEqual( 0, scroller.ScrollVelocity.x, "Resizing beyond the hard limit discards outward inertia" );
		for ( int i = 0; i < 1000; i++ ) scroller.Step( new Vector2( 200, 200 ) );
		Assert.AreEqual( 200, scroller.ScrollOffset.x, 0.001f );
	}

	/// <summary>
	/// A ScrollOffset within the valid extents survives repeated layouts unchanged - the
	/// constrain step only rewrites the offset when it's out of bounds or has velocity.
	/// </summary>
	[TestMethod]
	public void ScrollOffsetPersistsAcrossLayout()
	{
		var (root, scroller) = CreateVerticalScroller();

		RealTime.Delta = RealTime.SmoothDelta = 1.0f / 60.0f;

		scroller.ScrollOffset = new Vector2( 0, 300 );

		for ( int i = 0; i < 5; i++ )
		{
			scroller.SetNeedsPreLayout();
			root.Layout();
		}

		Assert.AreEqual( 300, scroller.ScrollOffset.y, 0.001f );
	}
}

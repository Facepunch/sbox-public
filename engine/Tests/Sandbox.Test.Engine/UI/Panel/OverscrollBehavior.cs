using Sandbox.UI;

namespace UITests.Panels;

public partial class PanelScrollingTest
{
	[TestMethod]
	public void OverscrollBehaviorParsingAndCascade()
	{
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 1000, 1000 ) };
		root.Style.Set( "overscroll-behavior: none contain;" );
		var child = root.Add.Panel();
		root.Layout();
		Assert.AreEqual( OverscrollBehavior.None, root.ComputedStyle.OverscrollBehaviorX );
		Assert.AreEqual( OverscrollBehavior.Contain, root.ComputedStyle.OverscrollBehaviorY );
		Assert.AreEqual( OverscrollBehavior.Auto, child.ComputedStyle.OverscrollBehaviorX, "Not inherited by default" );
		Assert.AreEqual( OverscrollBehavior.Auto, child.ComputedStyle.OverscrollBehaviorY );

		child.Style.Set( "overscroll-behavior: inherit;" );
		root.Layout();
		Assert.AreEqual( OverscrollBehavior.None, child.ComputedStyle.OverscrollBehaviorX );
		Assert.AreEqual( OverscrollBehavior.Contain, child.ComputedStyle.OverscrollBehaviorY );
		child.Style.Set( "overscroll-behavior: none; overscroll-behavior-x: auto;" );
		root.Layout();
		Assert.AreEqual( OverscrollBehavior.Auto, child.ComputedStyle.OverscrollBehaviorX );
		Assert.AreEqual( OverscrollBehavior.None, child.ComputedStyle.OverscrollBehaviorY );
		child.Style.Set( "overscroll-behavior: initial;" );
		root.Layout();
		Assert.AreEqual( OverscrollBehavior.Auto, child.ComputedStyle.OverscrollBehaviorX );
		Assert.AreEqual( OverscrollBehavior.Auto, child.ComputedStyle.OverscrollBehaviorY );

		var style = new Styles();
		Assert.IsTrue( style.Set( "overscroll-behavior", "contain none" ) );
		Assert.IsFalse( style.Set( "overscroll-behavior", "auto invalid" ) );
		Assert.IsFalse( style.Set( "overscroll-behavior", "auto none contain" ) );
		Assert.IsFalse( style.Set( "overscroll-behavior-x", "1" ) );
		Assert.AreEqual( OverscrollBehavior.Contain, style.OverscrollBehaviorX );
		Assert.AreEqual( OverscrollBehavior.None, style.OverscrollBehaviorY );
	}

	static (RootPanel Root, Panel Outer, DragScroller Inner) CreateNestedScroller( string behavior, bool reversed = false, bool fits = false )
	{
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 1000, 1000 ) };
		var outer = root.Add.Panel();
		outer.Style.Set( "width: 300px; height: 300px; overflow: scroll;" );
		var content = outer.Add.Panel();
		content.Style.Set( "width: 1000px; height: 1000px; flex-shrink: 0;" );
		var inner = content.AddChild<DragScroller>();
		inner.Style.Set( $"width: 200px; height: 200px; overflow: scroll; overscroll-behavior: {behavior}; flex-direction: {(reversed ? "column-reverse" : "column")};" );
		inner.Add.Panel().Style.Set( $"width: {(fits ? 100 : 800)}px; height: {(fits ? 100 : 800)}px; flex-shrink: 0;" );
		root.Layout();
		RealTime.Delta = RealTime.SmoothDelta = 1.0f / 240;
		outer.ScrollTo( new Vector2( 100, 100 ) );
		return (root, outer, inner);
	}

	[TestMethod]
	[DataRow( "auto", false )]
	[DataRow( "contain", false )]
	[DataRow( "none", false )]
	[DataRow( "auto", true )]
	[DataRow( "contain", true )]
	[DataRow( "none", true )]
	public void NestedWheelChainingAndBounce( string behavior, bool reversed )
	{
		foreach ( var direction in new[] { -1, 1 } )
		{
			var (root, outer, inner) = CreateNestedScroller( behavior, reversed );
			var min = reversed ? -inner.ScrollSize : Vector2.Zero;
			var max = reversed ? Vector2.Zero : inner.ScrollSize;
			var edge = direction < 0 ? min : max;
			inner.ScrollTo( edge );
			inner.OnMouseWheel( new Vector2( -direction, direction ) );
			root.Layout();
			Assert.AreEqual( behavior == "auto", outer.ScrollVelocity.y * direction > 0 );
			Assert.AreEqual( behavior == "auto", outer.ScrollVelocity.x * direction > 0 );
			Assert.AreEqual( behavior == "contain", (inner.ScrollOffset.y - edge.y) * direction > 0 );
			Assert.AreEqual( behavior == "contain", (inner.ScrollOffset.x - edge.x) * direction > 0 );
			if ( behavior != "contain" ) Assert.AreEqual( edge, inner.ScrollOffset );
		}
	}

	[TestMethod]
	public void DiagonalWheelChainsOnlyAutoAxis()
	{
		var (root, outer, inner) = CreateNestedScroller( "auto contain" );
		inner.ScrollTo( inner.ScrollSize );
		inner.OnMouseWheel( new Vector2( -1, 1 ) );
		root.Layout();
		Assert.IsTrue( outer.ScrollVelocity.x > 0 );
		Assert.AreEqual( 0, outer.ScrollVelocity.y );
		Assert.AreEqual( inner.ScrollSize.x, inner.ScrollOffset.x );
		Assert.IsTrue( inner.ScrollOffset.y > inner.ScrollSize.y );
	}

	[TestMethod]
	[DataRow( "auto", true )]
	[DataRow( "contain", false )]
	[DataRow( "none", false )]
	public void EmptyScrollContainerCanBlockChaining( string behavior, bool chains )
	{
		var (_, outer, inner) = CreateNestedScroller( behavior, fits: true );
		inner.OnMouseWheel( new Vector2( 0, 1 ) );
		Assert.AreEqual( chains, outer.ScrollVelocity.y > 0 );
		Assert.AreEqual( Vector2.Zero, inner.ScrollVelocity );
	}

	[TestMethod]
	[DataRow( "auto" )]
	[DataRow( "contain" )]
	[DataRow( "none" )]
	public void DragBoundaryHonorsOverscrollBehavior( string behavior )
	{
		var (_, outer, inner) = CreateNestedScroller( behavior );
		var e = new DragEvent( "ondrag", inner, 0, 0 ) { LocalPosition = new Vector2( 0, 100 ) };
		inner.Start( e );
		inner.Drag( e );
		Assert.AreEqual( behavior == "auto", outer.ScrollOffset.y < 100 );
		Assert.AreEqual( behavior == "contain", inner.ScrollOffset.y < 0 );
		if ( behavior == "none" ) Assert.AreEqual( 0, inner.ScrollOffset.y );
		inner.End( e );
	}

	[TestMethod]
	public void NoneClampsOnlyItsAxisDuringInertia()
	{
		var (root, _, inner) = CreateNestedScroller( "none contain" );
		inner.ScrollTo( inner.ScrollSize );
		inner.ScrollVelocity = new Vector2( 1000, 1000 );
		root.Layout();
		Assert.AreEqual( inner.ScrollSize.x, inner.ScrollOffset.x );
		Assert.AreEqual( 0, inner.ScrollVelocity.x );
		Assert.IsTrue( inner.ScrollOffset.y > inner.ScrollSize.y );
	}

	[TestMethod]
	[DataRow( "contain" )]
	[DataRow( "none" )]
	public void EmptyContainedScrollerCapturesDrag( string behavior )
	{
		var (_, outer, inner) = CreateNestedScroller( behavior, fits: true );
		Assert.IsTrue( inner.WantsDrag, "An empty scroll container must still capture a drag to prevent chaining" );
		var e = new DragEvent( "ondrag", inner, 0, 0 ) { LocalPosition = new Vector2( 0, 100 ) };
		inner.Start( e );
		inner.Drag( e );
		Assert.AreEqual( 100, outer.ScrollOffset.y );
		Assert.AreEqual( Vector2.Zero, inner.ScrollOffset );
		inner.End( e );
	}

	[TestMethod]
	public void ChainingStopsAtIntermediateContainedScroller()
	{
		var (root, outer, inner) = CreateNestedScroller( "contain", fits: true );
		var child = inner.Children.First();
		child.Style.Set( "width: 80px; height: 80px; overflow: scroll;" );
		child.Add.Panel().Style.Set( "width: 40px; height: 200px; flex-shrink: 0;" );
		root.Layout();
		child.ScrollTo( child.ScrollSize );
		child.OnMouseWheel( new Vector2( 0, 1 ) );
		Assert.AreEqual( Vector2.Zero, outer.ScrollVelocity );
		Assert.AreEqual( Vector2.Zero, inner.ScrollVelocity );
		Assert.AreEqual( Vector2.Zero, child.ScrollVelocity );
	}

	[TestMethod]
	public void ChangingToNoneCancelsActiveBounce()
	{
		var (root, _, inner) = CreateNestedScroller( "contain" );
		inner.ScrollTo( inner.ScrollSize );
		inner.OnMouseWheel( new Vector2( 0, 1 ) );
		root.Layout();
		Assert.IsTrue( inner.ScrollOffset.y > inner.ScrollSize.y );
		inner.Style.Set( "overscroll-behavior: none;" );
		root.Layout();
		Assert.AreEqual( inner.ScrollSize.y, inner.ScrollOffset.y );
		Assert.AreEqual( 0, inner.ScrollVelocity.y );
	}
}

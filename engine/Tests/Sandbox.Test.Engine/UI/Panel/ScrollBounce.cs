using Sandbox.UI;

namespace UITests.Panels;

public partial class PanelScrollingTest
{
	[TestMethod]
	[DataRow( 60, false )]
	[DataRow( 240, false )]
	[DataRow( 2000, false )]
	[DataRow( 60, true )]
	[DataRow( 240, true )]
	[DataRow( 2000, true )]
	public void ScrollMotionMatchesPreBranchBelowHardLimit( int fps, bool reversed )
	{
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 1000, 1000 ) };
		var scroller = root.AddChild<DragScroller>();
		scroller.Style.Set( $"width: 600px; height: 600px; overflow: scroll; overscroll-behavior: contain; flex-direction: {(reversed ? "column-reverse" : "column")};" );
		root.Layout();
		RealTime.Delta = RealTime.SmoothDelta = 1.0f / fps;
		var size = new Vector2( 800, 800 );
		scroller.Step( size );
		var min = reversed ? -size : Vector2.Zero;
		var max = reversed ? Vector2.Zero : size;
		foreach ( var direction in new[] { -1, 1 } )
		{
			var boundary = direction < 0 ? min : max;
			scroller.ScrollTo( boundary );
			scroller.OnMouseWheel( new Vector2( -direction, direction ) );
			var offset = boundary;
			var velocity = new Vector2( 20, 20 ) * direction;
			var derivative = Vector2.Zero;
			for ( int frame = 0; frame < fps * 3; frame++ )
			{
				// Pre-branch pump: damp inertia, integrate, then lerp toward the content boundary.
				if ( velocity.IsNearZeroLength ) velocity = 0;
				else
				{
					velocity = Vector2.SmoothDamp( velocity, 0, ref derivative, 0.5f, RealTime.SmoothDelta );
					if ( velocity.x.AlmostEqual( 0, 0.01f ) ) velocity.x = 0;
					if ( velocity.y.AlmostEqual( 0, 0.01f ) ) velocity.y = 0;
				}
				offset += velocity * RealTime.SmoothDelta * 60;
				offset.x = offset.x.LerpTo( offset.x.Clamp( min.x, max.x ), RealTime.SmoothDelta * 100 );
				offset.y = offset.y.LerpTo( offset.y.Clamp( min.y, max.y ), RealTime.SmoothDelta * 100 );
				scroller.Step( size );
				Assert.AreEqual( offset.x, scroller.ScrollOffset.x, 0.001f );
				Assert.AreEqual( offset.y, scroller.ScrollOffset.y, 0.001f );
				Assert.AreEqual( velocity, scroller.ScrollVelocity, "Ordinary overshoot must retain inertia" );
			}
		}
	}

	[TestMethod]
	[DataRow( 40, 8, 240 )]
	[DataRow( 200, 40, 240 )]
	[DataRow( 600, 120, 240 )]
	[DataRow( 1200, 150, 240 )]
	[DataRow( 40, 8, 2000 )]
	[DataRow( 200, 40, 2000 )]
	[DataRow( 600, 120, 2000 )]
	[DataRow( 1200, 150, 2000 )]
	public void ExtremeWheelHitsHardLimitThenSpringsBack( int viewport, int limit, int fps )
	{
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 2000, 2000 ) };
		var scroller = root.AddChild<DragScroller>();
		scroller.Style.Set( $"width: {viewport}px; height: {viewport}px; overflow: scroll; overscroll-behavior: contain;" );
		root.Layout();
		RealTime.Delta = RealTime.SmoothDelta = 1.0f / fps;
		var size = new Vector2( 2000, 2000 );
		scroller.Step( size );
		foreach ( var direction in new[] { -1, 1 } )
		{
			var boundary = direction < 0 ? Vector2.Zero : size;
			scroller.ScrollTo( boundary );
			for ( int frame = 0; frame < 10; frame++ )
			{
				for ( int i = 0; i < 1000; i++ ) scroller.OnMouseWheel( new Vector2( -direction, direction ) );
				scroller.Step( size );
				var stretch = (scroller.ScrollOffset - boundary) * direction;
				Assert.AreEqual( limit, stretch.x, 0.001f );
				Assert.AreEqual( limit, stretch.y, 0.001f );
				Assert.AreEqual( Vector2.Zero, scroller.ScrollVelocity, "Only the hard limit discards outward inertia" );
			}
			var previous = (scroller.ScrollOffset - boundary).Length;
			for ( int frame = 0; frame < fps; frame++ )
			{
				scroller.Step( size );
				var stretch = (scroller.ScrollOffset - boundary).Length;
				Assert.IsTrue( stretch <= previous );
				previous = stretch;
			}
			Assert.AreEqual( boundary.x, scroller.ScrollOffset.x, 0.01f );
			Assert.AreEqual( boundary.y, scroller.ScrollOffset.y, 0.01f );
		}
	}
}

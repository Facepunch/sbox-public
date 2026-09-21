using Sandbox.UI;

namespace UITests.Controls;

[TestClass]
[DoNotParallelize]
public class CanvasPanelTests
{
	sealed class TestCanvas : CanvasPanel
	{
		public Rect TestViewport { get; set; } = new( 40, 20, 600, 300 );
		protected override Rect Viewport => TestViewport;
	}
	TestCanvas canvas;
	[TestInitialize] public void Setup() { ThreadSafe.MarkMainThread(); canvas = new(); }
	[TestCleanup] public void Cleanup() => canvas.Delete( true );
	static void Near( Vector2 expected, Vector2 actual ) => Assert.IsTrue( (expected - actual).Length < 0.001f, $"Expected {expected}, got {actual}" );

	[DataTestMethod]
	[DataRow( false, false )]
	[DataRow( true, false )]
	[DataRow( true, true )]
	public void NavigationBoundsConstrainTheWholeViewportThroughPanZoomAndResize( bool uniform, bool up )
	{
		canvas.PreserveAspectRatio = uniform; canvas.YAxisUp = up;
		canvas.NavigationBounds = new Rect( 0, 0, 1000, 600 );
		void Check()
		{
			var a = canvas.ScreenToCanvas( canvas.TestViewport.Position );
			var b = canvas.ScreenToCanvas( canvas.TestViewport.Position + canvas.TestViewport.Size );
			var min = Vector2.Min( a, b ); var max = Vector2.Max( a, b );
			Assert.IsTrue( min.x >= -0.001f && min.y >= -0.001f && max.x <= 1000.001f && max.y <= 600.001f, $"Visible {min} to {max}" );
		}
		canvas.SetView( new( -500, -500 ), new( 1500, 1500 ) ); Check();
		canvas.ZoomAt( new( 340, 170 ), 0.25f ); Check();
		foreach ( var delta in new[] { new Vector2( 10000, 10000 ), new Vector2( -10000, -10000 ) } ) { canvas.Pan( delta ); Check(); }
		canvas.ZoomAt( new( 140, 70 ), 100 ); Check();
		canvas.TestViewport = new( 40, 20, 1200, 200 ); canvas.Tick(); Check();
		canvas.TestViewport = new( 40, 20, 200, 600 ); canvas.Tick(); Check();
		if ( uniform )
		{
			var origin = canvas.CanvasToScreen( Vector2.Zero );
			Assert.AreEqual( (canvas.CanvasToScreen( new( 1, 0 ) ) - origin).Length, (canvas.CanvasToScreen( new( 0, 1 ) ) - origin).Length, 0.001f );
		}
		canvas.NavigationBounds = null;
		canvas.Pan( new( 10000, 10000 ) ); Assert.IsTrue( canvas.ViewMin.x < 0 );
		Assert.ThrowsException<System.ArgumentException>( () => canvas.NavigationBounds = new Rect( 0, 0, 0, 1 ) );
	}

	[TestMethod]
	public void CoordinatesRoundTripWithInsetAndEitherAxisDirection()
	{
		canvas.PreserveAspectRatio = false;
		canvas.SetView( new( -2, -5 ), new( 8, 15 ) );
		foreach ( bool up in new[] { false, true } )
		{
			canvas.YAxisUp = up;
			var point = new Vector2( 3, 2 );
			Near( point, canvas.ScreenToCanvas( canvas.CanvasToScreen( point ) ) );
			Near( new( 40, up ? 320 : 20 ), canvas.CanvasToScreen( canvas.ViewMin ) );
		}
	}
	[TestMethod]
	public void ZoomPreservesCursorAnchorAndPanTracksScreenDelta()
	{
		canvas.YAxisUp = true;
		var cursor = new Vector2( 173, 219 );
		var point = canvas.ScreenToCanvas( cursor );
		canvas.ZoomAt( cursor, 0.5f );
		Near( cursor, canvas.CanvasToScreen( point ) );
		canvas.Pan( new( 30, -17 ) );
		Near( cursor + new Vector2( 30, -17 ), canvas.CanvasToScreen( point ) );
	}
	[TestMethod]
	public void ResizingPreservesUniformScaleAndCursorAnchoredNavigation()
	{
		canvas.SetView( new( -100, -50 ), new( 900, 450 ) );
		foreach ( var size in new[] { new Vector2( 1200, 300 ), new Vector2( 300, 600 ) } )
		{
			canvas.TestViewport = new Rect( new Vector2( 40, 20 ), size );
			var origin = canvas.CanvasToScreen( Vector2.Zero );
			var x = canvas.CanvasToScreen( new( 100, 0 ) ) - origin;
			var y = canvas.CanvasToScreen( new( 0, 100 ) ) - origin;
			Assert.AreEqual( x.Length, y.Length, 0.001f );
			Near( new Vector2( 40, 20 ) + size * 0.5f, canvas.CanvasToScreen( (canvas.ViewMin + canvas.ViewMax) * 0.5f ) );
			var cursor = new Vector2( 173, 219 );
			var point = canvas.ScreenToCanvas( cursor );
			canvas.ZoomAt( cursor, 0.8f );
			canvas.Pan( new( 20, -10 ) );
			Near( cursor + new Vector2( 20, -10 ), canvas.CanvasToScreen( point ) );
		}
	}
	[TestMethod]
	public void InvalidZoomDoesNotCorruptView()
	{
		canvas.MaximumViewSpan = 1000;
		foreach ( float factor in new[] { 0, -1, float.NaN, float.PositiveInfinity, 10000 } )
			canvas.ZoomAt( new( 100, 100 ), factor );
		Near( Vector2.Zero, canvas.ViewMin ); Near( Vector2.One, canvas.ViewMax );
	}
	[DataTestMethod]
	[DataRow( 1f, false )]
	[DataRow( 1.5f, true )]
	public void OptionalBoxSelectionUsesCanvasCoordinatesAndCancelsWithoutStealingChildInput( float dpi, bool up )
	{
		var previous = UiTesting.DisableTextRendering();
		try
		{
			using var surface = new UISurface { Size = new Vector2( 1200, 900 ), DpiScale = dpi, MouseInside = true };
			canvas.Parent = surface.Root;
			canvas.Style.Set( "margin-left: 30px; margin-top: 40px; width: 700px; height: 400px;" );
			canvas.SetView( Vector2.Zero, new( 600, 300 ) ); canvas.YAxisUp = up;
			var child = canvas.Content.AddChild<Button>();
			child.Style.Set( "position: absolute; left: 200px; top: 80px; width: 100px; height: 40px; pointer-events: all;" );
			int starts = 0, finishes = 0, cancellations = 0;
			bool additive = false; Rect bounds = default;
			canvas.BoxSelectionStarted += shift => { starts++; additive = shift; };
			canvas.BoxSelectionChanged += rect => bounds = rect;
			canvas.BoxSelectionFinished += cancelled => { finishes++; if ( cancelled ) cancellations++; };
			void Frame() { for ( int i = 0; i < 4; i++ ) UiTesting.Frame( surface ); }
			void Move( Vector2 point ) { surface.MouseMoved( canvas.Box.Rect.Position + canvas.CanvasToScreen( point ) ); Frame(); }
			void Press( bool down ) { surface.Input.AddMouseButton( NativeEngine.ButtonCode.MouseLeft, down, KeyboardModifiers.Shift ); Frame(); }
			Frame();
			Move( new( 400, 250 ) ); Press( true ); Move( new( 100, 50 ) ); Press( false );
			Assert.AreEqual( 0, starts );
			canvas.BoxSelectionEnabled = true;
			canvas.ZoomAt( canvas.CanvasToScreen( new( 300, 150 ) ), 0.8f ); canvas.Pan( new( 10, -5 ) ); Frame();
			Move( new( 400, 250 ) ); Press( true ); Move( new( 100, 50 ) );
			Assert.IsTrue( canvas.IsBoxSelecting ); Assert.IsTrue( additive );
			Near( new( 100, 50 ), bounds.Position ); Near( new( 300, 200 ), bounds.Size );
			Press( false ); Assert.AreEqual( 1, finishes ); Assert.IsFalse( canvas.IsBoxSelecting );
			Move( new( 250, 100 ) ); Press( true ); Press( false ); Assert.AreEqual( 1, starts );
			Move( new( 400, 250 ) ); Press( true ); Move( new( 100, 50 ) );
			canvas.OnButtonTyped( new ButtonEvent( "escape", true, 0, default ) ); Press( false );
			Assert.AreEqual( 1, cancellations ); Assert.IsFalse( canvas.IsBoxSelecting );
			Move( new( 400, 250 ) ); Press( true ); canvas.BoxSelectionEnabled = false; Frame(); Press( false );
			Assert.AreEqual( 2, cancellations );
			canvas.BoxSelectionEnabled = true;
			Move( new( 400, 250 ) ); Press( true ); child.Focus(); Frame(); Press( false );
			Assert.AreEqual( 3, cancellations ); Assert.IsFalse( canvas.IsBoxSelecting );
		}
		finally { TextBlock.ui_rendertext = previous; }
	}

	[DataTestMethod]
	[DataRow( 1f )]
	[DataRow( 1.5f )]
	public void ChildPanelsKeepPickingFocusAndTextInputAfterNavigation( float dpi )
	{
		var previous = UiTesting.DisableTextRendering();
		try
		{
			using var surface = new UISurface { Size = new Vector2( 1200, 900 ), DpiScale = dpi, MouseInside = true };
			canvas.Parent = surface.Root;
			canvas.Style.Set( "margin-left: 30px; margin-top: 40px; width: 700px; height: 400px;" );
			canvas.SetView( new( -100, -50 ), new( 900, 450 ) );
			int clicks = 0;
			var group = canvas.Content.AddChild<Panel>();
			group.Style.Set( "position: absolute; left: 200px; top: 100px; width: 150px; height: 130px; pointer-events: all;" );
			var button = group.AddChild( new Button( "Click", () => clicks++ ) );
			button.Style.Set( "position: absolute; left: 0px; top: 0px; width: 120px; height: 50px; pointer-events: all;" );
			var entry = group.AddChild<TextEntry>();
			entry.Style.Set( "position: absolute; left: 0px; top: 70px; width: 120px; height: 40px; pointer-events: all;" );
			void Frame() { for ( int i = 0; i < 4; i++ ) UiTesting.Frame( surface ); }
			void Move( Vector2 point ) { surface.MouseMoved( point ); Frame(); }
			void Click( Vector2 point )
			{
				Move( point );
				surface.Input.AddMouseButton( NativeEngine.ButtonCode.MouseLeft, true, default ); Frame();
				surface.Input.AddMouseButton( NativeEngine.ButtonCode.MouseLeft, false, default ); Frame();
			}
			Vector2 Screen( Vector2 point ) => canvas.Box.Rect.Position + canvas.CanvasToScreen( point );
			Frame();
			Click( Screen( new( 260, 125 ) ) ); Assert.AreEqual( 1, clicks );
			var originalSize = group.Box.Rect.Size;
			canvas.ZoomAt( canvas.CanvasToScreen( new( 260, 125 ) ), 0.6f );
			canvas.Pan( new( 24, -12 ) ); Frame();
			Click( Screen( new( 260, 125 ) ) ); Assert.AreEqual( 2, clicks );
			Assert.AreEqual( originalSize, group.Box.Rect.Size, "Zoom should not reflow the panel subtree." );
			Click( Screen( new( 260, 190 ) ) );
			Assert.IsTrue( entry.HasFocus );
			surface.Input.TypeText( "Canvas" ); Frame();
			Assert.AreEqual( "Canvas", entry.Text );
			var beforePan = canvas.ViewMin;
			var start = Screen( new( 260, 125 ) ); Move( start );
			surface.Input.AddMouseButton( NativeEngine.ButtonCode.MouseMiddle, true, default ); Frame();
			Move( start + new Vector2( 24, 16 ) );
			surface.Input.AddMouseButton( NativeEngine.ButtonCode.MouseMiddle, false, default ); Frame();
			Assert.AreNotEqual( beforePan, canvas.ViewMin, "Middle dragging over a child should pan the canvas." );
			Click( Screen( new( 260, 125 ) ) ); Assert.AreEqual( 3, clicks );
			canvas.TestViewport = new( 40, 20, 650, 200 ); Frame();
			Click( Screen( new( 260, 125 ) ) ); Assert.AreEqual( 4, clicks );
			var corner = button.PanelPositionToScreenPosition( Vector2.Zero );
			var right = button.PanelPositionToScreenPosition( new( button.Box.Rect.Width, 0 ) );
			var bottom = button.PanelPositionToScreenPosition( new( 0, button.Box.Rect.Height ) );
			Assert.AreEqual( (right - corner).Length / button.Box.Rect.Width, (bottom - corner).Length / button.Box.Rect.Height, 0.001f );
			canvas.TestViewport = new( 40, 20, 600, 300 );
			// The inset viewport clips input as well as drawing.
			canvas.SetView( new( 260, 100 ), new( 860, 400 ) ); Frame();
			Panel hit = null;
			PanelInput.CheckHover( surface.Root, Screen( new( 220, 125 ) ), ref hit );
			Assert.AreNotSame( button, hit );
		}
		finally { TextBlock.ui_rendertext = previous; }
	}

	[TestMethod]
	public void FitBoundsPadsFlatContentAndHonoursLimits()
	{
		canvas.PreserveAspectRatio = false;
		canvas.FitBounds( new Rect( 2, 3, 10, 0 ), new( 0.1f, 0.2f ), new( 0.5f, 1 ) );
		Near( new( 1, 2 ), canvas.ViewMin );
		Near( new( 13, 4 ), canvas.ViewMax );
		canvas.FitBounds( new Rect( 1000, 1000, 0, 0 ) );
		Assert.IsTrue( canvas.ViewMin.x < 1000 && canvas.ViewMax.x > 1000 );
		canvas.MinimumViewSpan = 0.1f;
		canvas.FitBounds( new Rect( 5, 5, 0, 0 ) );
		Near( new( 4.95f, 4.95f ), canvas.ViewMin );
		Near( new( 5.05f, 5.05f ), canvas.ViewMax );
		canvas.NavigationBounds = new Rect( 0, 0, 10, 10 );
		canvas.FitBounds( new Rect( -20, -20, 50, 50 ) );
		Near( Vector2.Zero, canvas.ViewMin );
		Near( new( 10, 10 ), canvas.ViewMax );
		Assert.ThrowsException<System.ArgumentException>( () => canvas.FitBounds( new Rect( 0, 0, float.NaN, 1 ) ) );
	}

	[DataTestMethod]
	[DataRow( 1f, false )]
	[DataRow( 1.5f, true )]
	public void ChildDragLocksAxisBlocksNavigationAndFinishesOnce( float dpi, bool up )
	{
		var previous = UiTesting.DisableTextRendering();
		try
		{
			using var surface = new UISurface { Size = new Vector2( 1200, 900 ), DpiScale = dpi, MouseInside = true };
			canvas.Parent = surface.Root;
			canvas.Style.Set( "width: 700px; height: 400px;" );
			canvas.SetView( Vector2.Zero, new( 600, 300 ) );
			canvas.YAxisUp = up;
			var handle = canvas.AddChild<CanvasPanel.Handle>();
			handle.Position = new( 250, 100 );
			handle.Style.Set( "width: 100px; height: 40px;" );
			Vector2 delta = default;
			int finishes = 0, cancellations = 0;
			handle.DragMoved += value => delta = value;
			handle.DragFinished += cancelled =>
			{
				finishes++;
				if ( cancelled )
					cancellations++;
			};
			void Frame()
			{
				for ( int i = 0; i < 4; i++ )
					UiTesting.Frame( surface );
			}
			void Move( Vector2 point )
			{
				surface.MouseMoved( canvas.Box.Rect.Position + canvas.CanvasToScreen( point ) );
				Frame();
			}
			void Press( NativeEngine.ButtonCode button, bool down )
			{
				surface.Input.AddMouseButton( button, down, KeyboardModifiers.Shift );
				Frame();
			}
			Frame();
			var originalSize = handle.Box.Rect.Size;
			canvas.ZoomAt( canvas.CanvasToScreen( handle.Position ), 0.8f );
			canvas.Pan( new( 10, 5 ) );
			Frame();
			Assert.AreEqual( originalSize, handle.Box.Rect.Size );
			Move( handle.Position );
			Press( NativeEngine.ButtonCode.MouseLeft, true );
			Assert.IsTrue( handle.IsDragging );
			Move( new( 280, 110 ) );
			Near( new( 30, 0 ), delta );
			var view = canvas.ViewMin;
			canvas.OnMouseWheel( new( 0, 1 ) );
			Press( NativeEngine.ButtonCode.MouseMiddle, true );
			Move( new( 300, 180 ) );
			Press( NativeEngine.ButtonCode.MouseMiddle, false );
			Assert.IsTrue( handle.IsDragging );
			Assert.IsFalse( canvas.IsPanning );
			Near( view, canvas.ViewMin );
			Near( new( 50, 0 ), delta );
			Press( NativeEngine.ButtonCode.MouseLeft, false );
			Assert.AreEqual( 1, finishes );
			Assert.AreEqual( 0, cancellations );
			handle.MovementAxis = CanvasPanel.Handle.Axis.Vertical;
			Move( handle.Position );
			Press( NativeEngine.ButtonCode.MouseLeft, true );
			Move( handle.Position + new Vector2( 5, 40 ) );
			Near( new( 0, 40 ), delta );
			handle.OnButtonTyped( new ButtonEvent( "escape", true, 0, default ) );
			Press( NativeEngine.ButtonCode.MouseLeft, false );
			Assert.AreEqual( 2, finishes );
			Assert.AreEqual( 1, cancellations );
			Move( handle.Position );
			Press( NativeEngine.ButtonCode.MouseLeft, true );
			var other = surface.Root.AddChild<TextEntry>();
			other.Focus();
			Frame();
			Press( NativeEngine.ButtonCode.MouseLeft, false );
			Assert.AreEqual( 3, finishes );
			Assert.AreEqual( 2, cancellations );
			Move( handle.Position );
			Press( NativeEngine.ButtonCode.MouseLeft, true );
			handle.Delete( true );
			Frame();
			Press( NativeEngine.ButtonCode.MouseLeft, false );
			Assert.AreEqual( 4, finishes );
			Assert.AreEqual( 3, cancellations );
		}
		finally
		{
			TextBlock.ui_rendertext = previous;
		}
	}

	[TestMethod]
	public void StandaloneHandleSnapsClampsAndRestoresOnDisable()
	{
		var previous = UiTesting.DisableTextRendering();
		try
		{
			using var surface = new UISurface { Size = new Vector2( 800, 600 ), MouseInside = true };
			canvas.Parent = surface.Root;
			canvas.Style.Set( "width: 700px; height: 400px;" );
			canvas.SetView( Vector2.Zero, new( 600, 300 ) );
			var handle = canvas.AddChild<CanvasPanel.Handle>();
			handle.Position = new( 200, 100 );
			handle.Style.Width = handle.Style.Height = 20;
			handle.SnapIncrement = new( 10 );
			handle.MovementBounds = new Rect( 100, 50, 200, 150 );
			void Frame()
			{
				for ( int i = 0; i < 4; i++ )
					UiTesting.Frame( surface );
			}
			void Move( Vector2 point )
			{
				surface.MouseMoved( canvas.Box.Rect.Position + canvas.CanvasToScreen( point ) );
				Frame();
			}
			Frame();
			Move( handle.Position );
			surface.Input.AddMouseButton( NativeEngine.ButtonCode.MouseLeft, true, default );
			Frame();
			Move( new( 237, 123 ) );
			Near( new( 240, 120 ), handle.Position );
			Move( new( 500, 250 ) );
			Near( new( 300, 200 ), handle.Position );
			handle.DragEnabled = false;
			Frame();
			Near( new( 200, 100 ), handle.Position );
			Assert.IsFalse( handle.IsDragging );
			surface.Input.AddMouseButton( NativeEngine.ButtonCode.MouseLeft, false, default );
			Frame();
		}
		finally
		{
			TextBlock.ui_rendertext = previous;
		}
	}

	[DataTestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void ZoomReversesAcrossASingleAxisLimit( bool horizontal )
	{
		canvas.PreserveAspectRatio = false;
		canvas.ConstrainHorizontalNavigation = horizontal;
		canvas.ConstrainVerticalNavigation = !horizontal;
		canvas.NavigationBounds = new Rect( 0, 0, 1, 1 );
		canvas.SetView( new( 0.1f, 0.2f ), new( 0.9f, 0.8f ) );
		var min = canvas.ViewMin;
		var max = canvas.ViewMax;
		var anchor = canvas.CanvasToScreen( new( 0.5f ) );
		canvas.ZoomAt( anchor, 4 );
		canvas.ZoomAt( anchor, 0.5f );
		Assert.AreEqual( 1f, horizontal ? canvas.ViewMax.x - canvas.ViewMin.x : canvas.ViewMax.y - canvas.ViewMin.y, 0.0001f );
		canvas.ZoomAt( anchor, 0.5f );
		Near( min, canvas.ViewMin );
		Near( max, canvas.ViewMax );
		canvas.ZoomAt( anchor, 4 );
		canvas.Pan( horizontal ? new( 0, 30 ) : new( 30, 0 ) );
		canvas.ZoomAt( anchor, 0.25f );
		Near( max - min, canvas.ViewMax - canvas.ViewMin );
		canvas.SetView( Vector2.Zero, Vector2.One );
		canvas.ZoomAt( anchor, 0.5f );
		Near( new( 0.5f ), canvas.ViewMax - canvas.ViewMin );
	}

}

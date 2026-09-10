using Sandbox.UI;

namespace UITests.Panels;

[TestClass]
[DoNotParallelize] // Modifies UI System Global + RealTime.SmoothDelta
public partial class PanelScrollbarTest
{
	float savedSmoothDelta;

	[TestInitialize]
	public void Initialize()
	{
		savedSmoothDelta = RealTime.SmoothDelta;

		// The layout integration step scales by RealTime.SmoothDelta, which is never
		// ticked in the test host - give it a fixed 60fps frame time.
		RealTime.SmoothDelta = 1.0f / 60.0f;
	}

	[TestCleanup]
	public void Cleanup()
	{
		RealTime.SmoothDelta = savedSmoothDelta;
	}

	/// <summary>
	/// A 200px box with the given style around a block of content, laid out until everything settles
	/// </summary>
	static (RootPanel Root, Panel Scroller, Panel Content) CreateScroller( string style, int contentWidth = 100, int contentHeight = 1000 )
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var scroller = root.Add.Panel();
		scroller.Style.Set( $"width: 200px; height: 200px; flex-direction: column; {style}" );

		var content = scroller.Add.Panel();
		content.Style.Set( $"width: {contentWidth}px; height: {contentHeight}px; flex-shrink: 0;" );

		Settle( root );

		return (root, scroller, content);
	}

	static void Settle( RootPanel root )
	{
		for ( int i = 0; i < 3; i++ )
		{
			root.Layout();
		}
	}

	static ScrollBar VerticalBar( Panel scroller ) => scroller.Children.OfType<ScrollBar>().Single( x => x.IsVertical );

	static Panel Thumb( ScrollBar bar ) => bar.Children.Single();

	[TestMethod]
	public void NoScrollbarWithoutTheProperty()
	{
		var (_, scroller, content) = CreateScroller( "overflow-y: scroll;" );

		Assert.AreEqual( 1, scroller.ChildrenCount );
		Assert.AreEqual( content, scroller.Children.Single() );
		Assert.IsNull( scroller.ScrollbarY );
		Assert.IsNull( scroller.ScrollbarX );
	}

	[TestMethod]
	public void ScrollbarCreatedFromTheProperty()
	{
		var (_, scroller, content) = CreateScroller( "overflow-y: scroll; scrollbar-width: 8px;" );

		Assert.AreEqual( 2, scroller.ChildrenCount );
		Assert.AreEqual( content, scroller.Children.First() );

		var bar = VerticalBar( scroller );

		Assert.AreEqual( bar, scroller.Children.Last() );
		Assert.AreEqual( bar, scroller.ScrollbarY );
		Assert.IsNull( scroller.ScrollbarX, "overflow-y alone shouldn't get a horizontal bar" );

		Assert.AreEqual( "scrollbar", bar.ElementName );
		Assert.IsTrue( bar.HasClass( "vertical" ) );
		Assert.IsTrue( bar.IsVisible );

		// Pinned to the right edge, full height, as thick as asked for
		Assert.AreEqual( 192, bar.Box.Rect.Left, 0.001f );
		Assert.AreEqual( 0, bar.Box.Rect.Top, 0.001f );
		Assert.AreEqual( 8, bar.Box.Rect.Width, 0.001f );
		Assert.AreEqual( 200, bar.Box.Rect.Height, 0.001f );
	}

	[TestMethod]
	public void BothAxesLeaveTheCornerFree()
	{
		var (_, scroller, _) = CreateScroller( "overflow: scroll; scrollbar-width: 8px;", contentWidth: 1000 );

		Assert.AreEqual( 3, scroller.ChildrenCount );

		var vertical = scroller.ScrollbarY;
		var horizontal = scroller.ScrollbarX;

		Assert.IsNotNull( vertical );
		Assert.IsNotNull( horizontal );
		Assert.IsFalse( horizontal.IsVertical );

		Assert.AreEqual( 192, vertical.Box.Rect.Height, 0.001f, "the vertical bar stops above the horizontal one" );
		Assert.AreEqual( 192, horizontal.Box.Rect.Width, 0.001f, "the horizontal bar stops left of the vertical one" );
		Assert.AreEqual( 192, horizontal.Box.Rect.Top, 0.001f );
	}

	[TestMethod]
	public void KeywordsParse()
	{
		var (_, scroller, _) = CreateScroller( "overflow-y: scroll;" );

		scroller.Style.Set( "scrollbar-width: thin" );
		Assert.AreEqual( ScrollBar.ThinThickness, ScrollBar.Thickness( scroller.Style.ScrollbarWidth, 1 ), 0.001f );

		scroller.Style.Set( "scrollbar-width: auto" );
		Assert.AreEqual( ScrollBar.AutoThickness, ScrollBar.Thickness( scroller.Style.ScrollbarWidth, 1 ), 0.001f );

		scroller.Style.Set( "scrollbar-width: 12px" );
		Assert.AreEqual( 12, ScrollBar.Thickness( scroller.Style.ScrollbarWidth, 1 ), 0.001f );

		scroller.Style.Set( "scrollbar-width: none" );
		Assert.AreEqual( 0, ScrollBar.Thickness( scroller.Style.ScrollbarWidth, 1 ), 0.001f );
	}

	[TestMethod]
	public void PropertyInheritsAndTurnsOff()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );
		root.StyleSheet.Parse( ".host { scrollbar-width: thin; }" );

		var host = root.Add.Panel( "host" );
		var scroller = host.Add.Panel();
		scroller.Style.Set( "width: 200px; height: 200px; flex-direction: column; overflow-y: scroll;" );

		var content = scroller.Add.Panel();
		content.Style.Set( "width: 100px; height: 1000px; flex-shrink: 0;" );

		Settle( root );

		var bar = VerticalBar( scroller );
		Assert.AreEqual( ScrollBar.ThinThickness, bar.Box.Rect.Width, 0.001f );

		scroller.Style.Set( "scrollbar-width: none" );
		Settle( root );

		Assert.IsNull( scroller.ScrollbarY );
		Assert.IsTrue( bar.IsDeleting );
	}

	[TestMethod]
	public void ScrollbarDoesNotAffectScrollSize()
	{
		var (_, scroller, _) = CreateScroller( "overflow-y: scroll; scrollbar-width: 8px;" );

		Assert.AreEqual( 800, scroller.ScrollSize.y, 0.001f );
		Assert.AreEqual( 0, scroller.ScrollSize.x, 0.001f );
		Assert.IsFalse( scroller.HasScrollX );
	}

	[TestMethod]
	public void ScrollbarStaysPutWhileContentScrolls()
	{
		var (root, scroller, content) = CreateScroller( "overflow-y: scroll; scrollbar-width: 8px;" );
		var bar = VerticalBar( scroller );

		scroller.ScrollTo( new Vector2( 0, 300 ) );
		Settle( root );

		Assert.AreEqual( 300, scroller.ScrollOffset.y, 0.001f );
		Assert.AreEqual( -300, content.Box.Rect.Top, 0.001f, "the content scrolled" );
		Assert.AreEqual( 0, bar.Box.Rect.Top, 0.001f, "the bar didn't" );
		Assert.AreEqual( 192, bar.Box.Rect.Left, 0.001f );
	}

	[TestMethod]
	public void ThumbTracksTheOffset()
	{
		var (root, scroller, _) = CreateScroller( "overflow-y: scroll; scrollbar-width: 8px;" );
		var bar = VerticalBar( scroller );
		var thumb = Thumb( bar );

		// The thumb is to the track what the viewport is to the content: 200 of 1000
		Assert.AreEqual( 40, thumb.Box.Rect.Height, 0.001f );
		Assert.AreEqual( bar.Box.Rect.Top, thumb.Box.Rect.Top, 0.001f );

		// Halfway through the range, halfway along the free track
		scroller.ScrollTo( new Vector2( 0, 400 ) );
		Settle( root );

		Assert.AreEqual( 40, thumb.Box.Rect.Height, 0.001f );
		Assert.AreEqual( bar.Box.Rect.Top + 80, thumb.Box.Rect.Top, 0.001f );

		scroller.ScrollTo( new Vector2( 0, 800 ) );
		Settle( root );

		Assert.AreEqual( bar.Box.Rect.Bottom, thumb.Box.Rect.Bottom, 0.001f );
	}

	[TestMethod]
	public void BarComesAndGoesWithTheOverflow()
	{
		var (root, scroller, content) = CreateScroller( "overflow-y: scroll; scrollbar-width: 8px;", contentHeight: 100 );

		// Nothing to scroll, so there's no bar at all - not even a hidden one
		Assert.IsFalse( scroller.HasScrollY );
		Assert.IsNull( scroller.ScrollbarY );
		Assert.AreEqual( 1, scroller.ChildrenCount );

		content.Style.Height = 1000;
		Settle( root );

		Assert.IsTrue( scroller.HasScrollY );
		var bar = VerticalBar( scroller );
		Assert.IsTrue( bar.IsVisible );

		content.Style.Height = 100;
		Settle( root );

		Assert.IsNull( scroller.ScrollbarY );
		Assert.IsTrue( bar.IsDeleting );
	}

	[TestMethod]
	public void ScrollbarIsNotASibling()
	{
		var (_, scroller, content) = CreateScroller( "overflow-y: scroll; scrollbar-width: 8px;" );
		var bar = VerticalBar( scroller );

		Assert.IsTrue( content.PseudoClass.HasFlag( PseudoClass.FirstChild ) );
		Assert.IsTrue( content.PseudoClass.HasFlag( PseudoClass.LastChild ), "the real last child keeps :last-child" );
		Assert.IsTrue( content.PseudoClass.HasFlag( PseudoClass.OnlyChild ) );

		Assert.IsFalse( bar.PseudoClass.HasFlag( PseudoClass.FirstChild ) );
		Assert.IsFalse( bar.PseudoClass.HasFlag( PseudoClass.LastChild ) );
		Assert.IsFalse( bar.PseudoClass.HasFlag( PseudoClass.OnlyChild ) );
	}

	[TestMethod]
	public void AfterElementStaysBeforeTheScrollbar()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );
		root.StyleSheet.Parse( ".decorated::after { width: 10px; height: 10px; }" );

		var scroller = root.Add.Panel( "decorated" );
		scroller.Style.Set( "width: 200px; height: 200px; flex-direction: column; overflow-y: scroll; scrollbar-width: 8px;" );

		var content = scroller.Add.Panel();
		content.Style.Set( "width: 100px; height: 1000px; flex-shrink: 0;" );

		Settle( root );

		var children = scroller.Children.ToList();
		Assert.AreEqual( 3, children.Count );
		Assert.AreEqual( content, children[0] );
		Assert.IsTrue( children[1].PseudoClass.HasFlag( PseudoClass.After ) );
		Assert.IsInstanceOfType( children[2], typeof( ScrollBar ) );

		Settle( root );
		CollectionAssert.AreEqual( children, scroller.Children.ToList() );
	}

	[TestMethod]
	public void ScrollToClampsAndUnpins()
	{
		var (root, scroller, _) = CreateScroller( "overflow-y: scroll;" );

		scroller.PreferScrollToBottom = true;
		scroller.TryScrollToBottom();
		Settle( root );

		Assert.AreEqual( 800, scroller.ScrollOffset.y, 0.001f );
		Assert.IsTrue( scroller.IsScrollAtBottom );

		// A jump away from the bottom has to stick, not get pinned straight back
		scroller.ScrollTo( new Vector2( 0, 100 ) );
		Settle( root );

		Assert.AreEqual( 100, scroller.ScrollOffset.y, 0.001f );
		Assert.IsFalse( scroller.IsScrollAtBottom );

		scroller.ScrollTo( new Vector2( 0, 5000 ) );
		Settle( root );

		Assert.AreEqual( 800, scroller.ScrollOffset.y, 0.001f );
		Assert.IsTrue( scroller.IsScrollAtBottom );

		scroller.ScrollTo( new Vector2( 0, -50 ) );
		Settle( root );

		Assert.AreEqual( 0, scroller.ScrollOffset.y, 0.001f );
	}

	[TestMethod]
	public void TrackClickPages()
	{
		var (root, scroller, content) = CreateScroller( "overflow-y: scroll; scrollbar-width: 8px;" );
		var bar = VerticalBar( scroller );

		// Below the thumb, which covers the top 40px
		root.MousePos = new Vector2( 196, 190 );
		bar.CreateEvent( new MousePanelEvent( "onmousedown", bar, "mouseleft" ) );
		Settle( root );

		Assert.AreEqual( 180, scroller.ScrollOffset.y, 0.001f, "a page is most of a viewport" );
		Assert.AreEqual( -180, content.Box.Rect.Top, 0.001f );

		root.MousePos = new Vector2( 196, 5 );
		bar.CreateEvent( new MousePanelEvent( "onmousedown", bar, "mouseleft" ) );
		Settle( root );

		Assert.AreEqual( 0, scroller.ScrollOffset.y, 0.001f );
	}

	[TestMethod]
	public void ThumbDragScrolls()
	{
		var (root, scroller, _) = CreateScroller( "overflow-y: scroll; scrollbar-width: 8px;" );
		var bar = VerticalBar( scroller );
		var thumb = Thumb( bar );

		// Press on the thumb, then the input system starts a drag on the bar once the mouse moves
		root.MousePos = new Vector2( 196, 20 );
		thumb.CreateEvent( new MousePanelEvent( "onmousedown", thumb, "mouseleft" ) );
		root.Layout();

		var grabLocal = bar.MousePosition;
		var grabScreen = root.MousePos;

		bar.CreateEvent( new DragEvent( "ondragstart", bar, grabLocal, grabScreen ) );
		root.Layout();

		Assert.IsTrue( bar.HasClass( "dragging" ) );

		// The thumb has 160px of free track for 800px of range, so 80px of travel is half way
		root.MousePos = new Vector2( 196, 100 );
		bar.CreateEvent( new DragEvent( "ondrag", bar, grabLocal, grabScreen ) );
		Settle( root );

		Assert.AreEqual( 400, scroller.ScrollOffset.y, 0.001f );
		Assert.AreEqual( bar.Box.Rect.Top + 80, thumb.Box.Rect.Top, 0.001f );

		bar.CreateEvent( new DragEvent( "ondragend", bar, grabLocal, grabScreen ) );
		Settle( root );

		Assert.IsFalse( bar.HasClass( "dragging" ) );
		Assert.AreEqual( 400, scroller.ScrollOffset.y, 0.001f, "letting go leaves it where it was" );
	}

	/// <summary>
	/// Panel input with nowhere to hand a cursor to - there's no input context in this test host
	/// </summary>
	class QuietInput : PanelInput
	{
		public override void SetCursor( string name ) { }
	}

	[TestMethod]
	public void HoverReachesTheThumbNotThePanel()
	{
		// Panels take the mouse only with pointer-events, which a real root's sheet turns on
		var (_, scroller, _) = CreateScroller( "overflow-y: scroll; scrollbar-width: 8px; pointer-events: all;" );
		var bar = VerticalBar( scroller );
		var thumb = Thumb( bar );
		var input = new QuietInput();

		// Over the content: the panel is hovered, the bar and thumb are not
		input.UpdateMouse( (RootPanel)scroller.Parent, new InputData { MousePos = new Vector2( 100, 100 ) } );
		Assert.IsTrue( scroller.HasHovered );
		Assert.IsFalse( bar.HasHovered );
		Assert.IsFalse( thumb.HasHovered );

		// Over the thumb, which covers the top 40px of the bar: thumb, bar, and the panel above them
		input.UpdateMouse( (RootPanel)scroller.Parent, new InputData { MousePos = new Vector2( 196, 20 ) } );
		Assert.IsTrue( thumb.HasHovered );
		Assert.IsTrue( bar.HasHovered );

		// Over the track below the thumb: the bar but not the thumb
		input.UpdateMouse( (RootPanel)scroller.Parent, new InputData { MousePos = new Vector2( 196, 150 ) } );
		Assert.IsTrue( bar.HasHovered );
		Assert.IsFalse( thumb.HasHovered );
	}

	/// <summary>
	/// A 200px scroller with an 8px bar whose content fills its width, so a gutter shows up as a narrower content box.
	/// </summary>
	static (RootPanel Root, Panel Scroller, Panel Content) CreateGutterScroller( string style, int contentHeight = 1000 )
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var scroller = root.Add.Panel();
		scroller.Style.Set( $"width: 200px; height: 200px; flex-direction: column; overflow-y: scroll; scrollbar-width: 8px; {style}" );

		var content = scroller.Add.Panel();
		content.Style.Set( $"width: 100%; height: {contentHeight}px; flex-shrink: 0;" );

		Settle( root );

		return (root, scroller, content);
	}

	[TestMethod]
	public void GutterReservesTheBarsSpace()
	{
		var (_, scroller, content) = CreateGutterScroller( "scrollbar-gutter: stable;" );

		Assert.AreEqual( 0, content.Box.Rect.Left, 0.001f );
		Assert.AreEqual( 192, content.Box.Rect.Width, 0.001f, "the content box gives up the bar's width" );
		Assert.AreEqual( 0, scroller.ScrollSize.x, 0.001f, "the gutter isn't scrollable content" );
		Assert.AreEqual( 0, scroller.Box.Border.Right, 0.001f, "the gutter isn't border either" );
		Assert.AreEqual( 200, scroller.Box.ClipRect.Width, 0.001f, "and it's inside the clip, where the bar lives" );

		Assert.AreEqual( 192, VerticalBar( scroller ).Box.Rect.Left, 0.001f, "the bar sits in the gutter" );
	}

	[TestMethod]
	public void GutterOnBothEdgesCentresTheContent()
	{
		var (_, scroller, content) = CreateGutterScroller( "scrollbar-gutter: stable both-edges;" );

		Assert.AreEqual( 8, content.Box.Rect.Left, 0.001f );
		Assert.AreEqual( 184, content.Box.Rect.Width, 0.001f );
		Assert.AreEqual( 192, VerticalBar( scroller ).Box.Rect.Left, 0.001f, "the bar still draws on the right" );
	}

	[TestMethod]
	public void GutterIsStableWithoutABar()
	{
		var (_, scroller, content) = CreateGutterScroller( "scrollbar-gutter: stable;", contentHeight: 100 );

		Assert.IsNull( scroller.ScrollbarY );
		Assert.AreEqual( 192, content.Box.Rect.Width, 0.001f, "reserved whether or not there's a bar to put in it" );
	}

	[TestMethod]
	public void NoGutterByDefaultOrWithoutAWidth()
	{
		var (_, _, overlay) = CreateGutterScroller( "" );
		Assert.AreEqual( 200, overlay.Box.Rect.Width, 0.001f, "auto lays the bar over the content" );

		var (_, _, none) = CreateGutterScroller( "scrollbar-gutter: stable; scrollbar-width: none;" );
		Assert.AreEqual( 200, none.Box.Rect.Width, 0.001f, "no bar, no gutter" );
	}

	[TestMethod]
	public void GutterSitsOutsideThePadding()
	{
		var (_, scroller, content) = CreateGutterScroller( "scrollbar-gutter: stable; padding: 10px;" );

		Assert.AreEqual( 10, content.Box.Rect.Left, 0.001f );
		Assert.AreEqual( 172, content.Box.Rect.Width, 0.001f, "200 less 10px of padding each side less the 8px gutter" );
		Assert.AreEqual( 0, scroller.ScrollSize.x, 0.001f );
	}

	[TestMethod]
	public void GutterIsPartOfTheSidewaysRange()
	{
		// 300px of content in a 200px box scrolls 100px - or 108px with a gutter, because the content
		// stops at the gutter rather than passing under the bar
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var scroller = root.Add.Panel();
		scroller.Style.Set( "width: 200px; height: 200px; flex-direction: column; overflow: scroll; scrollbar-width: 8px; scrollbar-gutter: stable;" );

		var content = scroller.Add.Panel();
		content.Style.Set( "width: 300px; height: 1000px; flex-shrink: 0;" );

		Settle( root );

		Assert.AreEqual( 108, scroller.ScrollSize.x, 0.001f );

		scroller.ScrollTo( new Vector2( 108, 0 ) );
		Settle( root );

		Assert.AreEqual( 192, content.Box.Rect.Right, 0.001f, "scrolled all the way, the content ends where the gutter starts" );
		Assert.AreEqual( 192, scroller.ContentClipRect.Right, 0.001f, "and that's where the content is clipped" );
		Assert.AreEqual( 200, scroller.Box.ClipRect.Right, 0.001f, "while the bar's clip runs to the edge" );
	}

	[TestMethod]
	public void GutterRidesOnPercentPadding()
	{
		// 5% of the 1000px root is 50px a side
		var (_, _, plain) = CreateGutterScroller( "padding: 5%;" );
		Assert.AreEqual( 100, plain.Box.Rect.Width, 0.001f );

		var (_, _, guttered) = CreateGutterScroller( "scrollbar-gutter: stable; padding: 5%;" );
		Assert.AreEqual( 92, guttered.Box.Rect.Width, 0.001f, "the percentage still resolves, with the gutter on top" );
	}

	/// <summary>
	/// A root at a screen scale other than one, like a high-DPI window
	/// </summary>
	class ScaledRoot : RootPanel
	{
		public ScaledRoot( float scale )
		{
			Scale = scale;
		}
	}

	[TestMethod]
	public void GutterMatchesTheBarAtScreenScale()
	{
		// 1.75 is a common Windows scale, and one that puts style pixels on fractions of screen pixels
		var root = new ScaledRoot( 1.75f );
		root.PanelBounds = new Rect( 0, 0, 2000, 2000 );

		var scroller = root.Add.Panel();
		scroller.Style.Set( "width: 200px; height: 200px; flex-direction: column; overflow-y: scroll; scrollbar-width: 8px; scrollbar-gutter: stable;" );

		var content = scroller.Add.Panel();
		content.Style.Set( "width: 100%; height: 1000px; flex-shrink: 0;" );

		Settle( root );

		var bar = VerticalBar( scroller );
		var box = scroller.Box.Rect;
		Assert.AreEqual( 350, box.Width, 0.001f, "200 style pixels at 1.75" );

		// 8 * 1.75 = 14: the bar is exactly that wide, sits exactly in the gutter, and the content ends exactly where it starts
		Assert.AreEqual( 14, bar.Box.Rect.Width, 0.001f );
		Assert.AreEqual( box.Right - 14, bar.Box.Rect.Left, 0.001f );
		Assert.AreEqual( box.Right - 14, content.Box.Rect.Right, 0.001f );
		Assert.AreEqual( 0, scroller.ScrollSize.x, 0.001f );

		// 9 * 1.75 = 15.75: a half pixel has to round the same way for the bar and the gutter
		scroller.Style.Set( "scrollbar-width: 9px" );
		Settle( root );

		Assert.AreEqual( 16, bar.Box.Rect.Width, 0.001f );
		Assert.AreEqual( content.Box.Rect.Right, bar.Box.Rect.Left, 0.001f, "no gap and no overlap between content and bar" );
		Assert.AreEqual( box.Right, bar.Box.Rect.Right, 0.001f );
	}

	[TestMethod]
	public void ScrollbarColorParses()
	{
		var (_, scroller, _) = CreateScroller( "overflow-y: scroll;" );

		scroller.Style.Set( "scrollbar-color: #ff0000 #00ff00" );
		Assert.AreEqual( Color.Parse( "#ff0000" ), scroller.Style.ScrollbarThumbColor );
		Assert.AreEqual( Color.Parse( "#00ff00" ), scroller.Style.ScrollbarTrackColor );

		// A function with spaces inside is still one colour
		scroller.Style.Set( "scrollbar-color: rgba( 0, 0, 0, 0.5 ) #fff" );
		Assert.AreEqual( 0.5f, scroller.Style.ScrollbarThumbColor.Value.a, 0.01f );
		Assert.AreEqual( Color.Parse( "#fff" ), scroller.Style.ScrollbarTrackColor );

		// One colour on its own is the thumb
		scroller.Style.Set( "scrollbar-color: #0000ff" );
		Assert.AreEqual( Color.Parse( "#0000ff" ), scroller.Style.ScrollbarThumbColor );
		Assert.IsNull( scroller.Style.ScrollbarTrackColor );

		scroller.Style.Set( "scrollbar-color: auto" );
		Assert.IsNull( scroller.Style.ScrollbarThumbColor );
		Assert.IsNull( scroller.Style.ScrollbarTrackColor );
	}

	[TestMethod]
	public void ScrollbarColorColoursTheBar()
	{
		var (root, scroller, _) = CreateScroller( "overflow-y: scroll; scrollbar-width: 8px; scrollbar-color: #ff0000 #00ff00;" );
		var bar = VerticalBar( scroller );
		var thumb = Thumb( bar );

		Assert.AreEqual( Color.Parse( "#ff0000" ), thumb.ComputedStyle.BackgroundColor );
		Assert.AreEqual( Color.Parse( "#00ff00" ), bar.ComputedStyle.BackgroundColor );

		scroller.Style.Set( "scrollbar-color: auto" );
		Settle( root );

		Assert.AreNotEqual( Color.Parse( "#ff0000" ), thumb.ComputedStyle.BackgroundColor );
		Assert.AreEqual( Color.Transparent, bar.ComputedStyle.BackgroundColor );
	}

	[TestMethod]
	public void ScrollbarColorInherits()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );
		root.StyleSheet.Parse( ".host { scrollbar-width: 8px; scrollbar-color: #ff0000 #00ff00; }" );

		var host = root.Add.Panel( "host" );
		var scroller = host.Add.Panel();
		scroller.Style.Set( "width: 200px; height: 200px; flex-direction: column; overflow-y: scroll;" );

		var content = scroller.Add.Panel();
		content.Style.Set( "width: 100px; height: 1000px; flex-shrink: 0;" );

		Settle( root );

		var bar = VerticalBar( scroller );
		Assert.AreEqual( Color.Parse( "#ff0000" ), Thumb( bar ).ComputedStyle.BackgroundColor );
		Assert.AreEqual( Color.Parse( "#00ff00" ), bar.ComputedStyle.BackgroundColor );
	}
}

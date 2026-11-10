using Sandbox.UI;

namespace UITests;

/// <summary>
/// What UI tests share: a screen-sized root, ways to poke panels the way input does, and a
/// surface frame that works without the render system.
/// </summary>
static class UiTesting
{
	public static RootPanel CreateRoot()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );
		return root;
	}

	/// <summary>
	/// Sends a mouse event to a panel and ticks so it's dispatched.
	/// </summary>
	public static void Mouse( RootPanel root, Panel target, string name )
	{
		target.CreateEvent( new MousePanelEvent( name, target, "mouseleft" ) );
		root.TickInternal();
	}

	public static void Click( RootPanel root, Panel target ) => Mouse( root, target, "onclick" );
	public static void Hover( RootPanel root, Panel target ) => Mouse( root, target, "onmouseover" );

	/// <summary>
	/// A surface frame without the command-list build - that wants the render system, which this
	/// tier never boots. Tick, route input, rebuild selectors, lay out.
	/// </summary>
	public static void Frame( UISurface surface )
	{
		surface.System.TickPanels();
		surface.System.TickSurfaceInput( true );
		surface.Root.BuildStyleRules();
		surface.Root.Layout();
	}

	/// <summary>
	/// Turns text rendering off for a test - a visible label with text wants the GPU - and
	/// returns what to put back.
	/// </summary>
	public static bool DisableTextRendering()
	{
		var previous = TextBlock.ui_rendertext;
		TextBlock.ui_rendertext = false;
		return previous;
	}
}

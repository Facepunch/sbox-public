using Sandbox.UI;

namespace UITests;

/// <summary>
/// A tooltip laid out the way a tooltip window lays it out: first against the parent window's
/// size, then against its own. Long text has to wrap to the tooltip's max width and the height
/// has to follow it, or the window sized from it comes out wrong.
/// </summary>
[TestClass]
[DoNotParallelize]
public class TooltipLayoutTests
{
	const string Essay = "A tooltip can say quite a lot. This one goes on for long enough that it has to wrap onto several lines, which it should do at a sensible width rather than stretching across the whole screen.";

	// Text is measured on the CPU but rendered through the device, which the test host hasn't got
	bool previousRenderText;

	[TestInitialize]
	public void Setup()
	{
		previousRenderText = TextBlock.ui_rendertext;
		TextBlock.ui_rendertext = false;
	}

	[TestCleanup]
	public void Teardown()
	{
		TextBlock.ui_rendertext = previousRenderText;
	}

	static RootPanel MakeRoot( Vector2 size )
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, size.x, size.y );
		root.AddClass( "os-tooltip" );
		root.Style.AlignItems = Align.FlexStart;
		root.StyleSheet.Parse( ".os-tooltip .tooltip { flex-direction: column; align-items: flex-start; max-width: 360px; padding: 7px 10px; font-size: 12px; }" );
		return root;
	}

	/// <summary>
	/// Builds the tooltip the way the panel does and places it the way a tooltip window does: as
	/// an ordinary child of the root, not an absolute one. This guards against absolute measurement
	/// happening before max-width applies, which would give long text a one-line box.
	/// </summary>
	static Panel MakeTooltip( RootPanel root, string text )
	{
		var owner = new Panel { Parent = root, Tooltip = text };
		var tooltip = owner.BuildTooltip();
		Assert.IsNotNull( tooltip );

		tooltip.Style.Position = PositionMode.Relative;
		return tooltip;
	}

	[TestMethod]
	public void ShortTextIsOneLine()
	{
		var root = MakeRoot( new Vector2( 1280, 860 ) );
		var tooltip = MakeTooltip( root, "Save the scene" );
		root.Layout();

		var rect = tooltip.Box.Rect;
		Assert.IsTrue( rect.Width > 20 && rect.Width < 360, $"width {rect.Width}" );
		Assert.IsTrue( rect.Height > 10 && rect.Height < 60, $"height {rect.Height}" );
	}

	[TestMethod]
	public void LongTextWrapsAtMaxWidthAndGrowsTall()
	{
		var root = MakeRoot( new Vector2( 1280, 860 ) );
		var tooltip = MakeTooltip( root, Essay );
		root.Layout();

		var rect = tooltip.Box.Rect;
		Assert.IsTrue( rect.Width <= 360, $"wider than max-width: {rect.Width}" );
		Assert.IsTrue( rect.Width > 200, $"suspiciously narrow: {rect.Width}" );
		Assert.IsTrue( rect.Height > 40, $"not tall enough for wrapped text: {rect.Height}" );

		// The window shrinks to that, and the tooltip lays out again against the new size -
		// nothing should change
		root.PanelBounds = new Rect( 0, 0, rect.Width, rect.Height );
		root.Layout();

		var again = tooltip.Box.Rect;
		Assert.AreEqual( rect.Width, again.Width, 1.0f, "width changed on relayout" );
		Assert.AreEqual( rect.Height, again.Height, 1.0f, "height changed on relayout" );
	}
}

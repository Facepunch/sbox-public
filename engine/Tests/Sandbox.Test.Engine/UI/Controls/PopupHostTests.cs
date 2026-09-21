using Sandbox.UI;
using static UITests.UiTesting;

namespace UITests.Controls;

/// <summary>
/// The popup host seam: a UI that hosts its surface in windows decides where a popup goes.
/// </summary>
[TestClass]
[DoNotParallelize] // Popups are global state, and labels need text rendering off
public class PopupHostTest
{
	bool previousRenderText;
	UISurface surface;
	RecordingPopupHost host;

	[TestInitialize]
	public void Setup()
	{
		previousRenderText = DisableTextRendering();
		BasePopup.CloseAll();

		surface = new UISurface();
		host = new RecordingPopupHost();
		surface.System.PopupHost = host;
	}

	[TestCleanup]
	public void Cleanup()
	{
		surface.Dispose();
		TextBlock.ui_rendertext = previousRenderText;
	}

	[TestMethod]
	public void CaretPlacementFlipsAndClampsAtSurfaceEdges()
	{
		var bounds = new Rect( 0, 0, 800, 600 );
		var size = new Vector2( 300, 200 );
		Assert.AreEqual( new Vector2( 100, 124 ), Popup.AnchorPosition( new Rect( 100, 100, 1, 20 ), size, bounds, false, 4 ) );
		Assert.AreEqual( new Vector2( 500, 356 ), Popup.AnchorPosition( new Rect( 790, 560, 1, 20 ), size, bounds, false, 4 ) );
		Assert.AreEqual( new Vector2( 100, 44 ), Popup.AnchorPosition( new Rect( 100, 20, 1, 20 ), size, bounds, true, 4 ) );
		Assert.AreEqual( Vector2.Zero, Popup.AnchorPosition( new Rect( 100, 20, 1, 20 ), new Vector2( 900, 700 ), bounds, true, 4 ) );
	}

	[TestMethod]
	public void DetailsFlipToTheLeftWhenTheRightSideIsFull()
	{
		var bounds = new Rect( 0, 0, 800, 600 );
		var size = new Vector2( 200, 50 );
		Assert.AreEqual( new Vector2( 194, 100 ), Popup.AnchorPosition( new Rect( 100, 100, 90, 290 ), size, bounds, Popup.PositionMode.RightTop, 4 ) );
		Assert.AreEqual( new Vector2( 496, 100 ), Popup.AnchorPosition( new Rect( 700, 100, 90, 290 ), size, bounds, Popup.PositionMode.RightTop, 4 ) );
	}

	[TestMethod]
	public void PopupGoesToTheHostInsteadOfTheRoot()
	{
		var source = new Panel { Parent = surface.Root };

		var popup = new Popup( source, Popup.PositionMode.BelowLeft, 0 );

		Assert.AreEqual( host, popup.Host );
		Assert.AreEqual( host.Window, popup.Parent );
		Assert.AreEqual( source, host.ShownFor );
		Assert.AreEqual( Popup.PositionMode.BelowLeft, host.ShownAt );
	}

	[TestMethod]
	public void WithoutAHostThePopupFloatsInTheRoot()
	{
		surface.System.PopupHost = null;
		var source = new Panel { Parent = surface.Root };

		var popup = new Popup( source, Popup.PositionMode.BelowLeft, 0 );

		Assert.IsNull( popup.Host );
		Assert.AreEqual( surface.Root, popup.Parent );
	}

	[TestMethod]
	public void DeletingAPopupTellsTheHost()
	{
		var source = new Panel { Parent = surface.Root };
		var popup = new Popup( source, Popup.PositionMode.BelowLeft, 0 );

		popup.Delete( true );

		Assert.AreEqual( 1, host.HideCount );
		Assert.AreEqual( 0, host.Shown.Count );
	}

	/// <summary>
	/// A hosted popup doesn't position itself - the window it's in is what's positioned.
	/// </summary>
	[TestMethod]
	public void HostedPopupLeavesItsPositionToTheHost()
	{
		var source = new Panel { Parent = surface.Root };
		var popup = new Popup( source, Popup.PositionMode.BelowLeft, 0 );
		popup.Style.Left = 42;

		popup.Tick();

		Assert.AreEqual( 42, popup.Style.Left?.Value );
	}
}

using Sandbox.UI;
using static UITests.Controls.MenuTesting;
using static UITests.UiTesting;

namespace UITests.Controls;

/// <summary>
/// Menus through a popup host - the list goes where the host puts it, rows survive the host
/// taking it down, submenus open through the host of the surface their row is in.
/// </summary>
[TestClass]
[DoNotParallelize] // Popups are global state, and labels need text rendering off
public class MenuHostTest
{
	bool previousRenderText;
	UISurface surface;
	RecordingPopupHost host;

	[TestInitialize]
	public void Setup()
	{
		previousRenderText = DisableTextRendering();
		BasePopup.CloseAll();
		ResetTiming();

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
	public void MenuOpensThroughTheHost()
	{
		var source = new Panel { Parent = surface.Root };
		var menu = new Menu( "File" );
		var option = menu.AddOption( "New" );

		menu.Open( source, Popup.PositionMode.BelowLeft );

		Assert.AreEqual( 1, host.Shown.Count );
		Assert.AreEqual( menu.ListPanel, host.Shown[0] );
		Assert.AreEqual( host.Window, menu.ListPanel.Parent );
		Assert.AreEqual( menu.ListPanel, option.Parent );
		Assert.IsTrue( menu.IsOpen );

		menu.Close();

		Assert.AreEqual( 1, host.HideCount );
		Assert.IsFalse( menu.IsOpen );
		Assert.IsNull( option.Parent );
	}

	/// <summary>
	/// The host's window closing deletes the popup - the way a click elsewhere does. The rows
	/// must already be out by then, and the menu must know it's closed.
	/// </summary>
	[TestMethod]
	public void HostDeletingTheListClosesTheMenuAndSavesTheRows()
	{
		var source = new Panel { Parent = surface.Root };
		var menu = new Menu( "File" );
		var option = menu.AddOption( "New" );
		int closed = 0;
		menu.Closed += m => closed++;

		menu.Open( source, Popup.PositionMode.BelowLeft );
		menu.ListPanel.Delete( true );

		Assert.IsFalse( menu.IsOpen );
		Assert.AreEqual( 1, closed );
		Assert.IsNull( option.Parent );
		Assert.IsFalse( option.IsDeleting );
		Assert.IsFalse( menu.HasClass( "open" ) );

		menu.Open( source, Popup.PositionMode.BelowLeft );
		Assert.IsTrue( menu.IsOpen );
		Assert.AreEqual( menu.ListPanel, option.Parent );
	}

	/// <summary>
	/// A submenu opens through the host of the surface its row is in - in a window that's the
	/// popup window the parent list lives in.
	/// </summary>
	[TestMethod]
	public void SubmenuOpensThroughItsRowsHost()
	{
		var source = new Panel { Parent = surface.Root };
		var menu = new Menu( "File" );
		var recent = menu.AddMenu( "Recent" );
		recent.AddOption( "a.txt" );

		menu.Open( source, Popup.PositionMode.BelowLeft );

		// The host put the list on a second surface with its own host, the way a window would
		var inner = new UISurface();
		var innerHost = new RecordingPopupHost();
		inner.System.PopupHost = innerHost;
		menu.ListPanel.Parent = inner.Root;

		recent.Open();

		Assert.AreEqual( 1, innerHost.Shown.Count );
		Assert.AreEqual( recent, innerHost.ShownFor );
		Assert.AreEqual( Popup.PositionMode.RightTop, innerHost.ShownAt );

		menu.Close();
		Assert.IsFalse( recent.IsOpen );
		Assert.AreEqual( 1, innerHost.HideCount );

		inner.Dispose();
	}

	/// <summary>
	/// Wherever the list ends up, it's styled as if it sat inside the row that opened it - its
	/// stylesheets, its selectors, its font.
	/// </summary>
	[TestMethod]
	public void ListIsStyledUnderTheMenuThatOpenedIt()
	{
		var source = new Panel { Parent = surface.Root };
		var bar = new MenuBar { Parent = source };
		var file = bar.AddMenu( "File" );
		file.AddOption( "New" );

		file.Open( file, Popup.PositionMode.BelowLeft );

		Assert.AreEqual( host.Window, file.ListPanel.Parent );
		Assert.AreEqual( file, file.ListPanel.StyleParent );
		Assert.IsTrue( file.ListPanel.AllStyleSheets.Contains( surface.Root.StyleSheet.List[0] ) );
	}
}

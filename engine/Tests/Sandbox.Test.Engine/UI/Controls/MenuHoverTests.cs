using Sandbox.UI;
using static UITests.Controls.MenuTesting;
using static UITests.UiTesting;
using System.Collections.Generic;

namespace UITests.Controls;

/// <summary>
/// Hovering rows of an open menu on a laid-out surface - every row, not just the first.
/// </summary>
[TestClass]
[DoNotParallelize] // Popups are global state, and labels need text rendering off
public class MenuHoverTest
{
	bool previousRenderText;
	UISurface surface;

	[TestInitialize]
	public void Setup()
	{
		previousRenderText = DisableTextRendering();

		BasePopup.CloseAll();
		ResetTiming();

		surface = new UISurface();
		surface.Size = new Vector2( 800, 600 );
		surface.MouseInside = true;
	}

	[TestCleanup]
	public void Cleanup()
	{
		surface.Dispose();
		TextBlock.ui_rendertext = previousRenderText;
	}

	void Frame() => UiTesting.Frame( surface );

	void HoverAt( Vector2 position )
	{
		surface.MousePosition = position;
		Frame();
	}

	[TestMethod]
	public void EveryRowHighlightsWhenHovered()
	{
		var source = new Panel { Parent = surface.Root };
		source.Style.Width = 100;
		source.Style.Height = 30;

		var menu = new Menu( "File" );
		var a = menu.AddOption( "New" );
		var b = menu.AddOption( "Open" );
		var c = menu.AddOption( "Save" );

		Frame();
		menu.Open( source, Popup.PositionMode.BelowLeft );
		Frame();

		Assert.IsTrue( b.Box.Rect.Height > 0, "rows laid out" );
		Assert.IsTrue( c.Box.Rect.Top > b.Box.Rect.Top, "rows stack" );

		HoverAt( b.Box.Rect.Center );
		Frame();

		Assert.AreEqual( b, surface.Hovered?.AncestorsAndSelf.OfType<Menu>().FirstOrDefault(), "second row hovered" );
		Assert.AreEqual( b, menu.Highlighted );
		Assert.IsTrue( b.HasHovered );

		HoverAt( c.Box.Rect.Center );
		Frame();

		Assert.AreEqual( c, surface.Hovered?.AncestorsAndSelf.OfType<Menu>().FirstOrDefault(), "third row hovered" );
		Assert.AreEqual( c, menu.Highlighted );
		Assert.IsFalse( b.HasHovered );
		Assert.IsTrue( c.HasHovered );
	}

	/// <summary>
	/// Stands in for a popup window: the list goes into a surface of its own, laid out as an
	/// ordinary child the way PanelWindow does it.
	/// </summary>
	sealed class WindowHost : IPopupHost
	{
		public readonly List<UISurface> Windows = new();

		public void ShowPopup( Popup popup, Panel source, Popup.PositionMode position, float offset )
		{
			var window = new UISurface { Size = new Vector2( 400, 300 ), MouseInside = true };
			window.System.PopupHost = this;
			window.Root.Style.AlignItems = Align.FlexStart;

			popup.Parent = window.Root;
			popup.Style.Position = PositionMode.Relative;
			popup.Style.Left = null;
			popup.Style.Top = null;

			Windows.Add( window );
		}

		public void HidePopup( Popup popup )
		{
			var window = Windows.Find( x => x.Root == popup.Parent );
			if ( window is null ) return;

			Windows.Remove( window );
			window.Dispose();
		}
	}

	[TestMethod]
	public void EveryRowHighlightsWhenHoveredInAHostedWindow()
	{
		var host = new WindowHost();
		surface.System.PopupHost = host;

		var source = new Panel { Parent = surface.Root };
		source.Style.Width = 100;
		source.Style.Height = 30;

		var menu = new Menu( "File" );
		var a = menu.AddOption( "New" );
		var b = menu.AddOption( "Open" );
		var c = menu.AddOption( "Save" );

		Frame();
		menu.Open( source, Popup.PositionMode.BelowLeft );

		var window = host.Windows[0];
		UiTesting.Frame( window );
		UiTesting.Frame( window );

		Assert.IsTrue( b.Box.Rect.Height > 0, "rows laid out" );
		Assert.IsTrue( c.Box.Rect.Top > b.Box.Rect.Top, "rows stack" );

		window.MousePosition = b.Box.Rect.Center;
		UiTesting.Frame( window );
		UiTesting.Frame( window );

		Assert.AreEqual( b, window.Hovered?.AncestorsAndSelf.OfType<Menu>().FirstOrDefault(), "second row hovered" );
		Assert.AreEqual( b, menu.Highlighted );
		Assert.IsTrue( b.HasHovered );

		window.MousePosition = c.Box.Rect.Center;
		UiTesting.Frame( window );
		UiTesting.Frame( window );

		Assert.AreEqual( c, window.Hovered?.AncestorsAndSelf.OfType<Menu>().FirstOrDefault(), "third row hovered" );
		Assert.AreEqual( c, menu.Highlighted );
		Assert.IsTrue( c.HasHovered );

		menu.Close();
		Assert.AreEqual( 0, host.Windows.Count );
	}

	/// <summary>
	/// Backing out of a submenu in-surface: its list was focused and is now deleted, which
	/// clears the focus. The parent list has to end up with it or the keyboard goes dead.
	/// </summary>
	[TestMethod]
	public void FocusReturnsToTheParentListWhenASubmenuCloses()
	{
		var source = new Panel { Parent = surface.Root };
		var menu = new Menu( "File" );
		var recent = menu.AddMenu( "Recent" );
		recent.AddOption( "a.txt" );

		Frame();
		menu.Open( source, Popup.PositionMode.BelowLeft );
		Frame();
		Assert.AreEqual( menu.ListPanel, surface.System.CurrentFocus );

		Key( menu, "down" );
		Key( menu, "right" );
		Frame();
		Assert.AreEqual( recent.ListPanel, surface.System.CurrentFocus );

		Key( recent, "left" );
		Frame();

		Assert.IsFalse( recent.IsOpen );
		Assert.AreEqual( menu.ListPanel, surface.System.CurrentFocus );
	}

}

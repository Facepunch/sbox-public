using Sandbox.UI;

namespace UITests.Controls;

/// <summary>
/// The engine's end of the OS popup windows - dismissing them the way OS menus go, sparing the
/// chain a click landed in.
/// </summary>
[TestClass]
[DoNotParallelize] // The window registry is global
public class PanelWindowPopupTest
{
	FakePanelWindow main, menu, submenu, other;

	[TestInitialize]
	public void Setup()
	{
		main = new FakePanelWindow();
		menu = new FakePanelWindow { IsPopup = true, Parent = main };
		submenu = new FakePanelWindow { IsPopup = true, Parent = menu };
		other = new FakePanelWindow { IsPopup = true, Parent = main };

		foreach ( var window in new[] { main, menu, submenu, other } )
		{
			PanelWindows.Register( window );
		}
	}

	[TestCleanup]
	public void Cleanup()
	{
		foreach ( var window in new[] { main, menu, submenu, other } )
		{
			PanelWindows.Unregister( window );
		}
	}

	[TestMethod]
	public void ClickInSubmenuKeepsItsParentsUp()
	{
		PanelWindows.DismissPopups( except: submenu );

		Assert.IsFalse( submenu.CloseRequested );
		Assert.IsFalse( menu.CloseRequested );
		Assert.IsFalse( main.CloseRequested );
		Assert.IsTrue( other.CloseRequested );
	}

	[TestMethod]
	public void ClickInParentClosesTheSubmenu()
	{
		PanelWindows.DismissPopups( except: menu );

		Assert.IsTrue( submenu.CloseRequested );
		Assert.IsFalse( menu.CloseRequested );
	}

	[TestMethod]
	public void ClickInMainWindowClosesEveryPopup()
	{
		PanelWindows.DismissPopups( except: main );

		Assert.IsTrue( submenu.CloseRequested );
		Assert.IsTrue( menu.CloseRequested );
		Assert.IsTrue( other.CloseRequested );
		Assert.IsFalse( main.CloseRequested );
	}

	/// <summary>
	/// A click on nothing inside a popup window closes every popup - the one the window was
	/// showing included, which takes the window down mid-handler. The rest of the handler must
	/// leave it alone.
	/// </summary>
	[TestMethod]
	public void ClickOnNothingInAPopupWindowSurvivesTheWindowClosing()
	{
		using var surface = new UISurface { Size = new Vector2( 200, 200 ) };
		surface.Root.Style.PointerEvents = PointerEvents.None;
		surface.System.PopupHost = null;

		var window = new FakePanelWindow { IsPopup = true, Parent = main, Handle = 1234, Surface = surface };
		PanelWindows.Register( window );

		try
		{
			// The popup is shown by this window - deleting it closes the window
			using var owner = new UISurface { Size = new Vector2( 200, 200 ) };
			owner.System.PopupHost = window;
			var source = new Panel { Parent = owner.Root };
			var popup = new Popup( source, Popup.PositionMode.BelowLeft, 0 );
			Assert.AreEqual( surface.Root, popup.Parent );

			PanelWindowInput.OnMouseButton( window.Handle, NativeEngine.ButtonCode.MouseLeft, true, 1, 0 );

			Assert.IsTrue( window.CloseRequested );
			Assert.IsTrue( popup.IsDeleting );
		}
		finally
		{
			PanelWindows.Unregister( window );
		}
	}
}

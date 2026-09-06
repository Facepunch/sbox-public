using Sandbox.UI;

namespace UITests.Controls;

/// <summary>
/// An open menu window owns the keyboard, whichever window the OS thinks is focused.
/// </summary>
[TestClass]
[DoNotParallelize] // The window registry is global
public class PanelWindowKeyboardTest
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
	public void KeysGoToTheDeepestOpenPopup()
	{
		Assert.AreEqual( submenu, PanelWindows.KeyboardTarget( main ) );
		Assert.AreEqual( submenu, PanelWindows.KeyboardTarget( menu ) );
	}

	[TestMethod]
	public void KeysStayWithTheWindowWhenNoPopupIsOpen()
	{
		PanelWindows.DismissPopups();

		Assert.AreEqual( main, PanelWindows.KeyboardTarget( main ) );
	}

	[TestMethod]
	public void KeysSkipPopupsThatIgnoreInput()
	{
		var tooltip = new FakePanelWindow { IsPopup = true, Parent = main, IgnoresInput = true };
		PanelWindows.Register( tooltip );

		try
		{
			Assert.AreEqual( submenu, PanelWindows.KeyboardTarget( main ) );

			PanelWindows.DismissPopups();

			Assert.AreEqual( main, PanelWindows.KeyboardTarget( main ) );
		}
		finally
		{
			PanelWindows.Unregister( tooltip );
		}
	}
}

using Sandbox.UI;
using static UITests.Controls.MenuTesting;
using static UITests.UiTesting;

namespace UITests.Controls;

/// <summary>
/// The panel menu system: building menus, opening and closing them, cascading submenus, keyboard
/// navigation and the menu bar. Runs headless - lists never lay out, they're only ticked.
/// </summary>
[TestClass]
[DoNotParallelize] // Popups are global state, and labels need text rendering off
public class MenuTest
{
	bool previousRenderText;

	[TestInitialize]
	public void Setup()
	{
		previousRenderText = DisableTextRendering();
		BasePopup.CloseAll();
		ResetTiming();
	}

	[TestCleanup]
	public void Cleanup()
	{
		TextBlock.ui_rendertext = previousRenderText;
	}

	//
	// Building
	//

	[TestMethod]
	public void AddOptionKeepsOrderAndParent()
	{
		var menu = new Menu( "File" );
		var a = menu.AddOption( "New", "note_add" );
		var b = menu.AddOption( "Open" );
		var sep = menu.AddSeparator();
		var c = menu.AddMenu( "Recent" );

		CollectionAssert.AreEqual( new[] { a, b, sep, c }, menu.Options.ToArray() );
		Assert.AreEqual( menu, a.ParentMenu );
		Assert.AreEqual( menu, c.RootMenu );
		Assert.IsTrue( sep.IsSeparator );
		Assert.AreEqual( "New", a.Text );
		Assert.AreEqual( "note_add", a.Icon );
		Assert.IsTrue( menu.HasOptions );
		Assert.IsFalse( a.HasOptions );
	}

	[TestMethod]
	public void ShortcutMarksTheRow()
	{
		var option = new Menu( "Save" );
		Assert.IsFalse( option.HasClass( "has-shortcut" ) );

		option.Shortcut = "Ctrl+S";
		Assert.IsTrue( option.HasClass( "has-shortcut" ) );

		option.Shortcut = "";
		Assert.IsFalse( option.HasClass( "has-shortcut" ) );
	}

	[TestMethod]
	public void ToggleOptionIsCheckable()
	{
		var menu = new Menu();
		var option = menu.AddOption( "Show Grid", on => { } );

		Assert.IsTrue( option.Checkable );
		Assert.IsFalse( option.Checked );
	}

	[TestMethod]
	public void FindOptionByText()
	{
		var menu = new Menu();
		var save = menu.AddOption( "Save" );

		Assert.AreEqual( save, menu.FindOption( "Save" ) );
		Assert.IsNull( menu.FindOption( "Nope" ) );
	}

	[TestMethod]
	public void MenuAddedAsChildBecomesAnOption()
	{
		var root = CreateRoot();
		var source = new Panel { Parent = root };
		var menu = new Menu( "File" );
		var child = new Menu( "New" );

		menu.AddChild( child );

		Assert.AreEqual( menu, child.ParentMenu );
		CollectionAssert.AreEqual( new[] { child }, menu.Options.ToArray() );

		// Opening moves it out of the row and into the list
		menu.Open( source, Popup.PositionMode.BelowLeft );
		Assert.AreEqual( menu.ListPanel, child.Parent );
	}

	//
	// Opening and closing
	//

	[TestMethod]
	public void OpenShowsOptionsInPopupRootAndFiresAboutToShow()
	{
		var root = CreateRoot();
		var source = new Panel { Parent = root };
		var menu = new Menu( "File" );
		var option = menu.AddOption( "New" );

		int shown = 0;
		menu.AboutToShow += m => shown++;

		menu.Open( source, Popup.PositionMode.BelowLeft );

		Assert.IsTrue( menu.IsOpen );
		Assert.AreEqual( 1, shown );
		Assert.AreEqual( root, menu.ListPanel.Parent );
		Assert.AreEqual( menu.ListPanel, option.Parent );
		Assert.IsTrue( menu.HasClass( "open" ) );
	}

	[TestMethod]
	public void CloseDetachesOptionsButKeepsThem()
	{
		var root = CreateRoot();
		var source = new Panel { Parent = root };
		var menu = new Menu( "File" );
		var option = menu.AddOption( "New" );

		int closed = 0;
		menu.Closed += m => closed++;

		menu.Open( source, Popup.PositionMode.BelowLeft );
		menu.Close();

		Assert.IsFalse( menu.IsOpen );
		Assert.AreEqual( 1, closed );
		Assert.IsNull( option.Parent );
		Assert.IsFalse( menu.HasClass( "open" ) );
		CollectionAssert.AreEqual( new[] { option }, menu.Options.ToArray() );

		// And it opens again with the same options
		menu.Open( source, Popup.PositionMode.BelowLeft );
		Assert.IsTrue( menu.IsOpen );
		Assert.AreEqual( menu.ListPanel, option.Parent );
	}

	[TestMethod]
	public void AboutToShowCanRebuildOptions()
	{
		var root = CreateRoot();
		var source = new Panel { Parent = root };
		var menu = new Menu( "Recent" );
		var files = new[] { "a.txt", "b.txt" };

		menu.AboutToShow += m =>
		{
			m.Clear();
			foreach ( var f in files ) m.AddOption( f );
		};

		menu.Open( source, Popup.PositionMode.BelowLeft );
		Assert.AreEqual( 2, menu.Options.Count );
		menu.Close();

		files = new[] { "c.txt" };
		menu.Open( source, Popup.PositionMode.BelowLeft );

		Assert.AreEqual( 1, menu.Options.Count );
		Assert.AreEqual( "c.txt", menu.Options[0].Text );
		Assert.AreEqual( menu.ListPanel, menu.Options[0].Parent );
	}

	[TestMethod]
	public void ClosingAllPopupsClosesTheMenu()
	{
		var root = CreateRoot();
		var source = new Panel { Parent = root };
		var menu = new Menu( "File" );
		var option = menu.AddOption( "New" );

		menu.Open( source, Popup.PositionMode.BelowLeft );
		BasePopup.CloseAll();

		Assert.IsFalse( menu.IsOpen );
		Assert.IsFalse( menu.HasClass( "open" ) );
		CollectionAssert.AreEqual( new[] { option }, menu.Options.ToArray() );
	}

	//
	// Activation
	//

	[TestMethod]
	public void ClickingOptionFiresActionAndClosesWholeMenu()
	{
		var root = CreateRoot();
		var source = new Panel { Parent = root };
		var menu = new Menu( "File" );
		int clicks = 0;
		var option = menu.AddOption( "New", () => clicks++ );

		menu.Open( source, Popup.PositionMode.BelowLeft );
		Click( root, option );

		Assert.AreEqual( 1, clicks );
		Assert.IsFalse( menu.IsOpen );
	}

	[TestMethod]
	public void ClickingCheckableTogglesAndFiresOnToggle()
	{
		var root = CreateRoot();
		var source = new Panel { Parent = root };
		var menu = new Menu( "View" );
		bool? toggled = null;
		var option = menu.AddOption( "Show Grid", on => toggled = on );

		menu.Open( source, Popup.PositionMode.BelowLeft );
		Click( root, option );

		Assert.IsTrue( option.Checked );
		Assert.AreEqual( true, toggled );
		Assert.IsTrue( menu.IsOpen, "a toggle keeps the menu up" );

		Click( root, option );

		Assert.IsFalse( option.Checked );
		Assert.AreEqual( false, toggled );
		Assert.IsTrue( menu.IsOpen );
	}

	[TestMethod]
	public void StaysOpenIsTheRowsCallEitherWay()
	{
		var root = CreateRoot();
		var source = new Panel { Parent = root };
		var menu = new Menu( "View" );
		var command = menu.AddOption( "Refresh", () => { } );
		command.StaysOpen = true;
		var toggle = menu.AddOption( "Grid", on => { } );
		toggle.StaysOpen = false;

		Assert.IsFalse( menu.AddOption( "Plain" ).StaysOpen );
		Assert.IsTrue( menu.AddOption( "Check", on => { } ).StaysOpen );

		menu.Open( source, Popup.PositionMode.BelowLeft );
		Click( root, command );
		Assert.IsTrue( menu.IsOpen, "a command told to stay open does" );

		Click( root, toggle );
		Assert.IsTrue( toggle.Checked );
		Assert.IsFalse( menu.IsOpen, "a toggle told to close does" );
	}

	[TestMethod]
	public void DisabledOptionIgnoresClick()
	{
		var root = CreateRoot();
		var source = new Panel { Parent = root };
		var menu = new Menu( "File" );
		int clicks = 0;
		var option = menu.AddOption( "Save", () => clicks++ );
		option.Enabled = false;

		menu.Open( source, Popup.PositionMode.BelowLeft );
		Click( root, option );

		Assert.AreEqual( 0, clicks );
		Assert.IsTrue( menu.IsOpen );
		Assert.IsTrue( option.HasClass( "disabled" ) );
	}

	//
	// Cascading
	//

	[TestMethod]
	public void HoveringSubmenuRowOpensItBesideTheRow()
	{
		var root = CreateRoot();
		var source = new Panel { Parent = root };
		var menu = new Menu( "File" );
		var recent = menu.AddMenu( "Recent" );
		var inner = recent.AddOption( "a.txt" );

		menu.Open( source, Popup.PositionMode.BelowLeft );
		Hover( root, recent );
		root.TickInternal();

		Assert.IsTrue( recent.IsOpen );
		Assert.AreEqual( recent, recent.ListPanel.PopupSource );
		Assert.AreEqual( root, recent.ListPanel.Parent );
		Assert.AreEqual( recent.ListPanel, inner.Parent );
	}

	[TestMethod]
	public void HoveringSiblingClosesOpenSubmenu()
	{
		var root = CreateRoot();
		var source = new Panel { Parent = root };
		var menu = new Menu( "File" );
		var recent = menu.AddMenu( "Recent" );
		recent.AddOption( "a.txt" );
		var save = menu.AddOption( "Save" );

		menu.Open( source, Popup.PositionMode.BelowLeft );
		Hover( root, recent );
		root.TickInternal();
		Assert.IsTrue( recent.IsOpen );

		Hover( root, save );
		root.TickInternal();

		Assert.IsFalse( recent.IsOpen );
		Assert.IsTrue( menu.IsOpen );
	}

	[TestMethod]
	public void ClosingParentClosesOpenSubmenus()
	{
		var root = CreateRoot();
		var source = new Panel { Parent = root };
		var menu = new Menu( "File" );
		var recent = menu.AddMenu( "Recent" );
		recent.AddOption( "a.txt" );

		menu.Open( source, Popup.PositionMode.BelowLeft );
		Hover( root, recent );
		root.TickInternal();

		menu.Close();

		Assert.IsFalse( recent.IsOpen );
		Assert.IsFalse( menu.IsOpen );
	}

	/// <summary>
	/// A click lands on a row in a submenu: the surface closes every popup except the one under
	/// the cursor. The parent lists are that popup's ancestors, so they have to survive too.
	/// </summary>
	[TestMethod]
	public void ClickInsideSubmenuKeepsTheChainOpen()
	{
		var root = CreateRoot();
		var source = new Panel { Parent = root };
		var menu = new Menu( "File" );
		var recent = menu.AddMenu( "Recent" );
		var inner = recent.AddOption( "a.txt" );

		menu.Open( source, Popup.PositionMode.BelowLeft );
		Hover( root, recent );
		root.TickInternal();

		BasePopup.CloseAll( inner );

		Assert.IsTrue( menu.IsOpen );
		Assert.IsTrue( recent.IsOpen );
	}

	[TestMethod]
	public void ActivatingNestedOptionClosesEverything()
	{
		var root = CreateRoot();
		var source = new Panel { Parent = root };
		var menu = new Menu( "File" );
		var recent = menu.AddMenu( "Recent" );
		int clicks = 0;
		var inner = recent.AddOption( "a.txt", () => clicks++ );

		menu.Open( source, Popup.PositionMode.BelowLeft );
		Hover( root, recent );
		root.TickInternal();
		Click( root, inner );

		Assert.AreEqual( 1, clicks );
		Assert.IsFalse( recent.IsOpen );
		Assert.IsFalse( menu.IsOpen );
	}

	//
	// Keyboard
	//

	[TestMethod]
	public void DownSkipsSeparatorsAndDisabledAndWraps()
	{
		var root = CreateRoot();
		var source = new Panel { Parent = root };
		var menu = new Menu( "File" );
		var a = menu.AddOption( "A" );
		menu.AddSeparator();
		var off = menu.AddOption( "Off" );
		off.Enabled = false;
		var b = menu.AddOption( "B" );

		menu.Open( source, Popup.PositionMode.BelowLeft );
		Assert.IsNull( menu.Highlighted );

		Key( menu, "down" );
		Assert.AreEqual( a, menu.Highlighted );
		Assert.IsTrue( a.HasClass( "active" ) );

		Key( menu, "down" );
		Assert.AreEqual( b, menu.Highlighted );
		Assert.IsFalse( a.HasClass( "active" ) );

		Key( menu, "down" );
		Assert.AreEqual( a, menu.Highlighted );

		Key( menu, "up" );
		Assert.AreEqual( b, menu.Highlighted );
	}

	[TestMethod]
	public void EnterActivatesHighlighted()
	{
		var root = CreateRoot();
		var source = new Panel { Parent = root };
		var menu = new Menu( "File" );
		int clicks = 0;
		menu.AddOption( "A", () => clicks++ );

		menu.Open( source, Popup.PositionMode.BelowLeft );
		Key( menu, "down" );
		Key( menu, "enter" );

		Assert.AreEqual( 1, clicks );
		Assert.IsFalse( menu.IsOpen );
	}

	[TestMethod]
	public void RightOpensSubmenuAndLeftClosesIt()
	{
		var root = CreateRoot();
		var source = new Panel { Parent = root };
		var menu = new Menu( "File" );
		var recent = menu.AddMenu( "Recent" );
		var inner = recent.AddOption( "a.txt" );

		menu.Open( source, Popup.PositionMode.BelowLeft );
		Key( menu, "down" );
		Key( menu, "right" );

		Assert.IsTrue( recent.IsOpen );
		Assert.AreEqual( inner, recent.Highlighted );

		Key( recent, "left" );

		Assert.IsFalse( recent.IsOpen );
		Assert.IsTrue( menu.IsOpen );
		Assert.AreEqual( recent, menu.Highlighted );
	}

	[TestMethod]
	public void EscapeClosesOneLevel()
	{
		var root = CreateRoot();
		var source = new Panel { Parent = root };
		var menu = new Menu( "File" );
		var recent = menu.AddMenu( "Recent" );
		recent.AddOption( "a.txt" );

		menu.Open( source, Popup.PositionMode.BelowLeft );
		Key( menu, "down" );
		Key( menu, "right" );

		Key( recent, "escape" );
		Assert.IsFalse( recent.IsOpen );
		Assert.IsTrue( menu.IsOpen );

		Key( menu, "escape" );
		Assert.IsFalse( menu.IsOpen );
	}

	[TestMethod]
	public void TypingALetterJumpsToMatchingOption()
	{
		var root = CreateRoot();
		var source = new Panel { Parent = root };
		var menu = new Menu( "File" );
		menu.AddOption( "New" );
		var save = menu.AddOption( "Save" );
		var saveAs = menu.AddOption( "Save As" );

		menu.Open( source, Popup.PositionMode.BelowLeft );
		Key( menu, "s" );
		Assert.AreEqual( save, menu.Highlighted );

		Key( menu, "s" );
		Assert.AreEqual( saveAs, menu.Highlighted );
	}

	//
	// Menu bar
	//

	[TestMethod]
	public void MenuBarClickOpensAndClickAgainCloses()
	{
		var root = CreateRoot();
		var bar = new MenuBar { Parent = root };
		var file = bar.AddMenu( new Menu( "File" ) );
		file.AddOption( "New" );

		Assert.AreEqual( bar, file.Parent );

		Click( root, file );
		Assert.IsTrue( file.IsOpen );
		Assert.AreEqual( file, bar.OpenMenu );
		Assert.AreEqual( file, file.ListPanel.PopupSource );

		Click( root, file );
		Assert.IsFalse( file.IsOpen );
		Assert.IsNull( bar.OpenMenu );
	}

	[TestMethod]
	public void MenuBarHoverSwitchesWhileOpen()
	{
		var root = CreateRoot();
		var bar = new MenuBar { Parent = root };
		var file = bar.AddMenu( new Menu( "File" ) );
		file.AddOption( "New" );
		var edit = bar.AddMenu( new Menu( "Edit" ) );
		edit.AddOption( "Undo" );

		// Hovering with nothing open does nothing
		Hover( root, edit );
		Assert.IsFalse( edit.IsOpen );

		Click( root, file );
		Hover( root, edit );

		Assert.IsFalse( file.IsOpen );
		Assert.IsTrue( edit.IsOpen );
		Assert.AreEqual( edit, bar.OpenMenu );
	}

	[TestMethod]
	public void MenuBarArrowKeysMoveBetweenMenus()
	{
		var root = CreateRoot();
		var bar = new MenuBar { Parent = root };
		var file = bar.AddMenu( new Menu( "File" ) );
		file.AddOption( "New" );
		var edit = bar.AddMenu( new Menu( "Edit" ) );
		edit.AddOption( "Undo" );

		Click( root, file );
		Key( file, "right" );

		Assert.IsFalse( file.IsOpen );
		Assert.IsTrue( edit.IsOpen );

		// Wraps
		Key( edit, "right" );
		Assert.IsTrue( file.IsOpen );

		Key( file, "left" );
		Assert.IsTrue( edit.IsOpen );
	}

	//
	// One highlight, shared by the mouse and the keyboard
	//

	[TestMethod]
	public void KeyboardTakesTheHighlightOffTheHoveredRow()
	{
		var root = CreateRoot();
		var source = new Panel { Parent = root };
		var menu = new Menu( "File" );
		var a = menu.AddOption( "A" );
		var b = menu.AddOption( "B" );

		menu.Open( source, Popup.PositionMode.BelowLeft );
		Hover( root, a );
		Assert.AreEqual( a, menu.Highlighted );

		Key( menu, "down" );

		Assert.AreEqual( b, menu.Highlighted );
		Assert.IsFalse( a.HasClass( "active" ) );
	}

	[TestMethod]
	public void RightOpensSubmenuWhileTheMouseRestsOnAnotherRow()
	{
		var root = CreateRoot();
		var source = new Panel { Parent = root };
		var menu = new Menu( "File" );
		var plain = menu.AddOption( "Plain" );
		var recent = menu.AddMenu( "Recent" );
		recent.AddOption( "a.txt" );

		menu.Open( source, Popup.PositionMode.BelowLeft );
		Hover( root, plain );
		Key( menu, "down" );
		Key( menu, "right" );
		Assert.IsTrue( recent.IsOpen );

		// The mouse hasn't moved - the hover logic mustn't take it back down
		root.TickInternal();
		root.TickInternal();

		Assert.IsTrue( recent.IsOpen );
		Assert.AreEqual( recent, menu.Highlighted );
	}

	[TestMethod]
	public void LeavingARowDropsItsHighlightAndPendingSubmenu()
	{
		Menu.SubmenuOpenDelay = 1;

		var root = CreateRoot();
		var source = new Panel { Parent = root };
		var menu = new Menu( "File" );
		var recent = menu.AddMenu( "Recent" );
		recent.AddOption( "a.txt" );

		menu.Open( source, Popup.PositionMode.BelowLeft );
		Hover( root, recent );
		Assert.AreEqual( recent, menu.Highlighted );

		Mouse( root, recent, "onmouseout" );
		root.TickInternal();

		Assert.IsNull( menu.Highlighted );
		Assert.IsFalse( recent.IsOpen, "the submenu it was waiting to open stays shut" );
	}

	[TestMethod]
	public void LeavingARowWithItsSubmenuOpenKeepsItHighlighted()
	{
		var root = CreateRoot();
		var source = new Panel { Parent = root };
		var menu = new Menu( "File" );
		var recent = menu.AddMenu( "Recent" );
		recent.AddOption( "a.txt" );

		menu.Open( source, Popup.PositionMode.BelowLeft );
		Hover( root, recent );
		root.TickInternal();
		Assert.IsTrue( recent.IsOpen );

		Mouse( root, recent, "onmouseout" );

		Assert.AreEqual( recent, menu.Highlighted );
		Assert.IsTrue( recent.IsOpen );
	}

	[TestMethod]
	public void ClosedRowsForgetTheirHover()
	{
		var root = CreateRoot();
		var source = new Panel { Parent = root };
		var menu = new Menu( "File" );
		var a = menu.AddOption( "A" );

		menu.Open( source, Popup.PositionMode.BelowLeft );
		a.Switch( PseudoClass.Hover, true );
		menu.Close();

		Assert.IsFalse( a.HasHovered );
	}

	/// <summary>
	/// A row's click closes the menu from inside the list's own tick. The list is deleted by
	/// then, and the rest of its tick must not touch what a deleted panel no longer has.
	/// </summary>
	[TestMethod]
	public void ListDeletedDuringItsTickDoesNotThrow()
	{
		var root = CreateRoot();
		var source = new Panel { Parent = root };
		var menu = new Menu( "File" );
		menu.AddOption( "New" );

		menu.Open( source, Popup.PositionMode.BelowLeft );
		var list = menu.ListPanel;

		menu.Close();
		Assert.IsFalse( list.IsValid() );

		list.Tick();
	}

	[TestMethod]
	public void SubmenuFlipsToTheLeftAtTheScreenEdge()
	{
		var root = CreateRoot();
		var source = new Panel { Parent = root };
		source.Style.Position = PositionMode.Absolute;
		source.Style.Left = 900;
		source.Style.Top = 100;
		source.Style.Width = 50;
		source.Style.Height = 20;
		root.Layout();

		var menu = new Menu( "File" );
		var recent = menu.AddMenu( "Recent" );
		recent.AddOption( "a.txt" );

		menu.Open( source, Popup.PositionMode.BelowLeft );
		menu.ListPanel.Style.Width = 180;
		root.Layout();
		root.Layout();

		recent.Open();
		recent.ListPanel.Style.Width = 180;
		root.Layout();
		root.Layout();

		var row = recent.Box.Rect;
		var list = recent.ListPanel.Box.Rect;

		Assert.IsTrue( list.Right <= 1000, $"on screen, right edge at {list.Right}" );
		Assert.IsTrue( list.Right <= row.Left + 4, $"flipped to the left of the row: list {list} row {row}" );
	}
}

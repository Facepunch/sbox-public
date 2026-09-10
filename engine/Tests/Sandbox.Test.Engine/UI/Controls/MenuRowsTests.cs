using Sandbox.UI;
using static UITests.Controls.MenuTesting;
using static UITests.UiTesting;

namespace UITests.Controls;

/// <summary>
/// Rows that aren't options, menus with nothing in them, rows coming and going while the menu is
/// open, and the menu bar with other controls in it.
/// </summary>
[TestClass]
[DoNotParallelize] // Popups are global state, and labels need text rendering off
public class MenuRowsTest
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

	[TestMethod]
	public void EmptyMenuDoesNotOpen()
	{
		var root = CreateRoot();
		var source = new Panel { Parent = root };
		var menu = new Menu( "Empty" );
		int shown = 0;
		menu.AboutToShow += m => shown++;

		menu.Open( source, Popup.PositionMode.BelowLeft );

		Assert.AreEqual( 1, shown, "asked in case it wanted to fill itself" );
		Assert.IsFalse( menu.IsOpen );
		Assert.IsFalse( menu.HasClass( "open" ) );
	}

	[TestMethod]
	public void OpeningTwiceShowsOnce()
	{
		var root = CreateRoot();
		var source = new Panel { Parent = root };
		var menu = new Menu( "File" );
		menu.AddOption( "New" );
		int shown = 0;
		menu.AboutToShow += m => shown++;

		menu.Open( source, Popup.PositionMode.BelowLeft );
		var list = menu.ListPanel;
		menu.Open( source, Popup.PositionMode.BelowLeft );

		Assert.AreEqual( 1, shown );
		Assert.AreEqual( list, menu.ListPanel );
	}

	[TestMethod]
	public void RowsAddedWhileOpenJoinTheList()
	{
		var root = CreateRoot();
		var source = new Panel { Parent = root };
		var menu = new Menu( "File" );
		var first = menu.AddOption( "New" );

		menu.Open( source, Popup.PositionMode.BelowLeft );
		var late = menu.AddOption( "Late" );
		var widget = menu.AddWidget( new Panel() );

		Assert.AreEqual( menu.ListPanel, late.Parent );
		Assert.AreEqual( menu.ListPanel, widget.Parent );
		CollectionAssert.AreEqual( new Panel[] { first, late, widget }, menu.Rows.ToArray() );
	}

	[TestMethod]
	public void WidgetsAreRowsButNotOptions()
	{
		var root = CreateRoot();
		var source = new Panel { Parent = root };
		var menu = new Menu( "View" );
		var entry = menu.AddWidget( new TextEntry() );
		var option = menu.AddOption( "Reset" );

		CollectionAssert.AreEqual( new Panel[] { entry, option }, menu.Rows.ToArray() );
		CollectionAssert.AreEqual( new[] { option }, menu.Options.ToArray() );
		Assert.IsTrue( menu.HasOptions );

		menu.Open( source, Popup.PositionMode.BelowLeft );
		Assert.AreEqual( menu.ListPanel, entry.Parent );

		// The keyboard walks options only
		Key( menu, "down" );
		Assert.AreEqual( option, menu.Highlighted );

		menu.Close();
		Assert.IsNull( entry.Parent );
		Assert.IsFalse( entry.IsDeleting );
	}

	[TestMethod]
	public void AnyChildPanelBecomesAWidgetRow()
	{
		var menu = new Menu( "View" );
		var child = new Panel();

		menu.AddChild( child );

		CollectionAssert.AreEqual( new[] { child }, menu.Rows.ToArray() );
		Assert.AreEqual( 0, menu.Options.Count );
	}

	/// <summary>
	/// A row of a closed submenu is a child of that submenu's row until it opens. It mustn't show
	/// there - the row's own parts are the only things visible in it.
	/// </summary>
	[TestMethod]
	public void RowsWaitingInAClosedRowAreHidden()
	{
		var root = CreateRoot();
		var bar = new MenuBar { Parent = root };
		var menu = bar.AddMenu( "File" );
		var recent = menu.AddMenu( "Recent" );
		var inner = new Menu( "a.txt" );
		var widget = new Panel();
		recent.AddChild( inner );
		recent.AddChild( widget );

		// Through the bar, so the list is styled under a menu row - the heading
		Click( root, menu );
		root.Layout();
		root.Layout();

		Assert.AreEqual( recent, inner.Parent );
		Assert.AreEqual( DisplayMode.None, inner.ComputedStyle.Display );
		Assert.AreEqual( DisplayMode.None, widget.ComputedStyle.Display );
		Assert.AreNotEqual( DisplayMode.None, recent.Children.First().ComputedStyle.Display, "the row's own parts show" );
		Assert.AreNotEqual( DisplayMode.None, menu.ListPanel.ComputedStyle.Display, "the list shows, though it's styled under the heading row" );
		Assert.IsTrue( menu.ListPanel.Box.Rect.Height > 0, "the list has a size" );
	}

	[TestMethod]
	public void RemoveTakesARowOutWithoutDeletingIt()
	{
		var root = CreateRoot();
		var source = new Panel { Parent = root };
		var menu = new Menu( "File" );
		var a = menu.AddOption( "A" );
		var b = menu.AddOption( "B" );

		menu.Open( source, Popup.PositionMode.BelowLeft );
		menu.Remove( a );

		Assert.IsNull( a.Parent );
		Assert.IsNull( a.ParentMenu );
		Assert.IsFalse( a.IsDeleting );
		CollectionAssert.AreEqual( new[] { b }, menu.Options.ToArray() );

		menu.Remove( b );
		Assert.IsFalse( menu.HasOptions );
		Assert.IsFalse( menu.HasClass( "has-submenu" ) );
	}

	[TestMethod]
	public void ClearWhileOpenEmptiesTheList()
	{
		var root = CreateRoot();
		var source = new Panel { Parent = root };
		var menu = new Menu( "File" );
		var a = menu.AddOption( "A" );

		menu.Open( source, Popup.PositionMode.BelowLeft );
		menu.Clear();

		Assert.IsTrue( menu.IsOpen );
		Assert.AreEqual( 0, menu.Rows.Count );
		Assert.IsTrue( a.IsDeleting );
		Assert.AreEqual( 0, menu.ListPanel.ChildrenCount );
	}

	[TestMethod]
	public void MovingAnOptionBetweenMenusReparentsIt()
	{
		var a = new Menu( "A" );
		var b = new Menu( "B" );
		var option = a.AddOption( "X" );

		b.AddOption( option );

		Assert.AreEqual( 0, a.Options.Count );
		Assert.AreEqual( b, option.ParentMenu );
		Assert.IsFalse( a.HasOptions );
	}

	[TestMethod]
	public void DeletingAMenuClosesIt()
	{
		var root = CreateRoot();
		var source = new Panel { Parent = root };
		var menu = new Menu( "File" );
		var option = menu.AddOption( "New" );

		menu.Open( source, Popup.PositionMode.BelowLeft );
		menu.Delete( true );

		Assert.IsFalse( menu.IsOpen );
		Assert.IsNull( option.Parent );
	}

	[TestMethod]
	public void ClosingClearsTheHighlight()
	{
		var root = CreateRoot();
		var source = new Panel { Parent = root };
		var menu = new Menu( "File" );
		var a = menu.AddOption( "A" );

		menu.Open( source, Popup.PositionMode.BelowLeft );
		Key( menu, "down" );
		Assert.AreEqual( a, menu.Highlighted );

		menu.Close();

		Assert.IsNull( menu.Highlighted );
		Assert.IsFalse( a.HasClass( "active" ) );
	}

	[TestMethod]
	public void DisabledSubmenuDoesNotOpen()
	{
		var root = CreateRoot();
		var source = new Panel { Parent = root };
		var menu = new Menu( "File" );
		var recent = menu.AddMenu( "Recent" );
		recent.AddOption( "a.txt" );
		recent.Enabled = false;
		var other = menu.AddOption( "Other" );

		menu.Open( source, Popup.PositionMode.BelowLeft );

		Hover( root, recent );
		root.TickInternal();
		Assert.IsFalse( recent.IsOpen, "hover" );

		Click( root, recent );
		Assert.IsFalse( recent.IsOpen, "click" );
		Assert.IsTrue( menu.IsOpen );

		// The keyboard skips it altogether
		Key( menu, "down" );
		Assert.AreEqual( other, menu.Highlighted );
	}

	[TestMethod]
	public void EnterOnSubmenuRowOpensItAndHighlightsFirst()
	{
		var root = CreateRoot();
		var source = new Panel { Parent = root };
		var menu = new Menu( "File" );
		var recent = menu.AddMenu( "Recent" );
		var inner = recent.AddOption( "a.txt" );

		menu.Open( source, Popup.PositionMode.BelowLeft );
		Key( menu, "down" );
		Key( menu, "enter" );

		Assert.IsTrue( recent.IsOpen );
		Assert.AreEqual( inner, recent.Highlighted );
		Assert.IsTrue( menu.IsOpen );
	}

	[TestMethod]
	public void LetterJumpWrapsAndIgnoresDisabled()
	{
		var root = CreateRoot();
		var source = new Panel { Parent = root };
		var menu = new Menu( "File" );
		var save = menu.AddOption( "Save" );
		var off = menu.AddOption( "Skip" );
		off.Enabled = false;
		menu.AddOption( "Open" );

		menu.Open( source, Popup.PositionMode.BelowLeft );
		Key( menu, "s" );
		Assert.AreEqual( save, menu.Highlighted );

		// The only other S is disabled - wraps back round to Save
		Key( menu, "s" );
		Assert.AreEqual( save, menu.Highlighted );

		Key( menu, "x" );
		Assert.AreEqual( save, menu.Highlighted, "no match leaves it alone" );
	}

	[TestMethod]
	public void UnhandledKeysPassUpToTheRoot()
	{
		var root = CreateRoot();
		var source = new Panel { Parent = root };
		var menu = new Menu( "File" );
		menu.AddOption( "A" );

		menu.Open( source, Popup.PositionMode.BelowLeft );

		var e = new ButtonEvent( "f5", true, 0, default );
		menu.ListPanel.OnButtonTyped( e );
		Assert.IsFalse( e.StopPropagation );

		e = new ButtonEvent( "down", true, 0, default );
		menu.ListPanel.OnButtonTyped( e );
		Assert.IsTrue( e.StopPropagation );
	}

	[TestMethod]
	public void MenuBarEscapeClosesTheOpenMenu()
	{
		var root = CreateRoot();
		var bar = new MenuBar { Parent = root };
		var file = bar.AddMenu( "File" );
		file.AddOption( "New" );

		Click( root, file );
		Key( file, "escape" );

		Assert.IsFalse( file.IsOpen );
		Assert.IsNull( bar.OpenMenu );
	}

	[TestMethod]
	public void MenuBarCloseAll()
	{
		var root = CreateRoot();
		var bar = new MenuBar { Parent = root };
		var file = bar.AddMenu( "File" );
		file.AddOption( "New" );

		Click( root, file );
		bar.CloseAll();

		Assert.IsFalse( file.IsOpen );
		Assert.IsNull( bar.OpenMenu );
	}

	[TestMethod]
	public void MenuBarIgnoresOtherControlsInIt()
	{
		var root = CreateRoot();
		var bar = new MenuBar { Parent = root };
		var file = bar.AddMenu( "File" );
		file.AddOption( "New" );
		var search = bar.AddChild( new TextEntry() );
		var edit = bar.AddMenu( "Edit" );
		edit.AddOption( "Undo" );

		CollectionAssert.AreEqual( new[] { file, edit }, bar.Menus.ToArray() );

		Click( root, file );
		Hover( root, search );
		Assert.IsTrue( file.IsOpen, "hovering the text entry changes nothing" );

		Key( file, "right" );
		Assert.IsTrue( edit.IsOpen, "arrows skip the text entry" );
	}

	[TestMethod]
	public void MenuBarEmptyHeadingDoesNothingWhenClicked()
	{
		var root = CreateRoot();
		var bar = new MenuBar { Parent = root };
		var help = bar.AddMenu( "Help" );

		Click( root, help );

		Assert.IsFalse( help.IsOpen );
		Assert.IsNull( bar.OpenMenu );
	}

	[TestMethod]
	public void MenuBarRemovingTheOpenMenuClosesIt()
	{
		var root = CreateRoot();
		var bar = new MenuBar { Parent = root };
		var file = bar.AddMenu( "File" );
		file.AddOption( "New" );

		Click( root, file );
		file.Parent = null;

		Assert.IsFalse( file.IsOpen );
		Assert.IsNull( bar.OpenMenu );
	}

	/// <summary>
	/// A click on an open heading closes the menu on the way down and would open it again on the
	/// way up. The guard swallows that one reopen - the next click opens as usual.
	/// </summary>
	[TestMethod]
	public void ClickThatDismissedAMenuDoesNotReopenIt()
	{
		Menu.ReopenGuard = 0.3f;

		var root = CreateRoot();
		var bar = new MenuBar { Parent = root };
		var file = bar.AddMenu( "File" );
		file.AddOption( "New" );

		Click( root, file );
		Assert.IsTrue( file.IsOpen );

		// The mouse going down elsewhere - or on the heading - takes every popup down
		BasePopup.CloseAll();
		Assert.IsFalse( file.IsOpen );

		Click( root, file );
		Assert.IsFalse( file.IsOpen, "the same click doesn't reopen it" );

		Click( root, file );
		Assert.IsTrue( file.IsOpen, "the next one does" );

		// Closing it ourselves is not a dismissal
		file.Close();
		Click( root, file );
		Assert.IsTrue( file.IsOpen );
	}

	/// <summary>
	/// A list is styled under its heading, so it would inherit the heading's text styling - an
	/// uppercase, letter-spaced heading must not turn the rows and their icon names uppercase.
	/// </summary>
	[TestMethod]
	public void ListDoesNotInheritHeadingTextStyling()
	{
		var root = CreateRoot();
		var bar = new MenuBar { Parent = root };
		var file = bar.AddMenu( "File" );
		file.Style.TextTransform = TextTransform.Uppercase;
		file.Style.LetterSpacing = 2;
		var option = file.AddOption( "New", "note_add" );

		Click( root, file );
		root.Layout();
		root.Layout();

		Assert.AreEqual( TextTransform.None, file.ListPanel.ComputedStyle.TextTransform );
		Assert.AreEqual( 0, file.ListPanel.ComputedStyle.LetterSpacing?.Value );

		var icon = option.Children.First().Children.OfType<IconPanel>().Last();
		Assert.AreEqual( TextTransform.None, icon.ComputedStyle.TextTransform );
	}

	/// <summary>
	/// A heading in the bar shows its text and nothing else, however the menu sheet dresses a row.
	/// The bar's rules have to outweigh the menu's, which is a specificity tie by default.
	/// </summary>
	[TestMethod]
	public void MenuBarHeadingsHideRowParts()
	{
		var root = CreateRoot();
		var bar = new MenuBar { Parent = root };
		var file = bar.AddMenu( "File" );
		file.AddOption( "New" ).Shortcut = "Ctrl+N";
		var view = bar.AddMenu( "View" );
		var snapping = view.AddMenu( "Snapping" );
		snapping.AddOption( "Grid" );

		Click( root, view );
		root.Layout();
		root.Layout();

		var chevron = file.Children.Last();
		Assert.IsTrue( chevron.HasClass( "chevron" ) );
		Assert.AreEqual( DisplayMode.None, chevron.ComputedStyle.Display, "heading chevron" );
		Assert.AreEqual( DisplayMode.None, file.Children.First().ComputedStyle.Display, "heading gutter" );

		Assert.AreNotEqual( DisplayMode.None, snapping.Children.Last().ComputedStyle.Display, "a row with a submenu shows its chevron" );
	}
}

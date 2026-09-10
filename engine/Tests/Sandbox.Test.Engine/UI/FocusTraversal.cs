using Sandbox.UI;
using System.Collections.Generic;

namespace UITests;

/// <summary>
/// Moving focus with Tab and Shift+Tab, driven through a headless surface: the order things get
/// focused in, what gets skipped, and the keys that activate a focused control.
/// </summary>
[TestClass]
[DoNotParallelize] // Panels share global UI state
public class FocusTraversalTests
{
	UISurface surface;
	bool previousRenderText;

	[TestInitialize]
	public void Setup()
	{
		ThreadSafe.MarkMainThread();

		previousRenderText = TextBlock.ui_rendertext;
		TextBlock.ui_rendertext = false;

		surface = new UISurface();
		surface.Size = new Vector2( 800, 600 );
	}

	[TestCleanup]
	public void Teardown()
	{
		surface.Dispose();
		TextBlock.ui_rendertext = previousRenderText;
	}

	/// <summary>
	/// A panel that can take focus, on the root unless told otherwise.
	/// </summary>
	Panel Focusable( Panel parent = null, int tabIndex = 0 )
	{
		return new Panel { Parent = parent ?? surface.Root, AcceptsFocus = true, TabIndex = tabIndex };
	}

	/// <summary>
	/// Press and release a key. Two frames: one delivers the key, the next lands the focus change.
	/// </summary>
	void Press( string key, KeyboardModifiers modifiers = default )
	{
		surface.SetKey( key, true, 0, modifiers );
		surface.SetKey( key, false, 0, modifiers );
		Layout();
		Layout();
	}

	void Tab() => Press( "tab" );
	void ShiftTab() => Press( "tab", KeyboardModifiers.Shift );

	/// <summary>
	/// A surface frame up to and including layout, so visibility and boxes are known. Stops short
	/// of building render descriptors, which needs a GPU.
	/// </summary>
	void Layout()
	{
		var system = surface.System;
		system.TickPanels();
		system.TickSurfaceInput( false );
		system.PreLayout();
		system.Layout();
		system.PostLayout();
	}

	[TestMethod]
	public void TabWithNothingFocusedGoesToFirst()
	{
		var a = Focusable();
		Focusable();
		Layout();

		Tab();

		Assert.AreSame( a, surface.Focus );
	}

	[TestMethod]
	public void TabFollowsTreeOrder()
	{
		var a = Focusable();
		var group = new Panel { Parent = surface.Root };
		var b = Focusable( group );
		var c = Focusable( group );
		var d = Focusable();
		Layout();

		Tab();
		Assert.AreSame( a, surface.Focus );
		Tab();
		Assert.AreSame( b, surface.Focus );
		Tab();
		Assert.AreSame( c, surface.Focus );
		Tab();
		Assert.AreSame( d, surface.Focus );
	}

	[TestMethod]
	public void TabWrapsToFirst()
	{
		var a = Focusable();
		var b = Focusable();
		Layout();

		b.Focus();
		Layout();
		Tab();

		Assert.AreSame( a, surface.Focus );
	}

	[TestMethod]
	public void ShiftTabGoesBackwards()
	{
		var a = Focusable();
		var b = Focusable();
		Layout();

		b.Focus();
		Layout();
		ShiftTab();

		Assert.AreSame( a, surface.Focus );
	}

	[TestMethod]
	public void ShiftTabWithNothingFocusedGoesToLast()
	{
		Focusable();
		var b = Focusable();
		Layout();

		ShiftTab();

		Assert.AreSame( b, surface.Focus );
	}

	[TestMethod]
	public void SkipsPanelsThatDontAcceptFocus()
	{
		var a = Focusable();
		new Panel { Parent = surface.Root };
		var c = Focusable();
		Layout();

		a.Focus();
		Layout();
		Tab();

		Assert.AreSame( c, surface.Focus );
	}

	[TestMethod]
	public void SkipsHiddenPanels()
	{
		var a = Focusable();
		var hidden = Focusable();
		hidden.Style.Display = DisplayMode.None;
		var c = Focusable();
		Layout();

		a.Focus();
		Layout();
		Tab();

		Assert.AreSame( c, surface.Focus );
	}

	[TestMethod]
	public void SkipsChildrenOfHiddenPanels()
	{
		var a = Focusable();
		var hidden = new Panel { Parent = surface.Root };
		hidden.Style.Display = DisplayMode.None;
		Focusable( hidden );
		var c = Focusable();
		Layout();

		a.Focus();
		Layout();
		Tab();

		Assert.AreSame( c, surface.Focus );
	}

	[TestMethod]
	public void NegativeTabIndexIsSkipped()
	{
		var a = Focusable();
		Focusable( tabIndex: -1 );
		var c = Focusable();
		Layout();

		a.Focus();
		Layout();
		Tab();

		Assert.AreSame( c, surface.Focus );
	}

	[TestMethod]
	public void NegativeTabIndexCanStillBeFocusedDirectly()
	{
		var skipped = Focusable( tabIndex: -1 );
		Layout();

		skipped.Focus();
		Layout();

		Assert.AreSame( skipped, surface.Focus );
	}

	[TestMethod]
	public void TabFromSkippedPanelContinuesFromItsPlace()
	{
		Focusable();
		var skipped = Focusable( tabIndex: -1 );
		var c = Focusable();
		Layout();

		skipped.Focus();
		Layout();
		Tab();

		Assert.AreSame( c, surface.Focus );
	}

	[TestMethod]
	public void PositiveTabIndexComesFirstInAscendingOrder()
	{
		var plain = Focusable();
		var second = Focusable( tabIndex: 2 );
		var first = Focusable( tabIndex: 1 );
		Layout();

		Tab();
		Assert.AreSame( first, surface.Focus );
		Tab();
		Assert.AreSame( second, surface.Focus );
		Tab();
		Assert.AreSame( plain, surface.Focus );
	}

	[TestMethod]
	public void FocusNextAndPreviousMoveFromThePanel()
	{
		var a = Focusable();
		var b = Focusable();
		Layout();

		a.FocusNext();
		Layout();
		Assert.AreSame( b, surface.Focus );

		b.FocusPrevious();
		Layout();
		Assert.AreSame( a, surface.Focus );
	}

	[TestMethod]
	public void TabScrollsTheFocusedPanelIntoView()
	{
		var list = new Panel { Parent = surface.Root };
		list.Style.Set( "height: 100px; overflow-y: scroll; flex-direction: column;" );

		var items = new List<Panel>();
		for ( int i = 0; i < 10; i++ )
		{
			var item = Focusable( list );
			item.Style.Set( "height: 50px; flex-shrink: 0;" );
			items.Add( item );
		}

		Layout();
		items[0].Focus();
		Layout();

		for ( int i = 0; i < 5; i++ )
			Tab();

		Assert.AreSame( items[5], surface.Focus );
		Assert.IsTrue( list.ScrollOffset.y > 0, "list should have scrolled to show the focused item" );
	}

	[TestMethod]
	public void TabLeavesATextEntry()
	{
		var entry = new TextEntry { Parent = surface.Root };
		var b = Focusable();
		Layout();

		entry.Focus();
		Layout();
		Tab();

		Assert.AreSame( b, surface.Focus );
	}

	[TestMethod]
	public void EnterActivatesAFocusedButton()
	{
		var clicks = 0;
		var button = new Button { Parent = surface.Root };
		button.AddEventListener( "onclick", () => clicks++ );
		Layout();

		button.Focus();
		Layout();
		Press( "enter" );

		Assert.AreEqual( 1, clicks );
	}

	[TestMethod]
	public void SpaceActivatesAFocusedButton()
	{
		var clicks = 0;
		var button = new Button { Parent = surface.Root };
		button.AddEventListener( "onclick", () => clicks++ );
		Layout();

		button.Focus();
		Layout();
		Press( "space" );

		Assert.AreEqual( 1, clicks );
	}

	[TestMethod]
	public void TabReachesButtonsAndCheckboxes()
	{
		var button = new Button { Parent = surface.Root };
		var checkbox = new Checkbox { Parent = surface.Root };
		Layout();

		Tab();
		Assert.AreSame( button, surface.Focus );
		Tab();
		Assert.AreSame( checkbox, surface.Focus );
	}

	[TestMethod]
	public void SpaceTogglesAFocusedCheckbox()
	{
		var checkbox = new Checkbox { Parent = surface.Root };
		Layout();

		checkbox.Focus();
		Layout();
		Press( "space" );

		Assert.IsTrue( checkbox.Checked );
	}
}

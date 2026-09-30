using Sandbox.UI;

namespace Sandbox.PanelGallery;

/// <summary>
/// The menu system - a menu bar, a context menu and a button menu, all built from the same
/// Menu rows. Cascades, checks, icons, disabled rows, separators and shortcuts.
/// </summary>
public class MenusPage : GalleryPage
{
	readonly Sandbox.UI.Label _output;

	bool _dirty;
	bool _showGrid = true;
	bool _showGizmos = true;
	string _theme = "Dark";
	int _opened;

	public MenusPage() : base( "Menus", "Sandbox.UI.Menu and MenuBar. Click a heading, then slide along the bar. Hover a row with a chevron to cascade. Arrow keys, Enter, Escape and typing a letter all work while one is open." )
	{
		var row = Case( "Menu bar" );
		row.AddChild( BuildMenuBar() );

		row = Case( "Context menu - right click the box" );
		var area = row.Add.Panel( "menu-target" );
		area.Add.Label( "Right click me" );

		var context = BuildContextMenu();
		area.AddEventListener( "onrightclick", () => context.Open( area, Popup.PositionMode.UnderMouse ) );

		row = Case( "Button menu" );
		row.AddChild( MenuButton( "Options", BuildButtonMenu() ) );

		row = Case( "Awkward ones - a long list, an empty one, and one that fills itself late" );
		row.AddChild( MenuButton( "Sixty options", BuildLongMenu() ) );
		row.AddChild( MenuButton( "Nothing in it", new Sandbox.UI.Menu() ) );
		row.AddChild( MenuButton( "Empty until opened", BuildLateMenu() ) );

		row = Case( "Other controls inside a menu" );
		row.AddChild( MenuButton( "Filter", BuildWidgetMenu() ) );

		row = Case( "A menu bar with other things in it" );
		row.AddChild( BuildBusyMenuBar() );

		row = Case( "Restyled - a class on the bar restyles it and every menu it opens" );
		var neon = BuildMenuBar();
		neon.AddClass( "neon" );
		row.AddChild( neon );

		row = Case( "Restyled again - Workbench 1.3" );
		var amiga = BuildMenuBar();
		amiga.AddClass( "amiga" );
		row.AddChild( amiga );

		_output = Output();
	}

	/// <summary>
	/// A flat button that drops the menu under itself.
	/// </summary>
	Sandbox.UI.Button MenuButton( string text, Sandbox.UI.Menu menu )
	{
		Sandbox.UI.Button button = null;
		button = new Sandbox.UI.Button( text, "expand_more", "flatbutton", () => menu.Open( button, Popup.PositionMode.BelowLeft, 4 ) );
		return button;
	}

	Sandbox.UI.Menu BuildLongMenu()
	{
		var menu = new Sandbox.UI.Menu();

		for ( int i = 1; i <= 60; i++ )
		{
			var n = i;
			menu.AddOption( $"Option {n}", () => Report( $"Option {n}" ) );
			if ( n % 10 == 0 ) menu.AddSeparator();
		}

		return menu;
	}

	Sandbox.UI.Menu BuildLateMenu()
	{
		var menu = new Sandbox.UI.Menu();
		int opened = 0;

		menu.AboutToShow += m =>
		{
			m.Clear();
			opened++;
			for ( int i = 1; i <= opened; i++ )
			{
				var n = i;
				m.AddOption( $"Added on open {n}", () => Report( $"Late {n}" ) );
			}
		};

		return menu;
	}

	Sandbox.UI.Menu BuildWidgetMenu()
	{
		var menu = new Sandbox.UI.Menu();

		var search = menu.AddWidget( new TextEntry { Placeholder = "Search..." } );
		search.AddClass( "menu-widget" );
		menu.AddSeparator();
		menu.AddOption( "Meshes", on => Report( $"Meshes {on}" ) ).Checked = true;
		menu.AddOption( "Materials", on => Report( $"Materials {on}" ) ).Checked = true;
		menu.AddOption( "Sounds", on => Report( $"Sounds {on}" ) );
		menu.AddSeparator();

		var slider = menu.AddWidget( new SliderControl { Min = 0, Max = 100, Value = 50 } );
		slider.AddClass( "menu-widget" );
		menu.AddSeparator();
		menu.AddOption( "Reset Filters", "restart_alt", () => Report( "Reset Filters" ) );

		return menu;
	}

	Sandbox.UI.MenuBar BuildBusyMenuBar()
	{
		var bar = new Sandbox.UI.MenuBar();
		bar.AddClass( "busy" );

		var file = bar.AddMenu( "File" );
		file.AddOption( "New", "note_add", () => Report( "New" ) );
		file.AddOption( "Open...", "folder_open", () => Report( "Open" ) );

		var edit = bar.AddMenu( "Edit" );
		edit.AddOption( "Undo", "undo", () => Report( "Undo" ) );
		edit.AddOption( "Redo", "redo", () => Report( "Redo" ) );

		bar.AddChild( new Sandbox.UI.Button( null, "play_arrow", "iconbutton", () => Report( "Play" ) ) );
		bar.AddChild( new Sandbox.UI.Button( null, "stop", "iconbutton", () => Report( "Stop" ) ) );

		var search = bar.AddChild( new TextEntry { Placeholder = "Search the scene" } );
		search.AddClass( "bar-search" );

		var help = bar.AddMenu( "Help" );
		help.AddOption( "About", "info", () => Report( "About" ) );

		return bar;
	}

	Sandbox.UI.MenuBar BuildMenuBar()
	{
		var bar = new Sandbox.UI.MenuBar();

		var file = bar.AddMenu( "File" );
		file.AddOption( "New", "note_add", () => Report( "New" ) ).Shortcut = "Ctrl+N";
		file.AddOption( "Open...", "folder_open", () => Report( "Open" ) ).Shortcut = "Ctrl+O";

		var recent = file.AddMenu( "Open Recent", "history" );
		recent.AboutToShow += m =>
		{
			m.Clear();
			_opened++;

			for ( int i = 1; i <= 3; i++ )
			{
				var name = $"scene_{_opened}_{i}.scene";
				m.AddOption( name, "description", () => Report( $"Open {name}" ) );
			}

			m.AddSeparator();
			m.AddOption( "Clear Recent", "delete", () => Report( "Clear Recent" ) );
		};

		file.AddSeparator();

		var save = file.AddOption( "Save", "save", () => { _dirty = false; Report( "Save" ); } );
		save.Shortcut = "Ctrl+S";
		file.AddOption( "Save As...", () => Report( "Save As" ) ).Shortcut = "Ctrl+Shift+S";
		file.AboutToShow += m => save.Enabled = _dirty;

		file.AddSeparator();
		file.AddOption( "Exit", "logout", () => Report( "Exit" ) ).Shortcut = "Alt+F4";

		var edit = bar.AddMenu( "Edit" );
		edit.AddOption( "Undo", "undo", () => Report( "Undo" ) ).Shortcut = "Ctrl+Z";
		edit.AddOption( "Redo", "redo", () => Report( "Redo" ) ).Shortcut = "Ctrl+Y";
		edit.AddSeparator();
		edit.AddOption( "Cut", "content_cut", () => Report( "Cut" ) ).Shortcut = "Ctrl+X";
		edit.AddOption( "Copy", "content_copy", () => Report( "Copy" ) ).Shortcut = "Ctrl+C";
		edit.AddOption( "Paste", "content_paste", () => Report( "Paste" ) ).Shortcut = "Ctrl+V";
		edit.AddSeparator();
		edit.AddOption( "Make Dirty", on => { _dirty = on; Report( $"Dirty {on}" ); } );

		var preferences = edit.AddMenu( "Preferences", "settings" );
		var theme = preferences.AddMenu( "Theme", "palette" );
		foreach ( var name in new[] { "Dark", "Light", "System" } )
		{
			var current = name;
			var option = theme.AddOption( current, on => { _theme = current; TickTheme( theme ); Report( $"Theme {current}" ); } );
			theme.AboutToShow += m => option.Checked = _theme == current;
		}

		var font = preferences.AddMenu( "Font Size" );
		font.AddOption( "Small", () => Report( "Small" ) );
		font.AddOption( "Medium", () => Report( "Medium" ) );
		font.AddOption( "Large", () => Report( "Large" ) );
		preferences.AddSeparator();
		preferences.AddOption( "Reset to Defaults", "restart_alt", () => Report( "Reset" ) );

		var view = bar.AddMenu( "View" );
		view.AddOption( "Show Grid", on => { _showGrid = on; Report( $"Grid {on}" ); } ).Checked = _showGrid;
		view.AddOption( "Show Gizmos", on => { _showGizmos = on; Report( $"Gizmos {on}" ); } ).Checked = _showGizmos;
		view.AddSeparator();

		var snapping = view.AddMenu( "Snapping", "grid_on" );
		snapping.AddOption( "Grid", on => Report( $"Snap grid {on}" ) ).Checked = true;
		snapping.AddOption( "Angle", on => Report( $"Snap angle {on}" ) );
		snapping.AddOption( "Vertex", on => Report( $"Snap vertex {on}" ) ).Enabled = false;

		view.AddSeparator();
		view.AddOption( "Zoom In", "zoom_in", () => Report( "Zoom In" ) ).Shortcut = "Ctrl++";
		view.AddOption( "Zoom Out", "zoom_out", () => Report( "Zoom Out" ) ).Shortcut = "Ctrl+-";
		view.AddOption( "Reset Zoom", () => Report( "Reset Zoom" ) ).Shortcut = "Ctrl+0";

		var help = bar.AddMenu( "Help" );
		help.AddOption( "Documentation", "menu_book", () => Report( "Docs" ) ).Shortcut = "F1";
		help.AddOption( "About", "info", () => Report( "About" ) );

		return bar;
	}

	Sandbox.UI.Menu BuildContextMenu()
	{
		var menu = new Sandbox.UI.Menu();

		menu.AddOption( "Cut", "content_cut", () => Report( "Cut" ) ).Shortcut = "Ctrl+X";
		menu.AddOption( "Copy", "content_copy", () => Report( "Copy" ) ).Shortcut = "Ctrl+C";
		menu.AddOption( "Paste", "content_paste", () => Report( "Paste" ) ).Shortcut = "Ctrl+V";
		menu.AddSeparator();

		var create = menu.AddMenu( "Create", "add" );
		create.AddOption( "Empty", "check_box_outline_blank", () => Report( "Create Empty" ) );
		create.AddOption( "Camera", "videocam", () => Report( "Create Camera" ) );
		create.AddOption( "Light", "light_mode", () => Report( "Create Light" ) );

		var primitives = create.AddMenu( "Primitives", "category" );
		primitives.AddOption( "Cube", () => Report( "Cube" ) );
		primitives.AddOption( "Sphere", () => Report( "Sphere" ) );
		primitives.AddOption( "Cylinder", () => Report( "Cylinder" ) );

		menu.AddSeparator();
		menu.AddOption( "Rename", "edit", () => Report( "Rename" ) ).Shortcut = "F2";
		menu.AddOption( "Delete", "delete", () => Report( "Delete" ) ).Shortcut = "Del";

		return menu;
	}

	Sandbox.UI.Menu BuildButtonMenu()
	{
		var menu = new Sandbox.UI.Menu();

		menu.AddOption( "Refresh", "refresh", () => Report( "Refresh" ) );
		menu.AddOption( "Sort by Name", on => Report( $"Sort by name {on}" ) ).Checked = true;
		menu.AddOption( "Show Hidden", on => Report( $"Show hidden {on}" ) );
		menu.AddSeparator();
		menu.AddOption( "Unavailable", "block", () => Report( "??" ) ).Enabled = false;

		return menu;
	}

	/// <summary>
	/// The theme rows are a radio group: one ticked, the rest not.
	/// </summary>
	void TickTheme( Sandbox.UI.Menu theme )
	{
		foreach ( var option in theme.Options )
		{
			option.Checked = option.Text == _theme;
		}
	}

	void Report( string what )
	{
		_output.Text = what;
	}
}

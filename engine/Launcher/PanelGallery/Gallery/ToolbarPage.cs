using Sandbox.UI;
using Button = Sandbox.UI.Button;
using Label = Sandbox.UI.Label;
using Option = Sandbox.UI.Option;
using TextEntry = Sandbox.UI.TextEntry;

namespace Sandbox.PanelGallery;

/// <summary>
/// Command strips using the engine toolbar, including constrained space and embedded inputs.
/// </summary>
public class ToolbarPage : GalleryPage
{
	readonly Label _output;
	int _actions;

	public ToolbarPage() : base( "Toolbar", "Commands, toggles, menus and embedded controls. Tab to a button, then use arrow keys to move along the strip. Enter or Space activates it." )
	{
		var row = Case( "Document commands", column: true );
		var document = row.AddChild( new Toolbar() );
		document.Style.Width = Length.Percent( 100 );
		document.AddButton( "New", "note_add", () => Report( "New document" ) ).Tooltip = "New document";
		document.AddButton( "Open", "folder_open", () => Report( "Open document" ) );
		document.AddButton( "Save", "save", () => Report( "Saved document" ) );
		document.AddSeparator();
		document.AddButton( "", "undo", () => Report( "Undo" ) ).Tooltip = "Undo";
		document.AddButton( "", "redo", () => Report( "Redo" ) ).Disabled = true;
		document.AddSpacer();
		var menu = new Sandbox.UI.Menu();
		menu.AddOption( "Export image", () => Report( "Export image" ) );
		menu.AddOption( "Export selection", () => Report( "Export selection" ) );
		menu.AddSeparator();
		menu.AddOption( "Document settings", () => Report( "Document settings" ) );
		document.AddMenu( "More", "more_horiz", menu );

		row = Case( "Viewport tools · exclusive selection and independent toggles", column: true );
		var viewport = row.AddChild( new Toolbar() );
		viewport.Style.Width = Length.Percent( 100 );
		AddTools( viewport );
		viewport.AddSeparator();
		viewport.AddToggle( "Grid", "grid_on", true, value => Report( $"Grid {(value ? "on" : "off")}" ) );
		viewport.AddToggle( "Snap", "grid_4x4", false, value => Report( $"Snapping {(value ? "on" : "off")}" ) );
		viewport.AddSpacer();
		viewport.AddButton( "Play", "play_arrow", () => Report( "Play" ) );

		row = Case( "Embedded controls", column: true );
		var search = row.AddChild( new Toolbar() );
		search.Style.Width = Length.Percent( 100 );
		search.AddButton( "", "refresh", () => Report( "Refreshed" ) ).Tooltip = "Refresh";
		var entry = search.AddChild( new TextEntry { Placeholder = "Search assets…" } );
		entry.Style.Width = 220;
		entry.Style.FlexShrink = 1;
		entry.Style.MinWidth = 80;
		entry.OnTextEdited = value => Report( $"Search: {value}" );
		search.AddSeparator();
		var filter = search.AddChild( new DropDown() );
		filter.Options.Add( new Option( "All assets", "all" ) );
		filter.Options.Add( new Option( "Models", "models" ) );
		filter.Options.Add( new Option( "Materials", "materials" ) );
		filter.Value = "all";
		filter.ValueChanged = value => Report( $"Filter: {value}" );

		row = Case( "Vertical tool strip" );
		var vertical = row.AddChild( new Toolbar { Vertical = true } );
		vertical.Style.Height = 205;
		AddTools( vertical );
		vertical.AddSeparator();
		vertical.AddSpacer();
		vertical.AddToggle( "", "visibility", true, value => Report( $"Visibility {(value ? "on" : "off")}" ) ).Tooltip = "Show overlays";
		var note = row.AddChild( new Label( "The same control, arranged vertically.\nHover icons for tooltips; use Up and Down to navigate." ) );
		note.Style.MarginLeft = 20;

		row = Case( "Limited width · scroll to reach every command" );
		var narrow = row.AddChild( new Toolbar() );
		narrow.Style.Width = 320;
		foreach ( var name in new[] { "Select", "Move", "Rotate", "Scale", "Duplicate", "Group", "Align", "Frame selection" } )
			narrow.AddButton( name, null, () => Report( name ) );

		_output = Output();
		_output.Text = "Ready — try a command, toggle or menu.";
	}

	void AddTools( Toolbar toolbar )
	{
		var buttons = new List<Button>();
		foreach ( var (name, icon) in new[] { ("Select", "near_me"), ("Move", "open_with"), ("Rotate", "rotate_right"), ("Scale", "aspect_ratio") } )
		{
			Button button = null;
			button = toolbar.AddButton( "", icon, () =>
			{
				foreach ( var item in buttons ) item.Active = item == button;
				Report( $"{name} tool" );
			} );
			button.Tooltip = name;
			buttons.Add( button );
		}
		buttons[0].Active = true;
	}

	void Report( string message ) => _output.Text = $"{++_actions} · {message}";
}

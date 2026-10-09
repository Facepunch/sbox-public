namespace Sandbox.PanelGallery;

/// <summary>
/// Keyboard focus traversal - Tab and Shift+Tab walk the controls, Enter and Space activate
/// the focused one. The output says which control has focus.
/// </summary>
public class FocusPage : GalleryPage
{
	readonly Sandbox.UI.Label _output;
	readonly Sandbox.UI.Label _focused;
	readonly Queue<string> _events = new();
	readonly Dictionary<Panel, string> _names = new();
	Panel _lastFocused;

	public FocusPage() : base( "Focus", "Tab and Shift+Tab move between the controls in tree order. Enter or Space activates a focused button or checkbox." )
	{
		var row = Case( "Tab order" );
		Track( row.AddChild( new Sandbox.UI.TextEntry { Placeholder = "First" } ), "first entry" );
		Track( row.AddChild( new Sandbox.UI.Checkbox { LabelText = "Second" } ), "checkbox" );
		Track( row.AddChild( new Sandbox.UI.Button( "Third", null, "primarybutton", () => Report( "clicked Third" ) ) ), "Third" );
		Track( row.AddChild( new Sandbox.UI.Button( "Fourth", null, "flatbutton", () => Report( "clicked Fourth" ) ) ), "Fourth" );

		row = Case( "Tab index - lands on 1, 2, 3 before the plain ones" );
		Track( row.AddChild( new Sandbox.UI.Button( "Plain", null, "flatbutton", () => Report( "clicked Plain" ) ) ), "Plain" );
		Track( row.AddChild( new Sandbox.UI.Button( "3", null, "flatbutton", () => Report( "clicked 3" ) ) { TabIndex = 3 } ), "3" );
		Track( row.AddChild( new Sandbox.UI.Button( "1", null, "flatbutton", () => Report( "clicked 1" ) ) { TabIndex = 1 } ), "1" );
		Track( row.AddChild( new Sandbox.UI.Button( "2", null, "flatbutton", () => Report( "clicked 2" ) ) { TabIndex = 2 } ), "2" );

		row = Case( "Skipped by Tab, still clickable" );
		Track( row.AddChild( new Sandbox.UI.Button( "Skipped", null, "flatbutton", () => Report( "clicked Skipped" ) ) { TabIndex = -1 } ), "Skipped" );

		_focused = Add.Label( "Focused control: none", "focus-current" );
		_output = Output();
		_output.AddClass( "event-log" );
		Report( "Ready. Press Tab to start." );
	}

	void Track( Panel panel, string name )
	{
		_names[panel] = name;
		panel.AddEventListener( "onblur", () => Report( $"blur: {name}" ) );
	}

	void Report( string text )
	{
		_events.Enqueue( text );
		while ( _events.Count > 10 ) _events.Dequeue();
		if ( _output is not null ) _output.Text = string.Join( "\n", _events );
	}

	public override void Tick()
	{
		base.Tick();
		var focused = _names.Keys.FirstOrDefault( x => x.HasFocus );
		if ( focused == _lastFocused ) return;
		_lastFocused = focused;
		string name = focused is null ? "none" : _names[focused];
		_focused.Text = $"Focused control: {name}";
		Report( $"focus: {name}" );
	}
}

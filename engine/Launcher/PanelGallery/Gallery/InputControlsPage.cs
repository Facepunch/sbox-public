namespace Sandbox.PanelGallery;

/// <summary>
/// The controls that edit a value - numbers, vectors, enums, switches and dropdowns. The
/// property driven ones are bound to a real object, so typing in them has somewhere to land.
/// </summary>
public class InputControlsPage : GalleryPage
{
	readonly Sandbox.UI.Label _output;
	readonly GalleryTarget _target = new();

	public InputControlsPage() : base( "Value Controls", "The controls that edit a value. Numbers, vectors and enums build themselves from a property; switches and dropdowns work either way." )
	{
		// NumberEntry is a TextEntry underneath, so it inherits all of its caret and selection
		// behaviour along with the numeric filtering
		var row = Case( "Number entry" );
		row.AddChild( new Sandbox.UI.NumberEntry { Property = _target.Property( "Scale" ) } );
		row.AddChild( new Sandbox.UI.NumberEntry { Property = _target.Property( "Count" ) } );

		row = Case( "Vector 2" );
		row.AddChild( new Sandbox.UI.VectorControl { Property = _target.Property( "Offset" ) } );

		row = Case( "Vector 3" );
		row.AddChild( new Sandbox.UI.VectorControl { Property = _target.Property( "Position" ) } );

		row = Case( "Vector 4" );
		row.AddChild( new Sandbox.UI.VectorControl { Property = _target.Property( "Tint" ) } );

		// Four options or fewer becomes a button group, more becomes a dropdown
		row = Case( "Enum, as buttons" );
		row.AddChild( new Sandbox.UI.EnumControl { Property = _target.Property( "Direction" ) } );

		row = Case( "Enum, as a dropdown" );
		row.AddChild( new Sandbox.UI.EnumControl { Property = _target.Property( "Detail" ) } );

		row = Case( "Switch" );
		var boundSwitch = new Sandbox.UI.SwitchControl { Property = _target.Property( "Enabled" ), Label = "Bound to a property" };
		row.AddChild( boundSwitch );

		var looseSwitch = new Sandbox.UI.SwitchControl { Label = "On its own" };
		looseSwitch.OnValueChanged = x => _output.Text = $"switch: {x}";
		row.AddChild( looseSwitch );

		row = Case( "Dropdown" );
		var dropdown = new Sandbox.UI.DropDown();
		dropdown.Options.Add( new Sandbox.UI.Option( "First", "one" ) );
		dropdown.Options.Add( new Sandbox.UI.Option( "Second", "two" ) );
		dropdown.Options.Add( new Sandbox.UI.Option( "Third", "three" ) );
		dropdown.Value = "two";
		dropdown.ValueChanged = x => _output.Text = $"dropdown: {x}";
		row.AddChild( dropdown );

		// Icons come through the option too
		var iconDropdown = new Sandbox.UI.DropDown();
		iconDropdown.Options.Add( new Sandbox.UI.Option( "Play", "play_arrow", "play" ) );
		iconDropdown.Options.Add( new Sandbox.UI.Option( "Pause", "pause", "pause" ) );
		iconDropdown.Options.Add( new Sandbox.UI.Option( "Stop", "stop", "stop" ) );
		iconDropdown.Value = "play";
		row.AddChild( iconDropdown );

		_output = Output();
	}
}

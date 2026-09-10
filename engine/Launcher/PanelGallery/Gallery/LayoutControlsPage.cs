namespace Sandbox.PanelGallery;

/// <summary>
/// The controls that arrange other controls - button groups, forms and fields. These own their
/// children's layout, so a styling break shows up as things sitting in the wrong place.
/// </summary>
public class LayoutControlsPage : GalleryPage
{
	readonly Sandbox.UI.Label _output;
	readonly GalleryTarget _target = new();

	public LayoutControlsPage() : base( "Grouping", "Sandbox.UI.ButtonGroup, Form and Field. The controls that lay other controls out." )
	{
		// One of these, exclusively - the active button stays pressed
		var row = Case( "Button group" );
		var group = new Sandbox.UI.ButtonGroup();
		group.Options =
		[
			new Sandbox.UI.Option( "Left", "left" ),
			new Sandbox.UI.Option( "Middle", "middle" ),
			new Sandbox.UI.Option( "Right", "right" ),
		];
		group.Value = "middle";
		group.ValueChanged = x => _output.Text = $"group: {x}";
		row.AddChild( group );

		row = Case( "Button group, icons" );
		var iconGroup = new Sandbox.UI.ButtonGroup();
		iconGroup.Options =
		[
			new Sandbox.UI.Option( "Align left", "format_align_left", "left" ),
			new Sandbox.UI.Option( "Align centre", "format_align_center", "center" ),
			new Sandbox.UI.Option( "Align right", "format_align_right", "right" ),
		];
		iconGroup.Value = "center";
		row.AddChild( iconGroup );

		// A form is titled rows of controls, which is what most editor panels are made of
		row = Case( "Form", column: true );
		var form = new Sandbox.UI.Form();
		form.AddHeader( "Transform", "open_with" );
		form.AddRow( "Position", new Sandbox.UI.VectorControl { Property = _target.Property( "Position" ) } );
		form.AddRow( "Scale", new Sandbox.UI.NumberEntry { Property = _target.Property( "Scale" ) } );
		form.AddHeader( "Appearance", "palette" );
		form.AddRow( "Colour", new Sandbox.UI.ColorControl { Property = _target.Property( "Colour" ) } );
		form.AddRow( "Detail", new Sandbox.UI.EnumControl { Property = _target.Property( "Detail" ) } );
		form.AddRow( "Enabled", new Sandbox.UI.SwitchControl { Property = _target.Property( "Enabled" ) } );
		row.AddChild( form );

		// The whole editor for an object, built from its properties without being told what they are
		row = Case( "Control sheet", column: true );
		row.AddChild( new Sandbox.UI.ControlSheet { Target = _target } );

		_output = Output();
	}
}

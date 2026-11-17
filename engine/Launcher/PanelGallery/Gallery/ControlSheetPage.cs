namespace Sandbox.PanelGallery;

/// <summary>
/// Every control on one page - a control sheet built from a property of each editable type, the
/// Form that lays controls out by hand, then only the controls neither makes - so a styling pass
/// can be judged in one place.
/// </summary>
public class ControlSheetPage : GalleryPage
{
	readonly Sandbox.UI.Label _output;
	readonly ControlSheetTarget _target = new();

	public ControlSheetPage() : base( "Control Sheet", "Every control. The sheet builds a row for each property type the UI can edit, the form places the same controls by hand, and below are the controls neither makes." )
	{
		var row = Case( "Control sheet", column: true );
		row.AddChild( new Sandbox.UI.ControlSheet { Target = _target } );

		// A form is titled rows of controls placed by hand - its label column lines up with the sheet's
		row = Case( "Form", column: true );
		var form = new Sandbox.UI.Form();
		form.AddHeader( "Transform", "open_with" );
		form.AddRow( "Position", new Sandbox.UI.VectorControl { Property = _target.Property( nameof( ControlSheetTarget.Position ) ) } );
		form.AddRow( "Scale", new Sandbox.UI.NumberEntry { Property = _target.Property( nameof( ControlSheetTarget.Scale ) ) } );
		form.AddHeader( "Appearance", "palette" );
		form.AddRow( "Colour", new Sandbox.UI.ColorControl { Property = _target.Property( nameof( ControlSheetTarget.Colour ) ) } );
		form.AddRow( "Shading", new Sandbox.UI.EnumControl { Property = _target.Property( nameof( ControlSheetTarget.Shading ) ) } );
		form.AddRow( "Enabled", new Sandbox.UI.SwitchControl { Property = _target.Property( nameof( ControlSheetTarget.Enabled ) ) } );
		row.AddChild( form );

		row = Case( "Buttons" );
		row.AddChild( new Sandbox.UI.Button( "Primary", "add_box", "primarybutton", () => Clicked( "primary" ) ) );
		row.AddChild( new Sandbox.UI.Button( "Flat", "folder_open", "flatbutton", () => Clicked( "flat" ) ) );
		row.AddChild( new Sandbox.UI.Button( null, "settings", "iconbutton", () => Clicked( "icon" ) ) );
		row.AddChild( new Sandbox.UI.Button( "Disabled", "block", "primarybutton", null ) { Disabled = true } );
		row.AddChild( new Sandbox.UI.Button( "Disabled", null, "flatbutton", null ) { Disabled = true } );

		row = Case( "Button group, icons only" );
		var iconGroup = new Sandbox.UI.ButtonGroup();
		iconGroup.Options =
		[
			new Sandbox.UI.Option( null, "format_align_left", "left" ),
			new Sandbox.UI.Option( null, "format_align_center", "center" ),
			new Sandbox.UI.Option( null, "format_align_right", "right" ),
		];
		iconGroup.Value = "center";
		iconGroup.ValueChanged = x => _output.Text = $"align: {x}";
		row.AddChild( iconGroup );

		row = Case( "Text entry, placeholder and disabled" );
		var text = new Sandbox.UI.TextEntry { Placeholder = "Type here" };
		text.OnTextEdited = x => _output.Text = $"text: {x}";
		row.AddChild( text );
		row.AddChild( new Sandbox.UI.TextEntry { Text = "Disabled", Disabled = true } );

		row = Case( "Checkbox" );
		var check = new Sandbox.UI.Checkbox { LabelText = "Checkbox" };
		check.ValueChanged = x => _output.Text = $"checkbox: {x}";
		row.AddChild( check );
		row.AddChild( new Sandbox.UI.Checkbox { LabelText = "Checked", Checked = true } );

		_output = Output();
	}

	void Clicked( string which ) => _output.Text = $"clicked: {which}";
}

/// <summary>
/// One property of every type the control sheet has an editor for, grouped the way components group them.
/// </summary>
public class ControlSheetTarget
{
	public enum BlendMode
	{
		[Icon( "circle" )] Opaque,
		[Icon( "radio_button_unchecked" )] Masked,
		[Icon( "blur_on" )] Translucent
	}

	// More than four options, so the enum control makes it a dropdown
	public enum ShadingModel
	{
		[Icon( "view_in_ar" )] Surface,
		[Icon( "flashlight_off" )] Unlit,
		[Icon( "layers" )] Decal,
		[Icon( "cloud" )] Sky,
		[Icon( "auto_fix_high" )] PostProcess
	}

	public string Name { get; set; } = "Kitchen sink";

	[Description( "Hover the label - the description shows in its tooltip." )]
	public bool Enabled { get; set; } = true;

	[Group( "Numbers" )] public int Count { get; set; } = 4;
	[Group( "Numbers" )] public float Scale { get; set; } = 1.0f;
	[Group( "Numbers" )] public double Precise { get; set; } = 0.125;
	[Group( "Numbers" ), Range( 0, 1 )] public float Opacity { get; set; } = 0.75f;
	[Group( "Numbers" ), Range( 0, 360 )] public float Angle { get; set; } = 90;

	[Group( "Vectors" )] public Vector2 Offset { get; set; } = new( 10, 20 );
	[Group( "Vectors" )] public Vector3 Position { get; set; } = new( 1, 2, 3 );
	[Group( "Vectors" )] public Vector4 Tint { get; set; } = new( 1, 1, 1, 1 );

	[Group( "Choices" )] public ShadingModel Shading { get; set; } = ShadingModel.Surface;
	[Group( "Choices" )] public BlendMode Blend { get; set; } = BlendMode.Opaque;

	[Group( "Colours" )] public Color Colour { get; set; } = Color.Orange;
	[Group( "Colours" ), ColorUsage( hasAlpha: false )] public Color Opaque { get; set; } = Color.Cyan;

	[Group( "Curves" )] public Curve Response { get; set; } = Curve.Ease;
	[Group( "Curves" )] public CurveRange ResponseRange { get; set; } = new( Curve.EaseIn, Curve.EaseOut );

	[ToggleGroup( "Shadows" )] public bool Shadows { get; set; } = true;
	[Group( "Shadows" )] public float ShadowBias { get; set; } = 0.01f;

	[Group( "Folded", StartFolded = true ), TextArea] public string Notes { get; set; } = "This group starts folded.";

	/// <summary>
	/// A property of this object, for binding a control to by hand.
	/// </summary>
	public SerializedProperty Property( string name ) => Game.TypeLibrary.GetSerializedObject( this ).GetProperty( name );
}

namespace Sandbox.PanelGallery;

/// <summary>
/// The colour controls - the small swatch that opens a picker, and the picker itself with its
/// saturation square, hue strip and alpha strip. All of them are drag targets, so this page is
/// where dragging inside a control gets eyeballed.
/// </summary>
public class ColorControlsPage : GalleryPage
{
	readonly Sandbox.UI.Label _output;
	readonly GalleryTarget _target = new();

	public ColorControlsPage() : base( "Colour", "Sandbox.UI.ColorControl and the picker it opens. Drag inside the square and the strips - the swatch follows." )
	{
		// The swatch. Clicking it opens the picker in a popup
		var row = Case( "Swatch" );
		row.AddChild( new Sandbox.UI.ColorControl { Property = _target.Property( "Colour" ) } );

		// The whole picker, inline rather than in its popup
		row = Case( "Picker" );
		row.AddChild( new Sandbox.UI.ColorPickerControl { Property = _target.Property( "Colour" ) } );

		// The pieces the picker is made of, so a break in one is obvious
		row = Case( "Saturation and value" );
		row.AddChild( new Sandbox.UI.ColorSaturationValueControl { Property = _target.Property( "Colour" ) } );

		row = Case( "Hue" );
		row.AddChild( new Sandbox.UI.ColorHueControl { Property = _target.Property( "Colour" ) } );

		row = Case( "Alpha" );
		row.AddChild( new Sandbox.UI.ColorAlphaControl { Property = _target.Property( "Colour" ) } );

		_output = Output();
		_output.Text = "Drag in any of them - they all edit the same colour.";
	}
}

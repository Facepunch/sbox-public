namespace Sandbox.PanelGallery;

/// <summary>
/// The colour controls - the swatch that opens a picker, and the picker itself. The picker's
/// square, strips and wheels are all drag targets, so this page is where dragging inside a
/// control gets eyeballed, along with the layouts behind its menu.
/// </summary>
public class ColorControlsPage : GalleryPage
{
	readonly Sandbox.UI.Label _output;
	readonly GalleryTarget _target = new();

	public ColorControlsPage() : base( "Colour", "Sandbox.UI.ColorControl and the picker it opens. Drag inside the square and the strips - the swatch follows. The menu in the picker's corner swaps the square for a ring, a disc or sliders." )
	{
		// The swatch. Clicking it opens the picker in a popup
		var row = Case( "Swatch" );
		row.AddChild( new Sandbox.UI.ColorControl { Property = _target.Property( "Colour" ) } );

		// The whole picker, inline rather than in its popup
		row = Case( "Picker" );
		row.AddChild( new Sandbox.UI.ColorPickerControl { Property = _target.Property( "Colour" ) } );

		// The other layouts the menu offers, side by side
		row = Case( "Hue ring, hue disc" );
		row.AddChild( new Sandbox.UI.ColorPickerControl { Property = _target.Property( "Colour" ), Layout = Sandbox.UI.ColorPickerControl.PickerLayout.HueRing } );
		row.AddChild( new Sandbox.UI.ColorPickerControl { Property = _target.Property( "Colour" ), Layout = Sandbox.UI.ColorPickerControl.PickerLayout.HueDisc } );

		row = Case( "HSV sliders, RGB sliders" );
		row.AddChild( new Sandbox.UI.ColorPickerControl { Property = _target.Property( "Colour" ), Layout = Sandbox.UI.ColorPickerControl.PickerLayout.HsvSliders } );
		row.AddChild( new Sandbox.UI.ColorPickerControl { Property = _target.Property( "Colour" ), Layout = Sandbox.UI.ColorPickerControl.PickerLayout.RgbSliders } );

		// What a property without alpha or without HDR gets
		row = Case( "No alpha, no HDR" );
		row.AddChild( new Sandbox.UI.ColorPickerControl { Property = _target.Property( "Opaque" ) } );
		row.AddChild( new Sandbox.UI.ColorPickerControl { Property = _target.Property( "Sdr" ) } );

		_output = Output();
		_output.Text = "Drag in the swatch's picker or the inline one - they edit the same colour.";
	}
}

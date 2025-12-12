namespace Sandbox.UI;

/// <summary>
/// A control for picking a color using sliders and whatever
/// </summary>
public partial class ColorPickerControl : BaseControl
{
	readonly ColorSaturationValueControl _svControl;
	readonly ColorHueControl _hueControl;
	readonly ColorAlphaControl _alphaControl;

	public ColorPickerControl()
	{
		_svControl = AddChild<ColorSaturationValueControl>( "sv" );
		_hueControl = AddChild<ColorHueControl>( "hue" );
		_alphaControl = AddChild<ColorAlphaControl>( "alpha" );
	}

	public override void Rebuild()
	{
		_svControl.Property = Property;
		_hueControl.Property = Property;
		_alphaControl.Property = Property;
	}

	public override void Tick()
	{
		base.Tick();
	}
}

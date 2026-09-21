namespace Sandbox.UI;

/// <summary>
/// A control for editing Color properties. A swatch that opens the picker, and a text entry that
/// takes the colour in any form - hex, rgb(), a name, with "* N" for HDR brightness.
/// </summary>
[CustomEditor( typeof( Color ) )]
[StyleSheet.Inline( "colorcontrol", Styles )]
public partial class ColorControl : BaseControl
{
	const string Styles = """
		.colorcontrol
		{
			flex-direction: row;
			align-items: center;
			gap: 6px;
			flex-grow: 1;
			height: 28px;
			padding: 2px;
			border-radius: 4px;
			background-color: #0000004d;
			pointer-events: all;

			> .colorswatch
			{
				height: 100%;
				width: 34px;
				cursor: pointer;
			}

			> .colortextentry
			{
				flex-grow: 1;
				min-width: 0px;
				background-color: transparent;
			}
		}
		""";

	readonly ColorSwatch _swatch;
	readonly ColorTextEntry _text;

	bool _hasAlpha = true;
	bool _isHdr = true;
	PickerColor _color = PickerColor.FromColor( Color.White );
	Color _applied;

	public override bool SupportsMultiEdit => true;

	public ColorControl()
	{
		AddClass( "colorcontrol" );

		_swatch = AddChild( new ColorSwatch() );
		_swatch.AddEventListener( "onmousedown", OpenPopup );

		_text = AddChild( new ColorTextEntry() );
		_text.ColorEntered = text =>
		{
			if ( !PickerColor.TryParse( text, _color.Hsv.Hue, out var color ) ) return;

			_color = color;
			Apply();
		};
		_text.Blurred = Sync;
	}

	public override void Rebuild()
	{
		var usage = Property?.GetAttributes<ColorUsageAttribute>().FirstOrDefault();
		_hasAlpha = usage?.HasAlpha ?? true;
		_isHdr = usage?.IsHDR ?? true;

		if ( Property is null ) return;

		_applied = Property.GetValue<Color>();
		_color = PickerColor.FromColor( _applied, _color.Hsv.Hue );
		Sync();
	}

	public override void Tick()
	{
		base.Tick();

		if ( Property is null ) return;

		var current = Property.GetValue<Color>();
		if ( current == _applied ) return;

		_applied = current;
		_color = PickerColor.FromColor( current, _color.Hsv.Hue );
		Sync();
	}

	void Apply()
	{
		if ( !_hasAlpha ) _color.Hsv.Alpha = 1.0f;
		if ( !_isHdr ) _color.Brightness = 1.0f;

		_applied = _color.ToColor();
		Property?.SetValue( _applied );
		Sync();
	}

	void Sync()
	{
		_swatch.Set( _color, _hasAlpha, _isHdr );
		_text.Value = _color.ToText();
	}

	void OpenPopup()
	{
		_text.Blur();
		var popup = new Popup( _swatch, Popup.PositionMode.BelowLeft, 4 );

		var picker = popup.AddChild<ColorPickerControl>();
		picker.HasAlpha = _hasAlpha;
		picker.IsHdr = _isHdr;
		picker.Property = Property;
	}
}

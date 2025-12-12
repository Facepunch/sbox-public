namespace Sandbox.UI;

/// <summary>
/// A control for editing Color properties. Displays a text entry that can be edited, and a color swatch which pops up a mixer.
/// </summary>
[CustomEditor( typeof( Color ) )]
public partial class ColorControl : BaseControl
{
	readonly TextEntry _textEntry;
	readonly Panel _colorSwatch;

	public ColorControl()
	{
		_colorSwatch = AddChild<Panel>( "colorswatch" );
		_colorSwatch.AddEventListener( "onmousedown", OpenPopup );

		_textEntry = AddChild<TextEntry>( "textentry" );
		_textEntry.OnTextEdited = OnTextEntryChanged;
	}

	public override void Rebuild()
	{
		if ( Property == null ) return;

		_textEntry.Value = Property.GetValue<Color>().Hex;
	}

	public override void Tick()
	{
		base.Tick();

		_colorSwatch.Style.BackgroundColor = Property.GetValue<Color>();
	}

	void OnTextEntryChanged( string value )
	{
		Property.SetValue( value );
	}

	void OpenPopup()
	{
		var popup = new Popup( _colorSwatch, Popup.PositionMode.BelowLeft, 0 );

		var picker = popup.AddChild<ColorPickerControl>();
		picker.Property = Property;
	}
}

namespace Sandbox.UI;

/// <summary>
/// A control for editing Color properties. Displays a text entry that can be edited, and a color swatch which pops up a mixer.
/// </summary>
[CustomEditor( typeof( Color ) )]
public partial class ColorControl : BaseControl
{
	readonly TextEntry _textEntry;
	readonly Panel _colorSwatch;

    private Color _color
    {
        get;
        set
        {
            if (value == field) return;
            field = value;
            OnColorChanged?.Invoke(field);
        }
    }
    [Parameter] public Action<Color> OnColorChanged { get; set; }

	public override bool SupportsMultiEdit => true;

	public ColorControl()
	{
		_colorSwatch = AddChild<Panel>( "colorswatch" );
		_colorSwatch.AddEventListener( "onmousedown", OpenPopup );

		_textEntry = AddChild<TextEntry>( "textentry" );
		_textEntry.OnTextEdited = OnTextEntryChanged;

        SerializedObject serobject = TypeLibrary.GetSerializedObject(this);
        SerializedProperty prop = serobject.GetProperty("_color");
		if (prop != null) Property = prop;
	}

	public override void Rebuild()
	{
		if ( Property == null ) return;

		_textEntry.Value = Property.GetValue<Color>().Hex;
	}

	public override void Tick()
	{
		base.Tick();

		if (Property == null) return;
		_colorSwatch.Style.BackgroundColor = Property.GetValue<Color>();
	}

	void OnTextEntryChanged( string value )
	{
		if (Property == null) return;
		Property.SetValue( value );
	}

	void OpenPopup()
	{
		if (Property == null) return;
		var popup = new Popup( _colorSwatch, Popup.PositionMode.BelowLeft, 0 );

		var picker = popup.AddChild<ColorPickerControl>();
		picker.Property = Property;
	}
}

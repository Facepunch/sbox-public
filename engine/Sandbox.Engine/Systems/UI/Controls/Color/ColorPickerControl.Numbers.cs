using Sandbox.UI.Construct;

namespace Sandbox.UI;

//
// The number fields under the picker - hex, RGB or HSV, chosen by the tabs in the header - and
// the text entry at the bottom that takes a colour in any form.
//
public partial class ColorPickerControl
{
	const string NumbersStyles = """
		.colorpickercontrol
		{
			> .numbers
			{
				flex-direction: row;
				gap: 6px;

				> .fields
				{
					flex-direction: row;
					flex-grow: 1;
					gap: 6px;

					> numberentry
					{
						flex-grow: 1;
						flex-basis: 0px;
						min-width: 0px;

						.prefix-label
						{
							margin-right: 5px;
							opacity: 0.6;
						}
					}
				}

				> .colortextentry
				{
					flex-grow: 1;
				}
			}
		}
		""";

	enum NumberMode
	{
		Hex,
		Rgb,
		Hsv
	}

	ButtonGroup _tabs;
	ColorTextEntry _hexEntry;
	ColorTextEntry _textEntry;

	Panel _rgbFields;
	Panel _hsvFields;
	NumberEntry _red;
	NumberEntry _green;
	NumberEntry _blue;
	NumberEntry _alpha255;
	NumberEntry _hue;
	NumberEntry _saturation;
	NumberEntry _value;
	NumberEntry _alpha100;

	void BuildTabs( Panel header )
	{
		_tabs = header.AddChild( new ButtonGroup() );
		_tabs.AddClass( "tabs" );

		AddTab( "HEX", NumberMode.Hex );
		AddTab( "RGB", NumberMode.Rgb );
		AddTab( "HSV", NumberMode.Hsv );

		_tabs.Value = "HEX";
	}

	void AddTab( string text, NumberMode mode )
	{
		var button = _tabs.AddButton( text, () => SetNumberMode( mode ) );
		button.Value = text;
	}

	void BuildNumbers()
	{
		var numbers = Add.Panel( "numbers" );

		_hexEntry = numbers.AddChild( new ColorTextEntry() );
		WireTextEntry( _hexEntry );

		_rgbFields = numbers.Add.Panel( "fields" );
		_red = Channel( _rgbFields, "R", 255, v => SetRgb( r: v / 255.0f ) );
		_green = Channel( _rgbFields, "G", 255, v => SetRgb( g: v / 255.0f ) );
		_blue = Channel( _rgbFields, "B", 255, v => SetRgb( b: v / 255.0f ) );
		_alpha255 = Channel( _rgbFields, "A", 255, v => SetAlpha( v / 255.0f ) );

		_hsvFields = numbers.Add.Panel( "fields" );
		_hue = Channel( _hsvFields, "H", 360, SetHue );
		_saturation = Channel( _hsvFields, "S", 100, v => SetSaturationValue( v / 100.0f, _color.Hsv.Value ) );
		_value = Channel( _hsvFields, "V", 100, v => SetValue( v / 100.0f ) );
		_alpha100 = Channel( _hsvFields, "A", 100, v => SetAlpha( v / 100.0f ) );

		SetNumberMode( NumberMode.Hex );
	}

	void BuildTextRow()
	{
		var row = Add.Panel( "text-row" );
		_textEntry = row.AddChild( new ColorTextEntry() );
		_textEntry.Placeholder = "#ff8800, rgba( 255, 136, 0, 0.5 ), white * 4 ...";
		WireTextEntry( _textEntry );
	}

	/// <summary>
	/// A whole number from 0 to <paramref name="max"/> with a letter in front, that scrubs like the editor's.
	/// </summary>
	NumberEntry Channel( Panel parent, string prefix, float max, Action<float> set )
	{
		var entry = parent.AddChild( new NumberEntry() );
		entry.Prefix = prefix;
		entry.WholeNumbers = true;
		entry.MinValue = 0;
		entry.MaxValue = max;
		entry.OnTextEdited = text => { if ( TryParseNumber( text, out var v ) ) set( Math.Clamp( v, 0, max ) ); };
		entry.AddEventListener( "onblur", OnTextBlurred );
		entry.ScrubEnded += Commit;
		return entry;
	}

	void WireTextEntry( ColorTextEntry entry )
	{
		entry.ColorEntered = text => { if ( PickerColor.TryParse( text, _color.Hsv.Hue, out var color ) ) SetColor( color ); };
		entry.Blurred = OnTextBlurred;
	}

	void OnTextBlurred()
	{
		Commit();
		Sync();
	}

	void SetNumberMode( NumberMode mode )
	{
		Show( _hexEntry, mode == NumberMode.Hex );
		Show( _rgbFields, mode == NumberMode.Rgb );
		Show( _hsvFields, mode == NumberMode.Hsv );
	}

	void ShowAlphaNumbers( bool show )
	{
		Show( _alpha255, show );
		Show( _alpha100, show );
	}

	void SyncNumbers()
	{
		var color = _color.BaseColor;

		_red.Value = Whole( color.r * 255.0f );
		_green.Value = Whole( color.g * 255.0f );
		_blue.Value = Whole( color.b * 255.0f );
		_alpha255.Value = Whole( _color.Hsv.Alpha * 255.0f );

		_hue.Value = Whole( _color.Hsv.Hue );
		_saturation.Value = Whole( _color.Hsv.Saturation * 100.0f );
		_value.Value = Whole( _color.Hsv.Value * 100.0f );
		_alpha100.Value = Whole( _color.Hsv.Alpha * 100.0f );
	}

	static string Whole( float value ) => MathF.Round( value ).ToString( System.Globalization.CultureInfo.InvariantCulture );

	void SyncText()
	{
		var text = _color.ToText();
		_hexEntry.Value = text;
		_textEntry.Value = text;
	}
}

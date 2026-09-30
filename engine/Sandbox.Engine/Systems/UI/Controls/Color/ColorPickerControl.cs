using System.Globalization;
using Sandbox.UI.Construct;

namespace Sandbox.UI;

/// <summary>
/// A control for picking a colour. A square, hue ring, disc or set of sliders picks the colour
/// itself, with alpha and HDR brightness beside it, number fields, a palette and a text entry
/// that takes any form of colour. Which picker shows is up to the user, from the menu in the corner.
/// </summary>
[StyleSheet.Inline( "colorpickercontrol", Styles + NumbersStyles )]
public partial class ColorPickerControl : BaseControl
{
	/// <summary>
	/// Which control picks the colour.
	/// </summary>
	internal enum PickerLayout
	{
		/// <summary>
		/// A saturation and value square with hue up one side and alpha up the other.
		/// </summary>
		ColorSquare,

		/// <summary>
		/// A hue ring with the saturation and value square inside it.
		/// </summary>
		HueRing,

		/// <summary>
		/// A hue and saturation disc, with a value strip beside it.
		/// </summary>
		HueDisc,

		/// <summary>
		/// A strip for each of hue, saturation, value and alpha.
		/// </summary>
		HsvSliders,

		/// <summary>
		/// A strip for each of red, green, blue and alpha.
		/// </summary>
		RgbSliders
	}

	const string LayoutCookie = "colorpicker.layout";

	/// <summary>
	/// How far the brightness slider goes, in doublings - ×256 at the end.
	/// </summary>
	const int MaxStops = 8;

	const string Styles = """
		.colorpickercontrol
		{
			flex-direction: column;
			flex-shrink: 0;
			gap: 10px;
			width: 300px;
			padding: 12px;
			pointer-events: all;

			> .header
			{
				flex-direction: row;
				align-items: center;
				gap: 8px;

				> .compare
				{
					flex-direction: row;
					flex-grow: 1;
					height: 26px;
					border-radius: 5px;
					overflow: hidden;

					> .colorswatch
					{
						flex-grow: 1;
						border-radius: 0;
					}

					> .old
					{
						cursor: pointer;
					}
				}

				> .tabs
				{
					flex-shrink: 0;
					background-color: #0c0d0f;
					border: 0px;
					border-radius: 5px;
					padding: 2px;
					gap: 2px;

					> .button
					{
						padding: 2px 8px;
						font-size: 11px;
						border-radius: 4px;
						background-color: transparent;
						opacity: 0.6;

						&.active
						{
							opacity: 1;
							background-color: #ffffff18;
						}
					}
				}

				> .layout-button
				{
					flex-shrink: 0;
					width: 26px;
					height: 26px;
					padding: 0;
					justify-content: center;
				}
			}

			> .body
			{
				flex-direction: row;
				align-items: stretch;
				gap: 8px;
			}

			> .slider-rows
			{
				flex-direction: column;
				gap: 8px;
			}

			.slider-row
			{
				flex-direction: row;
				align-items: center;
				gap: 8px;

				> .label
				{
					width: 44px;
					flex-shrink: 0;
					font-size: 11px;
				}

				> .entry
				{
					width: 62px;
					flex-shrink: 0;
					flex-grow: 0;

					> numberentry
					{
						flex-grow: 1;
						min-width: 0px;
					}
				}

				> .slidercontrol
				{
					flex-grow: 1;
					flex-shrink: 1;
					flex-basis: 0px;
					min-width: 0px;
				}
			}

			> .section
			{
				flex-direction: column;
				gap: 5px;

				> .title
				{
					font-size: 10px;
					letter-spacing: 1px;
					text-transform: uppercase;
					opacity: 0.7;
				}

				> .swatches
				{
					flex-direction: row;
					flex-wrap: wrap;
					gap: 4px;
					min-height: 18px;

					> .colorswatch
					{
						width: 20px;
						height: 18px;
						cursor: pointer;
						border: 1px solid #0006;
					}

					> .add
					{
						width: 20px;
						height: 18px;
						border: 1px dashed #ffffff30;
						border-radius: 3px;
						font-size: 14px;
						align-items: center;
						justify-content: center;
						cursor: pointer;
						opacity: 0.6;

						&:hover
						{
							opacity: 1;
						}
					}
				}
			}

			> .text-row > .colortextentry
			{
				flex-grow: 1;
			}
		}
		""";

	bool _defaultHasAlpha = true;
	bool _defaultIsHdr = true;
	ColorUsageAttribute _usage;
	bool _built;

	// What the picker shows, what it opened with, and what it last wrote to the property
	PickerColor _color = PickerColor.FromColor( Color.White );
	PickerColor _original;
	Color _applied;

	PickerLayout _layout = PickerLayout.ColorSquare;

	readonly ColorSwatch _oldSwatch;
	readonly ColorSwatch _newSwatch;
	readonly Menu _layoutMenu;
	readonly List<(Menu option, PickerLayout layout)> _layoutOptions = new();

	readonly Panel _body;
	readonly ColorStrip _hueSide;
	readonly ColorStrip _valueSide;
	readonly ColorStrip _alphaSide;
	readonly ColorSquare _square;
	readonly HueRing _ring;
	readonly HueDisc _disc;

	// The slider layouts - a row per axis, then alpha under whichever is showing
	readonly Panel _hsvRows;
	readonly Panel _rgbRows;
	readonly Panel _alphaRow;
	readonly ColorStrip[] _hueStrips;
	readonly ColorStrip[] _saturationStrips;
	readonly ColorStrip[] _valueStrips;
	readonly ColorStrip[] _alphaStrips;
	readonly ColorStrip _redStrip;
	readonly ColorStrip _greenStrip;
	readonly ColorStrip _blueStrip;
	readonly NumberEntry _hueRowEntry;
	readonly NumberEntry _saturationRowEntry;
	readonly NumberEntry _valueRowEntry;
	readonly NumberEntry _alphaRowEntry;
	readonly NumberEntry _redRowEntry;
	readonly NumberEntry _greenRowEntry;
	readonly NumberEntry _blueRowEntry;

	readonly Panel _brightnessRow;
	readonly SliderControl _brightness;
	readonly NumberEntry _brightnessEntry;

	/// <summary>
	/// Whether alpha can be picked. A <see cref="ColorUsageAttribute"/> on the property overrides this setting.
	/// </summary>
	internal bool HasAlpha
	{
		get => _usage?.HasAlpha ?? _defaultHasAlpha;
		set
		{
			_defaultHasAlpha = value;
			ApplyLayout();
			Sync();
		}
	}

	/// <summary>
	/// Whether brightness above 1 can be picked. A <see cref="ColorUsageAttribute"/> on the property overrides this setting.
	/// </summary>
	internal bool IsHdr
	{
		get => _usage?.IsHDR ?? _defaultIsHdr;
		set
		{
			_defaultIsHdr = value;
			ApplyLayout();
			Sync();
		}
	}

	/// <summary>
	/// Which control picks the colour. Choosing from the menu remembers it between sessions.
	/// </summary>
	internal PickerLayout Layout
	{
		get => _layout;
		set
		{
			_layout = value;
			ApplyLayout();
		}
	}

	public override bool SupportsMultiEdit => true;

	public ColorPickerControl()
	{
		AddClass( "colorpickercontrol" );

		//
		// Header - before and after, the number tabs, the layout menu
		//
		var header = Add.Panel( "header" );

		var compare = header.Add.Panel( "compare" );
		_oldSwatch = compare.AddChild( new ColorSwatch() );
		_oldSwatch.AddClass( "old" );
		_oldSwatch.AddEventListener( "onclick", () => PickColor( _original ) );
		_newSwatch = compare.AddChild( new ColorSwatch() );

		BuildTabs( header );

		Button layoutButton = null;
		layoutButton = header.AddChild( new Button( "", "more_vert", "layout-button", () => _layoutMenu.Open( layoutButton, Popup.PositionMode.BelowRight, 4 ) ) );

		_layoutMenu = new Menu();
		AddLayoutOption( "Color Square", PickerLayout.ColorSquare );
		AddLayoutOption( "Hue Ring", PickerLayout.HueRing );
		AddLayoutOption( "Hue Disc", PickerLayout.HueDisc );
		AddLayoutOption( "HSV Sliders", PickerLayout.HsvSliders );
		AddLayoutOption( "RGB Sliders", PickerLayout.RgbSliders );
		_layoutMenu.AboutToShow += _ =>
		{
			foreach ( var (option, layout) in _layoutOptions )
				option.Checked = layout == _layout;
		};

		//
		// Body - the square, ring or disc with strips up the sides
		//
		_body = Add.Panel( "body" );

		_hueSide = _body.AddChild( new ColorStrip( true ) );
		_hueSide.ValueChanged = v => SetHue( v * 360.0f );

		_square = _body.AddChild( new ColorSquare() );
		_square.Changed = SetSaturationValue;

		_ring = _body.AddChild( new HueRing() );
		_ring.HueChanged = SetHue;
		_ring.Square.Changed = SetSaturationValue;

		_disc = _body.AddChild( new HueDisc() );
		_disc.Changed = SetHueSaturation;

		_valueSide = _body.AddChild( new ColorStrip( true ) );
		_valueSide.ValueChanged = SetValue;

		_alphaSide = _body.AddChild( new ColorStrip( true, transparent: true ) );
		_alphaSide.ValueChanged = SetAlpha;

		//
		// Slider layouts
		//
		_hsvRows = Add.Panel( "slider-rows" );
		SliderRow( _hsvRows, "Hue", out var hueStrip, out _hueRowEntry, 360, "°", SetHue );
		hueStrip.ValueChanged = v => SetHue( v * 360.0f );
		SliderRow( _hsvRows, "Sat", out var saturationStrip, out _saturationRowEntry, 100, "%", v => SetSaturationValue( v / 100.0f, _color.Hsv.Value ) );
		saturationStrip.ValueChanged = v => SetSaturationValue( v, _color.Hsv.Value );
		SliderRow( _hsvRows, "Value", out var valueStrip, out _valueRowEntry, 100, "%", v => SetValue( v / 100.0f ) );
		valueStrip.ValueChanged = SetValue;

		_rgbRows = Add.Panel( "slider-rows" );
		SliderRow( _rgbRows, "Red", out _redStrip, out _redRowEntry, 255, "", v => SetRgb( r: v / 255.0f ) );
		_redStrip.ValueChanged = v => SetRgb( r: v );
		SliderRow( _rgbRows, "Green", out _greenStrip, out _greenRowEntry, 255, "", v => SetRgb( g: v / 255.0f ) );
		_greenStrip.ValueChanged = v => SetRgb( g: v );
		SliderRow( _rgbRows, "Blue", out _blueStrip, out _blueRowEntry, 255, "", v => SetRgb( b: v / 255.0f ) );
		_blueStrip.ValueChanged = v => SetRgb( b: v );

		_alphaRow = SliderRow( this, "Alpha", out var alphaStrip, out _alphaRowEntry, 100, "%", v => SetAlpha( v / 100.0f ), transparent: true );
		alphaStrip.ValueChanged = SetAlpha;

		_hueStrips = [_hueSide, hueStrip];
		_saturationStrips = [saturationStrip];
		_valueStrips = [_valueSide, valueStrip];
		_alphaStrips = [_alphaSide, alphaStrip];

		var hueStops = Enumerable.Range( 0, 7 ).Select( i => new ColorHsv( i * 60.0f, 1.0f, 1.0f ).ToColor() ).ToArray();
		foreach ( var strip in _hueStrips ) strip.SetGradient( hueStops );

		foreach ( var panel in new ColorDragPanel[] { _hueSide, _square, _ring, _ring.Square, _disc, _valueSide, _alphaSide, hueStrip, saturationStrip, valueStrip, alphaStrip, _redStrip, _greenStrip, _blueStrip } )
			panel.DragEnded = Commit;

		//
		// Brightness - always underneath, it's exposure rather than a colour axis. The slider
		// works in doublings so each tick is a stop, and the number beside it shows the multiplier
		//
		_brightnessRow = Add.Panel( "slider-row" );
		_brightnessRow.Add.Label( "Bright", "label" );
		_brightness = _brightnessRow.AddChild( new SliderControl( 0, MaxStops, 0.01f ) { TickStep = 1, ShowValueTooltip = false } );
		_brightness.OnValueChanged = stops =>
		{
			UISystem.CurrentFocus?.Blur();
			SetBrightness( MathF.Pow( 2, stops ) );
		};
		_brightness.AddEventListener( "onmouseup", Commit );
		_brightnessEntry = _brightnessRow.Add.Panel( "entry" ).AddChild( new NumberEntry() );
		_brightnessEntry.Prefix = "×";
		_brightnessEntry.NumberFormat = "0.##";
		_brightnessEntry.MinValue = 1;
		_brightnessEntry.MaxValue = MathF.Pow( 2, MaxStops );
		_brightnessEntry.OnTextEdited = text => { if ( TryParseNumber( text, out var v ) ) SetBrightness( v ); };
		_brightnessEntry.AddEventListener( "onblur", OnTextBlurred );
		_brightnessEntry.ScrubEnded += Commit;

		BuildNumbers();
		BuildPalette();
		BuildTextRow();

		if ( Enum.TryParse<PickerLayout>( Game.Cookies?.GetString( LayoutCookie ), out var saved ) )
			_layout = saved;

		_built = true;
		ApplyLayout();
		Sync();
	}

	void AddLayoutOption( string text, PickerLayout layout )
	{
		// A checkable row toggles rather than clicks. These are radio choices, so picking one
		// always selects it - the ticks get put right the next time the menu opens
		var option = _layoutMenu.AddOption( text, ( bool _ ) =>
		{
			Layout = layout;
			Game.Cookies?.SetString( LayoutCookie, layout.ToString() );
		} );
		option.Checkable = true;
		option.StaysOpen = false;
		_layoutOptions.Add( (option, layout) );
	}

	/// <summary>
	/// A label, a strip and a number, for the slider layouts.
	/// </summary>
	Panel SliderRow( Panel parent, string label, out ColorStrip strip, out NumberEntry entry, float max, string suffix, Action<float> onNumber, bool transparent = false )
	{
		var row = parent.Add.Panel( "slider-row" );
		row.Add.Label( label, "label" );

		strip = row.AddChild( new ColorStrip( transparent: transparent ) );

		// A text entry grows to fill its row, so it sits in a box of its own and the strip gets the room
		entry = Channel( row.Add.Panel( "entry" ), "", max, onNumber );
		entry.Suffix = suffix;

		return row;
	}

	static bool TryParseNumber( string text, out float value )
	{
		return float.TryParse( text, NumberStyles.Float, CultureInfo.InvariantCulture, out value );
	}

	public override void Rebuild()
	{
		_usage = Property?.GetAttributes<ColorUsageAttribute>().FirstOrDefault();
		ApplyLayout();

		if ( Property is null ) return;

		_applied = Property.GetValue<Color>();
		_color = PickerColor.FromColor( _applied, _color.Hsv.Hue );
		_original = _color;

		Sync();
	}

	public override void Tick()
	{
		base.Tick();

		if ( Property is null ) return;

		// Changed from outside - undo, another control on the same property
		var current = Property.GetValue<Color>();
		if ( current == _applied ) return;

		_applied = current;
		_color = PickerColor.FromColor( current, _color.Hsv.Hue );
		Sync();
	}

	//
	// Edits from the controls
	//

	void SetHue( float hue )
	{
		_color.Hsv.Hue = MathF.Min( hue, 359.999f );
		Apply();
	}

	void SetSaturationValue( float saturation, float value )
	{
		_color.Hsv.Saturation = saturation;
		_color.Hsv.Value = value;
		Apply();
	}

	void SetHueSaturation( float hue, float saturation )
	{
		_color.Hsv.Hue = hue;
		_color.Hsv.Saturation = saturation;
		Apply();
	}

	void SetValue( float value )
	{
		_color.Hsv.Value = value;
		Apply();
	}

	void SetAlpha( float alpha )
	{
		_color.Hsv.Alpha = alpha;
		Apply();
	}

	void SetBrightness( float brightness )
	{
		_color.Brightness = Math.Clamp( brightness, 1.0f, MathF.Pow( 2, MaxStops ) );
		Apply();
	}

	/// <summary>
	/// Change one RGB channel, keeping the brightness the colour had.
	/// </summary>
	void SetRgb( float? r = null, float? g = null, float? b = null )
	{
		var color = _color.BaseColor;
		if ( r.HasValue ) color.r = r.Value;
		if ( g.HasValue ) color.g = g.Value;
		if ( b.HasValue ) color.b = b.Value;

		var brightness = _color.Brightness;
		_color = PickerColor.FromColor( color, _color.Hsv.Hue );
		_color.Brightness = brightness;

		Apply();
	}

	/// <summary>
	/// Apply a complete colour.
	/// </summary>
	void SetColor( PickerColor color )
	{
		_color = color;
		Apply();
	}

	/// <summary>
	/// A swatch takes over from text editing. Blur after the click, so committing the text
	/// doesn't rebuild the recent row while the clicked swatch is still being pressed.
	/// </summary>
	void PickColor( PickerColor color )
	{
		UISystem.CurrentFocus?.Blur();
		SetColor( color );
		Commit();
	}

	/// <summary>
	/// Write the colour to the property and show it everywhere.
	/// </summary>
	void Apply()
	{
		if ( !HasAlpha ) _color.Hsv.Alpha = 1.0f;
		if ( !IsHdr ) _color.Brightness = 1.0f;

		_applied = _color.ToColor();
		Property?.SetValue( _applied );

		Sync();
	}

	/// <summary>
	/// Show the colour on every control.
	/// </summary>
	void Sync()
	{
		if ( !_built ) return;

		var color = _color;
		var pureHue = new ColorHsv( color.Hsv.Hue, 1.0f, 1.0f ).ToColor();
		var opaque = color.BaseColor.WithAlpha( 1.0f );

		_oldSwatch.Set( _original, HasAlpha, IsHdr );
		_newSwatch.Set( color, HasAlpha, IsHdr );

		_square.Set( color );
		_ring.Set( color );
		_disc.Set( color );

		foreach ( var strip in _hueStrips )
		{
			strip.Value = color.Hsv.Hue / 360.0f;
			strip.SetHandleColor( pureHue );
		}

		foreach ( var strip in _saturationStrips )
		{
			strip.SetGradient( new ColorHsv( color.Hsv.Hue, 0.0f, color.Hsv.Value ).ToColor(), new ColorHsv( color.Hsv.Hue, 1.0f, color.Hsv.Value ).ToColor() );
			strip.Value = color.Hsv.Saturation;
			strip.SetHandleColor( opaque );
		}

		foreach ( var strip in _valueStrips )
		{
			strip.SetGradient( Color.Black, new ColorHsv( color.Hsv.Hue, color.Hsv.Saturation, 1.0f ).ToColor() );
			strip.Value = color.Hsv.Value;
			strip.SetHandleColor( opaque );
		}

		foreach ( var strip in _alphaStrips )
		{
			strip.SetGradient( opaque.WithAlpha( 0.0f ), opaque );
			strip.Value = color.Hsv.Alpha;
			strip.SetHandleColor( color.BaseColor );
		}

		// Each RGB strip runs its own channel from 0 to 1 with the other two as they are
		_redStrip.SetGradient( opaque with { r = 0.0f }, opaque with { r = 1.0f } );
		_redStrip.Value = opaque.r;
		_redStrip.SetHandleColor( opaque );
		_greenStrip.SetGradient( opaque with { g = 0.0f }, opaque with { g = 1.0f } );
		_greenStrip.Value = opaque.g;
		_greenStrip.SetHandleColor( opaque );
		_blueStrip.SetGradient( opaque with { b = 0.0f }, opaque with { b = 1.0f } );
		_blueStrip.Value = opaque.b;
		_blueStrip.SetHandleColor( opaque );

		_brightness.Value = MathF.Log2( color.Brightness );
		_brightnessEntry.Value = color.Brightness.ToString( "0.##", CultureInfo.InvariantCulture );

		_hueRowEntry.Value = Whole( color.Hsv.Hue );
		_saturationRowEntry.Value = Whole( color.Hsv.Saturation * 100.0f );
		_valueRowEntry.Value = Whole( color.Hsv.Value * 100.0f );
		_alphaRowEntry.Value = Whole( color.Hsv.Alpha * 100.0f );
		_redRowEntry.Value = Whole( opaque.r * 255.0f );
		_greenRowEntry.Value = Whole( opaque.g * 255.0f );
		_blueRowEntry.Value = Whole( opaque.b * 255.0f );

		SyncNumbers();
		SyncText();
	}

	/// <summary>
	/// Show and hide the parts the layout and the property's usage call for.
	/// </summary>
	void ApplyLayout()
	{
		if ( !_built ) return;

		var square = _layout == PickerLayout.ColorSquare;
		var ring = _layout == PickerLayout.HueRing;
		var disc = _layout == PickerLayout.HueDisc;
		var hsv = _layout == PickerLayout.HsvSliders;
		var rgb = _layout == PickerLayout.RgbSliders;
		var sliders = hsv || rgb;

		Show( _body, !sliders );
		Show( _hueSide, square );
		Show( _square, square );
		Show( _ring, ring );
		Show( _disc, disc );
		Show( _valueSide, disc );
		Show( _alphaSide, HasAlpha );

		Show( _hsvRows, hsv );
		Show( _rgbRows, rgb );
		Show( _alphaRow, sliders && HasAlpha );

		Show( _brightnessRow, IsHdr );

		ShowAlphaNumbers( HasAlpha );
		SyncPaletteUsage();
	}

	static void Show( Panel panel, bool show )
	{
		panel.Style.Display = show ? DisplayMode.Flex : DisplayMode.None;
	}
}

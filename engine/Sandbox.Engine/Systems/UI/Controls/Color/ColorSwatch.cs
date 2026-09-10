using System.Globalization;
using Sandbox.UI.Construct;

namespace Sandbox.UI;

/// <summary>
/// A block of colour over a checkerboard, with a small badge when it's brighter than SDR.
/// </summary>
[StyleSheet.Inline( "colorswatch", Styles )]
internal class ColorSwatch : Panel
{
	const string Styles = """
		.colorswatch
		{
			position: relative;
			border-radius: 4px;
			overflow: hidden;
			flex-shrink: 0;

			> .fill
			{
				position: absolute;
				left: 0;
				top: 0;
				right: 0;
				bottom: 0;
			}

			> .hdr
			{
				position: absolute;
				right: 2px;
				bottom: 1px;
				font-size: 9px;
				font-weight: 600;
				padding: 1px 3px;
				border-radius: 3px;
				background-color: #000c;
				color: #fff;
			}

			&.compact > .hdr
			{
				right: 0;
				bottom: 0;
				font-size: 7px;
				padding: 0 2px;
				border-radius: 2px 0 0 0;
			}
		}
		""";

	readonly Panel _fill;
	readonly Label _badge;

	/// <summary>
	/// A swatch too small for the full multiplier shows it rounded, "×4" rather than "×4.22".
	/// </summary>
	public bool Compact { get; set; }

	/// <summary>
	/// The colour before any display restrictions are applied.
	/// </summary>
	public PickerColor Color { get; private set; }

	public ColorSwatch()
	{
		AddClass( "colorswatch" );

		Style.BackgroundSizeX = ColorPickerTextures.CheckerSize;
		Style.BackgroundSizeY = ColorPickerTextures.CheckerSize;
		Style.BackgroundRepeat = BackgroundRepeat.Repeat;

		_fill = Add.Panel( "fill" );
		_badge = Add.Label( null, "hdr" );
		_badge.Style.Display = DisplayMode.None;
	}

	/// <summary>
	/// Show this colour. Alpha and the brightness badge can be left out for properties without them.
	/// </summary>
	public void Set( PickerColor color, bool showAlpha = true, bool showBrightness = true )
	{
		Color = color;
		var fill = color.BaseColor;
		if ( !showAlpha ) fill.a = 1.0f;

		// An opaque colour is painted straight onto the swatch. The checkerboard and the fill
		// layered over it only come out for transparency - otherwise the fill's anti-aliased
		// corners let the checkerboard show through as a grey ring
		var transparent = fill.a < 1.0f;
		Style.BackgroundImage = transparent ? ColorPickerTextures.Checkerboard : null;
		// A bare null here can bind to Color's implicit string conversion.
		Style.BackgroundColor = transparent ? (Color?)null : fill;
		_fill.Style.Display = transparent ? DisplayMode.Flex : DisplayMode.None;
		_fill.Style.BackgroundColor = fill;

		var hdr = showBrightness && color.Brightness != 1.0f;
		_badge.Style.Display = hdr ? DisplayMode.Flex : DisplayMode.None;
		if ( hdr ) _badge.Text = "×" + color.Brightness.ToString( Compact ? "0" : "0.##", CultureInfo.InvariantCulture );
	}
}

using Sandbox.UI.Construct;

namespace Sandbox.UI;

/// <summary>
/// A block of colour over a checkerboard. Brightness isn't drawn on it - the text beside it says that.
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
		}
		""";

	readonly Panel _fill;

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
	}

	/// <summary>
	/// Show this colour. Alpha can be left out for properties without it.
	/// </summary>
	public void Set( PickerColor color, bool showAlpha = true )
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
	}
}

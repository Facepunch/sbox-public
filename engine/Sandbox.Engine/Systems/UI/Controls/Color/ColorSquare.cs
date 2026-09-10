using Sandbox.UI.Construct;

namespace Sandbox.UI;

/// <summary>
/// Saturation across and value up, on a square of one hue.
/// </summary>
[StyleSheet.Inline( "colorsquare", Styles )]
internal class ColorSquare : ColorDragPanel
{
	const string Styles = ColorPickerStyles.Handle + """
		// Square corners on purpose - the white and black layers anti-alias separately from the
		// hue underneath, so a rounded corner shows the hue peeking out from under them
		.colorsquare
		{
			position: relative;
			flex-grow: 1;
			height: 220px;
			cursor: crosshair;
			pointer-events: all;

			> .gradient
			{
				position: absolute;
				left: 0;
				top: 0;
				right: 0;
				bottom: 0;
				background: linear-gradient( to right, #fff, #fff0 );

				&:after
				{
					content: "";
					position: absolute;
					left: 0;
					top: 0;
					right: 0;
					bottom: 0;
					background: linear-gradient( to top, #000, #0000 );
				}
			}
		}
		""";

	readonly Panel _handle;

	/// <summary>
	/// Called with the new saturation and value as the handle is dragged.
	/// </summary>
	public Action<float, float> Changed { get; set; }

	public ColorSquare()
	{
		AddClass( "colorsquare" );

		Add.Panel( "gradient" );
		_handle = Add.Panel( "color-handle" );
	}

	/// <summary>
	/// Show this colour: the square takes its hue, the handle moves to its saturation and value.
	/// </summary>
	public void Set( PickerColor color )
	{
		Style.BackgroundColor = new ColorHsv( color.Hsv.Hue, 1.0f, 1.0f ).ToColor();

		_handle.Style.Left = Length.Percent( color.Hsv.Saturation * 100.0f );
		_handle.Style.Top = Length.Percent( (1.0f - color.Hsv.Value) * 100.0f );
		_handle.Style.BackgroundColor = color.BaseColor.WithAlpha( 1.0f );
	}

	protected override void OnDrag( Vector2 fraction )
	{
		Changed?.Invoke( fraction.x, 1.0f - fraction.y );
	}
}

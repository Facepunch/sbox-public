using Sandbox.UI.Construct;

namespace Sandbox.UI;

/// <summary>
/// Hue around a ring with a saturation and value square inside it, the shape Unity uses.
/// </summary>
[StyleSheet.Inline( "huering", Styles )]
internal class HueRing : ColorDragPanel
{
	const string Styles = ColorPickerStyles.Handle + """
		.huering
		{
			position: relative;
			width: 220px;
			height: 220px;
			flex-shrink: 0;
			cursor: pointer;
			pointer-events: all;

			> .ring
			{
				position: absolute;
				left: 0;
				top: 0;
				right: 0;
				bottom: 0;
				pointer-events: none;
			}

			> .colorsquare
			{
				position: absolute;
				left: 47px;
				top: 47px;
				width: 126px;
				height: 126px;
				flex-grow: 0;
			}
		}
		""";

	readonly ColorSquare _square;
	readonly Panel _handle;

	/// <summary>
	/// Called with the new hue as the ring is dragged.
	/// </summary>
	public Action<float> HueChanged { get; set; }

	/// <summary>
	/// The square in the middle, for its own callbacks.
	/// </summary>
	public ColorSquare Square => _square;

	public HueRing()
	{
		AddClass( "huering" );

		var ring = Add.Panel( "ring" );
		ring.Style.BackgroundImage = ColorPickerTextures.HueRing;

		_square = AddChild<ColorSquare>();
		_handle = Add.Panel( "color-handle" );
	}

	/// <summary>
	/// Show this colour: the handle moves round to its hue, the square inside shows the rest.
	/// </summary>
	public void Set( PickerColor color )
	{
		var radians = color.Hsv.Hue.DegreeToRadian();
		var radius = (ColorPickerTextures.RingOuter + ColorPickerTextures.RingInner) * 0.5f;
		var centre = ColorPickerTextures.WheelSize * 0.5f;

		_handle.Style.Left = Length.Pixels( centre + MathF.Cos( radians ) * radius );
		_handle.Style.Top = Length.Pixels( centre + MathF.Sin( radians ) * radius );
		_handle.Style.BackgroundColor = new ColorHsv( color.Hsv.Hue, 1.0f, 1.0f ).ToColor();

		_square.Set( color );
	}

	protected override void OnDrag( Vector2 fraction )
	{
		// Presses on the square never reach here - it keeps them - so anything here is the ring
		var dx = fraction.x - 0.5f;
		var dy = fraction.y - 0.5f;
		var hue = (MathF.Atan2( dy, dx ).RadianToDegree() + 360.0f) % 360.0f;

		HueChanged?.Invoke( MathF.Min( hue, 359.999f ) );
	}
}

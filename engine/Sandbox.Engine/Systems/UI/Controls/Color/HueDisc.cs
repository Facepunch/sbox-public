using Sandbox.UI.Construct;

namespace Sandbox.UI;

/// <summary>
/// Hue by angle and saturation by distance from the middle, the shape Unreal uses. Value comes
/// from a strip beside it.
/// </summary>
[StyleSheet.Inline( "huedisc", Styles )]
internal class HueDisc : ColorDragPanel
{
	const string Styles = ColorPickerStyles.Handle + """
		.huedisc
		{
			position: relative;
			width: 220px;
			height: 220px;
			flex-shrink: 0;
			cursor: crosshair;
			pointer-events: all;
		}
		""";

	readonly Panel _handle;

	/// <summary>
	/// Called with the new hue and saturation as the disc is dragged.
	/// </summary>
	public Action<float, float> Changed { get; set; }

	public HueDisc()
	{
		AddClass( "huedisc" );

		Style.BackgroundImage = ColorPickerTextures.HueDisc;
		_handle = Add.Panel( "color-handle" );
	}

	/// <summary>
	/// Show this colour: the handle moves to its hue and saturation.
	/// </summary>
	public void Set( PickerColor color )
	{
		var radians = color.Hsv.Hue.DegreeToRadian();
		var radius = color.Hsv.Saturation * ColorPickerTextures.DiscRadius;
		var centre = ColorPickerTextures.WheelSize * 0.5f;

		_handle.Style.Left = Length.Pixels( centre + MathF.Cos( radians ) * radius );
		_handle.Style.Top = Length.Pixels( centre + MathF.Sin( radians ) * radius );
		_handle.Style.BackgroundColor = new ColorHsv( color.Hsv.Hue, color.Hsv.Saturation, 1.0f ).ToColor();
	}

	protected override void OnDrag( Vector2 fraction )
	{
		var dx = (fraction.x - 0.5f) * ColorPickerTextures.WheelSize;
		var dy = (fraction.y - 0.5f) * ColorPickerTextures.WheelSize;

		var hue = (MathF.Atan2( dy, dx ).RadianToDegree() + 360.0f) % 360.0f;
		var saturation = MathF.Min( 1.0f, MathF.Sqrt( dx * dx + dy * dy ) / ColorPickerTextures.DiscRadius );

		Changed?.Invoke( MathF.Min( hue, 359.999f ), saturation );
	}
}

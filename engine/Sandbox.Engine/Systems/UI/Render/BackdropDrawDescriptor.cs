using Sandbox.Rendering;

namespace Sandbox.UI;

public record struct BackdropDrawDescriptor( Rect PanelRect )
{
	/// <summary>
	/// Corner radii, resolved. What the renderer draws with.
	/// </summary>
	internal BorderRadii Radii;

	/// <summary>
	/// Circular corner radii as (bottom-right, top-right, bottom-left, top-left).
	/// </summary>
	public Vector4 BorderRadius
	{
		readonly get => Radii.ToPublic();
		set => Radii = BorderRadii.FromPublic( value );
	}

	public float Opacity;

	public float Brightness;
	public float Contrast;
	public float Saturate;
	public float Sepia;
	public float Invert;
	public float HueRotate;
	public float BlurScale;

	internal BlendMode OverrideBlendMode;
	internal bool IsLayered;

}

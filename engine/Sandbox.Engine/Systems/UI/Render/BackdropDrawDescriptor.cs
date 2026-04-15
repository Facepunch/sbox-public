using Sandbox.Rendering;

namespace Sandbox.UI;

public record struct BackdropDrawDescriptor( Rect PanelRect )
{
	public Vector4 BorderRadius;
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

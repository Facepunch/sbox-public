using Sandbox.Rendering;

namespace Sandbox.UI;

public record struct OutlineDrawDescriptor( Rect PanelRect, Color Color, float Width )
{
	public Vector4 BorderRadius;
	public float Offset;

	internal BlendMode OverrideBlendMode;

}

using Sandbox.Rendering;

namespace Sandbox.UI;

public record struct ShadowDrawDescriptor( Rect PanelRect, Color Color )
{
	public Vector4 BorderRadius;
	public Vector2 Offset;
	public float Blur;
	public float Spread;
	public bool Inset;

	internal BlendMode OverrideBlendMode;

	// Scissor for inset/outset clipping
	internal Vector4 ScissorRect;
	internal Vector4 ScissorCornerRadius;
	internal Matrix ScissorTransformMat;

}

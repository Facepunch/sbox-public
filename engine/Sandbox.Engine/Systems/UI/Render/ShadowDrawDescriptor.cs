using Sandbox.Rendering;

namespace Sandbox.UI;

/// <summary>
/// A box-shadow. For an outset shadow PanelRect is the border box, for an inset one it's the padding box the
/// shadow is drawn inside; Radii are that box's corners.
/// </summary>
public record struct ShadowDrawDescriptor( Rect PanelRect, Color Color )
{
	/// <summary>
	/// Corner radii of PanelRect, resolved. What the renderer draws with.
	/// </summary>
	internal BorderRadii Radii;

	/// <summary>
	/// Circular corner radii as (top-left, top-right, bottom-left, bottom-right).
	/// </summary>
	public Vector4 BorderRadius
	{
		readonly get => Radii.ToVector4();
		set => Radii = BorderRadii.FromCorners( value );
	}

	public Vector2 Offset;
	public float Blur;
	public float Spread;
	public bool Inset;

	internal BlendMode OverrideBlendMode;

	// Outset shadows are kept out of PanelRect, reached from screen space by this matrix. Inset ones are kept inside it.
	internal Matrix ScissorTransformMat;

}

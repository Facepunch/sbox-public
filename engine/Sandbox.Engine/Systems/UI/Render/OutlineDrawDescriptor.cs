using Sandbox.Rendering;

namespace Sandbox.UI;

public record struct OutlineDrawDescriptor( Rect PanelRect, Color Color, float Width )
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

	public float Offset;

	internal BlendMode OverrideBlendMode;

}

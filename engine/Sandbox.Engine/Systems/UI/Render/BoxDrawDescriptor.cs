using Sandbox.Rendering;

namespace Sandbox.UI;

public record struct BoxDrawDescriptor( Rect PanelRect, Color Color )
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

	public Vector4 BorderSize;

	/// <summary>Which box the background paints into.</summary>
	public BackgroundClip BackgroundClip;

	/// <summary>Inset of the clip box from the border box, as left, top, right, bottom.</summary>
	internal Vector4 BackgroundClipInset;

	/// <summary>Text the background is clipped to, and where it sits as x, y, w, h relative to PanelRect.</summary>
	internal Texture TextMask;
	internal Vector4 TextMaskRect;

	public Color BorderColorL;
	public Color BorderColorT;
	public Color BorderColorR;
	public Color BorderColorB;
	public Texture BackgroundImage;
	public Vector4 BackgroundRect;
	public Color BackgroundTint;
	public float BackgroundAngle;
	public BackgroundRepeat BackgroundRepeat;
	public FilterMode FilterMode;

	internal BlendMode BackgroundBlendMode;
	internal BlendMode OverrideBlendMode;

	/// <summary>A shader-evaluated background gradient. Mutually exclusive with BackgroundImage.</summary>
	internal GradientInfo BackgroundGradient;

	internal Texture BorderImageTexture;
	internal Vector4 BorderImageSlice;
	internal BorderImageRepeat BorderImageRepeat;
	internal BorderImageFill BorderImageFill;
	internal Color BorderImageTint;

	internal bool HasImage => BackgroundImage != null && BackgroundImage != Texture.Invalid;
	internal bool HasGradient => !BackgroundGradient.ColorOffsets.IsDefaultOrEmpty;
	internal bool HasBorderImage => BorderImageTexture != null;
	internal bool HasTextMask => TextMask != null && TextMask != Texture.Invalid;
	internal bool IsTwoPass => HasImage && BackgroundBlendMode != BlendMode.Normal;

}

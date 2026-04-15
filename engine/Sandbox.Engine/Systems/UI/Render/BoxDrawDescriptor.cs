using Sandbox.Rendering;

namespace Sandbox.UI;

public record struct BoxDrawDescriptor( Rect PanelRect, Color Color )
{
	public Vector4 BorderRadius;
	public Vector4 BorderSize;
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

	internal Texture BorderImageTexture;
	internal Vector4 BorderImageSlice;
	internal BorderImageRepeat BorderImageRepeat;
	internal BorderImageFill BorderImageFill;
	internal Color BorderImageTint;

	internal bool PremultiplyAlpha;

	internal bool HasImage => BackgroundImage != null && BackgroundImage != Texture.Invalid;
	internal bool HasBorderImage => BorderImageTexture != null;
	internal bool IsTwoPass => HasImage && BackgroundBlendMode != BlendMode.Normal;

}

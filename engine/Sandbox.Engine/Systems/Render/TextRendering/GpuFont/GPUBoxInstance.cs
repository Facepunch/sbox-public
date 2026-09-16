using System.Runtime.InteropServices;

namespace Sandbox.UI;

/// <summary>
/// GPU text instance shared by glyph composition and Painter. Matches BoxInstanceData in ui/text.hlsl.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
struct GPUBoxInstance
{
	public Vector4 Rect;
	public Color Color;
	public Vector4 BorderRadius;   // horizontal, (top-left, top-right, bottom-left, bottom-right)
	public Vector4 BorderRadiusV;  // vertical, same order
	public Vector4 BorderSize;
	public Color BorderColorL;
	public Color BorderColorT;
	public Color BorderColorR;
	public Color BorderColorB;
	public int TextureIndex;
	public int SamplerIndex;
	public int BackgroundRepeat;
	public float BackgroundAngle;
	public Vector4 BackgroundRect;
	public Color BackgroundTint;
	public int BorderImageIndex;
	public int BorderImageSamplerIndex;
	public int BorderImageMode;
	public int BorderImageFill;
	public Vector4 BorderImageSlice;
	public Color BorderImageTint;
	public int Flags; // a glyph's GlyphTable index
	public int ScissorIndex;
	public int Mode;
	public int TransformIndex;
	public int InverseScissorIndex;
	public int TextMaskIndex;
	public int TextMaskSamplerIndex;
	public int BackgroundClip;
	public Vector4 BackgroundClipRect;

	/// <summary>Index into the border shape table, or -1 for a plain rounded rect.</summary>
	public int ShapeIndex;

}

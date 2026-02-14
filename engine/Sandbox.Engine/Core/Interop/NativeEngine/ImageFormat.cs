namespace Sandbox;

/// <summary>
/// Format used when creating textures.
/// </summary>
[Expose]
public enum ImageFormat : int
{
	None = -3,
	Default = -2,
	/// <summary>
	/// Four 8-bit components representing RGBA.
	/// </summary>
	RGBA8888 = 0,
	/// <summary>
	/// Four 8-bit components representing ABGR.
	/// </summary>
	ABGR8888,
	/// <summary>
	/// Three 8-bit components representing RGB.
	/// </summary>
	RGB888,
	/// <summary>
	/// Three 8-bit components representing BGR.
	/// </summary>
	BGR888,
	/// <summary>
	/// Three components representing RGB.
	/// Red and blue are 5 bit components, green is 6 bit totalling 16 bits.
	/// </summary>
	RGB565,
	/// <summary>
	/// Single 8-bit component representing luminance.
	/// </summary>
	I8,
	/// <summary>
	/// Two 8-bit components representing luminance and alpha.
	/// </summary>
	IA88,
	/// <summary>
	/// Single 8-bit component representing alpha.
	/// </summary>
	A8 = 8,
	ARGB8888 = 11,
	BGRA8888,
	/// <summary>
	/// Compressed texture format with no alpha.
	/// </summary>
	DXT1,
	/// <summary>
	/// Compressed texture format with alpha.
	/// </summary>
	DXT3,
	/// <summary>
	/// Compressed texture format with alpha, generally better than DXT3.
	/// </summary>
	DXT5,
	BGRX8888,
	BGR565,
	BGRX5551,
	BGRA4444,
	DXT1_ONEBITALPHA,
	BGRA5551,
	/// <summary>
	/// Four 16-bit float components representing RGBA.
	/// </summary>
	RGBA16161616F = 24,
	/// <summary>
	/// Four 16-bit integer components representing RGBA.
	/// </summary>
	RGBA16161616,
	/// <summary>
	/// Three 32-bit float components representing RGB.
	/// </summary>
	RGB323232F = 28,
	/// <summary>
	/// Single 32-bit float component representing R.
	/// </summary>
	R32F = 27,
	/// <summary>
	/// Four 32-bit float components representing RGBA.
	/// </summary>
	RGBA32323232F = 29,

	// Compressed normal map formats
	ATI2N = 36,         // One-surface ATI2N / DXN format
	ATI1N,         // Two-surface ATI1N format

	// should we be exposing any of this shit
	// supporting these specific formats as non-tiled for procedural cpu access
	LINEAR_BGRX8888 = 41,
	LINEAR_RGBA8888,
	LINEAR_ABGR8888,
	LINEAR_ARGB8888,
	LINEAR_BGRA8888,
	LINEAR_RGB888,
	LINEAR_BGR888,
	LINEAR_BGRX5551,
	LINEAR_I8,
	LINEAR_RGBA16161616,

	LE_BGRX8888,
	LE_BGRA8888,

	RG1616F,
	RG3232F,
	RGBX8888,

	RGBA1010102 = 57,   // 10 bit-per component render targets
	BGRA1010102,
	R16F,          // 16 bit FP format

	// Depth-stencil texture formats
	D16,
	D15S1,
	D32,
	D24S8 = 63,
	LINEAR_D24S8,
	D24X8,
	D24X4S4,
	D24FS8,
	D16_SHADOW,    // Specific formats for shadow mapping
	D24X8_SHADOW,  // Specific formats for shadow mapping

	DXT5_NM = 78,

	RG1616,
	R16,           // 16 bit int format

	RGBA8888_LINEAR,
	BGRA8888_LINEAR,
	BGRX8888_LINEAR,

	RGBX555,
	BC6H,
	BC7,
	R32_UINT,

	R8G8B8_ETC2,
	R8G8B8A8_ETC2_EAC,
	R11_EAC,
	RG11_EAC,
	D32FS8,
	RGBA32323232,
	I16F,

	RG3232,
}

internal static class ImageFormatExtensions
{
	public static bool IsDepthFormat( this ImageFormat format )
	{
		switch ( format )
		{
			case ImageFormat.D16:
			case ImageFormat.D15S1:
			case ImageFormat.D32:
			case ImageFormat.D24S8:
			case ImageFormat.LINEAR_D24S8:
			case ImageFormat.D24X8:
			case ImageFormat.D24X4S4:
			case ImageFormat.D24FS8:
			case ImageFormat.D16_SHADOW:
			case ImageFormat.D24X8_SHADOW:
			case ImageFormat.D32FS8:
				return true;
			default:
				return false;
		}
	}
}

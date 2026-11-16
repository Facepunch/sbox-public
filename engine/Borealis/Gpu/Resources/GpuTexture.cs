namespace Borealis;

/// <summary>
/// Pixel formats Borealis creates and recognises. A texture in any other format reads as <see cref="Unknown"/>.
/// </summary>
public enum TextureFormat
{
	/// <summary>No format: a target without this attachment, or a texture in a format Borealis doesn't name.</summary>
	Unknown,

	/// <summary>No colour or depth buffer.</summary>
	None,

	/// <summary>8-bit RGBA.</summary>
	RGBA8888,

	/// <summary>8-bit BGRA.</summary>
	BGRA8888,

	/// <summary>8-bit alpha.</summary>
	A8,

	/// <summary>16-bit float red.</summary>
	R16F,

	/// <summary>32-bit float red.</summary>
	R32F,

	/// <summary>32-bit unsigned integer red.</summary>
	R32_UINT,

	/// <summary>16-bit float red and green.</summary>
	RG1616F,

	/// <summary>32-bit float red and green.</summary>
	RG3232F,

	/// <summary>Packed 11-11-10 float RGB.</summary>
	R11G11B10F,

	/// <summary>32-bit float RGB.</summary>
	RGB323232F,

	/// <summary>16-bit unsigned normalised RGBA.</summary>
	RGBA16161616,

	/// <summary>16-bit float RGBA.</summary>
	RGBA16161616F,

	/// <summary>32-bit float RGBA.</summary>
	RGBA32323232F,

	/// <summary>10-bit RGB, 2-bit alpha.</summary>
	RGBA1010102,

	/// <summary>10-bit BGR, 2-bit alpha.</summary>
	BGRA1010102,

	/// <summary>16-bit depth.</summary>
	D16,

	/// <summary>32-bit depth.</summary>
	D32,

	/// <summary>24-bit depth, 8-bit stencil.</summary>
	D24S8,

	/// <summary>32-bit float depth, 8-bit stencil.</summary>
	D32FS8,
}

/// <summary>
/// Format queries.
/// </summary>
public static class TextureFormats
{
	/// <summary>
	/// Whether it's a depth format.
	/// </summary>
	public static bool IsDepth( this TextureFormat format ) => format is TextureFormat.D16 or TextureFormat.D32 or TextureFormat.D24S8 or TextureFormat.D32FS8;

	/// <summary>Whether a depth format has a stencil.</summary>
	public static bool HasStencil( this TextureFormat format ) => format is TextureFormat.D24S8 or TextureFormat.D32FS8;

	/// <summary>
	/// Linear float formats, which bypass sRGB encoding (<c>ImageLoader::IsFloatFormat</c>).
	/// </summary>
	public static bool IsFloat( this TextureFormat format ) => format is TextureFormat.RGBA16161616F or TextureFormat.RGBA32323232F
		or TextureFormat.RGB323232F or TextureFormat.RG1616F or TextureFormat.RG3232F or TextureFormat.R16F or TextureFormat.R32F;
}

/// <summary>
/// The dimension of a texture view looked up for bindless sampling (<see cref="GpuTexture.ViewIndex"/>).
/// </summary>
public enum TextureViewDimension
{
	/// <summary>The texture's own dimension: a cube's cube view, a 2D texture's 2D view.</summary>
	Default,

	/// <summary>A 2D view.</summary>
	Texture2D,
}

/// <summary>
/// What a texture to create looks like (<see cref="GpuDevice.CreateTexture"/>).
/// </summary>
public struct TextureDesc
{
	/// <summary>Size in pixels.</summary>
	public int Width, Height;

	/// <summary>Slices of a volume texture; 1 for a 2D texture.</summary>
	public int Depth;

	/// <summary>Its format.</summary>
	public TextureFormat Format;

	/// <summary>Samples per pixel; 1 without MSAA.</summary>
	public int Samples;

	/// <summary>Mip levels; 1 for none.</summary>
	public int Mips;

	/// <summary>Whether it's a render target (2D textures only).</summary>
	public bool RenderTarget;

	/// <summary>Whether a render target is a cube map: six square faces.</summary>
	public bool Cube;

	/// <summary>Whether compute can write it.</summary>
	public bool Storage;

	/// <summary>Whether shaders read a multisampled render target (one with a single sample always can be).</summary>
	public bool SampledMultisampled;

	/// <summary>Whether the device can generate its mips.</summary>
	public bool GenerateMips;

	/// <summary>Initial pixels, or empty.</summary>
	public ReadOnlyMemory<byte> Data;

	/// <summary>
	/// A 2D render target.
	/// </summary>
	public static TextureDesc Target( int width, int height, TextureFormat format, int samples = 1, int mips = 1 ) => new()
	{
		Width = width,
		Height = height,
		Depth = 1,
		Format = format,
		Samples = Math.Max( samples, 1 ),
		Mips = Math.Max( mips, 1 ),
		RenderTarget = true,
	};

	/// <summary>
	/// A volume texture.
	/// </summary>
	public static TextureDesc Volume( int width, int height, int depth, TextureFormat format ) => new()
	{
		Width = width,
		Height = height,
		Depth = depth,
		Format = format,
		Samples = 1,
		Mips = 1,
	};
}

/// <summary>
/// A texture on the GPU, made or wrapped by the <see cref="GpuDevice"/>. Equal textures are the same object, so identity
/// works as a cache key.
/// </summary>
public abstract class GpuTexture : IDisposable
{
	/// <summary>Width in pixels.</summary>
	public abstract int Width { get; }

	/// <summary>Height in pixels.</summary>
	public abstract int Height { get; }

	/// <summary>Size in pixels.</summary>
	public Vector2Int Size => new( Width, Height );

	/// <summary>Mip levels.</summary>
	public abstract int Mips { get; }

	/// <summary>Its format, or <see cref="TextureFormat.Unknown"/> for one Borealis doesn't name.</summary>
	public abstract TextureFormat Format { get; }

	/// <summary>Samples per pixel; 1 without MSAA.</summary>
	public abstract int Samples { get; }

	/// <summary>Whether it's a cubemap.</summary>
	public abstract bool IsCube { get; }

	/// <summary>Whether it's a texture array.</summary>
	public abstract bool IsArray { get; }

	/// <summary>Whether the texture exists on the GPU.</summary>
	public abstract bool IsValid { get; }

	/// <summary>Its bindless index through its linear 2D view.</summary>
	public abstract int BindlessIndex { get; }

	/// <summary>
	/// Its bindless index through an sRGB or linear view of <paramref name="dimension"/>.
	/// </summary>
	public abstract int ViewIndex( bool srgb, TextureViewDimension dimension );

	/// <summary>
	/// A sheeted texture's sequence data, or zero for one without sequences.
	/// </summary>
	public abstract Vector4 SheetData { get; }

	/// <summary>
	/// Keep every mip resident while it's sampled bindlessly this frame.
	/// </summary>
	public abstract void KeepResident();

	/// <summary>
	/// Release it. Only for textures this renderer created.
	/// </summary>
	public abstract void Dispose();
}

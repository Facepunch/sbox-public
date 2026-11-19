using Borealis.Gpu.Vulkan;

namespace Borealis.Gpu;

/// <summary>
/// Texture and render target formats. A format with an sRGB form (<see cref="Formats.HasSrgb"/>) gets both views,
/// whichever of the two it's created as.
/// </summary>
public enum Format
{
	/// <summary>No format: no attachment, or the texture's own in a view.</summary>
	Unknown,
	/// <summary>8-bit red.</summary>
	R8Unorm,
	/// <summary>8-bit red and green.</summary>
	RG8Unorm,
	/// <summary>8-bit RGBA.</summary>
	RGBA8Unorm,
	/// <summary>8-bit RGBA, sRGB encoded.</summary>
	RGBA8Srgb,
	/// <summary>8-bit BGRA.</summary>
	BGRA8Unorm,
	/// <summary>8-bit BGRA, sRGB encoded.</summary>
	BGRA8Srgb,
	/// <summary>8-bit signed normalised RGBA.</summary>
	RGBA8Snorm,
	/// <summary>8-bit unsigned integer RGBA.</summary>
	RGBA8Uint,
	/// <summary>10-bit RGB, 2-bit alpha.</summary>
	RGB10A2Unorm,
	/// <summary>10-bit BGR, 2-bit alpha.</summary>
	BGR10A2Unorm,
	/// <summary>16-bit red.</summary>
	R16Unorm,
	/// <summary>16-bit unsigned integer red.</summary>
	R16Uint,
	/// <summary>16-bit float red.</summary>
	R16Float,
	/// <summary>16-bit red and green.</summary>
	RG16Unorm,
	/// <summary>16-bit float red and green.</summary>
	RG16Float,
	/// <summary>16-bit RGBA.</summary>
	RGBA16Unorm,
	/// <summary>16-bit unsigned integer RGBA.</summary>
	RGBA16Uint,
	/// <summary>16-bit float RGBA.</summary>
	RGBA16Float,
	/// <summary>32-bit unsigned integer red.</summary>
	R32Uint,
	/// <summary>32-bit signed integer red.</summary>
	R32Sint,
	/// <summary>32-bit float red.</summary>
	R32Float,
	/// <summary>32-bit unsigned integer red and green.</summary>
	RG32Uint,
	/// <summary>32-bit float red and green.</summary>
	RG32Float,
	/// <summary>32-bit float RGB.</summary>
	RGB32Float,
	/// <summary>32-bit unsigned integer RGBA.</summary>
	RGBA32Uint,
	/// <summary>32-bit float RGBA.</summary>
	RGBA32Float,
	/// <summary>Packed 11-11-10 float RGB.</summary>
	RG11B10Float,
	/// <summary>16-bit depth.</summary>
	D16Unorm,
	/// <summary>32-bit float depth.</summary>
	D32Float,
	/// <summary>24-bit depth, 8-bit stencil. Created as <see cref="D32FloatS8Uint"/> where it isn't supported.</summary>
	D24UnormS8Uint,
	/// <summary>32-bit float depth, 8-bit stencil.</summary>
	D32FloatS8Uint,
	/// <summary>BC1 (DXT1).</summary>
	BC1Unorm,
	/// <summary>BC1, sRGB.</summary>
	BC1Srgb,
	/// <summary>BC3 (DXT5).</summary>
	BC3Unorm,
	/// <summary>BC3, sRGB.</summary>
	BC3Srgb,
	/// <summary>BC4: one channel.</summary>
	BC4Unorm,
	/// <summary>BC5: two channels.</summary>
	BC5Unorm,
	/// <summary>BC6H: unsigned float HDR.</summary>
	BC6HUfloat,
	/// <summary>BC7.</summary>
	BC7Unorm,
	/// <summary>BC7, sRGB.</summary>
	BC7Srgb,
}

/// <summary>
/// Format queries.
/// </summary>
public static class Formats
{
	/// <summary>Whether it's a depth format, with or without stencil.</summary>
	public static bool IsDepth( this Format format ) => format is Format.D16Unorm or Format.D32Float or Format.D24UnormS8Uint or Format.D32FloatS8Uint;

	/// <summary>Whether it has a stencil channel.</summary>
	public static bool HasStencil( this Format format ) => format is Format.D24UnormS8Uint or Format.D32FloatS8Uint;

	/// <summary>Whether it's block compressed (4x4 blocks).</summary>
	public static bool IsCompressed( this Format format ) => format >= Format.BC1Unorm;

	/// <summary>Whether it has an sRGB form, so textures in it get linear and sRGB views.</summary>
	public static bool HasSrgb( this Format format ) => format is Format.RGBA8Unorm or Format.RGBA8Srgb or Format.BGRA8Unorm or Format.BGRA8Srgb
		or Format.BC1Unorm or Format.BC1Srgb or Format.BC3Unorm or Format.BC3Srgb or Format.BC7Unorm or Format.BC7Srgb;

	/// <summary>The linear form of an sRGB format, or the format itself.</summary>
	public static Format Linear( this Format format ) => format switch
	{
		Format.RGBA8Srgb => Format.RGBA8Unorm,
		Format.BGRA8Srgb => Format.BGRA8Unorm,
		Format.BC1Srgb => Format.BC1Unorm,
		Format.BC3Srgb => Format.BC3Unorm,
		Format.BC7Srgb => Format.BC7Unorm,
		_ => format,
	};

	/// <summary>The sRGB form of a format that has one, or the format itself.</summary>
	public static Format Srgb( this Format format ) => format switch
	{
		Format.RGBA8Unorm => Format.RGBA8Srgb,
		Format.BGRA8Unorm => Format.BGRA8Srgb,
		Format.BC1Unorm => Format.BC1Srgb,
		Format.BC3Unorm => Format.BC3Srgb,
		Format.BC7Unorm => Format.BC7Srgb,
		_ => format,
	};

	/// <summary>Bytes per texel, or per 4x4 block for compressed formats.</summary>
	public static int BytesPerBlock( this Format format ) => format switch
	{
		Format.R8Unorm => 1,
		Format.RG8Unorm or Format.R16Unorm or Format.R16Uint or Format.R16Float or Format.D16Unorm => 2,
		Format.RGBA8Unorm or Format.RGBA8Srgb or Format.BGRA8Unorm or Format.BGRA8Srgb or Format.RGBA8Snorm or Format.RGBA8Uint
			or Format.RGB10A2Unorm or Format.BGR10A2Unorm or Format.RG16Unorm or Format.RG16Float or Format.R32Uint or Format.R32Sint
			or Format.R32Float or Format.RG11B10Float or Format.D32Float or Format.D24UnormS8Uint => 4,
		Format.RGBA16Unorm or Format.RGBA16Uint or Format.RGBA16Float or Format.RG32Uint or Format.RG32Float or Format.D32FloatS8Uint => 8,
		Format.RGB32Float => 12,
		Format.RGBA32Uint or Format.RGBA32Float => 16,
		Format.BC1Unorm or Format.BC1Srgb or Format.BC4Unorm => 8,
		Format.BC3Unorm or Format.BC3Srgb or Format.BC5Unorm or Format.BC6HUfloat or Format.BC7Unorm or Format.BC7Srgb => 16,
		_ => 0,
	};

	/// <summary>Bytes in one row of a mip <paramref name="width"/> texels wide (a row of blocks when compressed).</summary>
	public static int RowPitch( this Format format, int width ) => format.IsCompressed() ? (width + 3) / 4 * format.BytesPerBlock() : width * format.BytesPerBlock();

	/// <summary>Rows in a mip <paramref name="height"/> texels high (rows of blocks when compressed).</summary>
	public static int Rows( this Format format, int height ) => format.IsCompressed() ? (height + 3) / 4 : height;

	internal static VkFormat ToVk( this Format format ) => format switch
	{
		Format.R8Unorm => VkFormat.R8Unorm,
		Format.RG8Unorm => VkFormat.R8G8Unorm,
		Format.RGBA8Unorm => VkFormat.R8G8B8A8Unorm,
		Format.RGBA8Srgb => VkFormat.R8G8B8A8Srgb,
		Format.BGRA8Unorm => VkFormat.B8G8R8A8Unorm,
		Format.BGRA8Srgb => VkFormat.B8G8R8A8Srgb,
		Format.RGBA8Snorm => VkFormat.R8G8B8A8Snorm,
		Format.RGBA8Uint => VkFormat.R8G8B8A8Uint,
		Format.RGB10A2Unorm => VkFormat.A2B10G10R10UnormPack32,
		Format.BGR10A2Unorm => VkFormat.A2R10G10B10UnormPack32,
		Format.R16Unorm => VkFormat.R16Unorm,
		Format.R16Uint => VkFormat.R16Uint,
		Format.R16Float => VkFormat.R16Sfloat,
		Format.RG16Unorm => VkFormat.R16G16Unorm,
		Format.RG16Float => VkFormat.R16G16Sfloat,
		Format.RGBA16Unorm => VkFormat.R16G16B16A16Unorm,
		Format.RGBA16Uint => VkFormat.R16G16B16A16Uint,
		Format.RGBA16Float => VkFormat.R16G16B16A16Sfloat,
		Format.R32Uint => VkFormat.R32Uint,
		Format.R32Sint => VkFormat.R32Sint,
		Format.R32Float => VkFormat.R32Sfloat,
		Format.RG32Uint => VkFormat.R32G32Uint,
		Format.RG32Float => VkFormat.R32G32Sfloat,
		Format.RGB32Float => VkFormat.R32G32B32Sfloat,
		Format.RGBA32Uint => VkFormat.R32G32B32A32Uint,
		Format.RGBA32Float => VkFormat.R32G32B32A32Sfloat,
		Format.RG11B10Float => VkFormat.B10G11R11UfloatPack32,
		Format.D16Unorm => VkFormat.D16Unorm,
		Format.D32Float => VkFormat.D32Sfloat,
		Format.D24UnormS8Uint => VkFormat.D24UnormS8Uint,
		Format.D32FloatS8Uint => VkFormat.D32SfloatS8Uint,
		Format.BC1Unorm => VkFormat.Bc1RgbaUnorm,
		Format.BC1Srgb => VkFormat.Bc1RgbaSrgb,
		Format.BC3Unorm => VkFormat.Bc3Unorm,
		Format.BC3Srgb => VkFormat.Bc3Srgb,
		Format.BC4Unorm => VkFormat.Bc4Unorm,
		Format.BC5Unorm => VkFormat.Bc5Unorm,
		Format.BC6HUfloat => VkFormat.Bc6hUfloat,
		Format.BC7Unorm => VkFormat.Bc7Unorm,
		Format.BC7Srgb => VkFormat.Bc7Srgb,
		_ => VkFormat.Undefined,
	};

	internal static Format FromVk( VkFormat format ) => format switch
	{
		VkFormat.B8G8R8A8Unorm => Format.BGRA8Unorm,
		VkFormat.B8G8R8A8Srgb => Format.BGRA8Srgb,
		VkFormat.R8G8B8A8Unorm => Format.RGBA8Unorm,
		VkFormat.R8G8B8A8Srgb => Format.RGBA8Srgb,
		VkFormat.A2B10G10R10UnormPack32 => Format.RGB10A2Unorm,
		VkFormat.A2R10G10B10UnormPack32 => Format.BGR10A2Unorm,
		VkFormat.R16G16B16A16Sfloat => Format.RGBA16Float,
		_ => Format.Unknown,
	};

	internal static uint Aspect( this Format format ) => format.HasStencil() ? Vk.AspectDepth | Vk.AspectStencil : format.IsDepth() ? Vk.AspectDepth : Vk.AspectColor;

	/// <summary>Whether a vertex format is read as integers (a shader's int or uint input), not as floats.</summary>
	public static bool IsInteger( this VertexFormat format ) => format is VertexFormat.UByte4 or VertexFormat.UInt or VertexFormat.UInt2 or VertexFormat.UInt3 or VertexFormat.UInt4 or VertexFormat.Int or VertexFormat.UByte or VertexFormat.Byte;

	internal static VkFormat ToVk( this VertexFormat format ) => format switch
	{
		VertexFormat.Float => VkFormat.R32Sfloat,
		VertexFormat.Float2 => VkFormat.R32G32Sfloat,
		VertexFormat.Float3 => VkFormat.R32G32B32Sfloat,
		VertexFormat.Float4 => VkFormat.R32G32B32A32Sfloat,
		VertexFormat.Half2 => VkFormat.R16G16Sfloat,
		VertexFormat.Half4 => VkFormat.R16G16B16A16Sfloat,
		VertexFormat.UByte4Norm => VkFormat.R8G8B8A8Unorm,
		VertexFormat.Byte4Norm => VkFormat.R8G8B8A8Snorm,
		VertexFormat.UByte4 => VkFormat.R8G8B8A8Uint,
		VertexFormat.UInt => VkFormat.R32Uint,
		VertexFormat.UInt2 => VkFormat.R32G32Uint,
		VertexFormat.UInt3 => VkFormat.R32G32B32Uint,
		VertexFormat.UInt4 => VkFormat.R32G32B32A32Uint,
		VertexFormat.UInt1010102Norm => VkFormat.A2B10G10R10UnormPack32,
		VertexFormat.UShort2Norm => VkFormat.R16G16Unorm,
		VertexFormat.Int => VkFormat.R32Sint,
		VertexFormat.UByte => VkFormat.R8Uint,
		VertexFormat.Byte => VkFormat.R8Sint,
		_ => VkFormat.Undefined,
	};

	internal static VkImageLayout ToVk( this ImageLayout layout ) => layout switch
	{
		ImageLayout.General => VkImageLayout.General,
		ImageLayout.ColorTarget => VkImageLayout.ColorAttachmentOptimal,
		ImageLayout.DepthTarget => VkImageLayout.DepthStencilAttachmentOptimal,
		// One read-only layout for sampling and depth testing (synchronization2), so a bindless descriptor's layout holds
		// whether depth is being tested at the same time or not
		ImageLayout.DepthReadOnly or ImageLayout.ShaderRead => VkImageLayout.ReadOnlyOptimal,
		ImageLayout.CopySource => VkImageLayout.TransferSrcOptimal,
		ImageLayout.CopyDestination => VkImageLayout.TransferDstOptimal,
		ImageLayout.Present => VkImageLayout.PresentSrc,
		_ => VkImageLayout.Undefined,
	};
}

using Borealis.Source2.Materials;
using NativeEngine;

namespace Sandbox;

public partial class Texture
{
	/// <summary>
	/// Made by <see cref="Create(string, bool, TextureBuilder, IntPtr, int)"/> as a texture of its own: its description is
	/// what it was made with.
	/// </summary>
	bool describedAtCreation;

	/// <summary>
	/// Its description as Source 2 makes one from a creation config (<c>CRenderDeviceBase::FindOrCreateTexture2</c>): the
	/// config's size, format and flags, with what generating mips needs where it has mips and isn't given all of them.
	/// </summary>
	internal static CTextureDesc DescriptionOf( in TextureCreationConfig_t config, int dataSize )
	{
		var desc = new CTextureDesc
		{
			m_nWidth = config.m_nWidth,
			m_nHeight = config.m_nHeight,
			m_nDepth = config.m_nDepth,
			m_nNumMipLevels = config.m_nNumMipLevels,
			m_nDecodeFlags = config.m_nDecodeFlags,
			m_nImageFormat = config.m_nImageFormat,
			m_nFlags = config.m_nFlags,
			m_nDisplayRectWidth = config.m_nDisplayRectWidth,
			m_nDisplayRectHeight = config.m_nDisplayRectHeight,
			m_nMotionVectorsMaxDistanceInPixels = config.m_nMotionVectorsMaxDistanceInPixels,
		};

		var alreadyHasMipsAndIsStatic = config.m_nUsage == TextureUsage.TEXTURE_USAGE_STATIC && dataSize == MemorySize( desc );
		if ( desc.m_nNumMipLevels > 1 && !alreadyHasMipsAndIsStatic )
			desc.m_nFlags |= RuntimeTextureSpecificationFlags.TSPEC_TEXTURE_GEN_MIP_MAPS | RuntimeTextureSpecificationFlags.TSPEC_RENDER_TARGET | RuntimeTextureSpecificationFlags.TSPEC_RENDER_TARGET_SAMPLEABLE;

		return desc;
	}

	/// <summary>Bytes of a texture with every mip and slice, as Source 2 sizes it (<c>ComputeTextureMemorySize</c>).</summary>
	static long MemorySize( in CTextureDesc desc )
		=> (long)desc.ArrayCount * ImageLoader.GetMemRequired( desc.m_nWidth, desc.m_nHeight, desc.Depth, desc.m_nNumMipLevels, desc.m_nImageFormat );

	/// <summary>The compiled file's header, read once (null when there's none to read, or it can't be).</summary>
	TextureHeader fileHeader;
	bool fileHeaderRead;

	/// <summary>
	/// The header of the compiled <c>.vtex</c> a loaded texture came from, as the drawing device finds the file, when the
	/// process has no Source 2 device to ask: null for one made at run time, or whose file isn't found.
	/// </summary>
	TextureHeader FileHeader
	{
		get
		{
			if ( fileHeaderRead ) return fileHeader;
			if ( describedAtCreation || Graphics.HasSource2Device || native.IsNull ) return null;

			var path = FilePath();
			TextureHeader header = null;
			if ( path is not null )
			{
				try
				{
					var prefix = RenderBackend.Default.ReadCompiledPrefix( path, 4096 );
					if ( prefix is not null && TextureHeader.BytesNeeded( prefix ) is var needed && needed > prefix.Length )
						prefix = RenderBackend.Default.ReadCompiledPrefix( path, needed );
					if ( prefix is not null ) header = TextureHeader.Read( prefix );
				}
				catch ( System.IO.InvalidDataException )
				{
					// Not a texture we can read: Source 2's description
				}
			}

			fileHeader = header;
			fileHeaderRead = true;
			return header;
		}
	}

	/// <summary>The compiled file a loaded texture came from: its path, else the resource name Source 2 loaded it by.</summary>
	string FilePath()
	{
		var path = ResourcePath;
		if ( string.IsNullOrEmpty( path ) || !path.EndsWith( ".vtex", StringComparison.OrdinalIgnoreCase ) )
			path = RenderTools.GetTextureResourceName( native );

		if ( string.IsNullOrEmpty( path ) ) return null;
		if ( path.EndsWith( ".vtex_c", StringComparison.OrdinalIgnoreCase ) ) path = path[..^2];
		return path.EndsWith( ".vtex", StringComparison.OrdinalIgnoreCase ) ? path : null;
	}

	/// <summary>
	/// A loaded texture's description as Source 2 makes it from the file's header (<c>VTEX_InitCreationDescFromHeader</c>),
	/// or null when it can't be made.
	/// </summary>
	internal static CTextureDesc? DescriptionOf( TextureHeader header )
	{
		// VTEX_Flags_t
		const int NoLod = 0x8, Cube = 0x10, Volume = 0x20, Array = 0x40, DilateColor = 0x80, YCoCg = 0x100, Linear = 0x200;

		var format = FormatOf( header.Format );
		if ( format is null ) return null;

		var desc = new CTextureDesc
		{
			m_nWidth = (short)header.Width,
			m_nHeight = (short)header.Height,
			m_nDepth = (short)header.Depth,
			m_nNumMipLevels = (short)header.Mips,
			m_nImageFormat = format.Value,
		};

		var flags = header.Flags;
		if ( (flags & NoLod) != 0 ) desc.m_nFlags |= RuntimeTextureSpecificationFlags.TSPEC_NO_LOD;
		if ( (flags & Cube) != 0 ) desc.m_nFlags |= RuntimeTextureSpecificationFlags.TSPEC_CUBE_TEXTURE;
		if ( (flags & Volume) != 0 ) desc.m_nFlags |= RuntimeTextureSpecificationFlags.TSPEC_VOLUME_TEXTURE;
		if ( (flags & Array) != 0 ) desc.m_nFlags |= RuntimeTextureSpecificationFlags.TSPEC_TEXTURE_ARRAY;
		if ( (flags & Linear) != 0 ) desc.m_nFlags |= RuntimeTextureSpecificationFlags.TSPEC_LINEAR_COLOR_SPACE;

		// A PNG or JPEG in place of the mips is one 2D image, whatever the header says
		var image = header.Format is 15 or 16 or 17 or 18;
		if ( image )
		{
			if ( (desc.m_nFlags & (RuntimeTextureSpecificationFlags.TSPEC_CUBE_TEXTURE | RuntimeTextureSpecificationFlags.TSPEC_VOLUME_TEXTURE | RuntimeTextureSpecificationFlags.TSPEC_TEXTURE_ARRAY)) != 0 )
				return null;

			desc.m_nNumMipLevels = 1;
			desc.m_nFlags |= RuntimeTextureSpecificationFlags.TSPEC_NO_LOD;
		}

		if ( (flags & DilateColor) != 0 ) desc.m_nDecodeFlags |= TextureDecodingFlags.TEXTURE_DECODE_COLOR_DILATION;
		if ( (flags & YCoCg) != 0 ) desc.m_nDecodeFlags |= TextureDecodingFlags.TEXTURE_DECODE_CONVERT_TO_YCOCG_DXT5;

		// A JPEG or PNG made into DXT5: the image's size is what's shown, the texture rounded up to whole blocks
		if ( header.Format is 17 or 18 )
		{
			desc.m_nDisplayRectWidth = (short)header.Width;
			desc.m_nDisplayRectHeight = (short)header.Height;
			desc.m_nWidth = (short)((header.Width + 3) & ~3);
			desc.m_nHeight = (short)((header.Height + 3) & ~3);
		}

		if ( header.HasMetadata )
		{
			if ( header.DisplayWidth != 0 && header.DisplayHeight != 0 )
			{
				desc.m_nDisplayRectWidth = (short)header.DisplayWidth;
				desc.m_nDisplayRectHeight = (short)header.DisplayHeight;
			}

			desc.m_nMotionVectorsMaxDistanceInPixels = (short)header.MotionVectorsMaxDistance;
		}

		return desc;
	}

	/// <summary><c>VTEX_FormatToRuntime</c>; null for one Source 2 doesn't know.</summary>
	static ImageFormat? FormatOf( int format ) => format switch
	{
		1 => ImageFormat.DXT1,
		2 => ImageFormat.DXT5,
		3 => ImageFormat.I8,
		4 => ImageFormat.RGBA8888,
		5 => ImageFormat.R16,
		6 => ImageFormat.RG1616,
		7 => ImageFormat.RGBA16161616,
		8 => ImageFormat.R16F,
		9 => ImageFormat.RG1616F,
		10 => ImageFormat.RGBA16161616F,
		11 => ImageFormat.R32F,
		12 => ImageFormat.RG3232F,
		13 => ImageFormat.RGB323232F,
		14 => ImageFormat.RGBA32323232F,
		15 or 16 => ImageFormat.RGBA8888,
		17 or 18 => ImageFormat.DXT5,
		19 => ImageFormat.BC6H,
		20 => ImageFormat.BC7,
		21 => ImageFormat.ATI2N,
		22 => ImageFormat.IA88,
		23 => ImageFormat.R8G8B8_ETC2,
		24 => ImageFormat.R8G8B8A8_ETC2_EAC,
		25 => ImageFormat.R11_EAC,
		26 => ImageFormat.RG11_EAC,
		27 => ImageFormat.ATI1N,
		28 => ImageFormat.BGRA8888,
		_ => null,
	};
}

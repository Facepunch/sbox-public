namespace Sandbox.Resources;

/// <summary>
/// Writes the VTEX data format (texture header and streaming data).
///
/// The format consists of:
/// 1. Header (width, height, depth, format, flags, etc.)
/// 2. Extra data blocks (not currently used)
/// 3. Streaming data (encoded texture pixels)
///    - Ordered from smallest mip to largest
///    - For cubemaps: each mip contains all 6 faces in order (face 0-5)
/// </summary>
internal class VTexWriter
{
	Logger log = new Logger( "VTexWriter" );
	public VTEX_Header_t Header = new VTEX_Header_t();

	/// <summary>
	/// Given what we know, work out the best texture
	/// </summary>
	public void CalculateFormat()
	{
		//
		// The general rule seems to be
		//
		// Use BC3 for RGBA
		// Use BC7 for HQ RGBA
		// Use BC1 for RGB
		// Use BC5 for Normal Maps
		// Use BC6H for HDR textures
		//

		bool wantsUncompressed = false; // todo flag
		var bestFormat = VTexWriter.VTEX_Format_t.VTEX_FORMAT_BC7;

		if ( Layers.All( x => x.Opaque ) )
		{
			bestFormat = VTEX_Format_t.VTEX_FORMAT_BC1;
		}

		if ( Layers.Any( x => x.Hdr ) )
		{
			bestFormat = VTEX_Format_t.VTEX_FORMAT_BC6H;
		}

		// todo: if hint high quality VTEX_FORMAT_BC7
		// todo: if hint normalmap VTEX_FORMAT_BC5

		// Don't bother compressing tiny textures
		// avoid 1x1 textures becoming 4x4 in bc
		if ( Header.Width < 16 || Header.Height < 16 )
		{
			wantsUncompressed = true;
		}

		if ( wantsUncompressed )
		{
			if ( Layers.Any( x => x.Hdr ) ) bestFormat = VTexWriter.VTEX_Format_t.VTEX_FORMAT_RGBA16161616F;
			else bestFormat = VTexWriter.VTEX_Format_t.VTEX_FORMAT_RGBA8888;
		}

		Header.Format = bestFormat;

		//
		// Disable texture streaming for NP2 textures, since their mipmaps aren't all multiples of 4
		//
		if ( IsCompressed( Header.Format ) && Layers.Any( x => !x.IsPowerOfTwo ) )
		{
			Header.Flags |= VTEX_Flags_t.VTEX_FLAG_NO_LOD;
		}
	}

	public byte[] GetData()
	{
		using var buffer = ByteStream.Create( 256 );
		buffer.Write( Header );

		buffer.Write( (int)0 ); // extra data offset
		buffer.Write( (uint)0 ); // extra data count

		return buffer.ToArray();
	}

	public byte[] GetStreamingData()
	{
		using var buffer = ByteStream.Create( 256 );

		var outputFormat = VTexWriter.VTEX_FormatToRuntime( Header.Format );

		log.Trace( $"Writing Textures: {Header.Format} / {outputFormat}" );

		//
		// Smallest mip first, then by face (for cubemaps)
		// Layout: [mip N face 0][mip N face 1]...[mip N face 5][mip N-1 face 0]...
		//
		foreach ( var layer in Layers.OrderByDescending( x => x.Mip ).ThenBy( x => x.Face ) )
		{
			var encoded = layer.Bitmap.ToFormat( outputFormat );

			if ( encoded is null )
			{
				throw new System.Exception( $"Failed to encode mip {layer.Mip} face {layer.Face} [{layer.Bitmap.Width}x{layer.Bitmap.Height}] to {outputFormat}" );
			}

			log.Trace( $"Writing Bitmap: mip {layer.Mip} face {layer.Face} [{layer.Bitmap.Width}x{layer.Bitmap.Height}] [{encoded.Length}]" );
			buffer.Write( encoded );
		}

		log.Trace( $"Streaming data {buffer.Length}" );
		return buffer.ToArray();
	}


	[System.Runtime.InteropServices.StructLayout( System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1 )]
	public struct VTEX_Header_t
	{
		public VTEX_Header_t() { }

		public ushort InternalVersion = 1;
		public VTEX_Flags_t Flags = 0;
		public Color Reflectivity = Color.White;
		public ushort Width = 0;
		public ushort Height = 0;
		public ushort Depth = 0; // Doubles as slice count if VTEX_FLAG_TEXTURE_ARRAY is specified. Cannot do arrays of volume textures
		public VTEX_Format_t Format = 0;
		public byte MipCount = 0;
		public int m_nPicmip0Res_Unused = 0;
	};

	public enum VTEX_Flags_t : ushort
	{
		NONE = 0,
		// *************************************************
		// WARNING: THESE VALUES ARE SERIALIZED TO DISK.
		//          DO NOT CHANGE EXISTING ENTRIES!
		// *************************************************
		VTEX_FLAG_SUGGEST_CLAMPS = 0x0001,
		VTEX_FLAG_SUGGEST_CLAMPT = 0x0002,
		VTEX_FLAG_SUGGEST_CLAMPU = 0x0004,
		VTEX_FLAG_NO_LOD = 0x0008,
		VTEX_FLAG_CUBE_TEXTURE = 0x0010,
		VTEX_FLAG_VOLUME_TEXTURE = 0x0020,
		VTEX_FLAG_TEXTURE_ARRAY = 0x0040,
		VTEX_FLAG_DILATE_COLOR = 0x0080,    // 1 pixel dilate into regions where alpha == 0
		VTEX_FLAG_CONVERT_TO_YCOCG_DXT5 = 0x0100,
		VTEX_FLAG_CREATE_LINEAR_API_TEXTURE = 0x0200,   // For APIs that don't support dynamically switching a texture to be fetched as srgb or linear at runtime (fuck you, GL ES 3.0!)
														// this flag indicates which color space the texture will be in at creation time.
	};

	public enum VTEX_Format_t : byte
	{
		VTEX_FORMAT_INVALID = 0,

		VTEX_FORMAT_BC1 = 1,
		VTEX_FORMAT_BC3 = 2,
		VTEX_FORMAT_I8 = 3,
		VTEX_FORMAT_RGBA8888 = 4,
		VTEX_FORMAT_R16 = 5,
		VTEX_FORMAT_RG1616 = 6,
		VTEX_FORMAT_RGBA16161616 = 7,
		VTEX_FORMAT_R16F = 8,
		VTEX_FORMAT_RG1616F = 9,
		VTEX_FORMAT_RGBA16161616F = 10,
		VTEX_FORMAT_R32F = 11,
		VTEX_FORMAT_RG3232F = 12,
		VTEX_FORMAT_RGB323232F = 13,
		VTEX_FORMAT_RGBA32323232F = 14,
		VTEX_FORMAT_JPEG_RGBA8888 = 15,
		VTEX_FORMAT_PNG_RGBA8888 = 16,
		VTEX_FORMAT_JPEG_DXT5 = 17,
		VTEX_FORMAT_PNG_DXT5 = 18,
		VTEX_FORMAT_BC6H = 19,
		VTEX_FORMAT_BC7 = 20,
		VTEX_FORMAT_BC5 = 21,
		VTEX_FORMAT_IA88 = 22,
		VTEX_FORMAT_ETC2 = 23,
		VTEX_FORMAT_ETC2_EAC = 24,
		VTEX_FORMAT_R11_EAC = 25,
		VTEX_FORMAT_RG11_EAC = 26,
		VTEX_FORMAT_BC4 = 27,
		VTEX_FORMAT_BGRA8888 = 28
	};

	public static ImageFormat VTEX_FormatToRuntime( VTEX_Format_t n )
	{
		switch ( n )
		{
			case VTEX_Format_t.VTEX_FORMAT_BC1: return ImageFormat.DXT1;
			case VTEX_Format_t.VTEX_FORMAT_BC3: return ImageFormat.DXT5;
			case VTEX_Format_t.VTEX_FORMAT_I8: return ImageFormat.I8;
			case VTEX_Format_t.VTEX_FORMAT_IA88: return ImageFormat.IA88;
			case VTEX_Format_t.VTEX_FORMAT_RGBA8888: return ImageFormat.RGBA8888;
			case VTEX_Format_t.VTEX_FORMAT_R16: return ImageFormat.R16;
			case VTEX_Format_t.VTEX_FORMAT_RG1616: return ImageFormat.RG1616;
			case VTEX_Format_t.VTEX_FORMAT_RGBA16161616: return ImageFormat.RGBA16161616;
			case VTEX_Format_t.VTEX_FORMAT_R16F: return ImageFormat.R16F;
			case VTEX_Format_t.VTEX_FORMAT_RG1616F: return ImageFormat.RG1616F;
			case VTEX_Format_t.VTEX_FORMAT_RGBA16161616F: return ImageFormat.RGBA16161616F;
			case VTEX_Format_t.VTEX_FORMAT_R32F: return ImageFormat.R32F;
			case VTEX_Format_t.VTEX_FORMAT_RG3232F: return ImageFormat.RG3232F;
			case VTEX_Format_t.VTEX_FORMAT_RGB323232F: return ImageFormat.RGB323232F;
			case VTEX_Format_t.VTEX_FORMAT_RGBA32323232F: return ImageFormat.RGBA32323232F;
			case VTEX_Format_t.VTEX_FORMAT_JPEG_RGBA8888: return ImageFormat.RGBA8888;
			case VTEX_Format_t.VTEX_FORMAT_PNG_RGBA8888: return ImageFormat.RGBA8888;
			case VTEX_Format_t.VTEX_FORMAT_JPEG_DXT5: return ImageFormat.DXT5;
			case VTEX_Format_t.VTEX_FORMAT_PNG_DXT5: return ImageFormat.DXT5;
			case VTEX_Format_t.VTEX_FORMAT_BC6H: return ImageFormat.BC6H;
			case VTEX_Format_t.VTEX_FORMAT_BC7: return ImageFormat.BC7;
			case VTEX_Format_t.VTEX_FORMAT_BC5: return ImageFormat.ATI2N;
			case VTEX_Format_t.VTEX_FORMAT_ETC2: return ImageFormat.R8G8B8_ETC2;
			case VTEX_Format_t.VTEX_FORMAT_ETC2_EAC: return ImageFormat.R8G8B8A8_ETC2_EAC;
			case VTEX_Format_t.VTEX_FORMAT_R11_EAC: return ImageFormat.R11_EAC;
			case VTEX_Format_t.VTEX_FORMAT_RG11_EAC: return ImageFormat.RG11_EAC;
			case VTEX_Format_t.VTEX_FORMAT_BC4: return ImageFormat.ATI1N;
			case VTEX_Format_t.VTEX_FORMAT_BGRA8888: return ImageFormat.BGRA8888;
			default: return 0;
		}
	}

	public static bool IsCompressed( VTEX_Format_t format )
	{
		switch ( format )
		{
			case VTEX_Format_t.VTEX_FORMAT_BC1:
			case VTEX_Format_t.VTEX_FORMAT_BC3:
			case VTEX_Format_t.VTEX_FORMAT_BC6H:
			case VTEX_Format_t.VTEX_FORMAT_BC7:
			case VTEX_Format_t.VTEX_FORMAT_BC4:
			case VTEX_Format_t.VTEX_FORMAT_BC5:
			case VTEX_Format_t.VTEX_FORMAT_ETC2:
			case VTEX_Format_t.VTEX_FORMAT_ETC2_EAC:
			case VTEX_Format_t.VTEX_FORMAT_R11_EAC:
			case VTEX_Format_t.VTEX_FORMAT_RG11_EAC:
				return true;
		}

		return false;
	}

	public class TextureLayer
	{
		public Bitmap Bitmap { get; set; }
		public bool Opaque { get; set; }
		public int Mip { get; set; }
		public int Face { get; set; }
		public bool Hdr { get; set; }
		public bool IsPowerOfTwo => Bitmap.Width.IsPowerOfTwo() && Bitmap.Height.IsPowerOfTwo();
	}

	public List<TextureLayer> Layers = new();

	internal void SetTexture( Bitmap bitmap, int mip, int face = 0 )
	{
		var layer = new TextureLayer
		{
			Bitmap = bitmap,
			Mip = mip,
			Face = face,
			Opaque = bitmap.IsOpaque(),
			Hdr = bitmap.IsFloatingPoint
		};

		Layers.Add( layer );
	}
}

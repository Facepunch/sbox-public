using System;
using Sandbox;
using ValvePak;

internal class HL2Texture : ResourceLoader<HL2Mount>
{
	private readonly VpkArchive _package;
	private readonly VpkEntry _entry;
	private readonly string _filePath;

	public HL2Texture( VpkArchive package, VpkEntry entry )
	{
		_package = package;
		_entry = entry;
	}

	public HL2Texture( string filePath )
	{
		_filePath = filePath;
	}

	protected override object Load()
	{
		byte[] data;
		if ( _package != null )
		{
			_package.ReadEntry( _entry, out data );
		}
		else
		{
			data = File.ReadAllBytes( _filePath );
		}
		return VtfLoader.Load( data );
	}
}

internal static class VtfLoader
{
	private enum VtfImageFormat
	{
		Unknown = -1,
		RGBA8888 = 0,
		ABGR8888 = 1,
		RGB888 = 2,
		BGR888 = 3,
		RGB565 = 4,
		I8 = 5,
		IA88 = 6,
		P8 = 7,
		A8 = 8,
		RGB888_BLUESCREEN = 9,
		BGR888_BLUESCREEN = 10,
		ARGB8888 = 11,
		BGRA8888 = 12,
		DXT1 = 13,
		DXT3 = 14,
		DXT5 = 15,
		BGRX8888 = 16,
		BGR565 = 17,
		BGRX5551 = 18,
		BGRA4444 = 19,
		DXT1_ONEBITALPHA = 20,
		BGRA5551 = 21,
		UV88 = 22,
		UVWQ8888 = 23,
		RGBA16161616F = 24,
		RGBA16161616 = 25,
		UVLX8888 = 26,
		R32F = 27,
		RGB323232F = 28,
		RGBA32323232F = 29,
		// 30-33: Depth-stencil formats (NV_DST16, NV_DST24, NV_INTZ, NV_RAWZ, ATI_DST16, ATI_DST24, NV_NULL) - not used in VTF files
		ATI2N = 34,
		ATI1N = 35,
	}

	[Flags]
	private enum TextureFlags : uint
	{
		PointSample = 0x00000001,
		Trilinear = 0x00000002,
		ClampS = 0x00000004,
		ClampT = 0x00000008,
		Anisotropic = 0x00000010,
		HintDxt5 = 0x00000020,
		Srgb = 0x00000040,
		Normal = 0x00000080,
		NoMip = 0x00000100,
		NoLod = 0x00000200,
		AllMips = 0x00000400,
		Procedural = 0x00000800,
		OneBitAlpha = 0x00001000,
		EightBitAlpha = 0x00002000,
		EnvMap = 0x00004000,
		RenderTarget = 0x00008000,
		DepthRenderTarget = 0x00010000,
		NoDebugOverride = 0x00020000,
		SingleCopy = 0x00040000,
		StagingMemory = 0x00080000,
		ImmediateCleanup = 0x00100000,
		IgnorePicmip = 0x00200000,
		NoDepthBuffer = 0x00800000,
		ClampU = 0x02000000,
		VertexTexture = 0x04000000,
		SSBump = 0x08000000,
		Border = 0x20000000,
		StreamableCoarse = 0x40000000,
		StreamableFine = 0x80000000,
	}

	private struct VtfHeader
	{
		public int Width;
		public int Height;
		public int Depth;
		public TextureFlags Flags;
		public int FrameCount;
		public ushort FirstFrame;
		public int VersionMinor;
		public VtfImageFormat ImageFormat;
		public int MipCount;
		public int ImageDataOffset;
	}

	public static Texture Load( byte[] data )
	{
		return !TryParseHeader( data, out var header )
			? null
			: header.Flags.HasFlag( TextureFlags.EnvMap )
			? LoadCubemap( data, header )
			: LoadTexture2D( data, header );
	}

	private static bool TryParseHeader( byte[] data, out VtfHeader header )
	{
		header = default;

		if ( data == null || data.Length < 64 )
			return false;

		using var stream = new MemoryStream( data );
		using var reader = new BinaryReader( stream );

		// VTFFileBaseHeader_t
		var signature = reader.ReadBytes( 4 );
		if ( signature[0] != 'V' || signature[1] != 'T' || signature[2] != 'F' || signature[3] != 0 )
			return false;

		var versionMajor = reader.ReadInt32();
		var versionMinor = reader.ReadInt32();
		var headerSize = reader.ReadInt32();

		if ( versionMajor != 7 || versionMinor < 0 || versionMinor > 5 )
			return false;

		// VTFFileHeaderV7_1_t
		header.VersionMinor = versionMinor;
		header.Width = reader.ReadUInt16();
		header.Height = reader.ReadUInt16();
		header.Flags = (TextureFlags)reader.ReadUInt32();
		header.FrameCount = reader.ReadUInt16();
		header.FirstFrame = reader.ReadUInt16();

		reader.ReadBytes( 4 ); // padding before reflectivity (VectorAligned)
		reader.ReadBytes( 12 ); // reflectivity Vector (3 floats)
		reader.ReadBytes( 4 ); // padding after reflectivity

		reader.ReadSingle(); // bumpScale
		header.ImageFormat = (VtfImageFormat)reader.ReadInt32();
		header.MipCount = reader.ReadByte();

		// Low-res thumbnail info (DXT1 format, used for fast loading)
		var lowResFormat = (VtfImageFormat)reader.ReadInt32();
		var lowResWidth = reader.ReadByte();
		var lowResHeight = reader.ReadByte();

		header.Depth = 1;
		uint numResources = 0;

		// VTFFileHeaderV7_2_t adds depth
		if ( versionMinor >= 2 )
		{
			header.Depth = reader.ReadUInt16();
		}

		// VTFFileHeaderV7_3_t adds resource entries
		if ( versionMinor >= 3 )
		{
			reader.ReadBytes( 3 ); // padding
			numResources = reader.ReadUInt32();
			reader.ReadBytes( 8 ); // padding for alignment
		}

		int lowResDataSize = CalculateImageSize( lowResFormat, lowResWidth, lowResHeight );

		header.ImageDataOffset = versionMinor >= 3 && numResources > 0
			? FindImageDataOffset( reader, numResources, headerSize )
			: headerSize + lowResDataSize;

		return true;
	}

	private static Texture LoadTexture2D( byte[] data, VtfHeader header )
	{
		using var stream = new MemoryStream( data );
		using var reader = new BinaryReader( stream );

		int highResMipOffset = CalculateMipDataOffset( header.ImageFormat, header.Width, header.Height, header.Depth, header.MipCount, header.FrameCount, 1 );

		long targetPosition = header.ImageDataOffset + highResMipOffset;
		if ( targetPosition >= data.Length )
			return null;
		stream.Position = targetPosition;

		int imageSize = CalculateImageSize( header.ImageFormat, header.Width, header.Height ) * header.Depth;
		if ( stream.Position + imageSize > data.Length )
			return null;

		byte[] imageData = reader.ReadBytes( imageSize );

		var rgbaData = ConvertToRgba( imageData, header.ImageFormat, header.Width, header.Height );
		if ( rgbaData == null || rgbaData.Length == 0 )
			return null;

		var builder = Texture.Create( header.Width, header.Height )
			.WithData( rgbaData );

		if ( !header.Flags.HasFlag( TextureFlags.NoMip ) && header.MipCount > 1 )
		{
			builder = builder.WithMips();
		}

		return builder.Finish();
	}

	private static Texture LoadCubemap( byte[] data, VtfHeader header )
	{
		using var stream = new MemoryStream( data );
		using var reader = new BinaryReader( stream );

		// Older cubemaps (pre-7.5) with firstFrame == 0xFFFF have a 7th spheremap face
		int faceCount = header.FirstFrame == 0xFFFF && header.VersionMinor < 5 ? 7 : 6;
		int highResMipOffset = CalculateMipDataOffset( header.ImageFormat, header.Width, header.Height, header.Depth, header.MipCount, header.FrameCount, faceCount );

		long targetPosition = header.ImageDataOffset + highResMipOffset;
		if ( targetPosition >= data.Length )
			return null;
		stream.Position = targetPosition;

		int faceSize = CalculateImageSize( header.ImageFormat, header.Width, header.Height );
		int totalSize = faceSize * faceCount;

		if ( stream.Position + totalSize > data.Length )
			return null;

		var rgbaFaces = new byte[6][];
		int rgbaFaceSize = header.Width * header.Height * 4;

		for ( int face = 0; face < 6; face++ )
		{
			var faceData = reader.ReadBytes( faceSize );
			rgbaFaces[face] = ConvertToRgba( faceData, header.ImageFormat, header.Width, header.Height );

			if ( rgbaFaces[face] == null )
				return null;
		}

		var combinedData = new byte[rgbaFaceSize * 6];
		for ( int face = 0; face < 6; face++ )
		{
			Array.Copy( rgbaFaces[face], 0, combinedData, face * rgbaFaceSize, rgbaFaceSize );
		}

		return Texture.CreateCube( header.Width, header.Height, ImageFormat.RGBA8888 )
			.WithData( combinedData )
			.Finish();
	}

	private static int FindImageDataOffset( BinaryReader reader, uint numResources, int headerSize )
	{
		const uint VTF_LEGACY_RSRC_IMAGE = 0x00000030;

		for ( int i = 0; i < numResources; i++ )
		{
			var type = reader.ReadUInt32();
			var data = reader.ReadUInt32();

			// Strip flags from type
			var resourceType = type & 0x00FFFFFF;

			if ( resourceType == VTF_LEGACY_RSRC_IMAGE )
			{
				return (int)data;
			}
		}

		return headerSize;
	}

	private static int CalculateMipDataOffset( VtfImageFormat format, int width, int height, int depth, int mipCount, int frameCount, int faceCount )
	{
		int offset = 0;

		for ( int mip = mipCount - 1; mip > 0; mip-- )
		{
			int mipWidth = Math.Max( 1, width >> mip );
			int mipHeight = Math.Max( 1, height >> mip );
			int mipDepth = Math.Max( 1, depth >> mip );

			int mipSize = CalculateImageSize( format, mipWidth, mipHeight ) * mipDepth;
			offset += mipSize * faceCount * frameCount;
		}

		return offset;
	}

	private static int CalculateImageSize( VtfImageFormat format, int width, int height )
	{
		int blockWidth = Math.Max( 4, width );
		int blockHeight = Math.Max( 4, height );
		int numBlocksX = (blockWidth + 3) / 4;
		int numBlocksY = (blockHeight + 3) / 4;

		return format switch
		{
			VtfImageFormat.DXT1 or VtfImageFormat.DXT1_ONEBITALPHA or VtfImageFormat.ATI1N =>
				numBlocksX * numBlocksY * 8,

			VtfImageFormat.DXT3 or VtfImageFormat.DXT5 or VtfImageFormat.ATI2N =>
				numBlocksX * numBlocksY * 16,

			VtfImageFormat.RGBA8888 or VtfImageFormat.ABGR8888 or VtfImageFormat.ARGB8888 or
			VtfImageFormat.BGRA8888 or VtfImageFormat.BGRX8888 or VtfImageFormat.UVWQ8888 or
			VtfImageFormat.UVLX8888 =>
				width * height * 4,

			VtfImageFormat.RGB888 or VtfImageFormat.BGR888 or VtfImageFormat.RGB888_BLUESCREEN or
			VtfImageFormat.BGR888_BLUESCREEN =>
				width * height * 3,

			VtfImageFormat.RGB565 or VtfImageFormat.BGR565 or VtfImageFormat.BGRA4444 or
			VtfImageFormat.BGRA5551 or VtfImageFormat.BGRX5551 or VtfImageFormat.IA88 or
			VtfImageFormat.UV88 =>
				width * height * 2,

			VtfImageFormat.I8 or VtfImageFormat.A8 or VtfImageFormat.P8 =>
				width * height,

			VtfImageFormat.RGBA16161616 or VtfImageFormat.RGBA16161616F =>
				width * height * 8,

			VtfImageFormat.R32F =>
				width * height * 4,

			VtfImageFormat.RGB323232F =>
				width * height * 12,

			VtfImageFormat.RGBA32323232F =>
				width * height * 16,

			_ => width * height * 4
		};
	}

	private static byte[] ConvertToRgba( byte[] data, VtfImageFormat format, int width, int height )
	{
		return format switch
		{
			VtfImageFormat.DXT1 => DecompressDxt1( data, width, height ),
			VtfImageFormat.DXT1_ONEBITALPHA => DecompressDxt1( data, width, height, hasAlpha: true ),
			VtfImageFormat.DXT3 => DecompressDxt3( data, width, height ),
			VtfImageFormat.DXT5 => DecompressDxt5( data, width, height ),
			VtfImageFormat.RGBA8888 => data,
			VtfImageFormat.BGRA8888 => ConvertBgraToRgba( data ),
			VtfImageFormat.ABGR8888 => ConvertAbgrToRgba( data ),
			VtfImageFormat.ARGB8888 => ConvertArgbToRgba( data ),
			VtfImageFormat.RGB888 or VtfImageFormat.RGB888_BLUESCREEN => ConvertRgb888ToRgba( data ),
			VtfImageFormat.BGR888 or VtfImageFormat.BGR888_BLUESCREEN => ConvertBgr888ToRgba( data ),
			VtfImageFormat.BGRX8888 => ConvertBgrxToRgba( data ),
			VtfImageFormat.I8 or VtfImageFormat.P8 => ConvertI8ToRgba( data ),
			VtfImageFormat.IA88 => ConvertIa88ToRgba( data ),
			VtfImageFormat.A8 => ConvertA8ToRgba( data ),
			VtfImageFormat.RGB565 => ConvertRgb565ToRgba( data ),
			VtfImageFormat.BGR565 => ConvertBgr565ToRgba( data ),
			VtfImageFormat.BGRA4444 => ConvertBgra4444ToRgba( data ),
			VtfImageFormat.BGRA5551 => ConvertBgra5551ToRgba( data ),
			VtfImageFormat.BGRX5551 => ConvertBgrx5551ToRgba( data ),
			VtfImageFormat.UV88 => ConvertUv88ToRgba( data ),
			VtfImageFormat.UVWQ8888 or VtfImageFormat.UVLX8888 => ConvertUvwq8888ToRgba( data ),
			VtfImageFormat.ATI1N => DecompressAti1n( data, width, height ),
			VtfImageFormat.ATI2N => DecompressAti2n( data, width, height ),
			VtfImageFormat.RGBA16161616F => ConvertRgba16161616FToRgba( data, width, height ),
			VtfImageFormat.RGBA16161616 => ConvertRgba16161616ToRgba( data, width, height ),
			VtfImageFormat.R32F => ConvertR32FToRgba( data, width, height ),
			VtfImageFormat.RGB323232F => ConvertRgb323232FToRgba( data, width, height ),
			VtfImageFormat.RGBA32323232F => ConvertRgba32323232FToRgba( data, width, height ),
			_ => null
		};
	}

	private static byte[] DecompressDxt1( byte[] data, int width, int height, bool hasAlpha = false )
	{
		var output = new byte[width * height * 4];
		int blockCountX = (width + 3) / 4;
		int blockCountY = (height + 3) / 4;
		int offset = 0;

		for ( int by = 0; by < blockCountY; by++ )
		{
			for ( int bx = 0; bx < blockCountX; bx++ )
			{
				ushort c0 = BitConverter.ToUInt16( data, offset );
				ushort c1 = BitConverter.ToUInt16( data, offset + 2 );
				uint lookupTable = BitConverter.ToUInt32( data, offset + 4 );
				offset += 8;

				var colors = new byte[4][];
				colors[0] = Rgb565ToRgba( c0 );
				colors[1] = Rgb565ToRgba( c1 );

				if ( c0 > c1 )
				{
					colors[2] = LerpColor( colors[0], colors[1], 1, 3 );
					colors[3] = LerpColor( colors[0], colors[1], 2, 3 );
				}
				else
				{
					colors[2] = LerpColor( colors[0], colors[1], 1, 2 );
					colors[3] = hasAlpha ? [0, 0, 0, 0] : LerpColor( colors[0], colors[1], 1, 2 );
				}

				for ( int y = 0; y < 4; y++ )
				{
					for ( int x = 0; x < 4; x++ )
					{
						int px = bx * 4 + x;
						int py = by * 4 + y;

						if ( px >= width || py >= height )
							continue;

						int index = (int)((lookupTable >> (2 * (y * 4 + x))) & 0x3);
						int destOffset = (py * width + px) * 4;

						output[destOffset + 0] = colors[index][0];
						output[destOffset + 1] = colors[index][1];
						output[destOffset + 2] = colors[index][2];
						output[destOffset + 3] = colors[index][3];
					}
				}
			}
		}

		return output;
	}

	private static byte[] DecompressDxt3( byte[] data, int width, int height )
	{
		var output = new byte[width * height * 4];
		int blockCountX = (width + 3) / 4;
		int blockCountY = (height + 3) / 4;
		int offset = 0;

		for ( int by = 0; by < blockCountY; by++ )
		{
			for ( int bx = 0; bx < blockCountX; bx++ )
			{
				var alphaData = new byte[8];
				Array.Copy( data, offset, alphaData, 0, 8 );
				offset += 8;

				ushort c0 = BitConverter.ToUInt16( data, offset );
				ushort c1 = BitConverter.ToUInt16( data, offset + 2 );
				uint lookupTable = BitConverter.ToUInt32( data, offset + 4 );
				offset += 8;

				var colors = new byte[4][];
				colors[0] = Rgb565ToRgba( c0 );
				colors[1] = Rgb565ToRgba( c1 );
				colors[2] = LerpColor( colors[0], colors[1], 1, 3 );
				colors[3] = LerpColor( colors[0], colors[1], 2, 3 );

				for ( int y = 0; y < 4; y++ )
				{
					for ( int x = 0; x < 4; x++ )
					{
						int px = bx * 4 + x;
						int py = by * 4 + y;

						if ( px >= width || py >= height )
							continue;

						int index = (int)((lookupTable >> (2 * (y * 4 + x))) & 0x3);
						int destOffset = (py * width + px) * 4;

						int alphaIndex = y * 4 + x;
						int alphaByte = alphaIndex / 2;
						int alphaShift = alphaIndex % 2 * 4;
						int alpha = ((alphaData[alphaByte] >> alphaShift) & 0xF) * 17;

						output[destOffset + 0] = colors[index][0];
						output[destOffset + 1] = colors[index][1];
						output[destOffset + 2] = colors[index][2];
						output[destOffset + 3] = (byte)alpha;
					}
				}
			}
		}

		return output;
	}

	private static byte[] DecompressDxt5( byte[] data, int width, int height )
	{
		var output = new byte[width * height * 4];
		int blockCountX = (width + 3) / 4;
		int blockCountY = (height + 3) / 4;
		int offset = 0;

		for ( int by = 0; by < blockCountY; by++ )
		{
			for ( int bx = 0; bx < blockCountX; bx++ )
			{
				byte alpha0 = data[offset];
				byte alpha1 = data[offset + 1];

				ulong alphaLookup = 0;
				for ( int i = 0; i < 6; i++ )
				{
					alphaLookup |= (ulong)data[offset + 2 + i] << (i * 8);
				}
				offset += 8;

				var alphas = new byte[8];
				alphas[0] = alpha0;
				alphas[1] = alpha1;

				if ( alpha0 > alpha1 )
				{
					for ( int i = 2; i < 8; i++ )
					{
						alphas[i] = (byte)(((8 - i) * alpha0 + (i - 1) * alpha1) / 7);
					}
				}
				else
				{
					for ( int i = 2; i < 6; i++ )
					{
						alphas[i] = (byte)(((6 - i) * alpha0 + (i - 1) * alpha1) / 5);
					}
					alphas[6] = 0;
					alphas[7] = 255;
				}

				ushort c0 = BitConverter.ToUInt16( data, offset );
				ushort c1 = BitConverter.ToUInt16( data, offset + 2 );
				uint lookupTable = BitConverter.ToUInt32( data, offset + 4 );
				offset += 8;

				var colors = new byte[4][];
				colors[0] = Rgb565ToRgba( c0 );
				colors[1] = Rgb565ToRgba( c1 );
				colors[2] = LerpColor( colors[0], colors[1], 1, 3 );
				colors[3] = LerpColor( colors[0], colors[1], 2, 3 );

				for ( int y = 0; y < 4; y++ )
				{
					for ( int x = 0; x < 4; x++ )
					{
						int px = bx * 4 + x;
						int py = by * 4 + y;

						if ( px >= width || py >= height )
							continue;

						int colorIndex = (int)((lookupTable >> (2 * (y * 4 + x))) & 0x3);
						int alphaIndex = (int)((alphaLookup >> (3 * (y * 4 + x))) & 0x7);
						int destOffset = (py * width + px) * 4;

						output[destOffset + 0] = colors[colorIndex][0];
						output[destOffset + 1] = colors[colorIndex][1];
						output[destOffset + 2] = colors[colorIndex][2];
						output[destOffset + 3] = alphas[alphaIndex];
					}
				}
			}
		}

		return output;
	}

	private static byte[] DecompressAti1n( byte[] data, int width, int height )
	{
		var output = new byte[width * height * 4];
		int blockCountX = (width + 3) / 4;
		int blockCountY = (height + 3) / 4;
		int offset = 0;

		for ( int by = 0; by < blockCountY; by++ )
		{
			for ( int bx = 0; bx < blockCountX; bx++ )
			{
				byte red0 = data[offset];
				byte red1 = data[offset + 1];

				ulong redLookup = 0;
				for ( int i = 0; i < 6; i++ )
				{
					redLookup |= (ulong)data[offset + 2 + i] << (i * 8);
				}
				offset += 8;

				var reds = new byte[8];
				reds[0] = red0;
				reds[1] = red1;

				if ( red0 > red1 )
				{
					for ( int i = 2; i < 8; i++ )
					{
						reds[i] = (byte)(((8 - i) * red0 + (i - 1) * red1) / 7);
					}
				}
				else
				{
					for ( int i = 2; i < 6; i++ )
					{
						reds[i] = (byte)(((6 - i) * red0 + (i - 1) * red1) / 5);
					}
					reds[6] = 0;
					reds[7] = 255;
				}

				for ( int y = 0; y < 4; y++ )
				{
					for ( int x = 0; x < 4; x++ )
					{
						int px = bx * 4 + x;
						int py = by * 4 + y;

						if ( px >= width || py >= height )
							continue;

						int redIndex = (int)((redLookup >> (3 * (y * 4 + x))) & 0x7);
						int destOffset = (py * width + px) * 4;

						output[destOffset + 0] = reds[redIndex];
						output[destOffset + 1] = reds[redIndex];
						output[destOffset + 2] = reds[redIndex];
						output[destOffset + 3] = 255;
					}
				}
			}
		}

		return output;
	}

	private static byte[] DecompressAti2n( byte[] data, int width, int height )
	{
		var output = new byte[width * height * 4];
		int blockCountX = (width + 3) / 4;
		int blockCountY = (height + 3) / 4;
		int offset = 0;

		for ( int by = 0; by < blockCountY; by++ )
		{
			for ( int bx = 0; bx < blockCountX; bx++ )
			{
				byte red0 = data[offset];
				byte red1 = data[offset + 1];
				ulong redLookup = 0;
				for ( int i = 0; i < 6; i++ )
				{
					redLookup |= (ulong)data[offset + 2 + i] << (i * 8);
				}
				offset += 8;

				byte green0 = data[offset];
				byte green1 = data[offset + 1];
				ulong greenLookup = 0;
				for ( int i = 0; i < 6; i++ )
				{
					greenLookup |= (ulong)data[offset + 2 + i] << (i * 8);
				}
				offset += 8;

				var reds = InterpolateAlphaBlock( red0, red1 );
				var greens = InterpolateAlphaBlock( green0, green1 );

				for ( int y = 0; y < 4; y++ )
				{
					for ( int x = 0; x < 4; x++ )
					{
						int px = bx * 4 + x;
						int py = by * 4 + y;

						if ( px >= width || py >= height )
							continue;

						int redIndex = (int)((redLookup >> (3 * (y * 4 + x))) & 0x7);
						int greenIndex = (int)((greenLookup >> (3 * (y * 4 + x))) & 0x7);
						int destOffset = (py * width + px) * 4;

						float nx = reds[redIndex] / 255.0f * 2.0f - 1.0f;
						float ny = greens[greenIndex] / 255.0f * 2.0f - 1.0f;
						float nz = (float)Math.Sqrt( Math.Max( 0, 1.0f - nx * nx - ny * ny ) );

						output[destOffset + 0] = reds[redIndex];
						output[destOffset + 1] = greens[greenIndex];
						output[destOffset + 2] = (byte)((nz * 0.5f + 0.5f) * 255);
						output[destOffset + 3] = 255;
					}
				}
			}
		}

		return output;
	}

	private static byte[] InterpolateAlphaBlock( byte a0, byte a1 )
	{
		var result = new byte[8];
		result[0] = a0;
		result[1] = a1;

		if ( a0 > a1 )
		{
			for ( int i = 2; i < 8; i++ )
			{
				result[i] = (byte)(((8 - i) * a0 + (i - 1) * a1) / 7);
			}
		}
		else
		{
			for ( int i = 2; i < 6; i++ )
			{
				result[i] = (byte)(((6 - i) * a0 + (i - 1) * a1) / 5);
			}
			result[6] = 0;
			result[7] = 255;
		}

		return result;
	}

	private static byte[] Rgb565ToRgba( ushort color )
	{
		int r = ((color >> 11) & 0x1F) * 255 / 31;
		int g = ((color >> 5) & 0x3F) * 255 / 63;
		int b = (color & 0x1F) * 255 / 31;
		return [(byte)r, (byte)g, (byte)b, 255];
	}

	private static byte[] LerpColor( byte[] c0, byte[] c1, int num, int denom )
	{
		return
		[
			(byte)((c0[0] * (denom - num) + c1[0] * num) / denom),
			(byte)((c0[1] * (denom - num) + c1[1] * num) / denom),
			(byte)((c0[2] * (denom - num) + c1[2] * num) / denom),
			255
		];
	}

	private static byte[] ConvertBgraToRgba( byte[] data )
	{
		var output = new byte[data.Length];
		for ( int i = 0; i < data.Length; i += 4 )
		{
			output[i + 0] = data[i + 2]; // R
			output[i + 1] = data[i + 1]; // G
			output[i + 2] = data[i + 0]; // B
			output[i + 3] = data[i + 3]; // A
		}
		return output;
	}

	private static byte[] ConvertAbgrToRgba( byte[] data )
	{
		var output = new byte[data.Length];
		for ( int i = 0; i < data.Length; i += 4 )
		{
			output[i + 0] = data[i + 3]; // R
			output[i + 1] = data[i + 2]; // G
			output[i + 2] = data[i + 1]; // B
			output[i + 3] = data[i + 0]; // A
		}
		return output;
	}

	private static byte[] ConvertArgbToRgba( byte[] data )
	{
		var output = new byte[data.Length];
		for ( int i = 0; i < data.Length; i += 4 )
		{
			output[i + 0] = data[i + 1]; // R
			output[i + 1] = data[i + 2]; // G
			output[i + 2] = data[i + 3]; // B
			output[i + 3] = data[i + 0]; // A
		}
		return output;
	}

	private static byte[] ConvertBgrxToRgba( byte[] data )
	{
		var output = new byte[data.Length];
		for ( int i = 0; i < data.Length; i += 4 )
		{
			output[i + 0] = data[i + 2]; // R
			output[i + 1] = data[i + 1]; // G
			output[i + 2] = data[i + 0]; // B
			output[i + 3] = 255;         // A
		}
		return output;
	}

	private static byte[] ConvertRgb888ToRgba( byte[] data )
	{
		var output = new byte[data.Length / 3 * 4];
		for ( int i = 0, j = 0; i < data.Length; i += 3, j += 4 )
		{
			output[j + 0] = data[i + 0];
			output[j + 1] = data[i + 1];
			output[j + 2] = data[i + 2];
			output[j + 3] = 255;
		}
		return output;
	}

	private static byte[] ConvertBgr888ToRgba( byte[] data )
	{
		var output = new byte[data.Length / 3 * 4];
		for ( int i = 0, j = 0; i < data.Length; i += 3, j += 4 )
		{
			output[j + 0] = data[i + 2]; // R
			output[j + 1] = data[i + 1]; // G
			output[j + 2] = data[i + 0]; // B
			output[j + 3] = 255;
		}
		return output;
	}

	private static byte[] ConvertI8ToRgba( byte[] data )
	{
		var output = new byte[data.Length * 4];
		for ( int i = 0; i < data.Length; i++ )
		{
			output[i * 4 + 0] = data[i];
			output[i * 4 + 1] = data[i];
			output[i * 4 + 2] = data[i];
			output[i * 4 + 3] = 255;
		}
		return output;
	}

	private static byte[] ConvertIa88ToRgba( byte[] data )
	{
		var output = new byte[data.Length * 2];
		for ( int i = 0; i < data.Length; i += 2 )
		{
			int j = i * 2;
			output[j + 0] = data[i];
			output[j + 1] = data[i];
			output[j + 2] = data[i];
			output[j + 3] = data[i + 1];
		}
		return output;
	}

	private static byte[] ConvertA8ToRgba( byte[] data )
	{
		var output = new byte[data.Length * 4];
		for ( int i = 0; i < data.Length; i++ )
		{
			output[i * 4 + 0] = 255;
			output[i * 4 + 1] = 255;
			output[i * 4 + 2] = 255;
			output[i * 4 + 3] = data[i];
		}
		return output;
	}

	private static byte[] ConvertRgb565ToRgba( byte[] data )
	{
		var output = new byte[data.Length * 2];
		for ( int i = 0; i < data.Length; i += 2 )
		{
			ushort pixel = BitConverter.ToUInt16( data, i );
			int j = i * 2;
			output[j + 0] = (byte)(((pixel >> 11) & 0x1F) * 255 / 31);
			output[j + 1] = (byte)(((pixel >> 5) & 0x3F) * 255 / 63);
			output[j + 2] = (byte)((pixel & 0x1F) * 255 / 31);
			output[j + 3] = 255;
		}
		return output;
	}

	private static byte[] ConvertBgr565ToRgba( byte[] data )
	{
		var output = new byte[data.Length * 2];
		for ( int i = 0; i < data.Length; i += 2 )
		{
			ushort pixel = BitConverter.ToUInt16( data, i );
			int j = i * 2;
			output[j + 0] = (byte)((pixel & 0x1F) * 255 / 31);
			output[j + 1] = (byte)(((pixel >> 5) & 0x3F) * 255 / 63);
			output[j + 2] = (byte)(((pixel >> 11) & 0x1F) * 255 / 31);
			output[j + 3] = 255;
		}
		return output;
	}

	private static byte[] ConvertBgra4444ToRgba( byte[] data )
	{
		var output = new byte[data.Length * 2];
		for ( int i = 0; i < data.Length; i += 2 )
		{
			ushort pixel = BitConverter.ToUInt16( data, i );
			int j = i * 2;
			output[j + 0] = (byte)(((pixel >> 8) & 0xF) * 17);
			output[j + 1] = (byte)(((pixel >> 4) & 0xF) * 17);
			output[j + 2] = (byte)((pixel & 0xF) * 17);
			output[j + 3] = (byte)(((pixel >> 12) & 0xF) * 17);
		}
		return output;
	}

	private static byte[] ConvertBgra5551ToRgba( byte[] data )
	{
		var output = new byte[data.Length * 2];
		for ( int i = 0; i < data.Length; i += 2 )
		{
			ushort pixel = BitConverter.ToUInt16( data, i );
			int j = i * 2;
			output[j + 0] = (byte)(((pixel >> 10) & 0x1F) * 255 / 31);
			output[j + 1] = (byte)(((pixel >> 5) & 0x1F) * 255 / 31);
			output[j + 2] = (byte)((pixel & 0x1F) * 255 / 31);
			output[j + 3] = (byte)((pixel >> 15) * 255);
		}
		return output;
	}

	private static byte[] ConvertBgrx5551ToRgba( byte[] data )
	{
		var output = new byte[data.Length * 2];
		for ( int i = 0; i < data.Length; i += 2 )
		{
			ushort pixel = BitConverter.ToUInt16( data, i );
			int j = i * 2;
			output[j + 0] = (byte)(((pixel >> 10) & 0x1F) * 255 / 31);
			output[j + 1] = (byte)(((pixel >> 5) & 0x1F) * 255 / 31);
			output[j + 2] = (byte)((pixel & 0x1F) * 255 / 31);
			output[j + 3] = 255;
		}
		return output;
	}

	private static byte[] ConvertUv88ToRgba( byte[] data )
	{
		var output = new byte[data.Length * 2];
		for ( int i = 0; i < data.Length; i += 2 )
		{
			int j = i * 2;
			output[j + 0] = data[i];     // X (U)
			output[j + 1] = data[i + 1]; // Y (V)
			output[j + 2] = 255;         // Z (computed as up)
			output[j + 3] = 255;
		}
		return output;
	}

	private static byte[] ConvertUvwq8888ToRgba( byte[] data )
	{
		var output = new byte[data.Length];
		for ( int i = 0; i < data.Length; i += 4 )
		{
			output[i + 0] = data[i + 0]; // U -> R
			output[i + 1] = data[i + 1]; // V -> G
			output[i + 2] = data[i + 2]; // W -> B
			output[i + 3] = 255;
		}
		return output;
	}

	private static float HalfToFloat( ushort half )
	{
		int sign = (half >> 15) & 1;
		int exponent = (half >> 10) & 0x1F;
		int mantissa = half & 0x3FF;

		if ( exponent == 0 )
		{
			if ( mantissa == 0 )
				return sign == 1 ? -0.0f : 0.0f;

			float value = mantissa / 1024.0f * (float)Math.Pow( 2, -14 );
			return sign == 1 ? -value : value;
		}
		else if ( exponent == 31 )
		{
			return mantissa == 0 ? (sign == 1 ? float.NegativeInfinity : float.PositiveInfinity) : float.NaN;
		}
		else
		{
			float value = (1.0f + mantissa / 1024.0f) * (float)Math.Pow( 2, exponent - 15 );
			return sign == 1 ? -value : value;
		}
	}

	private static byte ToneMapToByte( float hdrValue )
	{
		float ldr = hdrValue / (1.0f + hdrValue);
		return (byte)Math.Clamp( ldr * 255.0f, 0, 255 );
	}

	private static byte[] ConvertRgba16161616FToRgba( byte[] data, int width, int height )
	{
		int pixelCount = width * height;
		var output = new byte[pixelCount * 4];

		for ( int i = 0; i < pixelCount; i++ )
		{
			int srcOffset = i * 8;
			int dstOffset = i * 4;

			if ( srcOffset + 8 > data.Length )
				break;

			ushort rHalf = BitConverter.ToUInt16( data, srcOffset );
			ushort gHalf = BitConverter.ToUInt16( data, srcOffset + 2 );
			ushort bHalf = BitConverter.ToUInt16( data, srcOffset + 4 );
			ushort aHalf = BitConverter.ToUInt16( data, srcOffset + 6 );

			float r = HalfToFloat( rHalf );
			float g = HalfToFloat( gHalf );
			float b = HalfToFloat( bHalf );
			float a = HalfToFloat( aHalf );

			output[dstOffset + 0] = ToneMapToByte( r );
			output[dstOffset + 1] = ToneMapToByte( g );
			output[dstOffset + 2] = ToneMapToByte( b );
			output[dstOffset + 3] = (byte)Math.Clamp( a * 255.0f, 0, 255 );
		}

		return output;
	}

	private static byte[] ConvertRgba16161616ToRgba( byte[] data, int width, int height )
	{
		int pixelCount = width * height;
		var output = new byte[pixelCount * 4];

		for ( int i = 0; i < pixelCount; i++ )
		{
			int srcOffset = i * 8;
			int dstOffset = i * 4;

			if ( srcOffset + 8 > data.Length )
				break;

			output[dstOffset + 0] = data[srcOffset + 1];     // R high byte
			output[dstOffset + 1] = data[srcOffset + 3];     // G high byte
			output[dstOffset + 2] = data[srcOffset + 5];     // B high byte
			output[dstOffset + 3] = data[srcOffset + 7];     // A high byte
		}

		return output;
	}

	private static byte[] ConvertR32FToRgba( byte[] data, int width, int height )
	{
		int pixelCount = width * height;
		var output = new byte[pixelCount * 4];

		for ( int i = 0; i < pixelCount; i++ )
		{
			int srcOffset = i * 4;
			int dstOffset = i * 4;

			if ( srcOffset + 4 > data.Length )
				break;

			float r = BitConverter.ToSingle( data, srcOffset );
			byte rByte = ToneMapToByte( r );

			output[dstOffset + 0] = rByte;
			output[dstOffset + 1] = rByte;
			output[dstOffset + 2] = rByte;
			output[dstOffset + 3] = 255;
		}

		return output;
	}

	private static byte[] ConvertRgb323232FToRgba( byte[] data, int width, int height )
	{
		int pixelCount = width * height;
		var output = new byte[pixelCount * 4];

		for ( int i = 0; i < pixelCount; i++ )
		{
			int srcOffset = i * 12;
			int dstOffset = i * 4;

			if ( srcOffset + 12 > data.Length )
				break;

			float r = BitConverter.ToSingle( data, srcOffset );
			float g = BitConverter.ToSingle( data, srcOffset + 4 );
			float b = BitConverter.ToSingle( data, srcOffset + 8 );

			output[dstOffset + 0] = ToneMapToByte( r );
			output[dstOffset + 1] = ToneMapToByte( g );
			output[dstOffset + 2] = ToneMapToByte( b );
			output[dstOffset + 3] = 255;
		}

		return output;
	}

	private static byte[] ConvertRgba32323232FToRgba( byte[] data, int width, int height )
	{
		int pixelCount = width * height;
		var output = new byte[pixelCount * 4];

		for ( int i = 0; i < pixelCount; i++ )
		{
			int srcOffset = i * 16;
			int dstOffset = i * 4;

			if ( srcOffset + 16 > data.Length )
				break;

			float r = BitConverter.ToSingle( data, srcOffset );
			float g = BitConverter.ToSingle( data, srcOffset + 4 );
			float b = BitConverter.ToSingle( data, srcOffset + 8 );
			float a = BitConverter.ToSingle( data, srcOffset + 12 );

			output[dstOffset + 0] = ToneMapToByte( r );
			output[dstOffset + 1] = ToneMapToByte( g );
			output[dstOffset + 2] = ToneMapToByte( b );
			output[dstOffset + 3] = (byte)Math.Clamp( a * 255.0f, 0, 255 );
		}

		return output;
	}
}

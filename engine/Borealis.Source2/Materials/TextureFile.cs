using Borealis.Gpu;
using Borealis.Source2.Compression;
using Borealis.Source2.Resources;
using System.Buffers.Binary;
using System.IO;

namespace Borealis.Source2.Materials;

/// <summary>
/// A compiled texture (<c>.vtex_c</c>): the 40-byte <c>VTEX_Header_t</c> in its <c>DATA</c> block, and mips after the
/// resource's header, smallest first, each mip's slices (cube faces, array layers) in order. Mips may be LZ4 or
/// Zstandard compressed one by one (<c>VTEX_EXTRA_DATA_COMPRESSED_MIP_SIZE</c>, sizes listed largest mip first; a mip
/// whose stored size is its full size is stored raw). A <c>PNG_RGBA8888</c> texture (an image file compiled as is) holds a
/// PNG file there instead, read as 8-bit RGBA. See <c>src/public/rendersystem/vtex_file_format.h</c>.
/// </summary>
public sealed class TextureFile
{
	const int FlagCube = 0x10, FlagVolume = 0x20, FlagArray = 0x40;

	// VTEX_Format_t's PNG_RGBA8888 and PNG_DXT5: a PNG file in place of the mips
	const int PngRgba8888 = 16, PngDxt5 = 18;

	/// <summary>Size of mip 0.</summary>
	public int Width { get; private init; }

	/// <summary>Size of mip 0.</summary>
	public int Height { get; private init; }

	/// <summary>Depth of a volume; 1 otherwise.</summary>
	public int Depth { get; private init; }

	/// <summary>Slices per mip: 6 per cube, the layer count of an array.</summary>
	public int Slices { get; private init; }

	/// <summary>Mip levels.</summary>
	public int Mips { get; private init; }

	/// <summary><c>VTEX_Format_t</c>.</summary>
	public int SourceFormat { get; private init; }

	/// <summary>The Borealis.Gpu format, or <see cref="Format.Unknown"/> for ones it has no form of (JPEG, ETC).</summary>
	public Format Format { get; private init; }

	/// <summary>Its shape.</summary>
	public TextureDimension Dimension { get; private init; }

	/// <summary>
	/// Each mip's data (all its slices, tightly packed), largest first; null for the mips finer than <see cref="TopMip"/>
	/// when only some were read.
	/// </summary>
	public byte[][] MipData { get; private init; }

	/// <summary>The finest mip read: 0 unless it was read from <see cref="Tail"/> or a mip further down.</summary>
	public int TopMip { get; private init; }

	/// <summary>
	/// The finest mip of its tail: the smallest mips that fit <see cref="TailBytes"/> together, at least the last. Texture
	/// streaming keeps them always.
	/// </summary>
	public int TailTop { get; private init; }

	/// <summary>Bytes of mips a tail fits in.</summary>
	public const int TailBytes = 64 << 10;

	/// <summary>Read from <see cref="TailTop"/>, as <see cref="Read(byte[], int)"/>'s top.</summary>
	public const int Tail = -1;

	/// <summary>A mip's decoded bytes, every slice.</summary>
	public int MipBytes( int mip )
	{
		var w = Math.Max( Width >> mip, 1 );
		var h = Math.Max( Height >> mip, 1 );
		var d = Dimension == TextureDimension.Texture3D ? Math.Max( Depth >> mip, 1 ) : 1;
		return Format.RowPitch( w ) * Format.Rows( h ) * d * Slices;
	}

	/// <summary>Read a texture from its <c>.vtex_c</c> bytes.</summary>
	public static TextureFile Read( byte[] data ) => Read( data, 0 );

	/// <summary>
	/// Read a texture's mips from <paramref name="top"/> (or its tail, <see cref="Tail"/>) to the last, from its
	/// <c>.vtex_c</c> bytes or as much of their start as <see cref="PrefixNeeded"/> says.
	/// </summary>
	public static TextureFile Read( byte[] data, int top )
	{
		var header = new Header( data );
		top = Math.Clamp( top < 0 ? header.TailTop : top, 0, header.Mips - 1 );

		var mipData = new byte[header.Mips][];
		if ( header.Png )
		{
			var (width, height, rgba) = Png.Decode( data.AsSpan( header.HeaderBytes ) );
			if ( width != header.Width || height != header.Height ) throw new InvalidDataException( $"Texture's PNG is {width}x{height}, not {header.Width}x{header.Height}" );
			mipData[0] = rgba;
			top = 0;
		}
		else if ( header.GpuFormat != Format.Unknown )
		{
			var position = header.HeaderBytes;
			for ( int mip = header.Mips - 1; mip >= top; mip-- )
			{
				var size = header.Size( mip );
				var stored = header.Stored[mip];
				if ( (long)position + stored > data.Length ) throw new InvalidDataException( $"Texture data ends before mip {mip}" );
				var source = data.AsSpan( position, stored );
				mipData[mip] = stored == size || header.Compression == 0 ? source.ToArray()
					: header.Compression == 1 ? Lz4.Decompress( source, size )
					: Zstd.Decompress( source, size );
				position += stored;
			}
		}

		return new TextureFile
		{
			Width = header.Width,
			Height = header.Height,
			Depth = header.Volume ? Math.Max( header.Depth, 1 ) : 1,
			Slices = header.Slices,
			Mips = header.Mips,
			SourceFormat = header.SourceFormat,
			Format = header.GpuFormat,
			Dimension = header.Volume ? TextureDimension.Texture3D : header.Cube ? (header.Array ? TextureDimension.TextureCubeArray : TextureDimension.TextureCube) : header.Array ? TextureDimension.Texture2DArray : TextureDimension.Texture2D,
			MipData = mipData,
			TopMip = header.GpuFormat == Format.Unknown ? 0 : top,
			TailTop = header.TailTop,
		};
	}

	/// <summary>
	/// How many bytes from the start of a file <see cref="Read(byte[], int)"/> needs for mips from <paramref name="top"/>
	/// (or the tail), worked out from what <paramref name="prefix"/> holds. When that's more than the prefix, read that
	/// much and ask again: a prefix shorter than the header only says how long the header is.
	/// </summary>
	public static long PrefixNeeded( byte[] prefix, int top )
	{
		if ( prefix.Length < 16 ) return 16;
		var headerSize = BinaryPrimitives.ReadUInt32LittleEndian( prefix );
		if ( prefix.Length < headerSize ) return headerSize;

		var header = new Header( prefix );
		if ( header.GpuFormat == Format.Unknown ) return header.HeaderBytes;
		if ( header.Png ) return prefix.Length <= header.HeaderBytes ? header.HeaderBytes + 8 : header.HeaderBytes + Png.Length( prefix.AsSpan( header.HeaderBytes ) );
		top = Math.Clamp( top < 0 ? header.TailTop : top, 0, header.Mips - 1 );
		long bytes = header.HeaderBytes;
		for ( int mip = top; mip < header.Mips; mip++ ) bytes += header.Stored[mip];
		return bytes;
	}

	/// <summary>The <c>VTEX_Header_t</c>, and each mip's stored size.</summary>
	readonly struct Header
	{
		public readonly int Width, Height, Depth, SourceFormat, Mips, Slices, Compression, HeaderBytes, TailTop;
		public readonly bool Cube, Array, Volume, Png;
		public readonly Format GpuFormat;
		public readonly int[] Stored;

		public Header( byte[] data )
		{
			var resource = ResourceFile.Read( data );
			HeaderBytes = resource.HeaderSize;
			var (offset, _) = resource.Find( "DATA" );
			var header = new ByteReader( data, offset );
			header.UShort();
			var flags = header.UShort();
			header.Skip( 16 );
			Width = header.UShort();
			Height = header.UShort();
			Depth = header.UShort();
			SourceFormat = header.Byte();
			Mips = Math.Max( (int)header.Byte(), 1 );
			Png = SourceFormat is PngRgba8888 or PngDxt5;
			header.Int();
			var extraStart = header.Position;
			var extraOffset = header.Int();
			var extraCount = header.Int();

			Cube = (flags & FlagCube) != 0;
			Array = (flags & FlagArray) != 0;
			Volume = (flags & FlagVolume) != 0;
			Slices = (Array ? Math.Max( Depth, 1 ) : 1) * (Cube ? 6 : 1);
			GpuFormat = Png ? Format.RGBA8Unorm : FormatOf( SourceFormat );

			// The PNG is the first mip alone
			if ( Png ) Mips = 1;

			// Compressed mip sizes, if any
			uint[] compressedSizes = null;
			Compression = 0;
			for ( int i = 0; i < extraCount; i++ )
			{
				var entry = extraStart + extraOffset + i * 12;
				var type = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( entry ) );
				if ( type != 4 ) continue;

				var start = entry + 4 + BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( entry + 4 ) );
				Compression = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( start ) );
				var sizesStart = start + 4 + BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( start + 4 ) );
				compressedSizes = new uint[BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( start + 8 ) )];
				for ( int j = 0; j < compressedSizes.Length; j++ ) compressedSizes[j] = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( sizesStart + j * 4 ) );
			}

			Stored = new int[Mips];
			TailTop = Mips - 1;
			if ( GpuFormat == Format.Unknown || Png ) return;

			long tail = 0;
			for ( int mip = Mips - 1; mip >= 0; mip-- )
			{
				var size = Size( mip );
				Stored[mip] = compressedSizes is not null && mip < compressedSizes.Length ? (int)compressedSizes[mip] : size;
				tail += size;
				if ( tail <= TailBytes ) TailTop = mip;
			}
		}

		/// <summary>A mip's decoded bytes, every slice.</summary>
		public int Size( int mip )
		{
			var w = Math.Max( Width >> mip, 1 );
			var h = Math.Max( Height >> mip, 1 );
			var d = Volume ? Math.Max( Depth >> mip, 1 ) : 1;
			return GpuFormat.RowPitch( w ) * GpuFormat.Rows( h ) * d * Slices;
		}
	}

	/// <summary>Read a <c>.vtex_c</c> from disk.</summary>
	public static TextureFile Load( string path ) => Read( File.ReadAllBytes( path ) );

	static Format FormatOf( int format ) => format switch
	{
		1 => Format.BC1Unorm,
		2 => Format.BC3Unorm,
		3 => Format.R8Unorm,
		4 => Format.RGBA8Unorm,
		5 => Format.R16Unorm,
		6 => Format.RG16Unorm,
		7 => Format.RGBA16Unorm,
		8 => Format.R16Float,
		9 => Format.RG16Float,
		10 => Format.RGBA16Float,
		11 => Format.R32Float,
		12 => Format.RG32Float,
		13 => Format.RGB32Float,
		14 => Format.RGBA32Float,
		19 => Format.BC6HUfloat,
		20 => Format.BC7Unorm,
		21 => Format.BC5Unorm,
		22 => Format.RG8Unorm,
		27 => Format.BC4Unorm,
		28 => Format.BGRA8Unorm,
		_ => Format.Unknown,
	};

	/// <summary>
	/// Make it a sampled texture on <paramref name="gpu"/> of every mip read (<see cref="TopMip"/> its first) with every
	/// slice filled; throws for formats there's no Borealis.Gpu form of. Formats with an sRGB form get both views, so
	/// shaders that read sRGB can.
	/// </summary>
	public TextureHandle Create( Device gpu, string name )
	{
		var texture = CreateEmpty( gpu, name );
		for ( int mip = TopMip; mip < Mips; mip++ ) Write( gpu, texture, mip );
		return texture;
	}

	/// <summary>
	/// A sampled texture of the mips read (<see cref="TopMip"/> its first), nothing written yet: fill each with
	/// <see cref="Write"/>.
	/// </summary>
	public TextureHandle CreateEmpty( Device gpu, string name )
	{
		if ( Format == Format.Unknown ) throw new NotSupportedException( $"{name}: VTEX format {SourceFormat} has no Borealis.Gpu form" );

		return gpu.CreateTexture( new TextureInfo
		{
			Width = Math.Max( Width >> TopMip, 1 ),
			Height = Math.Max( Height >> TopMip, 1 ),
			Depth = Dimension == TextureDimension.Texture3D ? Math.Max( Depth >> TopMip, 1 ) : Depth,
			Layers = Dimension is TextureDimension.TextureCube or TextureDimension.TextureCubeArray ? Slices / 6 : Slices,
			Mips = Mips - TopMip,
			Samples = 1,
			Dimension = Dimension,
			Format = Format,
			Usage = TextureUsage.Sampled | TextureUsage.CopyDestination | TextureUsage.CopySource,
			Name = name,
			Content = true,
		} );
	}

	/// <summary>Write one read mip, every slice, into <see cref="CreateEmpty"/>'s texture; its bytes.</summary>
	public int Write( Device gpu, TextureHandle texture, int mip )
	{
		var size = MipData[mip].Length / Slices;
		for ( int slice = 0; slice < Slices; slice++ ) gpu.WriteTexture( texture, MipData[mip].AsSpan( slice * size, size ), mip - TopMip, slice );
		return MipData[mip].Length;
	}

	/// <summary>
	/// As <see cref="Create(Device, string)"/>, with every mip and slice written by <paramref name="list"/> instead of a
	/// submission that waits: readable (<see cref="ImageLayout.ShaderRead"/>) once the list has run.
	/// </summary>
	public TextureHandle Create( Device gpu, string name, CommandList list )
	{
		var texture = CreateEmpty( gpu, name );
		list.Barrier( TextureBarrier.Transition( texture, ImageLayout.Undefined, ImageLayout.CopyDestination, PipelineStage.AllCommands, MemoryAccess.None, PipelineStage.Transfer, MemoryAccess.TransferWrite ) );
		for ( int mip = TopMip; mip < Mips; mip++ )
		{
			var size = MipData[mip].Length / Slices;
			int width = Math.Max( Width >> mip, 1 ), height = Math.Max( Height >> mip, 1 );
			for ( int slice = 0; slice < Slices; slice++ ) list.UpdateTexture( texture, MipData[mip].AsSpan( slice * size, size ), mip - TopMip, slice, width, height );
		}

		list.Barrier( TextureBarrier.Transition( texture, ImageLayout.CopyDestination, ImageLayout.ShaderRead, PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.AllCommands, MemoryAccess.ShaderRead ) );
		return texture;
	}
}

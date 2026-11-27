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
/// PNG file there instead, read as 8-bit RGBA, its other mips made from it as Source 2 makes them on load (a 2x2 box filter).
/// A file whose data ends before its finest mips reads as the mips it holds (<see cref="TopMip"/> the first).
/// See <c>src/public/rendersystem/vtex_file_format.h</c>.
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
	/// when only some were read. Read pooled, each array may be longer than its mip and a mip stored raw is a slice of the
	/// file's own array (<see cref="Mip"/> is exactly it), and they're gone once <see cref="Release"/>d.
	/// </summary>
	public byte[][] MipData { get; private init; }

	/// <summary>Whether its mips are in <see cref="ContentBuffers"/> arrays, until <see cref="Release"/>.</summary>
	public bool Pooled { get; private set; }

	// A pooled read's file (ReadPooled), held while a mip is a slice of it or isn't decoded yet; each mip's piece when it's
	// one of its own; where each mip is in its array, and where each is stored in the file and how many bytes
	ContentBuffer file;
	ContentBuffer[] pooledMips;
	int[] mipOffsets, filePositions, fileStored;
	int compression;

	/// <summary>Bytes of the arrays its mips are held in, until <see cref="Release"/> for a pooled read.</summary>
	public long HeldBytes
	{
		get
		{
			if ( pooledMips is null )
			{
				long whole = 0;
				foreach ( var mip in MipData ) whole += mip?.Length ?? 0;
				return whole;
			}

			long bytes = file.Capacity;
			foreach ( var piece in pooledMips ) bytes += piece.Capacity;
			return bytes;
		}
	}

	/// <summary>A read mip's bytes, every slice; decoded now if <see cref="ReadPooled"/> left it to be.</summary>
	public ReadOnlySpan<byte> Mip( int mip )
	{
		if ( MipData[mip] is null && !file.IsNull && mip >= TopMip ) DecodeMip( mip );
		return MipData[mip].AsSpan( mipOffsets?[mip] ?? 0, MipBytes( mip ) );
	}

	/// <summary>
	/// Give a pooled read's mips back (<see cref="ContentBuffers"/>) once they've been written to the GPU: its mips are gone
	/// after. Nothing for one that isn't pooled; once only.
	/// </summary>
	public void Release()
	{
		if ( !Pooled ) return;
		Pooled = false;
		for ( int mip = 0; mip < MipData.Length; mip++ )
		{
			ContentBuffers.Return( pooledMips[mip] );
			pooledMips[mip] = default;
			MipData[mip] = null;
		}

		ContentBuffers.Return( file );
		file = default;
	}

	/// <summary>
	/// Decode every mip <see cref="ReadPooled"/> left to be decoded, into <see cref="ContentBuffers"/> pieces, and let the
	/// file go once no mip is in it.
	/// </summary>
	public void Decode()
	{
		if ( file.IsNull ) return;
		for ( int mip = TopMip; mip < Mips; mip++ )
		{
			if ( MipData[mip] is null ) DecodeMip( mip );
		}

		LetFileGo();
	}

	void DecodeMip( int mip )
	{
		var size = MipBytes( mip );
		var piece = ContentBuffers.Rent( size );
		try
		{
			Decompress( file.Span.Slice( filePositions[mip], fileStored[mip] ), piece.Span, compression );
		}
		catch
		{
			ContentBuffers.Return( piece );
			throw;
		}

		pooledMips[mip] = piece;
		MipData[mip] = piece.Array;
		mipOffsets[mip] = piece.Offset;
	}

	// The file's piece back once no mip is read from it
	void LetFileGo()
	{
		if ( file.IsNull ) return;
		for ( int mip = TopMip; mip < Mips; mip++ )
		{
			if ( pooledMips[mip].IsNull ) return;
		}

		ContentBuffers.Return( file );
		file = default;
	}

	static void Decompress( ReadOnlySpan<byte> source, Span<byte> into, int compression )
	{
		var size = into.Length;
		if ( source.Length == size || compression == 0 ) source[..Math.Min( source.Length, size )].CopyTo( into );
		else if ( compression == 1 && Lz4.Decompress( source, into ) is var lz4 && lz4 != size ) throw new InvalidDataException( $"LZ4 block held {lz4} bytes, expected {size}" );
		else if ( compression != 1 && Zstd.Decompress( source, into ) is var zstd && zstd != size ) throw new InvalidDataException( $"Zstandard data held {zstd} bytes, expected {size}" );
	}

	/// <summary>The finest mip read: 0 unless it was read from <see cref="Tail"/> or a mip further down, or the file holds only coarser ones.</summary>
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
	/// <c>.vtex_c</c> bytes or as much of their start as <see cref="PrefixNeeded(byte[], int)"/> says. Bytes that end before
	/// <paramref name="top"/>'s mip read from the finest mip they hold whole (<see cref="TopMip"/>); throws when they don't
	/// hold even the last.
	/// </summary>
	public static TextureFile Read( byte[] data, int top ) => Read( data, top, pooled: false );

	/// <summary>
	/// As <see cref="Read(byte[], int)"/>; <paramref name="pooled"/> decodes the mips into <see cref="ContentBuffers"/>
	/// arrays, given back with <see cref="Release"/> once they're written, so streaming makes no garbage. A PNG's mips are
	/// never pooled.
	/// </summary>
	public static TextureFile Read( ReadOnlySpan<byte> data, int top, bool pooled ) => Read( data, top, pooled, default, true );

	/// <summary>
	/// As <see cref="Read(ReadOnlySpan{byte}, int, bool)"/> pooled, from a <see cref="ContentBuffers"/> piece of the file's
	/// bytes, which it takes (given back now, or with <see cref="Release"/>). Mips
	/// stored raw are read where they are in it rather than copied; <paramref name="decode"/> false leaves the compressed
	/// ones in it too, for <see cref="Stage"/> to decode straight into its upload buffer (or <see cref="Decode"/>, or
	/// <see cref="Mip"/> as it's asked for).
	/// </summary>
	public static TextureFile ReadPooled( ContentBuffer file, int top, bool decode = true )
	{
		try
		{
			return Read( file.Span, top, true, file, decode );
		}
		catch
		{
			ContentBuffers.Return( file );
			throw;
		}
	}

	static TextureFile Read( ReadOnlySpan<byte> data, int top, bool pooled, ContentBuffer owned, bool decode )
	{
		var header = new Header( data );
		top = Math.Clamp( top < 0 ? header.TailTop : top, 0, header.Mips - 1 );

		var mipData = new byte[header.Mips][];
		ContentBuffer[] pieces = null;
		int[] offsets = null, positions = null, storedSizes = null;
		var inFile = false;
		if ( header.Png )
		{
			var (width, height, rgba) = Png.Decode( data[header.HeaderBytes..] );
			if ( width != header.Width || height != header.Height ) throw new InvalidDataException( $"Texture's PNG is {width}x{height}, not {header.Width}x{header.Height}" );
			mipData[0] = rgba;
			for ( int mip = 1; mip < header.Mips; mip++ ) mipData[mip] = Halve( mipData[mip - 1], Math.Max( width >> (mip - 1), 1 ), Math.Max( height >> (mip - 1), 1 ) );
			top = 0;
			pooled = false;
		}
		else if ( header.GpuFormat != Format.Unknown )
		{
			if ( pooled )
			{
				pieces = new ContentBuffer[header.Mips];
				offsets = new int[header.Mips];
			}

			if ( !owned.IsNull )
			{
				positions = new int[header.Mips];
				storedSizes = new int[header.Mips];
			}

			try
			{
				var position = header.HeaderBytes;
				for ( int mip = header.Mips - 1; mip >= top; mip-- )
				{
					var size = header.Size( mip );
					var stored = header.Stored( mip );
					if ( header.Compression == 0 && stored < size ) throw new InvalidDataException( $"Uncompressed mip {mip} held {stored} bytes, expected {size}" );
					if ( (long)position + stored > data.Length )
					{
						// A file holding only its smaller mips is read as the mips it holds, from the first whose data is all there: a
						// web-streamed texture's prefix (its finer mips download later) or a package's web texture without its finest
						if ( mip == header.Mips - 1 ) throw new InvalidDataException( $"Texture data ends before mip {mip}" );
						top = mip + 1;
						break;
					}

					var source = data.Slice( position, stored );
					if ( !owned.IsNull )
					{
						positions[mip] = position;
						storedSizes[mip] = stored;
					}

					// Stored raw in a file it was given: read where it is. Compressed, left there when not decoding now
					if ( !owned.IsNull && (stored == size || header.Compression == 0) )
					{
						mipData[mip] = owned.Array;
						offsets[mip] = owned.Offset + position;
						inFile = true;
					}
					else if ( !owned.IsNull && !decode )
					{
						inFile = true;
					}
					else if ( pooled )
					{
						var piece = ContentBuffers.Rent( size );
						pieces[mip] = piece;
						mipData[mip] = piece.Array;
						offsets[mip] = piece.Offset;
						Decompress( source, piece.Span, header.Compression );
					}
					else
					{
						var bytes = new byte[size];
						mipData[mip] = bytes;
						Decompress( source, bytes, header.Compression );
					}

					position += stored;
				}
			}
			catch when ( pooled )
			{
				foreach ( var piece in pieces ) ContentBuffers.Return( piece );
				throw;
			}
		}
		else
		{
			pooled = false;
		}

		// A file nothing is read from any more goes back at once (its caller gave it up)
		if ( !owned.IsNull && !inFile )
		{
			ContentBuffers.Return( owned );
			owned = default;
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
			Pooled = pooled,
			pooledMips = pooled ? pieces : null,
			file = owned,
			mipOffsets = offsets,
			filePositions = positions,
			fileStored = storedSizes,
			compression = header.Compression,
		};
	}

	/// <summary>
	/// How many bytes from the start of a file <see cref="Read(byte[], int)"/> needs for mips from <paramref name="top"/>
	/// (or the tail), worked out from what <paramref name="prefix"/> holds. When that's more than the prefix, read that
	/// much and ask again: a prefix shorter than the header only says how long the header is.
	/// </summary>
	public static long PrefixNeeded( byte[] prefix, int top ) => PrefixNeeded( prefix.AsSpan(), top );

	/// <summary>As <see cref="PrefixNeeded(byte[], int)"/>, from the bytes read so far.</summary>
	public static long PrefixNeeded( ReadOnlySpan<byte> prefix, int top )
	{
		if ( prefix.Length < 16 ) return 16;
		var headerSize = BinaryPrimitives.ReadUInt32LittleEndian( prefix );
		if ( prefix.Length < headerSize ) return headerSize;

		var header = new Header( prefix );
		if ( header.GpuFormat == Format.Unknown ) return header.HeaderBytes;
		if ( header.Png ) return prefix.Length <= header.HeaderBytes ? header.HeaderBytes + 8 : header.HeaderBytes + Png.Length( prefix[header.HeaderBytes..] );
		top = Math.Clamp( top < 0 ? header.TailTop : top, 0, header.Mips - 1 );
		long bytes = header.HeaderBytes;
		for ( int mip = top; mip < header.Mips; mip++ ) bytes += header.Stored( mip );
		return bytes;
	}

	/// <summary>The <c>VTEX_Header_t</c>, and each mip's stored size, read from the file's bytes as they're needed.</summary>
	readonly ref struct Header
	{
		public readonly int Width, Height, Depth, SourceFormat, Mips, Slices, Compression, HeaderBytes, TailTop;
		public readonly bool Cube, Array, Volume, Png;
		public readonly Format GpuFormat;

		// Where the compressed mip sizes are listed and how many, if they are
		readonly ReadOnlySpan<byte> compressedSizes;

		public Header( ReadOnlySpan<byte> data )
		{
			var (headerBytes, offset, _) = ResourceFile.FindBlock( data, "DATA"u8 );
			HeaderBytes = headerBytes;
			var header = data[offset..];
			var flags = BinaryPrimitives.ReadUInt16LittleEndian( header[2..] );
			Width = BinaryPrimitives.ReadUInt16LittleEndian( header[20..] );
			Height = BinaryPrimitives.ReadUInt16LittleEndian( header[22..] );
			Depth = BinaryPrimitives.ReadUInt16LittleEndian( header[24..] );
			SourceFormat = header[26];
			Mips = Math.Max( (int)header[27], 1 );
			Png = SourceFormat is PngRgba8888 or PngDxt5;
			var extraStart = offset + 32;
			var extraOffset = BinaryPrimitives.ReadInt32LittleEndian( header[32..] );
			var extraCount = BinaryPrimitives.ReadInt32LittleEndian( header[36..] );

			Cube = (flags & FlagCube) != 0;
			Array = (flags & FlagArray) != 0;
			Volume = (flags & FlagVolume) != 0;
			Slices = (Array ? Math.Max( Depth, 1 ) : 1) * (Cube ? 6 : 1);
			GpuFormat = Png ? Format.RGBA8Unorm : FormatOf( SourceFormat );

			// The PNG is the first mip, the rest made from it (all the way down where the header lists one, as Source 2 mips
			// it on load); one image only
			if ( Png ) Mips = Slices > 1 || Volume ? 1 : Mips > 1 ? Mips : System.Numerics.BitOperations.Log2( (uint)Math.Max( Math.Max( Width, Height ), 1 ) ) + 1;

			// Compressed mip sizes, if any
			compressedSizes = default;
			Compression = 0;
			for ( int i = 0; i < extraCount; i++ )
			{
				var entry = extraStart + extraOffset + i * 12;
				var type = BinaryPrimitives.ReadInt32LittleEndian( data[entry..] );
				if ( type != 4 ) continue;

				var start = entry + 4 + BinaryPrimitives.ReadInt32LittleEndian( data[(entry + 4)..] );
				Compression = BinaryPrimitives.ReadInt32LittleEndian( data[start..] );
				var sizesStart = start + 4 + BinaryPrimitives.ReadInt32LittleEndian( data[(start + 4)..] );
				compressedSizes = data.Slice( sizesStart, BinaryPrimitives.ReadInt32LittleEndian( data[(start + 8)..] ) * 4 );
			}

			TailTop = Mips - 1;
			if ( GpuFormat == Format.Unknown || Png ) return;

			long tail = 0;
			for ( int mip = Mips - 1; mip >= 0; mip-- )
			{
				tail += Size( mip );
				if ( tail <= TailBytes ) TailTop = mip;
			}
		}

		/// <summary>A mip's bytes in the file: its compressed size where they're listed, else its decoded size.</summary>
		public int Stored( int mip )
		{
			if ( GpuFormat == Format.Unknown || Png ) return 0;
			return mip * 4 < compressedSizes.Length ? (int)BinaryPrimitives.ReadUInt32LittleEndian( compressedSizes[(mip * 4)..] ) : Size( mip );
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

	/// <summary>The next mip of 8-bit RGBA pixels <paramref name="width"/> by <paramref name="height"/>: each texel the average of up to 2x2.</summary>
	static byte[] Halve( byte[] pixels, int width, int height )
	{
		int w = Math.Max( width >> 1, 1 ), h = Math.Max( height >> 1, 1 );
		var mip = new byte[w * h * 4];
		for ( int y = 0; y < h; y++ )
		{
			int y0 = Math.Min( y * 2, height - 1 ), y1 = Math.Min( y * 2 + 1, height - 1 );
			for ( int x = 0; x < w; x++ )
			{
				int x0 = Math.Min( x * 2, width - 1 ), x1 = Math.Min( x * 2 + 1, width - 1 );
				int a = (y0 * width + x0) * 4, b = (y0 * width + x1) * 4, c = (y1 * width + x0) * 4, d = (y1 * width + x1) * 4;
				for ( int channel = 0; channel < 4; channel++ )
					mip[(y * w + x) * 4 + channel] = (byte)((pixels[a + channel] + pixels[b + channel] + pixels[c + channel] + pixels[d + channel] + 2) >> 2);
			}
		}

		return mip;
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
	/// shaders that read sRGB can. A write that fails (memory for its upload ran out) frees the texture before it throws,
	/// so a load tried again doesn't leave another behind each time.
	/// </summary>
	public TextureHandle Create( Device gpu, string name )
	{
		var texture = CreateEmpty( gpu, name );
		try
		{
			for ( int mip = TopMip; mip < Mips; mip++ ) Write( gpu, texture, mip );
		}
		catch
		{
			gpu.Destroy( texture );
			throw;
		}

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
		var data = Mip( mip );
		var size = data.Length / Slices;
		for ( int slice = 0; slice < Slices; slice++ ) gpu.WriteTexture( texture, data.Slice( slice * size, size ), mip - TopMip, slice );
		return data.Length;
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
			var data = Mip( mip );
			var size = data.Length / Slices;
			int width = Math.Max( Width >> mip, 1 ), height = Math.Max( Height >> mip, 1 );
			for ( int slice = 0; slice < Slices; slice++ ) list.UpdateTexture( texture, data.Slice( slice * size, size ), mip - TopMip, slice, width, height );
		}

		list.Barrier( TextureBarrier.Transition( texture, ImageLayout.CopyDestination, ImageLayout.ShaderRead, PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.AllCommands, MemoryAccess.ShaderRead ) );
		return texture;
	}

	/// <summary>Bytes <see cref="Stage"/> writes: every mip read, each slice from a 16-byte boundary.</summary>
	public long StagedBytes
	{
		get
		{
			long at = 0;
			for ( int mip = TopMip; mip < Mips; mip++ )
			{
				var size = MipBytes( mip ) / Slices;
				for ( int slice = 0; slice < Slices; slice++ ) at = Staged( at ) + size;
			}

			return at;
		}
	}

	static long Staged( long at ) => (at + 15) & ~15L;

	// An upload buffer's memory, or none where it isn't mapped (it's written through the device then)
	static Span<byte> Mapped( Device gpu, BufferHandle buffer )
	{
		try
		{
			return gpu.Map( buffer );
		}
		catch ( InvalidOperationException )
		{
			return default;
		}
	}

	/// <summary>
	/// Copy every mip read and slice into <paramref name="staging"/>, an upload buffer of at least <see cref="StagedBytes"/>,
	/// for <see cref="CopyStaged"/> to copy into <see cref="CreateEmpty"/>'s texture later. Any thread: what reads a big
	/// texture ahead stages it too, so the frame that makes it only records copies. Mips <see cref="ReadPooled"/> left
	/// compressed are decoded straight into it where it's mapped and their slices lie end to end there.
	/// </summary>
	public void Stage( Device gpu, BufferHandle staging )
	{
		long at = 0;
		var mapped = !file.IsNull ? Mapped( gpu, staging ) : default;
		for ( int mip = TopMip; mip < Mips; mip++ )
		{
			var bytes = MipBytes( mip );
			if ( MipData[mip] is null && !file.IsNull && mapped.Length > 0 && (Slices == 1 || bytes / Slices % 16 == 0) )
			{
				// Decoded in a scratch array given back at once (upload memory is slow to read, and decoders read what they
				// wrote), then copied
				at = Staged( at );
				var scratch = ContentBuffers.Rent( bytes );
				try
				{
					Decompress( file.Span.Slice( filePositions[mip], fileStored[mip] ), scratch.Span, compression );
					scratch.Span.CopyTo( mapped.Slice( (int)at, bytes ) );
				}
				finally
				{
					ContentBuffers.Return( scratch );
				}

				at += bytes;
				continue;
			}

			var data = Mip( mip );
			var size = data.Length / Slices;
			for ( int slice = 0; slice < Slices; slice++ )
			{
				at = Staged( at );
				gpu.WriteBuffer( staging, data.Slice( slice * size, size ), at );
				at += size;
			}
		}
	}

	/// <summary>
	/// Record copies of what <see cref="Stage"/> wrote into every mip and slice of <paramref name="texture"/>, as
	/// <see cref="Create(Device, string, CommandList)"/> writes them: readable (<see cref="ImageLayout.ShaderRead"/>) once the
	/// list has run.
	/// </summary>
	public void CopyStaged( CommandList list, BufferHandle staging, TextureHandle texture )
	{
		list.Barrier( TextureBarrier.Transition( texture, ImageLayout.Undefined, ImageLayout.CopyDestination, PipelineStage.AllCommands, MemoryAccess.None, PipelineStage.Transfer, MemoryAccess.TransferWrite ) );
		long at = 0;
		for ( int mip = TopMip; mip < Mips; mip++ )
		{
			var size = MipBytes( mip ) / Slices;
			int width = Math.Max( Width >> mip, 1 ), height = Math.Max( Height >> mip, 1 );
			for ( int slice = 0; slice < Slices; slice++ )
			{
				at = Staged( at );
				list.CopyBufferToTexture( staging, at, texture, mip - TopMip, slice, width, height );
				at += size;
			}
		}

		list.Barrier( TextureBarrier.Transition( texture, ImageLayout.CopyDestination, ImageLayout.ShaderRead, PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.AllCommands, MemoryAccess.ShaderRead ) );
	}
}

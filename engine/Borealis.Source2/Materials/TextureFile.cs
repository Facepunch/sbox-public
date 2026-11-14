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
/// whose stored size is its full size is stored raw). See <c>src/public/rendersystem/vtex_file_format.h</c>.
/// </summary>
public sealed class TextureFile
{
	const int FlagCube = 0x10, FlagVolume = 0x20, FlagArray = 0x40;

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

	/// <summary>The Borealis.Gpu format, or <see cref="Format.Unknown"/> for ones it has no form of (JPEG, PNG, ETC).</summary>
	public Format Format { get; private init; }

	/// <summary>Its shape.</summary>
	public TextureDimension Dimension { get; private init; }

	/// <summary>Each mip's data (all its slices, tightly packed), largest first.</summary>
	public byte[][] MipData { get; private init; }

	/// <summary>Read a texture from its <c>.vtex_c</c> bytes.</summary>
	public static TextureFile Read( byte[] data )
	{
		var resource = ResourceFile.Read( data );
		var (offset, _) = resource.Find( "DATA" );
		var header = new ByteReader( data, offset );
		header.UShort();
		var flags = header.UShort();
		header.Skip( 16 );
		var width = header.UShort();
		var height = header.UShort();
		var depth = header.UShort();
		var format = header.Byte();
		var mips = Math.Max( (int)header.Byte(), 1 );
		header.Int();
		var extraStart = header.Position;
		var extraOffset = header.Int();
		var extraCount = header.Int();

		var cube = (flags & FlagCube) != 0;
		var array = (flags & FlagArray) != 0;
		var volume = (flags & FlagVolume) != 0;
		var slices = (array ? Math.Max( (int)depth, 1 ) : 1) * (cube ? 6 : 1);
		var gpuFormat = FormatOf( format );

		// Compressed mip sizes, if any
		uint[] compressedSizes = null;
		var compression = 0;
		for ( int i = 0; i < extraCount; i++ )
		{
			var entry = extraStart + extraOffset + i * 12;
			var type = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( entry ) );
			if ( type != 4 ) continue;

			var start = entry + 4 + BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( entry + 4 ) );
			compression = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( start ) );
			var sizesStart = start + 4 + BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( start + 4 ) );
			compressedSizes = new uint[BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( start + 8 ) )];
			for ( int j = 0; j < compressedSizes.Length; j++ ) compressedSizes[j] = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( sizesStart + j * 4 ) );
		}

		var mipData = new byte[mips][];
		if ( gpuFormat != Format.Unknown )
		{
			var position = resource.HeaderSize;
			for ( int mip = mips - 1; mip >= 0; mip-- )
			{
				var w = Math.Max( width >> mip, 1 );
				var h = Math.Max( height >> mip, 1 );
				var d = volume ? Math.Max( depth >> mip, 1 ) : 1;
				var size = gpuFormat.RowPitch( w ) * gpuFormat.Rows( h ) * d * slices;

				var stored = compressedSizes is not null && mip < compressedSizes.Length ? (int)compressedSizes[mip] : size;
				var source = data.AsSpan( position, stored );
				mipData[mip] = stored == size || compression == 0 ? source.ToArray()
					: compression == 1 ? Lz4.Decompress( source, size )
					: Zstd.Decompress( source, size );
				position += stored;
			}
		}

		return new TextureFile
		{
			Width = width,
			Height = height,
			Depth = volume ? Math.Max( (int)depth, 1 ) : 1,
			Slices = slices,
			Mips = mips,
			SourceFormat = format,
			Format = gpuFormat,
			Dimension = volume ? TextureDimension.Texture3D : cube ? (array ? TextureDimension.TextureCubeArray : TextureDimension.TextureCube) : array ? TextureDimension.Texture2DArray : TextureDimension.Texture2D,
			MipData = mipData,
		};
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
	/// Make it a sampled texture on <paramref name="gpu"/> with every mip and slice filled; throws for formats there's
	/// no Borealis.Gpu form of. Formats with an sRGB form get both views, so shaders that read sRGB can.
	/// </summary>
	public TextureHandle Create( Device gpu, string name )
	{
		if ( Format == Format.Unknown ) throw new NotSupportedException( $"{name}: VTEX format {SourceFormat} has no Borealis.Gpu form" );

		var info = new TextureInfo
		{
			Width = Width,
			Height = Height,
			Depth = Depth,
			Layers = Dimension is TextureDimension.TextureCube or TextureDimension.TextureCubeArray ? Slices / 6 : Slices,
			Mips = Mips,
			Samples = 1,
			Dimension = Dimension,
			Format = Format,
			Usage = TextureUsage.Sampled | TextureUsage.CopyDestination | TextureUsage.CopySource,
			Name = name,
		};

		var texture = gpu.CreateTexture( info );
		for ( int mip = 0; mip < Mips; mip++ )
		{
			var size = MipData[mip].Length / Slices;
			for ( int slice = 0; slice < Slices; slice++ ) gpu.WriteTexture( texture, MipData[mip].AsSpan( slice * size, size ), mip, slice );
		}

		return texture;
	}

	/// <summary>
	/// As <see cref="Create(Device, string)"/>, with every mip and slice written by <paramref name="list"/> instead of a
	/// submission that waits: readable (<see cref="ImageLayout.ShaderRead"/>) once the list has run.
	/// </summary>
	public TextureHandle Create( Device gpu, string name, CommandList list )
	{
		if ( Format == Format.Unknown ) throw new NotSupportedException( $"{name}: VTEX format {SourceFormat} has no Borealis.Gpu form" );

		var texture = gpu.CreateTexture( new TextureInfo
		{
			Width = Width,
			Height = Height,
			Depth = Depth,
			Layers = Dimension is TextureDimension.TextureCube or TextureDimension.TextureCubeArray ? Slices / 6 : Slices,
			Mips = Mips,
			Samples = 1,
			Dimension = Dimension,
			Format = Format,
			Usage = TextureUsage.Sampled | TextureUsage.CopyDestination | TextureUsage.CopySource,
			Name = name,
		} );

		list.Barrier( TextureBarrier.Transition( texture, ImageLayout.Undefined, ImageLayout.CopyDestination, PipelineStage.AllCommands, MemoryAccess.None, PipelineStage.Transfer, MemoryAccess.TransferWrite ) );
		for ( int mip = 0; mip < Mips; mip++ )
		{
			var size = MipData[mip].Length / Slices;
			int width = Math.Max( Width >> mip, 1 ), height = Math.Max( Height >> mip, 1 );
			for ( int slice = 0; slice < Slices; slice++ ) list.UpdateTexture( texture, MipData[mip].AsSpan( slice * size, size ), mip, slice, width, height );
		}

		list.Barrier( TextureBarrier.Transition( texture, ImageLayout.CopyDestination, ImageLayout.ShaderRead, PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.AllCommands, MemoryAccess.ShaderRead ) );
		return texture;
	}
}

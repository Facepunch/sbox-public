using Borealis.Source2.Resources;
using System.Buffers.Binary;
using System.IO;
using System.Numerics;
using System.Text;

namespace Borealis.Source2.Materials;

/// <summary>
/// A compiled texture's <c>VTEX_Header_t</c> as written, without its mips: what Source 2's texture manager describes a
/// loaded texture with (<c>VTEX_InitCreationDescFromHeader</c>), its metadata (<c>VTEX_ExtraMetaData_t</c>) and its sprite
/// sheet (<c>Sheet_t</c>). It's all in the resource's blocks, before the streamed mips, so a file's first
/// <see cref="BytesNeeded"/> bytes are enough.
/// </summary>
public sealed class TextureHeader
{
	/// <summary>Size as stored.</summary>
	public int Width { get; private init; }

	/// <summary>Size as stored.</summary>
	public int Height { get; private init; }

	/// <summary>Depth of a volume, or slice count of an array, as stored.</summary>
	public int Depth { get; private init; }

	/// <summary><c>VTEX_Format_t</c>.</summary>
	public int Format { get; private init; }

	/// <summary>Mip levels as stored.</summary>
	public int Mips { get; private init; }

	/// <summary><c>VTEX_Flags_t</c>.</summary>
	public int Flags { get; private init; }

	/// <summary>Whether it has metadata (<c>VTEX_EXTRA_DATA_METADATA</c>).</summary>
	public bool HasMetadata { get; private init; }

	/// <summary>The metadata's display rectangle, or 0 when it has none.</summary>
	public int DisplayWidth { get; private init; }

	/// <summary>The metadata's display rectangle, or 0 when it has none.</summary>
	public int DisplayHeight { get; private init; }

	/// <summary>The metadata's motion vector distance.</summary>
	public int MotionVectorsMaxDistance { get; private init; }

	/// <summary>Its sprite sheet's sequences, or null when it has no sheet.</summary>
	public IReadOnlyList<SheetSequence> Sheet { get; private init; }

	/// <summary>
	/// How many bytes from the start of a file <see cref="Read"/> needs, worked out from what <paramref name="prefix"/>
	/// holds: a prefix shorter than the resource header's first field only says to read that much first.
	/// </summary>
	public static int BytesNeeded( ReadOnlySpan<byte> prefix ) => prefix.Length < 16 ? 16 : (int)BinaryPrimitives.ReadUInt32LittleEndian( prefix );

	/// <summary>Read the header from at least the first <see cref="BytesNeeded"/> bytes of a <c>.vtex_c</c>.</summary>
	public static TextureHeader Read( byte[] data )
	{
		var resource = ResourceFile.Read( data );
		var (offset, size) = resource.Find( "DATA" );
		if ( size < 40 ) throw new InvalidDataException( "Texture DATA is shorter than its header" );

		var header = new ByteReader( data, offset, offset + size );
		var version = header.UShort();
		if ( version != 1 ) throw new InvalidDataException( $"Texture header version {version}, expected 1" );
		var flags = header.UShort();
		header.Skip( 16 );
		var width = header.UShort();
		var height = header.UShort();
		var depth = header.UShort();
		var format = header.Byte();
		var mips = header.Byte();
		header.Int();
		var extraStart = header.Position;
		var extraOffset = header.Int();
		var extraCount = header.Int();

		int displayWidth = 0, displayHeight = 0, motionVectors = 0;
		var hasMetadata = false;
		List<SheetSequence> sheet = null;
		for ( int i = 0; i < extraCount; i++ )
		{
			var entry = extraStart + extraOffset + i * 12;
			Check( entry, 12, data.Length );
			var type = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( entry ) );
			var start = entry + 4 + BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( entry + 4 ) );
			var length = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( entry + 8 ) );
			if ( length < 0 || start < 0 || (long)start + length > data.Length ) throw new InvalidDataException( $"Texture extra data {type} runs off the end" );

			if ( type == MetadataType && length >= 8 )
			{
				hasMetadata = true;
				displayWidth = BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( start + 2 ) );
				displayHeight = BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( start + 4 ) );
				motionVectors = BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( start + 6 ) );
			}
			else if ( type == SheetType )
			{
				sheet = ReadSheet( data, start, start + length );
			}
		}

		return new TextureHeader
		{
			Width = width,
			Height = height,
			Depth = depth,
			Format = format,
			Mips = mips,
			Flags = flags,
			HasMetadata = hasMetadata,
			DisplayWidth = displayWidth,
			DisplayHeight = displayHeight,
			MotionVectorsMaxDistance = motionVectors,
			Sheet = sheet,
		};
	}

	// VTEX_ExtraDataType_t
	const int SheetType = 2, MetadataType = 3;

	/// <summary>A <c>Sheet_t</c> as a resource stream writes it: arrays and strings are offsets from where they're stored.</summary>
	static List<SheetSequence> ReadSheet( byte[] data, int start, int end )
	{
		var (sequencesAt, sequenceCount) = Array( data, start, end );
		var sequences = new List<SheetSequence>( sequenceCount );
		for ( int i = 0; i < sequenceCount; i++ )
		{
			// SheetSequence_t: id, four bools, frames, total time, name, float params (32 bytes)
			var at = sequencesAt + i * 32;
			Check( at, 32, end );
			var (framesAt, frameCount) = Array( data, at + 8, end );
			var frames = new SheetFrame[frameCount];
			for ( int f = 0; f < frameCount; f++ )
			{
				// SheetSequenceFrame_t: display time, images (12 bytes); SheetFrameImage_t: uvCropped[2], uvUncropped[2]
				var frameAt = framesAt + f * 12;
				Check( frameAt, 12, end );
				var (imagesAt, imageCount) = Array( data, frameAt + 4, end );
				var images = new SheetImage[imageCount];
				for ( int m = 0; m < imageCount; m++ )
				{
					var imageAt = imagesAt + m * 32;
					Check( imageAt, 32, end );
					images[m] = new SheetImage( Vector2At( data, imageAt ), Vector2At( data, imageAt + 8 ), Vector2At( data, imageAt + 16 ), Vector2At( data, imageAt + 24 ) );
				}

				frames[f] = new SheetFrame( BinaryPrimitives.ReadSingleLittleEndian( data.AsSpan( frameAt ) ), images );
			}

			sequences.Add( new SheetSequence
			{
				Id = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( at ) ),
				Clamp = data[at + 4] != 0,
				AlphaCrop = data[at + 5] != 0,
				NoColor = data[at + 6] != 0,
				NoAlpha = data[at + 7] != 0,
				Frames = frames,
				TotalTime = BinaryPrimitives.ReadSingleLittleEndian( data.AsSpan( at + 16 ) ),
				Name = String( data, at + 20, end ),
			} );
		}

		return sequences;
	}

	static void Check( int at, int size, int end )
	{
		if ( at < 0 || (long)at + size > end ) throw new InvalidDataException( "Texture header runs off its data" );
	}

	/// <summary>A <c>CResourceArray</c> at <paramref name="at"/>: where its items start and how many.</summary>
	static (int Start, int Count) Array( byte[] data, int at, int end )
	{
		Check( at, 8, end );
		var count = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( at + 4 ) );
		if ( count == 0 ) return (0, 0);
		if ( count < 0 ) throw new InvalidDataException( "Texture sheet has a negative count" );
		return (at + BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( at ) ), count);
	}

	/// <summary>A <c>CResourceString</c> at <paramref name="at"/>, or null.</summary>
	static string String( byte[] data, int at, int end )
	{
		Check( at, 4, end );
		var offset = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( at ) );
		if ( offset == 0 ) return null;
		var start = at + offset;
		if ( start < 0 || start >= end ) throw new InvalidDataException( "Texture sheet string runs off its data" );
		var length = data.AsSpan( start, end - start ).IndexOf( (byte)0 );
		return Encoding.UTF8.GetString( data, start, length < 0 ? end - start : length );
	}

	static Vector2 Vector2At( byte[] data, int at ) => new( BinaryPrimitives.ReadSingleLittleEndian( data.AsSpan( at ) ), BinaryPrimitives.ReadSingleLittleEndian( data.AsSpan( at + 4 ) ) );
}

/// <summary>A sprite sheet's sequence (<c>SheetSequence_t</c>).</summary>
public sealed class SheetSequence
{
	/// <summary>The sequence number it was authored with.</summary>
	public uint Id { get; init; }

	/// <summary>Holds the last frame instead of looping.</summary>
	public bool Clamp { get; init; }

	/// <summary>Frames were cropped to their alpha.</summary>
	public bool AlphaCrop { get; init; }

	/// <summary>Alpha only.</summary>
	public bool NoColor { get; init; }

	/// <summary>Colour only.</summary>
	public bool NoAlpha { get; init; }

	/// <summary>Its frames in order.</summary>
	public IReadOnlyList<SheetFrame> Frames { get; init; }

	/// <summary>How long it plays, in seconds.</summary>
	public float TotalTime { get; init; }

	/// <summary>Its name, or null.</summary>
	public string Name { get; init; }
}

/// <summary>A sheet frame (<c>SheetSequenceFrame_t</c>): how long it shows, and its image in each layer.</summary>
public readonly record struct SheetFrame( float DisplayTime, IReadOnlyList<SheetImage> Images );

/// <summary>A frame's image (<c>SheetFrameImage_t</c>): its rectangle in the texture, cropped and not, as UV corners.</summary>
public readonly record struct SheetImage( Vector2 CroppedMin, Vector2 CroppedMax, Vector2 UncroppedMin, Vector2 UncroppedMax );

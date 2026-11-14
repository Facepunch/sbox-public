using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Borealis.Source2.Resources;

/// <summary>
/// A compiled Source 2 resource (<c>.shader_c</c>, <c>.vmat_c</c>, <c>.vtex_c</c>...): a header and a table of
/// four-character blocks. See <c>ResourceFileHeader_t</c> and <c>Sandbox.Resources.ResourceWriter</c>.
/// </summary>
public sealed class ResourceFile
{
	/// <summary>The file's bytes.</summary>
	public byte[] Data { get; }

	/// <summary>Bytes before the streamed data (texture mips follow them).</summary>
	public int HeaderSize { get; }

	/// <summary>The resource type's version (<c>VFX_SHADER_RESOURCE_VERSION</c> for shaders).</summary>
	public int ResourceVersion { get; }

	readonly Dictionary<string, (int Offset, int Size)> blocks = new();

	ResourceFile( byte[] data )
	{
		Data = data;
		if ( data.Length < 16 ) throw new InvalidDataException( "Too small to be a resource" );

		HeaderSize = (int)BinaryPrimitives.ReadUInt32LittleEndian( data );
		var headerVersion = BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( 4 ) );
		if ( headerVersion != 12 ) throw new InvalidDataException( $"Resource header version {headerVersion}, expected 12" );
		ResourceVersion = BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( 6 ) );

		var entry = 8 + BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( 8 ) );
		var count = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( 12 ) );
		for ( int i = 0; i < count; i++, entry += 12 )
		{
			if ( entry + 12 > data.Length ) throw new InvalidDataException( "Resource block table runs off the end" );
			var name = Encoding.ASCII.GetString( data, entry, 4 );
			var offset = entry + 4 + BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( entry + 4 ) );
			var size = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( entry + 8 ) );
			if ( offset < 0 || (long)offset + size > data.Length ) throw new InvalidDataException( $"Resource block {name} runs off the end" );
			blocks.TryAdd( name, (offset, size) );
		}
	}

	/// <summary>Read a resource from its bytes.</summary>
	public static ResourceFile Read( byte[] data ) => new( data );

	/// <summary>Read a resource from disk.</summary>
	public static ResourceFile Load( string path ) => new( File.ReadAllBytes( path ) );

	/// <summary>Whether it has a block of this type.</summary>
	public bool Has( string block ) => blocks.ContainsKey( block );

	/// <summary>Where a block starts in <see cref="Data"/> and how long it is; throws if there's none.</summary>
	public (int Offset, int Size) Find( string block ) => blocks.TryGetValue( block, out var found ) ? found : throw new InvalidDataException( $"Resource has no {block} block" );

	/// <summary>A block's bytes, or empty when there's none.</summary>
	public ReadOnlySpan<byte> Block( string block ) => blocks.TryGetValue( block, out var found ) ? Data.AsSpan( found.Offset, found.Size ) : default;

	/// <summary>The block types it has.</summary>
	public IEnumerable<string> Blocks => blocks.Keys;
}

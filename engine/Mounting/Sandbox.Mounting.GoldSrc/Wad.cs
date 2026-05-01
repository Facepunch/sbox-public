using System;
using System.Text;

public class WadHeader
{
	public string Magic;
	public int LumpCount;
	public int DirectoryOffset;
}

public class WadLump
{
	public int Offset;
	public int DiskSize;
	public int Size;
	public char Type;
	public char Compression;
	public short Padding;
	public string Name;
}

public class Wad
{
	public WadHeader Header { get; private set; }
	public List<WadLump> Lumps { get; private set; } = [];

	private string _filePath;

	public void LoadWadFile( string filePath )
	{
		_filePath = filePath;

		using var reader = new BinaryReader( File.OpenRead( filePath ), Encoding.ASCII );

		Header = new WadHeader
		{
			Magic = new string( reader.ReadChars( 4 ) ),
			LumpCount = reader.ReadInt32(),
			DirectoryOffset = reader.ReadInt32()
		};

		if ( Header.Magic != "WAD3" )
		{
			throw new Exception( "Invalid WAD file: Not a WAD3 format." );
		}

		reader.BaseStream.Seek( Header.DirectoryOffset, SeekOrigin.Begin );

		for ( int i = 0; i < Header.LumpCount; i++ )
		{
			Lumps.Add( new WadLump
			{
				Offset = reader.ReadInt32(),
				DiskSize = reader.ReadInt32(),
				Size = reader.ReadInt32(),
				Type = reader.ReadChar(),
				Compression = reader.ReadChar(),
				Padding = reader.ReadInt16(),
				Name = Encoding.ASCII.GetString( reader.ReadBytes( 16 ) ).TrimEnd( '\0' )
			} );
		}
	}

	public byte[] GetLumpData( string lumpName )
	{
		var lump = Lumps.Find( l => l.Name.Equals( lumpName, StringComparison.OrdinalIgnoreCase ) );
		if ( lump == null )
		{
			return null;
		}

		using BinaryReader reader = new BinaryReader( File.OpenRead( _filePath ) );
		reader.BaseStream.Seek( lump.Offset, SeekOrigin.Begin );
		return reader.ReadBytes( lump.DiskSize );
	}
}

using System;
using System.Text;

namespace PakLib;

struct PackHeader
{
	public byte[] Head;
	public byte[] IndexOffset;
	public byte[] IndexLength;

	public PackHeader()
	{
		Head = new byte[4];
		IndexOffset = new byte[4];
		IndexLength = new byte[4];
	}
}

public class PackFile
{
	public string FileName;
	public string FilePath;
	public int FilePosition;
	public int FileLength;

	public string FullPath => Path.Combine( FilePath, FileName ).Replace( '\\', '/' );
}

public class Pack : IDisposable
{
	private readonly FileStream packStream;
	private readonly BinaryReader br;
	private readonly Dictionary<string, PackFile> fileLookup = [];

	private PackHeader header;
	public List<PackFile> Files { get; private set; } = [];
	public byte[] Palette;
	public string Path { get; }
	public string Name => System.IO.Path.GetFileName( Path );

	public int NumFiles => Files.Count;
	public bool IsValid { get; private set; }

	public Pack( string path )
	{
		Path = path;
		packStream = File.OpenRead( path );
		br = new BinaryReader( packStream );

		if ( !ReadHeader() )
			return;

		var numFiles = BitConverter.ToInt32( header.IndexLength, 0 ) / 64;
		ReadPack( numFiles );

		IsValid = true;
	}

	private bool ReadHeader()
	{
		header = new PackHeader();
		var raw = new byte[12];
		packStream.Seek( 0, SeekOrigin.Begin );
		packStream.ReadExactly( raw, 0, 12 );

		Array.Copy( raw, 0, header.Head, 0, 4 );
		Array.Copy( raw, 4, header.IndexOffset, 0, 4 );
		Array.Copy( raw, 8, header.IndexLength, 0, 4 );

		if ( header.Head[0] != 'P' || header.Head[1] != 'A' || header.Head[2] != 'C' || header.Head[3] != 'K' )
			return false;

		if ( BitConverter.ToInt32( header.IndexLength, 0 ) % 64 != 0 )
			return false;

		return true;
	}

	private void ReadPack( int numFiles )
	{
		var seekPosition = BitConverter.ToInt32( header.IndexOffset, 0 );
		var indexData = new byte[64];
		Files = new List<PackFile>( numFiles );

		for ( var i = 0; i < numFiles; i++ )
		{
			packStream.Seek( seekPosition, SeekOrigin.Begin );
			packStream.ReadExactly( indexData, 0, 64 );

			var fullPath = Encoding.ASCII.GetString( indexData, 0, 56 ).TrimEnd( '\0' );
			var file = new PackFile
			{
				FilePosition = BitConverter.ToInt32( indexData, 56 ),
				FileLength = BitConverter.ToInt32( indexData, 60 ),
				FileName = System.IO.Path.GetFileName( fullPath ),
				FilePath = System.IO.Path.GetDirectoryName( fullPath )?.Replace( '\\', '/' ) ?? string.Empty
			};

			Files.Add( file );
			fileLookup[file.FullPath] = file;

			if ( file.FileName.EndsWith( ".wad", StringComparison.OrdinalIgnoreCase ) )
				ReadWad( file.FilePosition );

			seekPosition += 64;
		}
	}

	private void ReadWad( int seekPosition )
	{
		packStream.Seek( seekPosition, SeekOrigin.Begin );

		var magic = Encoding.ASCII.GetString( br.ReadBytes( 4 ) );
		if ( magic != "WAD2" )
			throw new Exception( "Invalid WAD file: Expected 'WAD2' magic." );

		var numEntries = br.ReadInt32();
		var directoryOffset = br.ReadInt32();
		packStream.Seek( seekPosition + directoryOffset, SeekOrigin.Begin );

		for ( var i = 0; i < numEntries; i++ )
		{
			var offset = seekPosition + br.ReadInt32();
			var dsize = br.ReadInt32();
			var size = br.ReadInt32();
			var type = br.ReadByte();
			var cmprs = br.ReadByte();
			var dummy = br.ReadInt16();
			var name = Encoding.ASCII.GetString( br.ReadBytes( 16 ) ).TrimEnd( '\0' );

			if ( type != 0x42 )
				continue;

			var file = new PackFile
			{
				FilePosition = offset,
				FileLength = size,
				FileName = $"{name}.lmp",
				FilePath = "wad"
			};

			Files.Add( file );
			fileLookup[file.FullPath] = file;
		}
	}

	public byte[] GetFileBytes( string filename )
	{
		return GetFileBytes( filename, -1 );
	}

	public byte[] GetFileBytes( string filename, int maxLength )
	{
		if ( !IsValid || string.IsNullOrEmpty( filename ) )
			return null;

		if ( !fileLookup.TryGetValue( filename, out var file ) )
			return null;

		var bytesToRead = maxLength >= 0
			? Math.Min( maxLength, file.FileLength )
			: file.FileLength;

		var fileBytes = new byte[bytesToRead];
		packStream.Seek( file.FilePosition, SeekOrigin.Begin );
		packStream.ReadExactly( fileBytes, 0, bytesToRead );
		return fileBytes;
	}

	public string GetFilePath( string filename )
	{
		if ( !IsValid || string.IsNullOrEmpty( filename ) )
			return null;

		if ( !fileLookup.TryGetValue( filename, out var file ) )
			return null;

		return file.FullPath;
	}

	public bool FileExists( string filename )
	{
		if ( !IsValid || string.IsNullOrEmpty( filename ) )
			return false;

		return fileLookup.ContainsKey( filename );
	}

	public void Dispose()
	{
		packStream.Dispose();
		br.Dispose();
		GC.SuppressFinalize( this );
	}
}

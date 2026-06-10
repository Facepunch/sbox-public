using System;
using System.IO;
using System.Text;

namespace RasLib;

public class RasFile
{
	public string FileName;
	public string FilePath;
	public long FilePosition;
	public int FileLength;
	public int StoredLength;

	public string FullPath => System.IO.Path.Combine( FilePath, FileName ).Replace( '\\', '/' );
}

public class Archive : IDisposable
{
	private readonly FileStream rasStream;

	private readonly Dictionary<string, RasFile> fileLookup = new( StringComparer.OrdinalIgnoreCase );

	public List<RasFile> Files { get; private set; } = [];
	public string Path { get; }
	public string Name => System.IO.Path.GetFileName( Path );

	public int NumFiles => Files.Count;
	public bool IsValid { get; private set; }

	public Archive( string path )
	{
		Path = path;
		rasStream = File.OpenRead( path );

		if ( !ReadArchive() )
			return;

		IsValid = true;
	}

	private bool ReadArchive()
	{
		using var br = new BinaryReader( rasStream, Encoding.Latin1, leaveOpen: true );

		rasStream.Seek( 0, SeekOrigin.Begin );
		if ( br.ReadUInt32() != 0x00534152 )
			return false;

		var seed = br.ReadInt32();

		var header = br.ReadBytes( 0x24 );
		Decrypt( header, seed );

		var numFiles = BitConverter.ToInt32( header, 0 );
		var numFolders = BitConverter.ToInt32( header, 4 );
		var fileTableLength = BitConverter.ToInt32( header, 8 );
		var folderTableLength = BitConverter.ToInt32( header, 12 );
		var version = BitConverter.ToSingle( header, 16 );

		if ( numFiles < 0 || numFolders <= 0 || fileTableLength <= 0 || folderTableLength <= 0 )
			return false;

		var fileTable = br.ReadBytes( fileTableLength );
		Decrypt( fileTable, seed );

		var folderTable = br.ReadBytes( folderTableLength );
		Decrypt( folderTable, seed );

		var dataStart = rasStream.Position;
		var legacy = version < 1.25f;

		var folders = new string[numFolders];
		var pos = 0;
		for ( var i = 0; i < numFolders; i++ )
		{
			folders[i] = ReadString( folderTable, ref pos );
			if ( legacy ) pos += 16;
		}

		Files = new List<RasFile>( numFiles );
		pos = 0;
		var runningOffset = dataStart;

		for ( var i = 0; i < numFiles; i++ )
		{
			var name = ReadString( fileTable, ref pos );
			var file = new RasFile { FileName = name };

			if ( legacy )
			{
				file.FileLength = BitConverter.ToInt32( fileTable, pos );
				file.StoredLength = BitConverter.ToInt32( fileTable, pos + 4 );
				file.FilePath = FolderName( folders, BitConverter.ToInt32( fileTable, pos + 12 ) );
				file.FilePosition = runningOffset;
				runningOffset += file.StoredLength;
				pos += 40;
			}
			else
			{
				file.FileLength = BitConverter.ToInt32( fileTable, pos );
				file.FilePosition = BitConverter.ToUInt32( fileTable, pos + 4 );
				file.FilePath = FolderName( folders, BitConverter.ToInt32( fileTable, pos + 8 ) );
				file.StoredLength = file.FileLength;
				pos += 12;
			}

			Files.Add( file );
			fileLookup[file.FullPath] = file;
		}

		return true;
	}

	private static string ReadString( byte[] data, ref int pos )
	{
		var start = pos;
		while ( data[pos] != 0 ) pos++;
		return Encoding.ASCII.GetString( data, start, pos++ - start );
	}

	private static string FolderName( string[] folders, int index )
	{
		if ( index < 0 || index >= folders.Length )
			return string.Empty;

		return folders[index].Trim( '\\', '/' ).Replace( '\\', '/' );
	}

	public byte[] GetFileBytes( string filename )
	{
		if ( !IsValid || string.IsNullOrEmpty( filename ) )
			return null;

		if ( !fileLookup.TryGetValue( filename, out var file ) )
			return null;

		return GetFileBytes( file );
	}

	public byte[] GetFileBytes( RasFile file )
	{
		var stored = new byte[file.StoredLength];
		rasStream.Seek( file.FilePosition, SeekOrigin.Begin );
		rasStream.ReadExactly( stored, 0, file.StoredLength );

		if ( stored.Length >= 12 && stored[0] == 'R' && stored[1] == 'A' && stored[2] == '-' && stored[3] == '>' )
		{
			var length = BitConverter.ToInt32( stored, 4 );
			return Decompress( stored, 12, stored.Length - 12, length );
		}

		return stored;
	}

	public bool FileExists( string filename )
	{
		if ( !IsValid || string.IsNullOrEmpty( filename ) )
			return false;

		return fileLookup.ContainsKey( filename );
	}

	// Stream cipher reverse engineered by Ekey (zenhax.com/viewtopic.php?t=2717)
	private static void Decrypt( byte[] data, int seed )
	{
		if ( seed == 0 )
			seed = 1;

		unchecked
		{
			for ( var i = 0; i < data.Length; i++ )
			{
				seed = -2 * (seed / 177) + 171 * (seed % 177);

				var rot = i % 5;
				var rolled = (byte)((data[i] << rot) | (data[i] >> (8 - rot)));
				var mixed = (byte)(rolled ^ (byte)((i + 3) * 6));

				data[i] = (byte)(mixed + (byte)seed);
			}
		}
	}

	// LZSS with a zero-initialized 4KB ring buffer ("RA->" blocks)
	private static byte[] Decompress( byte[] src, int srcOffset, int srcLength, int dstLength )
	{
		var dst = new byte[dstLength];
		var ring = new byte[4096];
		var r = 4096 - 18;

		var ip = srcOffset;
		var end = srcOffset + srcLength;
		var op = 0;
		var flags = 0;

		while ( op < dstLength && ip < end )
		{
			flags >>= 1;
			if ( (flags & 0x100) == 0 )
				flags = src[ip++] | 0xFF00;

			if ( (flags & 1) != 0 )
			{
				var b = src[ip++];
				dst[op++] = b;
				ring[r] = b;
				r = (r + 1) & 0xFFF;
			}
			else
			{
				if ( ip + 1 >= end )
					break;

				int low = src[ip++];
				int high = src[ip++];

				var offset = low | ((high & 0xF0) << 4);
				var length = (high & 0x0F) + 3;

				for ( var i = 0; i < length && op < dstLength; i++ )
				{
					var b = ring[(offset + i) & 0xFFF];
					dst[op++] = b;
					ring[r] = b;
					r = (r + 1) & 0xFFF;
				}
			}
		}

		return dst;
	}

	public void Dispose()
	{
		rasStream.Dispose();
		GC.SuppressFinalize( this );
	}
}

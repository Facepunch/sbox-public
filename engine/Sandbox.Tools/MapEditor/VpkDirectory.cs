using System;
using System.IO;
using System.Text;

namespace Editor.MapEditor;

/// <summary>
/// Reads the file listing of a .vpk - each file's path and size, without extracting anything.
/// Enough to see what a compiled map is made of.
/// </summary>
internal static class VpkDirectory
{
	const uint Signature = 0x55AA1234;

	/// <summary>
	/// Every file in the vpk with its size in bytes.
	/// </summary>
	public static List<(string Path, long Size)> Read( string vpkPath )
	{
		using var stream = File.OpenRead( vpkPath );
		using var reader = new BinaryReader( stream );

		if ( reader.ReadUInt32() != Signature )
			throw new InvalidDataException( $"{vpkPath} isn't a vpk" );

		var version = reader.ReadUInt32();
		var treeSize = reader.ReadUInt32();

		if ( version == 2 )
		{
			// file data, archive md5, other md5 and signature section sizes
			reader.ReadBytes( 16 );
		}
		else if ( version != 1 )
		{
			throw new InvalidDataException( $"Unsupported vpk version {version}" );
		}

		var treeEnd = stream.Position + treeSize;
		var files = new List<(string, long)>();

		while ( stream.Position < treeEnd )
		{
			var extension = ReadString( reader );
			if ( extension.Length == 0 ) break;

			while ( true )
			{
				var directory = ReadString( reader );
				if ( directory.Length == 0 ) break;

				while ( true )
				{
					var name = ReadString( reader );
					if ( name.Length == 0 ) break;

					reader.ReadUInt32(); // crc
					var preloadBytes = reader.ReadUInt16();
					reader.ReadUInt16(); // archive index
					reader.ReadUInt32(); // offset
					var length = reader.ReadUInt32();
					reader.ReadUInt16(); // terminator

					stream.Seek( preloadBytes, SeekOrigin.Current );

					var path = directory == " " ? $"{name}.{extension}" : $"{directory}/{name}.{extension}";
					files.Add( (path, preloadBytes + (long)length) );
				}
			}
		}

		return files;
	}

	static string ReadString( BinaryReader reader )
	{
		var bytes = new List<byte>();

		for ( var b = reader.ReadByte(); b != 0; b = reader.ReadByte() )
		{
			bytes.Add( b );
		}

		return Encoding.UTF8.GetString( bytes.ToArray() );
	}
}

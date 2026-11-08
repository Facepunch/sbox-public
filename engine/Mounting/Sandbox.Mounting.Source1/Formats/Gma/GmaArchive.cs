using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Sandbox.Mounting.Source1;

internal sealed class GmaArchive : IContentArchive
{
	private readonly string filename;
	private readonly Dictionary<string, Entry> files = new( StringComparer.OrdinalIgnoreCase );
	private readonly long dataOffset;
	private readonly object archiveLock = new();
	private ArchiveFile archive;
	private bool disposed;

	private readonly record struct Entry( long Offset, long Length );

	public GmaArchive( string filename )
	{
		this.filename = filename;
		using var stream = File.OpenRead( filename );
		using var reader = new BinaryReader( stream );
		if ( stream.Length < 21 || reader.ReadUInt32() != 0x44414d47 )
			throw new InvalidDataException( $"Invalid GMA header: {filename}" );
		var version = reader.ReadByte();
		if ( version is < 1 or > 3 )
			throw new NotSupportedException( $"Unsupported GMA version {version}: {filename}" );

		reader.ReadUInt64();
		reader.ReadUInt64();
		if ( version > 1 )
			while ( SkipString( reader ) ) { }
		SkipString( reader );
		SkipString( reader );
		SkipString( reader );
		reader.ReadInt32();

		var name = new ArrayBufferWriter<byte>();
		long offset = 0;
		while ( reader.ReadUInt32() != 0 )
		{
			name.Clear();
			byte value;
			while ( (value = reader.ReadByte()) != 0 )
			{
				name.GetSpan( 1 )[0] = value;
				name.Advance( 1 );
			}
			var path = Encoding.UTF8.GetString( name.WrittenSpan ).Replace( '\\', '/' );
			ValidatePath( path );
			var length = reader.ReadInt64();
			reader.ReadUInt32();
			if ( length < 0 || length > stream.Length - offset )
				throw new InvalidDataException( $"Invalid GMA entry size for '{path}' in '{filename}'." );
			if ( !files.TryAdd( path, new Entry( offset, length ) ) )
				throw new InvalidDataException( $"Duplicate GMA entry '{path}' in '{filename}'." );
			offset += length;
		}
		dataOffset = stream.Position;
		if ( offset > stream.Length - dataOffset )
			throw new InvalidDataException( $"GMA entries extend beyond archive '{filename}'." );
	}

	public IEnumerable<string> GetFilePaths() => files.Keys;

	public Stream OpenFile( string path )
	{
		lock ( archiveLock )
		{
			ObjectDisposedException.ThrowIf( disposed, this );
			if ( !files.TryGetValue( path.Replace( '\\', '/' ), out var entry ) )
				throw new FileNotFoundException( $"Could not find file '{path}' in GMA archive '{filename}'.", path );
			if ( entry.Length == 0 )
				return ArchiveFile.OpenPreload( default );
			archive ??= new ArchiveFile( filename );
			return archive.OpenRead( dataOffset + entry.Offset, entry.Length );
		}
	}

	public void Dispose()
	{
		lock ( archiveLock )
		{
			if ( disposed ) return;
			disposed = true;
			archive?.Release();
			archive = null;
		}
	}

	private static bool SkipString( BinaryReader reader )
	{
		if ( reader.ReadByte() == 0 ) return false;
		while ( reader.ReadByte() != 0 ) { }
		return true;
	}

	private static void ValidatePath( string path )
	{
		if ( path.Contains( ':' ) )
			throw new InvalidDataException( $"Invalid GMA entry path '{path}'." );
		foreach ( var range in path.AsSpan().Split( '/' ) )
		{
			var part = path.AsSpan()[range];
			if ( part.IsEmpty || part.SequenceEqual( "." ) || part.SequenceEqual( ".." ) )
				throw new InvalidDataException( $"Invalid GMA entry path '{path}'." );
		}
	}
}

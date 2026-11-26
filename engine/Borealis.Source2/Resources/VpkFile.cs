using Microsoft.Win32.SafeHandles;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Borealis.Source2.Resources;

/// <summary>
/// A Valve pak (<c>.vpk</c>, version 1 or 2), which compiled maps ship in: a directory tree of files by extension, then
/// folder, then name, each with a few preloaded bytes and the rest either after the tree in the same file (archive
/// <c>0x7FFF</c>) or in a numbered archive beside it (<c>name_000.vpk</c> for <c>name_dir.vpk</c>). Paths are lower-case
/// with forward slashes, <c>maps/construct_25/world.vwrld_c</c>. Reading is thread-safe.
/// </summary>
public sealed class VpkFile : IDisposable
{
	const uint Signature = 0x55AA1234;
	const ushort InDirectory = 0x7FFF;

	readonly record struct Entry( ushort Archive, uint Offset, uint Length, int PreloadOffset, ushort PreloadLength );

	readonly string path;
	readonly byte[] tree;
	readonly long dataStart;
	readonly Dictionary<string, Entry> entries = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<int, SafeFileHandle> archives = new();
	readonly object sync = new();
	bool disposed;

	VpkFile( string path )
	{
		this.path = path;
		using var file = File.OpenRead( path );
		Span<byte> header = stackalloc byte[28];
		file.ReadExactly( header[..12] );
		if ( BinaryPrimitives.ReadUInt32LittleEndian( header ) != Signature ) throw new InvalidDataException( $"{path} isn't a VPK" );

		var version = BinaryPrimitives.ReadUInt32LittleEndian( header[4..] );
		var treeSize = (int)BinaryPrimitives.ReadUInt32LittleEndian( header[8..] );
		var headerSize = version switch
		{
			1 => 12,
			2 => 28,
			_ => throw new InvalidDataException( $"{path}: VPK version {version} isn't supported" ),
		};

		if ( version == 2 ) file.ReadExactly( header[12..28] );
		tree = new byte[treeSize];
		file.ReadExactly( tree );
		dataStart = headerSize + treeSize;

		var position = 0;
		string Next()
		{
			var end = Array.IndexOf( tree, (byte)0, position );
			if ( end < 0 ) throw new InvalidDataException( $"{path}: directory tree runs off the end" );
			var text = Encoding.UTF8.GetString( tree, position, end - position );
			position = end + 1;
			return text;
		}

		while ( Next() is { Length: > 0 } extension )
		{
			while ( Next() is { Length: > 0 } folder )
			{
				while ( Next() is { Length: > 0 } name )
				{
					var at = tree.AsSpan( position );
					var preload = BinaryPrimitives.ReadUInt16LittleEndian( at[4..] );
					var archive = BinaryPrimitives.ReadUInt16LittleEndian( at[6..] );
					var offset = BinaryPrimitives.ReadUInt32LittleEndian( at[8..] );
					var length = BinaryPrimitives.ReadUInt32LittleEndian( at[12..] );
					position += 18;

					var full = folder == " " ? $"{name}.{extension}" : $"{folder}/{name}.{extension}";
					entries[full] = new Entry( archive, offset, length, position, preload );
					position += preload;
				}
			}
		}
	}

	/// <summary>Open a VPK's directory (the <c>_dir.vpk</c> of a split one).</summary>
	public static VpkFile Open( string path ) => new( Path.GetFullPath( path ) );

	/// <summary>Every file's path.</summary>
	public IEnumerable<string> Files => entries.Keys;

	/// <summary>How many files it holds.</summary>
	public int Count => entries.Count;

	/// <summary>Whether it holds <paramref name="file"/>.</summary>
	public bool Contains( string file ) => entries.ContainsKey( Normalise( file ) );

	/// <summary>A file's bytes, or null when it isn't there.</summary>
	public byte[] Read( string file )
	{
		if ( !entries.TryGetValue( Normalise( file ), out var entry ) ) return null;

		var data = new byte[entry.PreloadLength + entry.Length];
		tree.AsSpan( entry.PreloadOffset, entry.PreloadLength ).CopyTo( data );
		if ( entry.Length == 0 ) return data;

		return ReadAt( entry, data.AsSpan( entry.PreloadLength ) ) ? data : null;
	}

	/// <summary>
	/// The first <paramref name="most"/> bytes of a file (all of it when it's shorter), or null when it isn't in the
	/// package.
	/// </summary>
	public byte[] ReadPrefix( string file, long most )
	{
		if ( !entries.TryGetValue( Normalise( file ), out var entry ) ) return null;

		var data = new byte[(int)Math.Min( entry.PreloadLength + (long)entry.Length, Math.Max( most, 0 ) )];
		var preload = Math.Min( entry.PreloadLength, data.Length );
		tree.AsSpan( entry.PreloadOffset, preload ).CopyTo( data );
		if ( preload == data.Length ) return data;

		return ReadAt( entry, data.AsSpan( preload ) ) ? data : null;
	}

	/// <summary>
	/// As <see cref="ReadPrefix"/>, into a <see cref="ContentBuffers"/> array whose first <paramref name="length"/> bytes
	/// are the file's: give it back with <see cref="ContentBuffers.Return"/>.
	/// </summary>
	public byte[] ReadPrefixPooled( string file, long most, out int length )
	{
		length = 0;
		if ( !entries.TryGetValue( Normalise( file ), out var entry ) ) return null;

		length = (int)Math.Min( entry.PreloadLength + (long)entry.Length, Math.Max( most, 0 ) );
		var data = ContentBuffers.Rent( length );
		try
		{
			var preload = Math.Min( (int)entry.PreloadLength, length );
			tree.AsSpan( entry.PreloadOffset, preload ).CopyTo( data );
			if ( preload == length || ReadAt( entry, data.AsSpan( preload, length - preload ) ) ) return data;
		}
		catch
		{
			ContentBuffers.Return( data );
			length = 0;
			throw;
		}

		ContentBuffers.Return( data );
		length = 0;
		return null;
	}

	// An entry's bytes after its preload, read at its offset: positional reads on a shared handle, so readers on many threads
	// don't queue behind one another (a map's models, materials and textures all come from one archive). False once disposed
	bool ReadAt( Entry entry, Span<byte> into )
	{
		var handle = Archive( entry.Archive );
		if ( handle is null ) return false;
		var offset = (entry.Archive == InDirectory ? dataStart : 0) + entry.Offset;
		try
		{
			while ( into.Length > 0 )
			{
				var read = RandomAccess.Read( handle, into, offset );
				if ( read <= 0 ) throw new EndOfStreamException( $"{path}: archive {entry.Archive} ends inside a file" );
				into = into[read..];
				offset += read;
			}
		}
		catch ( ObjectDisposedException )
		{
			return false;
		}

		return true;
	}

	SafeFileHandle Archive( int index )
	{
		lock ( sync )
		{
			if ( disposed ) return null;
			if ( archives.TryGetValue( index, out var open ) ) return open;

			var file = index == InDirectory
				? path
				: path.EndsWith( "_dir.vpk", StringComparison.OrdinalIgnoreCase )
					? $"{path[..^"_dir.vpk".Length]}_{index:000}.vpk"
					: throw new InvalidDataException( $"{path} refers to archive {index} but isn't a split VPK's directory" );

			var handle = File.OpenHandle( file, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess );
			archives[index] = handle;
			return handle;
		}
	}

	static string Normalise( string file ) => file.Replace( '\\', '/' ).TrimStart( '/' );

	/// <summary>Close the archives it opened.</summary>
	public void Dispose()
	{
		lock ( sync )
		{
			disposed = true;
			foreach ( var handle in archives.Values ) handle.Dispose();
			archives.Clear();
		}
	}

	/// <inheritdoc/>
	public override string ToString() => $"{Path.GetFileName( path )} ({entries.Count} files)";
}

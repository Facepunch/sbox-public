using System.IO;
using System.IO.Compression;

namespace Sandbox.ShaderCompiler;

/// <summary>
/// One compiled shader or stage as bytes (<see cref="ShaderBinary"/>): its key, the files its compile read with their hashes,
/// then the compressed payload. The disk caches keep one per file; <see cref="SlangOutput"/> keeps a source's in one file.
/// </summary>
internal static class ShaderCacheFile
{
	const int Magic = 0x43534253; // "SBSC"

	/// <summary>An entry's bytes: <paramref name="key"/>, <paramref name="dependencies"/> as given (paths and hashes), the payload compressed.</summary>
	public static byte[] Write( string key, IEnumerable<(string Path, string Hash)> dependencies, Action<BinaryWriter> payload )
	{
		using var body = new MemoryStream();
		using ( var zip = new ZLibStream( body, CompressionLevel.Fastest, leaveOpen: true ) )
		using ( var w = new BinaryWriter( zip ) )
			payload( w );

		using var file = new MemoryStream();
		using ( var w = new BinaryWriter( file ) )
		{
			w.Write( Magic );
			w.Write( ShaderBinary.Format );
			w.Write( key );
			var list = dependencies.ToList();
			w.Write( list.Count );
			foreach ( var (path, hash) in list )
			{
				w.Write( path );
				w.Write( hash ?? "" );
			}

			w.Write( (int)body.Length );
			w.Write( body.GetBuffer(), 0, (int)body.Length );
		}

		return file.ToArray();
	}

	/// <summary>
	/// The payload of an entry whose key is <paramref name="key"/> and whose every dependency, resolved by
	/// <paramref name="resolve"/> (null: not here), still hashes the same; with the resolved paths. False otherwise. An offline
	/// (shipped) entry, read with an empty key, is used too where a dependency is missing rather than changed: a player's
	/// install has no private headers its compile read (the build leaves them out), and compiling again couldn't find them.
	/// </summary>
	public static bool TryRead<T>( ReadOnlySpan<byte> bytes, string key, Func<string, string> resolve, Func<BinaryReader, List<string>, T> payload, out T value )
	{
		value = default;
		try
		{
			using var stream = new MemoryStream( bytes.ToArray(), writable: false );
			using var r = new BinaryReader( stream );
			if ( r.ReadInt32() != Magic || r.ReadInt32() != ShaderBinary.Format || r.ReadString() != key ) return false;

			var count = r.ReadInt32();
			var dependencies = new List<string>( count );
			for ( int i = 0; i < count; i++ )
			{
				var path = resolve( r.ReadString() );
				var hash = r.ReadString();
				var current = path is null ? null : PassCache.FileHash( path );
				if ( current == hash )
				{
					dependencies.Add( path );
					continue;
				}

				if ( key.Length > 0 || current is not null ) return false;
			}

			var length = r.ReadInt32();
			using var zip = new ZLibStream( new MemoryStream( r.ReadBytes( length ), writable: false ), CompressionMode.Decompress );
			using var body = new BinaryReader( zip );
			value = payload( body, dependencies );
			return true;
		}
		catch ( Exception e ) when ( e is IOException or InvalidDataException or EndOfStreamException or ArgumentException or IndexOutOfRangeException or InvalidCastException )
		{
			return false;
		}
	}

	/// <summary>
	/// An entry's bytes with its key left out (written empty): what <see cref="SlangOutput"/> keeps, once for every key whose
	/// compile came out the same (a pass module's combos often do).
	/// </summary>
	public static byte[] WithoutKey( byte[] bytes )
	{
		using var r = new BinaryReader( new MemoryStream( bytes, writable: false ) );
		r.ReadInt32();
		r.ReadInt32();
		r.ReadString();
		var rest = (int)r.BaseStream.Position;
		var keyless = new byte[8 + 1 + bytes.Length - rest];
		bytes.AsSpan( 0, 8 ).CopyTo( keyless );
		bytes.AsSpan( rest ).CopyTo( keyless.AsSpan( 9 ) );
		return keyless;
	}

	/// <summary>Write <paramref name="bytes"/> to <paramref name="file"/> through a temporary file; a cache that can't be written compiles again next time.</summary>
	public static void Save( string file, byte[] bytes )
	{
		try
		{
			Directory.CreateDirectory( Path.GetDirectoryName( file ) );
			var temporary = file + "." + Environment.CurrentManagedThreadId + ".tmp";
			File.WriteAllBytes( temporary, bytes );
			File.Move( temporary, file, overwrite: true );
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
		{
		}
	}
}

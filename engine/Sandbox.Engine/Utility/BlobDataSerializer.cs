using System.Buffers.Binary;
using System.IO;
using System.Text.Json.Nodes;
using Sandbox.Hashing;

namespace Sandbox;

/// <summary>
/// Manages binary blobs during JSON serialization with support for nested contexts.
/// </summary>
internal static class BlobDataSerializer
{
	public const string CompiledBlobName = "DBLOB";

	private const int DefaultStreamSize = 4096;
	private const int HeaderSize = 8;
	private const int TocEntrySize = 32;

	internal readonly record struct RegisteredBlob( BlobData Blob, byte[] Data );

	private static BlobContext _current;

	/// <summary>
	/// Indicates whether a blob context is currently active.
	/// </summary>
	internal static bool IsActive => _current != null;

	/// <summary>
	/// Registers a <see cref="BlobData"/> instance for serialization in the current blob context.
	/// Returns a <see cref="Guid"/> that can be used to reference the blob in JSON.
	/// If no blob context is active, returns <see cref="Guid.Empty"/>.
	/// Called automatically by <c>BinaryBlobJsonConverter</c> during JSON serialization.
	/// </summary>
	internal static Guid RegisterBlob( BlobData blob )
	{
		if ( _current == null )
			return Guid.Empty;

		// The id is derived from the serialized content, so unchanged data always produces
		// the same id - repeated saves stay byte-identical and identical blobs deduplicate.
		var data = SerializeBlob( blob );
		var guid = DeriveContentGuid( data );

		_current.Blobs[guid] = new RegisteredBlob( blob, data );
		return guid;
	}

	/// <summary>
	/// Serialize a blob to its binary payload: version prefix followed by the blob data.
	/// </summary>
	private static byte[] SerializeBlob( BlobData blob )
	{
		var stream = ByteStream.Create( DefaultStreamSize );
		try
		{
			stream.Write( blob.Version );
			var writer = new BlobData.Writer { Stream = stream };
			blob.Serialize( ref writer );
			stream = writer.Stream;
			return stream.ToArray();
		}
		finally
		{
			stream.Dispose();
		}
	}

	/// <summary>
	/// Deterministically derive a blob id from its content, marked as an
	/// RFC 4122 version 8 (custom) guid so derived ids are well formed.
	/// </summary>
	private static Guid DeriveContentGuid( ReadOnlySpan<byte> data )
	{
		Span<byte> derived = stackalloc byte[16];
		BinaryPrimitives.WriteUInt64LittleEndian( derived[..8], XxHash3.HashToUInt64( data ) );
		BinaryPrimitives.WriteUInt64LittleEndian( derived[8..], XxHash3.HashToUInt64( data, seed: 0x5bd1e9955bd1e995 ) );

		derived[7] = (byte)((derived[7] & 0x0F) | 0x80);
		derived[8] = (byte)((derived[8] & 0x3F) | 0x80);

		return new Guid( derived );
	}

	internal static BlobData ReadBlob( Guid guid, Type expectedType )
	{
		if ( _current == null )
			return null;

		if ( _current.Blobs.TryGetValue( guid, out var registeredBlob ) )
			return registeredBlob.Blob;

		// Fall back to pre-existing binary data loaded from file
		var binaryData = _current.BinaryData;
		if ( binaryData == null || !binaryData.TryGetValue( guid, out var blobData ) )
			return null;

		if ( Activator.CreateInstance( expectedType ) is not BlobData instance )
			return null;

		var stream = ByteStream.CreateReader( blobData );
		try
		{
			int dataVersion = stream.Read<int>();
			var reader = new BlobData.Reader { Stream = stream, DataVersion = dataVersion };

			if ( dataVersion < instance.Version )
				instance.Upgrade( ref reader, dataVersion );
			else
				instance.Deserialize( ref reader );
		}
		finally
		{
			stream.Dispose();
		}

		return instance;
	}

	private static byte[] GetBlobData( Dictionary<Guid, RegisteredBlob> blobs )
	{
		if ( blobs == null || blobs.Count == 0 )
			return null;

		// Canonical order so unchanged content always produces byte-identical output,
		// independent of registration order or dictionary enumeration details
		var ordered = blobs.OrderBy( x => x.Key ).ToArray();

		long offset = HeaderSize + blobs.Count * TocEntrySize;

		long totalSize = offset;
		foreach ( var kvp in ordered )
			totalSize += kvp.Value.Data.Length;

		var outputStream = ByteStream.Create( (int)totalSize );
		try
		{
			outputStream.Write( 1 );
			outputStream.Write( blobs.Count );

			foreach ( var kvp in ordered )
			{
				outputStream.Write( kvp.Key.ToByteArray() );
				outputStream.Write( kvp.Value.Blob.Version );
				outputStream.Write( offset );
				outputStream.Write( kvp.Value.Data.Length );
				offset += kvp.Value.Data.Length;
			}

			foreach ( var kvp in ordered )
			{
				outputStream.Write( kvp.Value.Data );
			}

			return outputStream.ToArray();
		}
		finally
		{
			outputStream.Dispose();
		}
	}

	private static Dictionary<Guid, byte[]> ParseFile( ReadOnlySpan<byte> data )
	{
		var result = new Dictionary<Guid, byte[]>();
		if ( data.Length == 0 ) return result;

		try
		{
			var stream = ByteStream.CreateReader( data );
			try
			{
				int version = stream.Read<int>();
				if ( version != 1 ) return result;

				int count = stream.Read<int>();
				var toc = new List<(Guid guid, long offset, int size)>( count );

				for ( int i = 0; i < count; i++ )
				{
					var guid = stream.Read<Guid>();
					stream.Read<int>();
					long blobOffset = stream.Read<long>();
					int size = stream.Read<int>();
					toc.Add( (guid, blobOffset, size) );
				}

				foreach ( var (guid, blobOffset, size) in toc )
				{
					stream.Position = (int)blobOffset;
					var buffer = new byte[size];
					stream.Read( buffer, 0, size );
					result[guid] = buffer;
				}
			}
			finally
			{
				stream.Dispose();
			}
		}
		catch ( Exception e )
		{
			Log.Warning( e, "Failed to parse blob data" );
		}

		return result;
	}

	/// <summary>
	/// Create a blob serialization context for capturing blobs.
	/// </summary>
	public static BlobContext Capture()
	{
		var context = new BlobContext( _current );
		_current = context;
		return context;
	}

	/// <summary>
	/// Temporarily suppresses any active blob context, forcing nested serialization to write
	/// self-contained (inline) data instead of referencing an ambient context by guid.
	/// Use this when producing JSON that must remain valid outside the current context's lifetime,
	/// e.g. a diff patch that's cached and reapplied long after the context that created it is gone.
	/// </summary>
	internal static IDisposable Suppress()
	{
		return new SuppressScope( _current );
	}

	private sealed class SuppressScope : IDisposable
	{
		private readonly BlobContext _previous;

		public SuppressScope( BlobContext previous )
		{
			_previous = previous;
			_current = null;
		}

		public void Dispose() => _current = _previous;
	}

	/// <summary>
	/// Load blob data from memory if available, otherwise from file path.
	/// </summary>
	public static BlobContext Load( byte[] data, string filePath )
	{
		return data != null ? LoadFromMemory( data ) : LoadFrom( filePath );
	}

	/// <summary>
	/// Create a blob deserialization context from in-memory data.
	/// </summary>
	public static BlobContext LoadFromMemory( ReadOnlySpan<byte> data )
	{
		var binaryData = data.Length > 0 ? ParseFile( data ) : null;
		var context = new BlobContext( _current, binaryData );
		_current = context;
		return context;
	}

	/// <summary>
	/// Create a blob deserialization context from blob data stored on a json object
	/// by <see cref="BlobContext.SaveTo( JsonNode )"/>.
	/// </summary>
	internal static BlobContext LoadFrom( JsonNode json )
	{
		byte[] data = null;

		if ( json?["__blobdata"]?.GetValue<string>() is string base64 )
			data = Convert.FromBase64String( base64 );

		return LoadFromMemory( data );
	}

	/// <summary>
	/// Create a blob deserialization context from a file.
	/// </summary>
	public static BlobContext LoadFrom( string filePath )
	{
		var binaryData = ResolveBlobData( filePath );

		var context = new BlobContext( _current, binaryData );
		_current = context;
		return context;
	}

	/// <summary>
	/// Finds and parses the blob table for a resource, searching all relevant filesystems and file variations.
	/// Checks both "_d" sidecar and compiled "_c" files, with disk fallback if needed (for published resources).
	/// </summary>
	private static Dictionary<Guid, byte[]> ResolveBlobData( string filePath )
	{
		if ( string.IsNullOrEmpty( filePath ) )
			return null;

		if ( filePath.EndsWith( "_c" ) )
			filePath = filePath[..^2];

		var sidecarPath = filePath + "_d";
		var compiledPath = filePath + "_c";

		ReadOnlySpan<BaseFileSystem> filesystems =
		[
			FileSystem.Mounted,
			PackageManager.MountedFileSystem,
		];

		foreach ( var fs in filesystems )
		{
			if ( fs is null )
				continue;

			// The "_d" sidecar is already stored in the blob-file format.
			if ( fs.FileExists( sidecarPath ) )
				return ParseFile( fs.ReadAllBytes( sidecarPath ) );

			// Otherwise pull the compiled blob block out of the compiled resource.
			if ( fs.FileExists( compiledPath ) )
			{
				var blockData = Game.Resources.ReadCompiledResourceBlock( CompiledBlobName, fs.ReadAllBytes( compiledPath ) );
				if ( blockData != null )
					return ParseFile( blockData );
			}
		}

		// Final fallback: a loose sidecar on raw disk (e.g. freshly saved in the editor and not
		// yet visible through a mounted filesystem).
		if ( File.Exists( sidecarPath ) )
			return ParseFile( File.ReadAllBytes( sidecarPath ) );

		return null;
	}

	/// <summary>
	/// A disposable context for blob serialization/deserialization.
	/// </summary>
	public sealed class BlobContext : IDisposable
	{
		internal readonly Dictionary<Guid, RegisteredBlob> Blobs;
		internal readonly Dictionary<Guid, byte[]> BinaryData;
		private readonly BlobContext _previous;

		internal BlobContext( BlobContext previous, Dictionary<Guid, byte[]> binaryData = null )
		{
			Blobs = new();
			BinaryData = binaryData ?? new();
			_previous = previous;
		}

		public byte[] ToByteArray() => GetBlobData( Blobs );

		/// <summary>
		/// Merge extra serialized blob data into this open context, so separately-captured blobs
		/// stay readable through one deferred deserialize pass.
		/// </summary>
		internal void Load( ReadOnlySpan<byte> data )
		{
			if ( data.Length == 0 ) return;

			foreach ( var kvp in ParseFile( data ) )
				BinaryData[kvp.Key] = kvp.Value;
		}

		/// <summary>
		/// Merge blob data stored on a json object by <see cref="SaveTo( JsonNode )"/> into this open context.
		/// </summary>
		internal void LoadFrom( JsonNode json )
		{
			if ( json?["__blobdata"]?.GetValue<string>() is string base64 )
				Load( Convert.FromBase64String( base64 ) );
		}

		/// <summary>
		/// Store the captured blob data on a json object, so it travels with it.
		/// </summary>
		internal bool SaveTo( JsonNode json )
		{
			if ( json is not JsonObject jso ) return false;

			var data = GetBlobData( Blobs );
			if ( data == null || data.Length == 0 ) return false;

			jso["__blobdata"] = Convert.ToBase64String( data );
			return true;
		}

		public bool SaveTo( string filePath )
		{
			var data = GetBlobData( Blobs );
			if ( data == null || data.Length == 0 ) return false;

			try
			{
				File.WriteAllBytes( filePath + "_d", data );
				return true;
			}
			catch ( Exception e )
			{
				Log.Warning( e, "Failed to write blob file" );
				return false;
			}
		}

		public void Dispose() => _current = _previous;
	}
}

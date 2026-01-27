using System.IO;

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

	private readonly record struct BlobEntry( Guid Guid, int Version, byte[] Data, long Offset );

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

		var blobs = _current.Blobs;
		var guid = Guid.NewGuid();
		blobs[guid] = blob;
		return guid;
	}

	internal static BlobData ReadBlob( Guid guid, Type expectedType )
	{
		if ( _current == null )
			return null;

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

	private static byte[] GetBlobData( Dictionary<Guid, BlobData> blobs )
	{
		if ( blobs == null || blobs.Count == 0 )
			return null;

		long dataStart = HeaderSize + blobs.Count * TocEntrySize;

		using var entries = new PooledSpan<BlobEntry>( blobs.Count );
		var entrySpan = entries.Span;
		int entryCount = 0;

		long offset = dataStart;
		foreach ( var kvp in blobs )
		{
			var blobStream = ByteStream.Create( DefaultStreamSize );
			try
			{
				blobStream.Write( kvp.Value.Version );
				var writer = new BlobData.Writer { Stream = blobStream };
				kvp.Value.Serialize( ref writer );
				blobStream = writer.Stream;

				byte[] data = blobStream.ToArray();
				entrySpan[entryCount++] = new BlobEntry( kvp.Key, kvp.Value.Version, data, offset );
				offset += data.Length;
			}
			finally
			{
				blobStream.Dispose();
			}
		}

		var outputStream = ByteStream.Create( (int)offset );
		try
		{
			outputStream.Write( 1 );
			outputStream.Write( blobs.Count );

			for ( int i = 0; i < entryCount; i++ )
			{
				ref readonly var entry = ref entrySpan[i];
				outputStream.Write( entry.Guid.ToByteArray() );
				outputStream.Write( entry.Version );
				outputStream.Write( entry.Offset );
				outputStream.Write( entry.Data.Length );
			}

			for ( int i = 0; i < entryCount; i++ )
			{
				outputStream.Write( entrySpan[i].Data );
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
	/// Create a blob deserialization context from a file.
	/// </summary>
	public static BlobContext LoadFrom( string filePath )
	{
		Dictionary<Guid, byte[]> binaryData = null;

		if ( !string.IsNullOrEmpty( filePath ) )
		{
			if ( filePath.EndsWith( "_c" ) )
				filePath = filePath[..^2];

			var path = filePath + "_d";

			if ( FileSystem.Mounted?.FileExists( path ) == true )
				binaryData = ParseFile( FileSystem.Mounted.ReadAllBytes( path ) );
			else if ( File.Exists( path ) )
				binaryData = ParseFile( File.ReadAllBytes( path ) );
		}

		var context = new BlobContext( _current, binaryData );
		_current = context;
		return context;
	}

	/// <summary>
	/// A disposable context for blob serialization/deserialization.
	/// </summary>
	public sealed class BlobContext : IDisposable
	{
		internal readonly Dictionary<Guid, BlobData> Blobs;
		internal readonly Dictionary<Guid, byte[]> BinaryData;
		private readonly BlobContext _previous;

		internal BlobContext( BlobContext previous, Dictionary<Guid, byte[]> binaryData = null )
		{
			Blobs = new();
			BinaryData = binaryData ?? new();
			_previous = previous;
		}

		public byte[] ToByteArray() => GetBlobData( Blobs );

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

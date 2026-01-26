using System.IO;

namespace Sandbox;

/// <summary>
/// Manages binary blobs during JSON serialization.
/// Blobs are automatically captured during JSON serialization and can be retrieved afterward.
/// <para>
/// File Format:
/// <code>
///  HEADER (8 bytes)
///   - Version (4 bytes)
///   - Entry Count (4 bytes)
///  TABLE OF CONTENTS (Count x 32 bytes)
///   - Entry 1: Guid, Version, Offset, Size
///   - Entry 2: ...
///  BLOB DATA
///   - Blob 1 data
///   - Blob 2 data
/// </code>
/// </para>
/// </summary>
internal static class BlobDataSerializer
{
	/// <summary>
	/// Represents the name used to identify the compiled data blob.
	/// </summary>
	public const string CompiledBlobName = "DBLOB";

	private const int DefaultStreamSize = 4096; // bytes

	// File format constants
	private const int HeaderSize = 8;         // Version (4) + Count (4)
	private const int TocEntrySize = 32;      // Guid (16) + Version (4) + Offset (8) + Size (4)

	// Entry for serialization containing blob metadata and data
	private readonly record struct BlobEntry( Guid Guid, int Version, byte[] Data, long Offset );

	// Mapping between blob GUID and the binary blob object
	private static Dictionary<Guid, BlobData> Blobs;

	// Mapping between blob GUID and raw binary data for deserialization
	private static Dictionary<Guid, byte[]> BinaryData;

	/// <summary>
	/// Register a blob for serialization. Returns GUID to reference in JSON.
	/// Called automatically by BinaryBlobJsonConverter during JSON serialization.
	/// </summary>
	internal static Guid RegisterBlob( BlobData blob )
	{
		Blobs ??= [];

		var guid = Guid.NewGuid();
		Blobs[guid] = blob;
		return guid;
	}

	/// <summary>
	/// Read a blob by GUID from the loaded blob data.
	/// Called automatically by BinaryBlobJsonConverter during JSON deserialization.
	/// </summary>
	internal static BlobData ReadBlob( Guid guid, Type expectedType )
	{
		if ( BinaryData == null || !BinaryData.TryGetValue( guid, out var blobData ) )
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

	/// <summary>
	/// Get all captured blobs as a byte array. Returns null if no blobs were captured.
	/// </summary>
	internal static byte[] GetBlobData()
	{
		if ( Blobs == null || Blobs.Count == 0 )
			return null;

		// Calculate where blob data starts after header + TOC
		long dataStart = HeaderSize + Blobs.Count * TocEntrySize;

		using var entries = new PooledSpan<BlobEntry>( Blobs.Count );
		var entrySpan = entries.Span;
		int entryCount = 0;

		// Serialize each blob to get size
		long offset = dataStart;
		foreach ( var kvp in Blobs )
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

		// Write file
		var outputStream = ByteStream.Create( (int)offset );
		try
		{
			// Header
			outputStream.Write( 1 ); // Version
			outputStream.Write( Blobs.Count ); // Entry count

			// TOC
			for ( int i = 0; i < entryCount; i++ )
			{
				ref readonly var entry = ref entrySpan[i];
				outputStream.Write( entry.Guid.ToByteArray() );
				outputStream.Write( entry.Version );
				outputStream.Write( entry.Offset );
				outputStream.Write( entry.Data.Length );
			}

			// Write binary blobs
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

	/// <summary>
	/// Clear all captured and loaded blob data.
	/// </summary>
	internal static void Clear()
	{
		Blobs = null;
		BinaryData = null;
	}

	// Converts raw binary data into a mapping of GUID to blob data
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
					stream.Read<int>(); // version
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
	/// Create a blob serialization context. Automatically cleans up when disposed.
	/// <code>
	/// using var blobs = BinarySerializer.Capture();
	/// var json = Json.Serialize( obj );
	/// blobs.SaveTo( filePath );
	/// </code>
	/// </summary>
	public static BlobContext Capture() => new();

	/// <summary>
	/// Create a blob deserialization context from in-memory data.
	/// Used when loading scenes that have binary data already in memory.
	/// </summary>
	public static BlobContext LoadFromMemory( ReadOnlySpan<byte> data )
	{
		if ( data.Length > 0 )
		{
			BinaryData = ParseFile( data );
		}
		return new BlobContext();
	}

	/// <summary>
	/// Create a blob deserialization context from a file. Automatically cleans up when disposed.
	/// <code>
	/// using var blobs = BinarySerializer.LoadFrom( filePath );
	/// var obj = Json.Deserialize( json );
	/// </code>
	/// </summary>
	public static BlobContext LoadFrom( string filePath )
	{
		if ( string.IsNullOrEmpty( filePath ) ) return new BlobContext();

		// remove _c suffix
		if ( filePath.EndsWith( "_c" ) )
			filePath = filePath[..^2];

		var path = filePath + "_d";

		if ( FileSystem.Mounted?.FileExists( path ) == true )
		{
			var data = FileSystem.Mounted.ReadAllBytes( path );
			BinaryData = ParseFile( data );
		}
		else if ( File.Exists( path ) )
		{
			BinaryData = ParseFile( File.ReadAllBytes( path ) );
		}

		return new();
	}

	/// <summary>
	/// A disposable context for blob serialization/deserialization that automatically cleans up.
	/// </summary>
	public readonly struct BlobContext : IDisposable
	{
		/// <summary>
		/// Get the captured blob data as a byte array.
		/// </summary>
		public byte[] ToByteArray() => GetBlobData();

		/// <summary>
		/// Save blob data to a companion file.
		/// </summary>
		public bool SaveTo( string filePath )
		{
			var data = GetBlobData();
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

		/// <summary>
		/// Clean up
		/// </summary>
		public void Dispose() => Clear();
	}
}

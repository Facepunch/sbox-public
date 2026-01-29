using K4os.Compression.LZ4;
using K4os.Compression.LZ4.Streams;
using System.IO;
using System.IO.Compression;

namespace Sandbox.Compression;

/// <summary>
/// Encode and decode LZ4 compressed data.
/// </summary>
public static class LZ4
{
	private static LZ4Level CompressionLevelToLZ4Level( CompressionLevel level )
	{
		return level switch
		{
			CompressionLevel.NoCompression => throw new ArgumentException( "NoCompression is not supported for LZ4." ),
			CompressionLevel.Fastest => LZ4Level.L00_FAST,
			CompressionLevel.Optimal => LZ4Level.L09_HC,
			CompressionLevel.SmallestSize => LZ4Level.L12_MAX,
			_ => LZ4Level.L00_FAST,
		};
	}

	/// <summary>
	/// Encode data as an LZ4 block.
	/// </summary>
	/// <param name="data">Input buffer</param>
	/// <param name="compressionLevel">Compression level to use</param>
	/// <returns>Compressed LZ4 block data</returns>
	public static byte[] CompressBlock( ReadOnlySpan<byte> data, System.IO.Compression.CompressionLevel compressionLevel = System.IO.Compression.CompressionLevel.Fastest )
	{
		if ( data.IsEmpty )
			return Array.Empty<byte>();

		int maxLength = LZ4Codec.MaximumOutputSize( data.Length );
		var compressed = new byte[maxLength];

		int resultLength = LZ4Codec.Encode( data, compressed, CompressionLevelToLZ4Level( compressionLevel ) );
		if ( resultLength <= 0 )
			throw new InvalidDataException( "LZ4 encode failed." );

		Array.Resize( ref compressed, resultLength ); // trim to actual size
		return compressed;
	}


	/// <summary>
	/// Decode raw LZ4 block data.
	/// </summary>
	/// <param name="src">Input buffer, compressed LZ4 block data</param>
	/// <param name="dest">Output buffer, uncompressed</param>
	/// <returns>Number of bytes written</returns>
	public static int DecompressBlock( ReadOnlySpan<byte> src, Span<byte> dest )
	{
		int resultLength = LZ4Codec.Decode( src, dest );
		if ( resultLength <= 0 )
			throw new InvalidDataException( "LZ4 decode failed." );

		return resultLength;
	}

	/// <summary>
	/// Encode data as an LZ4 frame.
	/// </summary>
	/// <param name="data">Input buffer</param>
	/// <param name="compressionLevel">Compression level to use</param>
	/// <returns>Compressed LZ4 frame data</returns>
	public static byte[] CompressFrame( ReadOnlySpan<byte> data, System.IO.Compression.CompressionLevel compressionLevel = System.IO.Compression.CompressionLevel.Fastest )
	{
		if ( data.IsEmpty )
			return Array.Empty<byte>();

		try
		{
			using var outStream = new MemoryStream();
			using ( var encoder = LZ4Stream.Encode( outStream, CompressionLevelToLZ4Level( compressionLevel ) ) )
			{
				encoder.Write( data );
			}

			return outStream.ToArray();
		}
		catch ( Exception ex )
		{
			throw new InvalidDataException( "Failed to encode LZ4.", ex );
		}
	}

	/// <summary>
	/// Decode an LZ4 frame.
	/// </summary>
	/// <param name="data">Input buffer, compressed LZ4 frame data</param>
	/// <returns>Uncompressed data</returns>
	public static byte[] DecompressFrame( ReadOnlySpan<byte> data )
	{
		if ( data.IsEmpty )
			return Array.Empty<byte>();

		try
		{
			unsafe
			{
				fixed ( byte* ptr = data )
				{
					using var input = new UnmanagedMemoryStream( ptr, data.Length );
					using var decompressor = LZ4Stream.Decode( input );
					using var outStream = new MemoryStream();

					decompressor.CopyTo( outStream );
					return outStream.ToArray();
				}
			}
		}
		catch ( Exception ex )
		{
			throw new InvalidDataException( "Failed to decode LZ4.", ex );
		}
	}
}

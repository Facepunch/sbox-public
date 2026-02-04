using Managed.SandboxEngine;
using System.IO;
using System.IO.Compression;

namespace Sandbox.Compression;

/// <summary>
/// Encode and decode LZ4 compressed data.
/// </summary>
public static class LZ4
{
	private static int CompressionLevelToLZ4Level( CompressionLevel level )
	{
		return level switch
		{
			CompressionLevel.NoCompression => throw new ArgumentException( "NoCompression is not supported for LZ4." ),
			CompressionLevel.Fastest => 1, // Fast mode uses LZ4_compress_default
			CompressionLevel.Optimal => 2,
			CompressionLevel.SmallestSize => 12,
			_ => 1,
		};
	}

	/// <summary>
	/// Encode data as an LZ4 block.
	/// </summary>
	/// <param name="data">Input buffer</param>
	/// <param name="compressionLevel">Compression level to use</param>
	/// <returns>Compressed LZ4 block data</returns>
	public static byte[] CompressBlock( ReadOnlySpan<byte> data, CompressionLevel compressionLevel = CompressionLevel.Fastest )
	{
		if ( data.IsEmpty )
			return Array.Empty<byte>();

		int maxLength = NativeEngine.LZ4Glue.CompressBound( data.Length );
		using var compressed = new PooledSpan<byte>( maxLength );

		int resultLength;
		unsafe
		{
			fixed ( byte* srcPtr = data )
			fixed ( byte* dstPtr = compressed.Span )
			{
				int level = CompressionLevelToLZ4Level( compressionLevel );
				if ( level <= 1 )
				{
					// Use fast compression
					resultLength = NativeEngine.LZ4Glue.Compress( (nint)srcPtr, (nint)dstPtr, data.Length, maxLength );
				}
				else
				{
					// Use HC (high compression) mode
					resultLength = NativeEngine.LZ4Glue.CompressHC( (nint)srcPtr, (nint)dstPtr, data.Length, maxLength, level );
				}
			}
		}

		if ( resultLength <= 0 )
			throw new InvalidDataException( "LZ4 encode failed." );

		return compressed.Span.Slice( 0, resultLength ).ToArray();
	}


	/// <summary>
	/// Decode raw LZ4 block data.
	/// </summary>
	/// <param name="src">Input buffer, compressed LZ4 block data</param>
	/// <param name="dest">Output buffer, uncompressed</param>
	/// <returns>Number of bytes written</returns>
	public static int DecompressBlock( ReadOnlySpan<byte> src, Span<byte> dest )
	{
		int resultLength;
		unsafe
		{
			fixed ( byte* srcPtr = src )
			fixed ( byte* dstPtr = dest )
			{
				resultLength = NativeEngine.LZ4Glue.Decompress( (nint)srcPtr, (nint)dstPtr, src.Length, dest.Length );
			}
		}

		if ( resultLength < 0 )
			throw new InvalidDataException( "LZ4 decode failed." );

		return resultLength;
	}

	/// <summary>
	/// Encode data as an LZ4 frame (standard LZ4 frame format, compatible with lz4 CLI).
	/// </summary>
	/// <param name="data">Input buffer</param>
	/// <param name="compressionLevel">Compression level to use</param>
	/// <returns>Compressed LZ4 frame data</returns>
	public static byte[] CompressFrame( ReadOnlySpan<byte> data, CompressionLevel compressionLevel = CompressionLevel.Fastest )
	{
		if ( data.IsEmpty )
			return Array.Empty<byte>();

		int maxFrameSize = NativeEngine.LZ4Glue.CompressFrameBound( data.Length );
		using var compressed = new PooledSpan<byte>( maxFrameSize );

		int resultLength;
		unsafe
		{
			fixed ( byte* srcPtr = data )
			fixed ( byte* dstPtr = compressed.Span )
			{
				int level = CompressionLevelToLZ4Level( compressionLevel );
				resultLength = NativeEngine.LZ4Glue.CompressFrame( (nint)srcPtr, (nint)dstPtr, data.Length, maxFrameSize, level );
			}
		}

		if ( resultLength <= 0 )
			throw new InvalidDataException( "LZ4 frame encode failed." );

		return compressed.Span.Slice( 0, resultLength ).ToArray();
	}

	/// <summary>
	/// Decode an LZ4 frame (standard LZ4 frame format, compatible with lz4 CLI).
	/// </summary>
	/// <param name="data">Input buffer, compressed LZ4 frame data</param>
	/// <returns>Uncompressed data</returns>
	public static byte[] DecompressFrame( ReadOnlySpan<byte> data )
	{
		if ( data.IsEmpty )
			return Array.Empty<byte>();

		if ( data.Length < 7 ) // Minimum LZ4 frame header size
			throw new InvalidDataException( "LZ4 frame too small." );

		// Get the content size from frame header (we always set it during compression)
		long contentSize;
		unsafe
		{
			fixed ( byte* srcPtr = data )
			{
				contentSize = NativeEngine.LZ4Glue.GetFrameContentSize( (nint)srcPtr, data.Length );
			}
		}

		if ( contentSize < 0 )
			throw new InvalidDataException( "Failed to read LZ4 frame header." );

		if ( contentSize == 0 )
			throw new InvalidDataException( "LZ4 frame does not contain content size. Use DecompressFrame overload with maxDecompressedSize." );

		if ( contentSize > 1024 * 1024 * 1024 ) // Sanity check: max 1GB
			throw new InvalidDataException( "LZ4 frame content size too large." );

		var output = new byte[contentSize];

		int resultLength;
		unsafe
		{
			fixed ( byte* srcPtr = data )
			fixed ( byte* dstPtr = output )
			{
				resultLength = NativeEngine.LZ4Glue.DecompressFrame( (nint)srcPtr, (nint)dstPtr, data.Length, (int)contentSize );
			}
		}

		if ( resultLength < 0 )
			throw new InvalidDataException( "LZ4 frame decode failed." );

		return output.AsSpan( 0, resultLength ).ToArray();
	}
}

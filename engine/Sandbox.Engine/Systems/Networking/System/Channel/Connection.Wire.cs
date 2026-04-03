using Sandbox.Compression;
using Sandbox.Network;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;

namespace Sandbox;

/// <summary>
/// Decoded wire packet with automatic ArrayPool cleanup on dispose.
/// Only allocated when decompressing a <see cref="Connection.FlagCompressed"/> packet;
/// raw packets return a zero-allocation slice of the receive buffer.
/// <para>Always use with <see langword="using"/> — CA2000 does not enforce this for ref structs.</para>
/// </summary>
internal ref struct WirePacket
{
	public ReadOnlySpan<byte> Data;
	private byte[] _rentedBuffer;

	internal WirePacket( ReadOnlySpan<byte> data, byte[] rentedBuffer )
	{
		Data = data;
		_rentedBuffer = rentedBuffer;
	}

	public void Dispose()
	{
		if ( _rentedBuffer != null )
		{
			ArrayPool<byte>.Shared.Return( _rentedBuffer );
			_rentedBuffer = null;
			Data = default;
		}
	}
}

public abstract partial class Connection
{
	// Transport-level wire flags — the first byte of every packet sent or received.
	internal const byte FlagRaw = 0; // [FlagRaw:1][data:N]               — no origLen needed
	internal const byte FlagCompressed = 1; // [FlagCompressed:1][origLen:4][lz4:N]
	internal const byte FlagChunk = 2; // [FlagChunk:1][index:4][total:4][data:N]

	private const int MinimumCompressionByteCount = 128;

	/// <summary>
	/// Upper bound on decompressed payload size. Any <see cref="FlagCompressed"/> packet claiming a
	/// larger original length is treated as corrupt/malicious and rejected.
	/// </summary>
	private const int MaxDecompressedByteSize = 256 * 1024 * 1024;

	/// <summary>
	/// Encode a <see cref="ByteStream"/> into a wire-ready packet, applying LZ4 when beneficial.
	/// Wire format: <c>[FlagRaw][data]</c> or <c>[FlagCompressed][origLen:4][lz4data]</c>.
	/// Returns a heap-allocated <see cref="byte"/>[] that transports may store beyond the call.
	/// </summary>
	internal static byte[] Encode( ByteStream stream )
	{
		var src = stream.ToSpan();

		if ( src.Length > MinimumCompressionByteCount )
		{
			var compressed = LZ4.CompressBlock( src );

			if ( compressed.Length < src.Length )
			{
				var outputSize = 1 + sizeof( int ) + compressed.Length;
				var output = new byte[outputSize];
				output[0] = FlagCompressed;
				BinaryPrimitives.WriteInt32LittleEndian( output.AsSpan( 1 ), src.Length );
				compressed.CopyTo( output.AsSpan( 1 + sizeof( int ) ) );
				return output;
			}
		}

		var result = new byte[1 + src.Length];
		result[0] = FlagRaw;
		src.CopyTo( result.AsSpan( 1 ) );
		return result;
	}

	/// <summary>
	/// Decode a wire payload (<see cref="FlagRaw"/> or <see cref="FlagCompressed"/>).
	/// <see cref="FlagChunk"/> packets must be fully reassembled by <see cref="OnRawPacketReceived"/>
	/// before reaching this method.
	/// Returns a disposable struct that manages the rented buffer lifetime.
	/// </summary>
	internal static WirePacket Decode( ReadOnlySpan<byte> data )
	{
		if ( data.Length < 1 )
			return new WirePacket( ReadOnlySpan<byte>.Empty, null );

		switch ( data[0] )
		{
			case FlagRaw:
				return new WirePacket( data.Slice( 1 ), null );
			case FlagCompressed:
				{
					const int headerSize = 1 + sizeof( int ); // flag + origLen
					if ( data.Length < headerSize )
						throw new InvalidDataException( $"Compressed packet too short ({data.Length}b, need {headerSize}b)" );

					var origLen = BinaryPrimitives.ReadInt32LittleEndian( data.Slice( 1, sizeof( int ) ) );
					if ( origLen <= 0 || origLen > MaxDecompressedByteSize )
						throw new InvalidDataException( $"Compressed origLen {origLen} out of range (1..{MaxDecompressedByteSize})" );

					var lz4Data = data.Slice( headerSize );
					var rentedBuffer = ArrayPool<byte>.Shared.Rent( origLen );
					int written = LZ4.DecompressBlock( lz4Data, rentedBuffer );

					if ( written != origLen )
					{
						ArrayPool<byte>.Shared.Return( rentedBuffer );
						throw new InvalidDataException( $"LZ4 decompressed {written}b but header claimed {origLen}b" );
					}

					Networking.TryRecordMessage( rentedBuffer.AsSpan( 0, origLen ) );
					return new WirePacket( rentedBuffer.AsSpan( 0, origLen ), rentedBuffer );
				}
			default:
				throw new InvalidOperationException( $"Unknown wire flag {data[0]}" );
		}
	}

	/// <summary>
	/// Build a transport-level chunk packet: <c>[FlagChunk:1][index:4][total:4][data:N]</c>.
	/// Chunks carry raw slices of an already-encoded (possibly compressed) payload.
	/// Chunk header is 9 bytes vs the old 14-byte envelope.
	/// </summary>
	internal static byte[] BuildChunkPacket( byte[] encoded, int offset, int length, int chunkIndex, int totalChunks )
	{
		var result = new byte[1 + sizeof( uint ) + sizeof( uint ) + length]; // 9-byte header
		result[0] = FlagChunk;
		BinaryPrimitives.WriteUInt32LittleEndian( result.AsSpan( 1 ), (uint)chunkIndex );
		BinaryPrimitives.WriteUInt32LittleEndian( result.AsSpan( 5 ), (uint)totalChunks );
		encoded.AsSpan( offset, length ).CopyTo( result.AsSpan( 9 ) );
		return result;
	}
}

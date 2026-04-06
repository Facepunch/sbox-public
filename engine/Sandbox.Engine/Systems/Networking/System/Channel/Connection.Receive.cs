using Sandbox.Network;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;

namespace Sandbox;

public abstract partial class Connection
{
	// Per-connection chunk reassembly buffer — rented from ArrayPool for the duration of a
	// single chunked message assembly, then returned immediately after delivery.
	// _chunkBufferLength == -1 signals "no assembly in progress".
	byte[] _chunkBuffer;
	int _chunkBufferLength = -1;
	uint _chunkExpectedIndex;

	/// <summary>
	/// Entry point for all incoming transport packets. Handles chunk reassembly transparently;
	/// fully assembled (or non-chunked) payloads are decoded and dispatched to <paramref name="handler"/>.
	/// </summary>
	internal void OnRawPacketReceived( ReadOnlySpan<byte> rawPacket, NetworkSystem.MessageHandler handler )
	{
		if ( rawPacket.Length < 1 ) return;

		if ( rawPacket[0] == FlagChunk )
		{
			AssembleChunk( rawPacket, handler );
			return;
		}

		DeliverDecoded( rawPacket, handler );
	}

	private void AssembleChunk( ReadOnlySpan<byte> rawPacket, NetworkSystem.MessageHandler handler )
	{
		if ( rawPacket.Length < 9 ) throw new InvalidDataException( "Chunk packet too short" );

		var index = BinaryPrimitives.ReadUInt32LittleEndian( rawPacket.Slice( 1 ) );
		var total = BinaryPrimitives.ReadUInt32LittleEndian( rawPacket.Slice( 5 ) );
		var chunkData = rawPacket.Slice( 9 );

		if ( index + 1 > total ) throw new InvalidDataException( $"chunkIndex {index} >= total {total}" );
		if ( total <= 1 ) throw new InvalidDataException( "Chunk total must be > 1" );
		if ( total > 1024 ) throw new InvalidDataException( $"Chunk total {total} exceeds 1024 limit" );

		if ( index == 0 )
		{
			// Pre-size to the upper bound: total chunks × max chunk size.
			if ( _chunkBuffer != null )
				ArrayPool<byte>.Shared.Return( _chunkBuffer );
			_chunkBuffer = ArrayPool<byte>.Shared.Rent( (int)total * MaxChunkSize );
			_chunkBufferLength = 0;
			_chunkExpectedIndex = 0;
		}

		//
		// This can happen when leaving a lobby (usually during connect) and then rejoining:
		// packets from the previous connection arrive after the new one has started.
		//
		if ( _chunkBufferLength < 0 )
			throw new InvalidDataException( $"Received chunk {index + 1} of {total} with no assembly in progress for {this}" );

		// Chunks must arrive in strict sequential order. Out-of-order or duplicate chunks
		// would silently corrupt the reassembled payload.
		if ( index != _chunkExpectedIndex )
		{
			_chunkBufferLength = -1;
			throw new InvalidDataException( $"Expected chunk {_chunkExpectedIndex} but received {index} of {total} from {this}" );
		}

		_chunkExpectedIndex++;

		if ( chunkData.Length > MaxChunkSize )
			throw new InvalidDataException( $"Chunk payload {chunkData.Length}b exceeds MaxChunkSize ({MaxChunkSize}b) from {this}" );

		if ( _chunkBufferLength + chunkData.Length > _chunkBuffer.Length )
			throw new InvalidDataException( $"Chunk overflows reassembly buffer ({_chunkBufferLength} + {chunkData.Length} > {_chunkBuffer.Length}) from {this}" );

		chunkData.CopyTo( _chunkBuffer.AsSpan( _chunkBufferLength ) );
		_chunkBufferLength += chunkData.Length;

		if ( index + 1 < total ) return; // Not the final chunk yet.

		var assembledLength = _chunkBufferLength;
		var assembledBuffer = _chunkBuffer;
		_chunkBufferLength = -1;
		_chunkBuffer = null;

		try
		{
			DeliverDecoded( assembledBuffer.AsSpan( 0, assembledLength ), handler );
		}
		finally
		{
			ArrayPool<byte>.Shared.Return( assembledBuffer );
		}
	}

	private void DeliverDecoded( ReadOnlySpan<byte> encoded, NetworkSystem.MessageHandler handler )
	{
		using var decoded = Decode( encoded );
		using var stream = ByteStream.CreateReader( decoded.Data );
		handler( new NetworkSystem.NetworkMessage { Source = this, Data = stream } );
		MessagesRecieved++;
	}

	/// <summary>
	/// Returns the rented chunk reassembly buffer to the shared pool, if one is held.
	/// Called from <see cref="Connection.Close"/>.
	/// </summary>
	internal void ReleaseChunkBuffer()
	{
		if ( _chunkBuffer != null )
		{
			ArrayPool<byte>.Shared.Return( _chunkBuffer );
			_chunkBuffer = null;
			_chunkBufferLength = -1;
		}
	}
}

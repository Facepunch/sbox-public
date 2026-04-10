using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Sandbox.Compression;

namespace Networking;

// Decode uses a shared static buffer (single-threaded by design in production),
// so these tests must not run in parallel with each other.
[TestClass]
[DoNotParallelize]
public class ConnectionWire
{
	[TestMethod]
	public void RoundTrip_SmallPayload_UsesRawFlag()
	{
		using var stream = ByteStream.Create( 32 );
		stream.Write( 42 );
		stream.Write( "hello" );

		var encoded = Connection.Encode( stream );

		Assert.AreEqual( Connection.FlagRaw, encoded[0], "Small payload should use FlagRaw" );

		var decoded = Connection.Decode( encoded );
		var original = stream.ToSpan();
		Assert.IsTrue( decoded.SequenceEqual( original ) );
	}

	[TestMethod]
	public void RoundTrip_LargePayload_Compresses()
	{
		// Build a payload large enough that LZ4 should actually compress it (repetitive data).
		using var stream = ByteStream.Create( 1024 );
		for ( int i = 0; i < 200; i++ )
			stream.Write( 0x41414141 );

		var encoded = Connection.Encode( stream );

		Assert.AreEqual( Connection.FlagCompressed, encoded[0], "Large repetitive payload should compress" );

		var decoded = Connection.Decode( encoded );
		var original = stream.ToSpan();
		Assert.IsTrue( decoded.SequenceEqual( original ) );
	}

	[TestMethod]
	public void BuildChunkPacket_HeaderFormat()
	{
		var payload = new byte[] { 0xAA, 0xBB, 0xCC };
		var chunk = Connection.BuildChunkPacket( payload, 0, payload.Length, chunkIndex: 2, totalChunks: 5 );

		Assert.AreEqual( Connection.FlagChunk, chunk[0] );
		Assert.AreEqual( 2u, BinaryPrimitives.ReadUInt32LittleEndian( chunk.AsSpan( 1 ) ) );
		Assert.AreEqual( 5u, BinaryPrimitives.ReadUInt32LittleEndian( chunk.AsSpan( 5 ) ) );
		Assert.IsTrue( chunk.AsSpan( 9 ).SequenceEqual( payload ) );
	}

	[TestMethod]
	public void Decode_EmptyPacket_ReturnsEmpty()
	{
		var result = Connection.Decode( ReadOnlySpan<byte>.Empty );
		Assert.AreEqual( 0, result.Length );
	}

	[TestMethod]
	public void Decode_UnknownFlag_Throws()
	{
		var packet = new byte[] { 0xFF, 0x01, 0x02 };
		Assert.ThrowsException<InvalidOperationException>( () =>
		{
			Connection.Decode( packet );
		} );
	}

	[TestMethod]
	public void Decode_CompressedTooShort_Throws()
	{
		// FlagCompressed but no origLen bytes
		var packet = new byte[] { Connection.FlagCompressed, 0x01 };
		Assert.ThrowsException<InvalidDataException>( () =>
		{
			Connection.Decode( packet );
		} );
	}

	[TestMethod]
	public void Decode_CompressedNegativeOrigLen_Throws()
	{
		// FlagCompressed + origLen = -1
		var packet = new byte[1 + sizeof( int )];
		packet[0] = Connection.FlagCompressed;
		BinaryPrimitives.WriteInt32LittleEndian( packet.AsSpan( 1 ), -1 );

		Assert.ThrowsException<InvalidDataException>( () =>
		{
			Connection.Decode( packet );
		} );
	}

	[TestMethod]
	public void Decode_CompressedZeroOrigLen_Throws()
	{
		var packet = new byte[1 + sizeof( int )];
		packet[0] = Connection.FlagCompressed;
		BinaryPrimitives.WriteInt32LittleEndian( packet.AsSpan( 1 ), 0 );

		Assert.ThrowsException<InvalidDataException>( () =>
		{
			Connection.Decode( packet );
		} );
	}

	[TestMethod]
	public void Decode_CompressedHugeOrigLen_Throws()
	{
		// origLen = int.MaxValue — way above MaxDecompressedSize
		var packet = new byte[1 + sizeof( int ) + 4];
		packet[0] = Connection.FlagCompressed;
		BinaryPrimitives.WriteInt32LittleEndian( packet.AsSpan( 1 ), int.MaxValue );

		Assert.ThrowsException<InvalidDataException>( () =>
		{
			Connection.Decode( packet );
		} );
	}

	[TestMethod]
	public void Decode_CompressedSizeMismatch_Throws()
	{
		// Craft a valid compressed packet but tamper the origLen to be larger than actual data.
		var original = new byte[256];
		Random.Shared.NextBytes( original );
		var compressed = LZ4.CompressBlock( original );

		// Claim origLen is original.Length + 100 but LZ4 will only decompress original.Length bytes.
		var packet = new byte[1 + sizeof( int ) + compressed.Length];
		packet[0] = Connection.FlagCompressed;
		BinaryPrimitives.WriteInt32LittleEndian( packet.AsSpan( 1 ), original.Length + 100 );
		compressed.CopyTo( packet.AsSpan( 1 + sizeof( int ) ) );

		Assert.ThrowsException<InvalidDataException>( () =>
		{
			Connection.Decode( packet );
		} );
	}

	[TestMethod]
	public void AssembleChunk_TooShort_Throws()
	{
		// FlagChunk header needs 9 bytes minimum
		var packet = new byte[] { Connection.FlagChunk, 0x00, 0x00, 0x00, 0x00 };

		var conn = new StubConnection();
		Assert.ThrowsException<InvalidDataException>( () =>
		{
			conn.OnRawPacketReceived( packet, StubHandler );
		} );
	}

	[TestMethod]
	public void AssembleChunk_IndexExceedsTotal_Throws()
	{
		// index=5, total=3 — invalid
		var packet = MakeChunkPacket( chunkIndex: 5, totalChunks: 3, dataLength: 10 );

		var conn = new StubConnection();
		Assert.ThrowsException<InvalidDataException>( () =>
		{
			conn.OnRawPacketReceived( packet, StubHandler );
		} );
	}

	[TestMethod]
	public void AssembleChunk_TotalOne_Throws()
	{
		// total=1 is invalid for chunked messages (should have been sent as a single packet)
		var packet = MakeChunkPacket( chunkIndex: 0, totalChunks: 1, dataLength: 10 );

		var conn = new StubConnection();
		Assert.ThrowsException<InvalidDataException>( () =>
		{
			conn.OnRawPacketReceived( packet, StubHandler );
		} );
	}

	[TestMethod]
	public void AssembleChunk_TotalExceedsLimit_Throws()
	{
		var packet = MakeChunkPacket( chunkIndex: 0, totalChunks: 2000, dataLength: 10 );

		var conn = new StubConnection();
		Assert.ThrowsException<InvalidDataException>( () =>
		{
			conn.OnRawPacketReceived( packet, StubHandler );
		} );
	}

	[TestMethod]
	public void AssembleChunk_OutOfOrderWithoutFirst_Throws()
	{
		// Send chunk index=1 without a preceding index=0 — no assembly in progress
		var packet = MakeChunkPacket( chunkIndex: 1, totalChunks: 3, dataLength: 10 );

		var conn = new StubConnection();
		Assert.ThrowsException<InvalidDataException>( () =>
		{
			conn.OnRawPacketReceived( packet, StubHandler );
		} );
	}

	// Helpers

	private static byte[] MakeChunkPacket( uint chunkIndex, uint totalChunks, int dataLength )
	{
		var result = new byte[9 + dataLength];
		result[0] = Connection.FlagChunk;
		BinaryPrimitives.WriteUInt32LittleEndian( result.AsSpan( 1 ), chunkIndex );
		BinaryPrimitives.WriteUInt32LittleEndian( result.AsSpan( 5 ), totalChunks );
		return result;
	}

	private static void StubHandler( Sandbox.Network.NetworkSystem.NetworkMessage msg )
	{
		// No-op; we only care about whether the receive path throws.
	}

	/// <summary>
	/// Minimal concrete <see cref="Connection"/> for testing receive-side logic without a real transport.
	/// </summary>
	private sealed class StubConnection : Connection
	{
		public override bool IsHost => false;
		internal override void InternalSend( byte[] data, NetFlags flags ) { }
		internal override void InternalRecv( Sandbox.Network.NetworkSystem.MessageHandler handler ) { }
		internal override void InternalClose( int closeCode, string closeReason ) { }
	}
}

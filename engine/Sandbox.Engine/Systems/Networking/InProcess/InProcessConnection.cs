using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using Sandbox.Compression;

namespace Sandbox.Network;

/// <summary>
/// One endpoint of an in-process connection pair, used by editor "docked client" sessions
/// (<see cref="InProcessClientSession"/>). The peer endpoint lives in the same process:
/// the host's <see cref="NetworkSystem"/> owns one end (via <see cref="InProcessSocket"/>),
/// an in-process client's system owns the other. Messages are queued in memory and drained
/// on the main thread when each side's system ticks - there is no wire, no thread hops, and
/// no chunking (no packet size limits in memory).
/// </summary>
internal sealed class InProcessConnection : Connection
{
	/// <summary>
	/// The other end of this connection. Messages sent here arrive in the peer's inbox.
	/// </summary>
	public InProcessConnection Peer { get; private set; }

	// (payload, isWireEncoded): SendStream queues the raw payload directly - encoding
	// (compression) is pointless in-process. Broadcast's single-encode path arrives through
	// Send() with already-encoded bytes, flagged so InternalRecv knows to decode.
	readonly ConcurrentQueue<(byte[] Data, bool Encoded)> _inbox = new();

	readonly bool _representsHost;
	bool _closed;

	// Private decode buffer: Connection's shared static decode buffer belongs to the network
	// thread; we decode on the main thread and must not race it.
	byte[] _decodeBuffer;

	/// <summary>
	/// Fake identity stamped into the handshake so each in-process client appears as a
	/// distinct player. Only used on the client-side endpoint (the one that represents
	/// the host from the client's point of view).
	/// </summary>
	public string FakeName { get; init; }
	public SteamId FakeSteamId { get; init; }

	public override string Address => "in-process";
	public override bool IsHost => _representsHost;

	InProcessConnection( bool representsHost )
	{
		_representsHost = representsHost;
	}

	/// <summary>
	/// Create a connected endpoint pair. <c>hostSide</c> is registered with the host's system
	/// and represents the client; <c>clientSide</c> becomes the client system's connection and
	/// represents the host.
	/// </summary>
	public static (InProcessConnection HostSide, InProcessConnection ClientSide) CreatePair( string fakeName, SteamId fakeSteamId )
	{
		var hostSide = new InProcessConnection( representsHost: false );
		var clientSide = new InProcessConnection( representsHost: true )
		{
			FakeName = fakeName,
			FakeSteamId = fakeSteamId
		};

		hostSide.Peer = clientSide;
		clientSide.Peer = hostSide;

		return (hostSide, clientSide);
	}

	/// <summary>
	/// The client is composing its UserInfo to send to the host - swap in the fake identity.
	/// </summary>
	internal override bool OnReceiveServerInfo( ref UserInfo userInfo, ServerInfo serverInfo )
	{
		if ( !string.IsNullOrEmpty( FakeName ) )
		{
			userInfo.Name = FakeName;
			userInfo.SteamId = FakeSteamId;

			// Don't re-verify the same Steam inventory once per docked client.
			userInfo.InventoryBlob = null;
		}

		return true;
	}

	/// <summary>
	/// Normal send path: skip wire encoding entirely, hand the raw payload to the peer.
	/// </summary>
	internal override void SendStream( ByteStream stream, NetFlags flags = NetFlags.Reliable )
	{
		if ( _closed || Peer is null )
			return;

		Peer._inbox.Enqueue( (stream.ToArray(), false) );
		MessagesSent++;
	}

	/// <summary>
	/// Pre-encoded path (Broadcast encodes once for all connections). Never chunk - the inbox
	/// has no packet size limit.
	/// </summary>
	internal override void Send( byte[] encoded, NetFlags flags )
	{
		if ( _closed || Peer is null )
			return;

		Peer._inbox.Enqueue( (encoded, true) );
		MessagesSent++;
	}

	internal override void InternalSend( byte[] data, NetFlags flags )
	{
		// All sends are intercepted by the SendStream/Send overrides above.
		if ( _closed || Peer is null )
			return;

		Peer._inbox.Enqueue( (data, true) );
	}

	internal override void InternalRecv( NetworkSystem.MessageHandler handler )
	{
		// Bound the drain to what's queued right now so a handler that triggers an immediate
		// reply-to-self can't spin this loop forever.
		var count = _inbox.Count;

		while ( count-- > 0 && _inbox.TryDequeue( out var item ) )
		{
			MessagesRecieved++;

			var payload = item.Encoded ? DecodeLocal( item.Data ) : item.Data;

			var msg = new NetworkSystem.NetworkMessage
			{
				Source = this,
				Data = ByteStream.CreateReader( payload )
			};

			handler( msg );
			msg.Data.Dispose();
		}
	}

	internal override void InternalClose( int closeCode, string closeReason )
	{
		_closed = true;
	}

	/// <summary>
	/// Decode a wire-encoded payload using a private buffer (see field comment).
	/// Mirrors <see cref="Connection.Decode"/> minus the shared state; chunk packets can't
	/// occur because our Send override never chunks.
	/// </summary>
	ReadOnlySpan<byte> DecodeLocal( byte[] data )
	{
		if ( data.Length < 1 )
			return ReadOnlySpan<byte>.Empty;

		switch ( data[0] )
		{
			case FlagRaw:
				return data.AsSpan( 1 );

			case FlagCompressed:
				{
					const int headerSize = 1 + sizeof( int );
					if ( data.Length < headerSize )
						throw new InvalidDataException( $"Compressed packet too short ({data.Length}b)" );

					var origLen = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( 1, sizeof( int ) ) );

					if ( _decodeBuffer == null || _decodeBuffer.Length < origLen )
						_decodeBuffer = GC.AllocateUninitializedArray<byte>( origLen );

					var written = LZ4.DecompressBlock( data.AsSpan( headerSize ), _decodeBuffer );

					if ( written != origLen )
						throw new InvalidDataException( $"LZ4 decompressed {written}b but header claimed {origLen}b" );

					return _decodeBuffer.AsSpan( 0, origLen );
				}

			default:
				throw new InvalidOperationException( $"Unexpected wire flag {data[0]} on in-process connection" );
		}
	}
}

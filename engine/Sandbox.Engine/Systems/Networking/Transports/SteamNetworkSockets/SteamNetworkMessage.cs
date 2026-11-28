using System.Threading.Channels;

namespace Sandbox.Network;

internal readonly record struct IncomingSteamMessage( HSteamNetConnection Connection, ulong SteamId, NetPacket Packet )
{
	/// <summary>
	/// Reads this tick's batches from a Steam channel, connection or poll group.
	/// </summary>
	internal static unsafe int Receive<T>( ChannelWriter<IncomingSteamMessage> writer, T source, Func<T, nint, int, int> receiveBatch )
	{
		var batch = stackalloc nint[Networking.ReceiveBatchSize];
		int limit = Networking.ReceiveBatchSizePerTick;
		int total = 0;

		do
		{
			int count = receiveBatch( source, (nint)batch, Networking.ReceiveBatchSize );
			if ( count <= 0 )
				break;

			EnqueueBatch( writer, batch, count );
			total += count;
		} while ( limit <= 0 || total < limit );

		return total;
	}

	/// <summary>
	/// Copies the received payloads into the queue and releases the native batch.
	/// </summary>
	internal static unsafe void EnqueueBatch( ChannelWriter<IncomingSteamMessage> writer, nint* messages, int count )
	{
		try
		{
			for ( int i = 0; i < count; i++ )
			{
				var data = SteamNative.NetworkingMessages.GetMessageData( messages[i], out var size, out var connection, out var steamId );
				var packet = NetPacket.Rent( size );
				new ReadOnlySpan<byte>( (void*)data, size ).CopyTo( packet.Buffer );

				if ( !writer.TryWrite( new( connection, steamId, packet ) ) )
					packet.Release();
			}
		}
		finally
		{
			SteamNative.NetworkingMessages.ReleaseMessages( (nint)messages, count );
		}
	}

	/// <summary>
	/// Releases the queued packet after delivery, including when its connection has already closed.
	/// </summary>
	internal void Dispatch( Connection connection, NetworkSystem.MessageHandler handler )
	{
		try
		{
			connection?.OnRawPacketReceived( Packet.Span, handler );
		}
		finally
		{
			Packet.Release();
		}
	}

	/// <summary>
	/// Stops accepting packets and releases any still waiting for delivery.
	/// </summary>
	internal static void Close( Channel<IncomingSteamMessage> channel )
	{
		channel.Writer.TryComplete();
		while ( channel.Reader.TryRead( out var message ) )
			message.Packet.Release();
	}
}

internal struct OutgoingSteamMessage
{
	public HSteamNetConnection Connection { get; set; }
	public NetPacket Packet { get; set; }
	/// <summary>
	/// Delivery options for this packet.
	/// </summary>
	public NetFlags Flags { get; set; }

	/// <summary>
	/// Holds a reference until the network thread copies the packet to Steam.
	/// </summary>
	internal static void Enqueue( ChannelWriter<OutgoingSteamMessage> writer, HSteamNetConnection connection, NetPacket packet, NetFlags flags )
	{
		packet.AddReference();
		if ( !writer.TryWrite( new() { Connection = connection, Packet = packet, Flags = flags } ) )
			packet.Release();
	}

	/// <summary>
	/// Sends the queued packets in batches, up to the per-tick send limit.
	/// </summary>
	internal static unsafe int SendQueued( ChannelReader<OutgoingSteamMessage> reader )
	{
		if ( !reader.TryRead( out var message ) )
			return 0;

		const int batchSize = 256;
		var batch = stackalloc nint[batchSize];
		int count = 0;
		int sent = 0;
		int limit = Networking.MaxOutgoingMessagesPerTick;

		do
		{
			var native = message.AllocateMessage();
			message.Packet.Release();
			if ( native == 0 )
				continue;

			batch[count++] = native;
			sent++;
			if ( count == batchSize )
			{
				SteamNative.NetworkingSockets.SendMessages( (nint)batch, count );
				count = 0;
			}
		} while ( (limit <= 0 || sent < limit) && reader.TryRead( out message ) );

		if ( count > 0 )
			SteamNative.NetworkingSockets.SendMessages( (nint)batch, count );

		return sent;
	}

	/// <summary>
	/// Releases queued send references when the socket closes.
	/// </summary>
	internal static void Close( Channel<OutgoingSteamMessage> channel )
	{
		channel.Writer.TryComplete();
		while ( channel.Reader.TryRead( out var message ) )
			message.Packet.Release();
	}

	/// <summary>
	/// Copies the packet into a Steam-owned message for sending.
	/// </summary>
	internal unsafe nint AllocateMessage()
	{
		fixed ( byte* data = Packet.Buffer )
		{
			return SteamNative.NetworkingUtils.AllocateMessage( Connection, data, Packet.Length, Flags );
		}
	}
}

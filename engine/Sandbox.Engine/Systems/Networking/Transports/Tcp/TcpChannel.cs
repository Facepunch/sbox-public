using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;

namespace Sandbox.Network;


/// <summary>
/// A listen socket, one socket to many. We should really use this just dedicated servers imo.
/// </summary>
internal class TcpChannel : Connection
{
	internal readonly Channel<byte[]> incoming = Channel.CreateUnbounded<byte[]>();

	string _address = "Tcp";

	public override string Address => _address;

	public bool IsConnected => client?.Connected ?? false;

	async Task SocketLoop( CancellationToken token )
	{
		try
		{
			while ( client?.Connected != true )
			{
				await Task.Delay( 10, token );
				token.ThrowIfCancellationRequested();
			}

			_address = client?.Client?.RemoteEndPoint?.ToString() ?? client?.Client?.LocalEndPoint?.ToString() ?? "Tcp";

			var stream = client.GetStream();

			_ = Task.Run( async () => await SendThread( token ), token );
			_ = Task.Run( async () => await FakeLagProcess( token ), token );

			while ( !token.IsCancellationRequested )
			{
				var header = new byte[sizeof( int )];
				await stream.ReadExactlyAsync( header.AsMemory( 0, header.Length ), token );

				var messageLength = BitConverter.ToInt32( header );
				if ( messageLength <= 0 )
				{
					Log.Warning( $"TcpChannel: Invalid message length {messageLength} from {Address}" );
					break;
				}

				var packet = new byte[messageLength];
				await stream.ReadExactlyAsync( packet.AsMemory( 0, packet.Length ), token );
				await incoming.Writer.WriteAsync( packet, token );
			}
		}
		catch ( OperationCanceledException )
		{
			// normal
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"TcpChannel: {e.Message}" );
		}
		finally
		{
			client?.Close();
			client?.Dispose();
			client = null;
		}
	}

	readonly CancellationTokenSource tokenSource;

	public bool IsValid => true;

	bool isHost;
	public override bool IsHost => isHost;

	TcpClient client;

	public TcpChannel( TcpClient client )
	{
		this.client = client;
		client.ReceiveBufferSize = 1024 * 1024;
		client.SendBufferSize = 1024 * 1024;
		client.NoDelay = true;

		tokenSource = new();
		isHost = false;

		_ = Task.Run( () => SocketLoop( tokenSource.Token ), tokenSource.Token );
	}

	public TcpChannel( string host, int port )
	{
		client = new();
		client.ReceiveBufferSize = 1024 * 1024;
		client.SendBufferSize = 1024 * 1024;
		client.LingerState = new( true, 15 ); // 15 seconds is a long time, but we want reliability

		tokenSource = new();
		isHost = true;

		_ = Task.Run( () => ConnectAndRunAsync( host, port, tokenSource.Token ) );
	}

	~TcpChannel()
	{
		tokenSource?.Cancel();
	}

	Channel<byte[]> sendChannel = Channel.CreateUnbounded<byte[]>();

	private ConcurrentQueue<(byte[], RealTimeUntil, NetworkSystem.MessageHandler)> fakeLagIncoming = new();
	private ConcurrentQueue<(byte[], RealTimeUntil)> fakeLagOutgoing = new();

	private async Task ConnectAndRunAsync( string host, int port, CancellationToken token )
	{
		try
		{
			await client.ConnectAsync( host, port );
			await SocketLoop( token );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"TcpChannel connect failed: {e.Message}" );
			client?.Close();
			client?.Dispose();
			client = null;
		}
	}

	private async Task FakeLagProcess( CancellationToken token )
	{
		try
		{
			while ( !token.IsCancellationRequested )
			{
				var processedPacket = false;

				if ( fakeLagIncoming.TryPeek( out var i ) )
				{
					if ( i.Item2 )
					{
						if ( fakeLagIncoming.TryDequeue( out _ ) )
						{
							processedPacket = true;
							InvokeMessageHandler( i.Item3, i.Item1 );
						}
					}
				}

				if ( fakeLagOutgoing.TryPeek( out var o ) )
				{
					if ( o.Item2 )
					{
						if ( fakeLagOutgoing.TryDequeue( out _ ) )
						{
							processedPacket = true;
							await sendChannel.Writer.WriteAsync( BitConverter.GetBytes( o.Item1.Length ), token );
							await sendChannel.Writer.WriteAsync( o.Item1, token );
						}
					}
				}

				if ( !processedPacket ) // Maybe something will be ready later?
					await Task.Delay( 1, token );
			}
		}
		catch ( OperationCanceledException )
		{
			// normal
		}
		catch ( Exception e )
		{
			Log.Error( e );
		}
	}

	internal override void InternalSend( byte[] output, NetFlags flags )
	{
		if ( client?.Connected != true )
			return;

		if ( Networking.FakePacketLoss > 0 && !flags.HasFlag( NetFlags.Reliable ) )
		{
			var chance = Random.Shared.Next( 0, 100 );
			if ( chance <= Networking.FakePacketLoss )
				return;
		}

		if ( Networking.FakeLag > 0 )
		{
			fakeLagOutgoing.Enqueue( (output, Networking.FakeLag / 1000f) );
			return;
		}

		try
		{
			sendChannel.Writer.TryWrite( BitConverter.GetBytes( output.Length ) );
			sendChannel.Writer.TryWrite( output );
		}
		catch ( Exception )
		{
			// Probably disconnected, who cares
		}
	}

	/// <summary>
	/// Send the network data in a thread. This prevents the client from freezing
	/// up when running a client and server in the same process. In reality this only
	/// really happens in unit tests, but better safe than sorry.
	/// </summary>
	private async Task SendThread( CancellationToken token )
	{
		try
		{
			while ( !token.IsCancellationRequested )
			{
				await sendChannel.Reader.WaitToReadAsync( token );

				if ( client?.Connected != true || !sendChannel.Reader.TryRead( out byte[] data ) )
					continue;

				var writeStream = client.GetStream();
				try
				{
					await writeStream.WriteAsync( data, token );
					MessagesSent++;
				}
				catch ( System.IO.IOException )
				{
					continue;
				}
			}
		}
		catch ( OperationCanceledException )
		{
			// normal
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"TcpChannel: {e.Message}" );
		}
	}

	internal override void InternalClose( int closeCode, string closeReason )
	{
		tokenSource.Cancel();
		tokenSource.Dispose();
		GC.SuppressFinalize( this );
	}

	internal override void InternalRecv( NetworkSystem.MessageHandler handler )
	{
		while ( incoming.Reader.TryRead( out byte[] data ) )
		{
			if ( Networking.FakeLag > 0 )
			{
				fakeLagIncoming.Enqueue( (data, Networking.FakeLag / 1000f, handler) );
				continue;
			}

			OnRawPacketReceived( data, handler );
		}
	}

	private void InvokeMessageHandler( NetworkSystem.MessageHandler handler, byte[] data )
	{
		OnRawPacketReceived( data, handler );
	}
}

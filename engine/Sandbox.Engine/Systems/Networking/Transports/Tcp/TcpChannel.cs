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

	string _name = "Tcp Channel";
	string _address = "Tcp";

	public override string Address => _address;
	public override string Name => _name;

	public bool IsConnected => client?.Connected ?? false;

	async void SocketLoop( CancellationToken token )
	{
		try
		{
			while ( !client.Connected )
			{
				await Task.Delay( 10 );
				token.ThrowIfCancellationRequested();
			}

			_name = client.Client.RemoteEndPoint?.ToString() ?? client.Client.LocalEndPoint.ToString();
			_address = _name;

			var stream = client.GetStream();

			_ = Task.Run( async () => await SendThread( token ) );
			_ = FakeLagProcess();

			while ( !token.IsCancellationRequested )
			{
				while ( client.Available > 0 )
				{
					var header = new byte[sizeof( int )];
					stream.ReadExactly( header );

					var messageLength = BitConverter.ToInt32( header );
					if ( messageLength <= 0 )
					{
						Log.Warning( "Weird stuff here" );
						return;
					}

					var packet = new byte[messageLength];
					stream.ReadExactly( packet );

					incoming.Writer.TryWrite( packet );
				}

				await Task.Delay( 1 );
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

		client?.Close();
		client?.Dispose();
		client = null;
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

		SocketLoop( tokenSource.Token );
	}

	public TcpChannel( string host, int port )
	{
		client = new();
		client.ConnectAsync( host, port );
		client.ReceiveBufferSize = 1024 * 1024;
		client.SendBufferSize = 1024 * 1024;
		client.LingerState = new( true, 15 ); // 15 seconds is a long time, but we want reliability

		tokenSource = new();
		isHost = true;

		SocketLoop( tokenSource.Token );
	}

	~TcpChannel()
	{
		tokenSource.Cancel();
	}

	Channel<byte[]> sendChannel = Channel.CreateUnbounded<byte[]>();

	private Queue<(byte[], RealTimeUntil, NetworkSystem.MessageHandler)> fakeLagIncoming = new();
	private Queue<(byte[], RealTimeUntil)> fakeLagOutgoing = new();

	private async Task FakeLagProcess()
	{
		try
		{
			while ( !tokenSource.IsCancellationRequested )
			{
				var processedPacket = false;

				if ( fakeLagIncoming.TryPeek( out var i ) )
				{
					if ( i.Item2 )
					{
						processedPacket = true;
						InvokeMessageHandler( i.Item3, i.Item1 );
						fakeLagIncoming.Dequeue();
					}
				}

				if ( fakeLagOutgoing.TryPeek( out var o ) )
				{
					if ( o.Item2 )
					{
						processedPacket = true;
						sendChannel.Writer.TryWrite( BitConverter.GetBytes( o.Item1.Length ) );
						sendChannel.Writer.TryWrite( o.Item1 );
						fakeLagOutgoing.Dequeue();
					}
				}

				if ( !processedPacket ) // Maybe something will be ready later?
					await Task.Delay( 1 );
			}
		}
		catch ( Exception e )
		{
			Log.Error( e );
		}
	}

	internal override void InternalSend( ByteStream stream, NetFlags flags )
	{
		if ( !client.Connected )
			return;

		if ( Networking.FakePacketLoss > 0 && !flags.HasFlag( NetFlags.Reliable ) )
		{
			var chance = Random.Shared.Next( 0, 100 );
			if ( chance <= Networking.FakePacketLoss )
				return;
		}

		byte[] output = Networking.EncodeStream( stream );

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
				await sendChannel.Reader.WaitToReadAsync();

				if ( !sendChannel.Reader.TryRead( out byte[] data ) )
					continue;

				client.GetStream().Write( data );
				MessagesSent++;
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
		GC.SuppressFinalize( this );
	}

	internal override void InternalRecv( NetworkSystem.MessageHandler handler )
	{
		while ( incoming.Reader.TryRead( out byte[] data ) )
		{
			Span<byte> output = Networking.DecodeStream( data );

			if ( Networking.FakeLag > 0 )
			{
				fakeLagIncoming.Enqueue( (output.ToArray(), Networking.FakeLag / 1000f, handler) );
				continue;
			}

			using ByteStream stream = ByteStream.CreateReader( output );

			handler( new NetworkSystem.NetworkMessage
			{
				Data = stream,
				Source = this
			} );

			MessagesRecieved++;
		}
	}

	private void InvokeMessageHandler( NetworkSystem.MessageHandler handler, byte[] data )
	{
		using ByteStream stream = ByteStream.CreateReader( data );
		handler( new() { Data = stream, Source = this } );

		MessagesRecieved++;
	}
}

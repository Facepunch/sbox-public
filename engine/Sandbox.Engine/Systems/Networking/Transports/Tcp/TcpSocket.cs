using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Sandbox.Network;


/// <summary>
/// A listen socket over TCP. For testing locally.
/// </summary>
internal class TcpSocket : NetworkSocket, IValid
{
	Action queue;

	List<TcpChannel> Connections = new List<TcpChannel>();

	async Task SocketThread( string address, int port, CancellationToken token )
	{
		using var listener = new TcpListener( new IPEndPoint( IPAddress.Parse( address ), port ) );

		try
		{
			listener.Start();
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, "Couldnt start TcpSocket" );
			return;
		}

		while ( !token.IsCancellationRequested )
		{
			try
			{
				var client = await listener.AcceptTcpClientAsync( token );
				var c = new TcpChannel( client );

				Connections.Add( c );
				queue += () => OnClientConnect?.Invoke( c );
			}
			catch ( OperationCanceledException )
			{
				// Dispose() was called
			}
			catch ( Exception e )
			{
				Log.Warning( e, $"TcpSocket exception: {e.Message}" );
			}
		}

		listener.Stop();
	}

	[SkipHotload]
	CancellationTokenSource tokenSource;

	public bool IsValid => true;

	public TcpSocket( string address, int port )
	{
		tokenSource = new();
		_ = SocketThread( address, port, tokenSource.Token );
	}

	~TcpSocket()
	{
		Dispose();
	}

	internal override void Dispose()
	{
		tokenSource.Cancel();
		tokenSource.Dispose();

		GC.SuppressFinalize( this );
	}

	internal override void ProcessMessagesInThread()
	{

	}

	internal override void GetIncomingMessages( NetworkSystem.MessageHandler handler )
	{
		try
		{
			queue?.Invoke();
		}
		catch ( Exception e )
		{
			Log.Warning( e );
		}

		queue = null;

		foreach ( var c in Connections )
		{
			if ( !c.IsConnected )
			{
				Connections.Remove( c );
				OnClientDisconnect?.Invoke( c );
				c.Close( 0, "Disconnect" );
				return;
			}

			c.GetIncomingMessages( handler );
		}
	}
}

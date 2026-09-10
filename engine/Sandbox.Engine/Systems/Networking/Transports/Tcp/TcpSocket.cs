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

	internal override bool SupportsHostMigration => true;

	async Task SocketThread( string address, int port, CancellationToken token )
	{
		TcpListener listener = null;
		SocketException lastError = null;

		try
		{
			// The previous host may still hold the port; a failed listener can't be reused
			for ( var attempt = 0; attempt < 20 && !token.IsCancellationRequested; attempt++ )
			{
				try
				{
					listener = new TcpListener( new IPEndPoint( IPAddress.Parse( address ), port ) );
					listener.Start();
					break;
				}
				catch ( SocketException e )
				{
					lastError = e;
					listener?.Dispose();
					listener = null;
					await Task.Delay( 500 );
				}
				catch ( System.Exception e )
				{
					Log.Warning( e, "Couldnt start TcpSocket" );
					return;
				}
			}

			if ( listener is null )
			{
				Log.Warning( $"Couldnt start TcpSocket on port {port}: {lastError?.Message}" );
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
		finally
		{
			listener?.Dispose();
		}
	}

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

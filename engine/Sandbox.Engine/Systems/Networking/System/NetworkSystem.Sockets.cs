using System.Collections.Concurrent;

namespace Sandbox.Network;

internal partial class NetworkSystem
{
	[SkipHotload] readonly ConcurrentBag<NetworkSocket> sockets = new();

	internal IEnumerable<NetworkSocket> Sockets => sockets;

	internal void AddSocket( NetworkSocket socket )
	{
		if ( IsDisconnected )
		{
			Log.Warning( "Tried to call AddSocket on a disconnected NetworkSystem!" );
			socket.Dispose();
			return;
		}

		sockets.Add( socket );

		socket.OnClientConnect = OnConnected;
		socket.OnClientDisconnect = OnDisconnected;
		socket.Initialize( this );
	}

	void CloseSockets()
	{
		Log.Trace( $"NetworkSystem.CloseSockets" );

		foreach ( var socket in sockets )
		{
			if ( !socket.AutoDispose )
				continue;

			socket?.Dispose();
		}

		sockets.Clear();
	}
}

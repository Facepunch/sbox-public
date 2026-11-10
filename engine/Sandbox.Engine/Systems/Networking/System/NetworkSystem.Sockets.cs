using System.Threading;

namespace Sandbox.Network;

internal partial class NetworkSystem
{
	// Swapped rather than mutated, the network thread reads it without a lock
	[SkipHotload] NetworkSocket[] sockets = [];
	[SkipHotload] readonly Lock socketsLock = new();

	internal NetworkSocket[] Sockets => sockets;

	internal void AddSocket( NetworkSocket socket )
	{
		if ( IsDisconnected )
		{
			Log.Warning( "Tried to call AddSocket on a disconnected NetworkSystem!" );
			socket.Dispose();
			return;
		}

		lock ( socketsLock )
			sockets = [.. sockets, socket];

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

		lock ( socketsLock )
			sockets = [];
	}
}

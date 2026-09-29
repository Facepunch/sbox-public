namespace Sandbox.Network;

internal sealed class InProcessSocket : NetworkSocket
{
	readonly List<InProcessConnection> _clients = new();

	internal void Accept( InProcessConnection hostSide )
	{
		_clients.Add( hostSide );
		OnClientConnect?.Invoke( hostSide );
	}

	internal void Disconnect( InProcessConnection hostSide )
	{
		if ( !_clients.Remove( hostSide ) )
			return;

		OnClientDisconnect?.Invoke( hostSide );
		hostSide.Close( 0, "Disconnected" );
	}

	internal override void GetIncomingMessages( NetworkSystem.MessageHandler handler )
	{
		for ( var i = _clients.Count - 1; i >= 0; i-- )
		{
			if ( i >= _clients.Count )
				continue;

			_clients[i].GetIncomingMessages( handler );
		}
	}

	internal override void ProcessMessagesInThread()
	{
	}

	internal override void Dispose()
	{
		foreach ( var client in _clients.ToArray() )
		{
			client.Close( 0, "Socket disposed" );
		}

		_clients.Clear();
	}
}

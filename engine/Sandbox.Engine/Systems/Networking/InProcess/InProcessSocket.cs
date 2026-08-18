namespace Sandbox.Network;

/// <summary>
/// Host-side socket for in-process client sessions. Accepting an endpoint kicks off the
/// standard handshake; the host drains each client's messages through the normal per-frame
/// socket path. Main-thread only.
/// </summary>
internal sealed class InProcessSocket : NetworkSocket
{
	readonly List<InProcessConnection> _clients = new();

	/// <summary>
	/// Accept an in-process client. Must be called under the HOST's networking context -
	/// this starts the handshake, which sends ServerInfo referencing host state.
	/// </summary>
	internal void Accept( InProcessConnection hostSideEndpoint )
	{
		_clients.Add( hostSideEndpoint );
		OnClientConnect?.Invoke( hostSideEndpoint );
	}

	/// <summary>
	/// Remove an in-process client. Must be called under the HOST's networking context -
	/// this triggers OnLeave cleanup against the host's scene.
	/// </summary>
	internal void Disconnect( InProcessConnection hostSideEndpoint )
	{
		if ( !_clients.Remove( hostSideEndpoint ) )
			return;

		OnClientDisconnect?.Invoke( hostSideEndpoint );
		hostSideEndpoint.Close( 0, "Disconnected" );
	}

	internal override void GetIncomingMessages( NetworkSystem.MessageHandler handler )
	{
		// Reverse iteration: a handler can disconnect (kick) the client it belongs to.
		for ( var i = _clients.Count - 1; i >= 0; i-- )
		{
			_clients[i].GetIncomingMessages( handler );
		}
	}

	internal override void ProcessMessagesInThread()
	{
		// Nothing to do on the network thread.
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

namespace Sandbox.Network;

internal partial class NetworkSystem
{
	/// <summary>
	/// Send a message to all connections. You can optionally pass in a filter to determine who actually receives the message.
	/// </summary>
	/// <param name="msg">The message to send.</param>
	/// <param name="minimumState">The minumum state the connection must be to receive the message.</param>
	/// <param name="filter">If specified, the connection must pass this filter to receive the message.</param>
	/// <param name="flags">Network message flags that may dictate how the message is sent.</param>
	public void Broadcast( ByteStream msg, Connection.ChannelState minimumState = Connection.ChannelState.Snapshot, Connection.Filter? filter = null, NetFlags flags = NetFlags.Reliable )
	{
		// Reused, unless a send ends up broadcasting again
		var recipients = _broadcastRecipients.Count == 0 ? _broadcastRecipients : new List<Connection>();
		CollectRecipients( recipients, minimumState, filter );

		try
		{
			SendToRecipients( recipients, msg, flags );
		}
		finally
		{
			recipients.Clear();
		}
	}

	void SendToRecipients( List<Connection> recipients, ByteStream msg, NetFlags flags )
	{
		// Encode once so every recipient gets the same wire bytes without re-compressing per connection.
		var encoded = Connection.EncodePacket( msg );
		byte[][] packets = null;
		_relayed.Clear();

		foreach ( var c in recipients )
		{
			// Peers we reach through the host get one message between them, see below
			if ( c is MockConnection )
			{
				_relayed.Add( c );
				continue;
			}

			c.Send( encoded, flags, ref packets );
		}

		if ( _relayed.Count > 0 )
			SendMulticast( _relayed, Connection.Decode( encoded.Span ), flags );

		_relayed.Clear();
		encoded.Release();
	}

	readonly List<Connection> _relayed = new();

	readonly List<Connection> _broadcastRecipients = new();

	/// <summary>
	/// Everyone a message to all goes to: the host's connections, or on a client everyone in the connection table
	/// (directly or through the host) and the host.
	/// </summary>
	void CollectRecipients( List<Connection> into, Connection.ChannelState minimumState, Connection.Filter? filter )
	{
		into.Clear();

		if ( Networking.IsHost )
		{
			foreach ( var c in _connections )
				AddRecipient( into, c, minimumState, filter );
		}
		else
		{
			foreach ( var info in ConnectionInfo.All )
			{
				if ( Connection.Find( info.Key ) is { } c )
					AddRecipient( into, c, minimumState, filter );
			}
		}

		if ( Connection is not null && !into.Contains( Connection ) )
			AddRecipient( into, Connection, minimumState, filter );
	}

	static void AddRecipient( List<Connection> into, Connection c, Connection.ChannelState minimumState, Connection.Filter? filter )
	{
		if ( c == Connection.Local ) return;
		if ( c.State < minimumState ) return;
		if ( filter.HasValue && !filter.Value.IsRecipient( c ) ) return;

		into.Add( c );
	}

	/// <summary>
	/// Send one message, starting with its <see cref="InternalMessageType"/>, to several peers we reach through the
	/// host as one <see cref="InternalMessageType.Multicast"/>, rather than uploading a copy for each of them.
	/// </summary>
	internal void SendMulticast( List<Connection> targets, ReadOnlySpan<byte> message, NetFlags flags )
	{
		Span<Guid> ids = targets.Count <= 64 ? stackalloc Guid[targets.Count] : new Guid[targets.Count];

		for ( var i = 0; i < targets.Count; i++ )
			ids[i] = targets[i].Id;

		SendMulticast( ids, message, flags );
	}

	/// <inheritdoc cref="SendMulticast(List{Connection}, ReadOnlySpan{byte}, NetFlags)"/>
	internal void SendMulticast( ReadOnlySpan<Guid> ids, ReadOnlySpan<byte> message, NetFlags flags )
	{
		var host = Connection.Host;
		if ( host is null or MockConnection )
		{
			if ( Networking.Debug )
				Log.Warning( "No host to send a message for other peers through" );

			return;
		}

		var encoded = EncodeMulticast( Connection.Local.Id, flags, ids, message );
		host.Send( encoded, flags );
		encoded.Release();
	}

	/// <summary>
	/// Broadcast a packed message to all connections.
	/// </summary>
	internal void Broadcast<T>( T msg, Connection.ChannelState minimumState = Connection.ChannelState.Snapshot,
		Connection.Filter? filter = null, NetFlags flags = NetFlags.Reliable )
	{
		var bs = ByteStream.Create( 32 );

		bs.Write( InternalMessageType.Packed );
		TypeLibrary.ToBytes( msg, ref bs );

		Broadcast( bs, minimumState, filter, flags );

		bs.Dispose();
	}

	/// <summary>
	/// Get a list of connections that meet a specific criteria.
	/// </summary>
	/// <param name="minimumState">The minumum state the connection must be to receive the message.</param>
	/// <param name="filter">If specified, the connection must pass this filter to receive the message.</param>
	public IEnumerable<Connection> GetFilteredConnections( Connection.ChannelState minimumState = Connection.ChannelState.Snapshot, Connection.Filter? filter = null )
	{
		var connections = new List<Connection>();
		CollectRecipients( connections, minimumState, filter );
		return connections;
	}

	/// <inheritdoc cref="GetFilteredConnections"/>
	internal void CollectConnections( List<Connection> into, Connection.ChannelState minimumState ) => CollectRecipients( into, minimumState, null );
}

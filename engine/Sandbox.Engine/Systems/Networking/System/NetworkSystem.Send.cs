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
		IEnumerable<Connection> availableConnections = Networking.IsHost ? _connections : Connection.All;

		if ( Connection is not null )
		{
			availableConnections = availableConnections
				.Append( Connection )
				.Distinct();
		}

		// Encode once so every recipient gets the same wire bytes without re-compressing per connection.
		var encoded = Connection.Encode( msg );

		foreach ( var c in availableConnections )
		{
			if ( c == Connection.Local ) continue;
			if ( c.State < minimumState ) continue;
			if ( filter.HasValue && !filter.Value.IsRecipient( c ) ) continue;
			c.Send( encoded, flags );
		}
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

		Broadcast( bs, minimumState );

		bs.Dispose();
	}

	/// <summary>
	/// Get a list of connections that meet a specific criteria.
	/// </summary>
	/// <param name="minimumState">The minumum state the connection must be to receive the message.</param>
	/// <param name="filter">If specified, the connection must pass this filter to receive the message.</param>
	public IEnumerable<Connection> GetFilteredConnections( Connection.ChannelState minimumState = Connection.ChannelState.Snapshot, Connection.Filter? filter = null )
	{
		IEnumerable<Connection> availableConnections = Networking.IsHost ? _connections : Connection.All;

		if ( Connection is not null )
		{
			availableConnections = availableConnections
				.Append( Connection )
				.Distinct();
		}

		foreach ( var c in availableConnections )
		{
			if ( c == Connection.Local )
				continue;

			if ( c.State < minimumState )
				continue;

			if ( filter.HasValue && !filter.Value.IsRecipient( c ) )
				continue;

			yield return c;
		}
	}
}

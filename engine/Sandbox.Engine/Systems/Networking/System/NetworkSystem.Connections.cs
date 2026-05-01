using Sandbox.Engine;
using Sandbox.Internal;

namespace Sandbox.Network;

internal partial class NetworkSystem
{
	[SkipHotload]
	private readonly HashSet<Connection> _connections = [];

	[SkipHotload]
	private readonly Dictionary<Guid, Connection> _connectionLookup = new();

	public IEnumerable<Connection> Connections => _connections;

	/// <summary>
	/// If true then this host is sending assemblies and other files via network tables
	/// and as such, does not need to load assemblies from the package (if it even exists).
	/// </summary>
	public bool IsDeveloperHost { get; set; }

	/// <summary>
	/// Get whether there are any connections still doing the handshake process.
	/// </summary>
	/// <returns></returns>
	internal bool IsHandshaking()
	{
		return _connections.Count != 0 && _connections.Any( c => c.State != Connection.ChannelState.Connected );
	}

	/// <summary>
	/// Find a connection by its unique id.
	/// </summary>
	/// <param name="id"></param>
	/// <returns></returns>
	internal Connection FindConnection( Guid id )
	{
		if ( Connection is not null && Connection.Id == id )
			return Connection;

		if ( _connectionLookup.TryGetValue( id, out var cached ) )
			return cached;

		foreach ( var c in _connections )
		{
			if ( c.Id == id )
			{
				_connectionLookup[id] = c;
				return c;
			}
		}

		return null;
	}

	internal void OnConnected( Connection channel )
	{
		Log.Trace( $"{this}: On Connected {channel}" );

		_connections.Add( channel );

		channel.InitializeSystem( this );

		if ( IsHost )
		{
			// If we're the host and have a new connection then start handshaking
			channel.GenerateConnectionId();
			StartHandshake( channel );
		}

		if ( channel.Id != Guid.Empty )
		{
			_connectionLookup[channel.Id] = channel;
		}
	}

	/// <summary>
	/// Start the handshaking process with the specified client <see cref="Connection"/>.
	/// </summary>
	/// <param name="channel"></param>
	void StartHandshake( Connection channel )
	{
		channel.HandshakeId = Guid.NewGuid();

		var hello = new ServerInfo
		{
			ServerName = Networking.ServerName,
			ServerData = Networking.ServerData,
			MaxPlayers = Networking.MaxPlayers,
			MapName = Networking.MapName,
			EngineVersion = 234,
			GamePackage = Application.GamePackage?.GetIdent( false, true ) ?? "",
			MapPackage = Application.MapPackage?.GetIdent( false, true ) ?? "",
			Host = new ChannelInfo { Id = Connection.Local.Id },
			Assigned = new ChannelInfo { Id = channel.Id },
			IsDeveloperHost = IsDeveloperHost,
			HandshakeId = channel.HandshakeId
		};

		channel.SendMessage( hello );
		channel.State = Connection.ChannelState.LoadingServerInformation;
	}

	/// <summary>
	/// Restart the handshaking process. This could be used if the host changed
	/// while we're connecting.
	/// </summary>
	internal void RestartHandshake()
	{
		var host = _connections.FirstOrDefault( c => c.IsHost );
		if ( host is null )
			return;

		Connection.Local.State = Connection.ChannelState.Unconnected;
		host.SendMessage( new RestartHandshakeMsg() );
	}

	/// <summary>
	/// Called from the host to add a connection to the ConnectionInfo table
	/// </summary>
	internal void AddConnection( Connection source, UserInfo data )
	{
		var info = ConnectionInfo.Add( source );
		info.Update( data );
		OnConnectionInfoUpdated();
	}

	internal void OnDisconnected( Connection source )
	{
		if ( source.State >= Connection.ChannelState.Welcome )
		{
			// We only need to call this if we called OnConnected, which would only
			// be the case if they got to at least the Welcome state during the handshake.
			GameSystem?.OnLeave( source );
		}

		_connectionLookup.Remove( source.Id );
		_connections.Remove( source );

		source.State = Connection.ChannelState.Unconnected;

		if ( !IsHost )
			return;

		Log.Info( $"{source.DisplayName} [{source.SteamId}] disconnected" );

		ConnectionInfo.Remove( source.Id );
		OnConnectionInfoUpdated();
	}

	/// <summary>
	/// We were disconnected from the server. This will almost always be when we're on a dedicated server
	/// and it has closed down.
	/// </summary>
	internal void OnServerDisconnection( int reasonCode, string reasonString )
	{
		if ( Connection.Local.State == Connection.ChannelState.Unconnected )
			return;

		IGameInstanceDll.Current.Disconnect( $"You have been disconnected from the server.\nReason: {reasonString}" );
	}

	/// <summary>
	/// Called any time we suspect the connection info might have changed, which
	/// lets us tell the sockets/connections to update any information they hold.
	/// </summary>
	internal void OnConnectionInfoUpdated()
	{
		foreach ( var socket in sockets )
		{
			socket.OnConnectionInfoUpdated( this );
		}

		_connectionLookup.Clear();
	}
}

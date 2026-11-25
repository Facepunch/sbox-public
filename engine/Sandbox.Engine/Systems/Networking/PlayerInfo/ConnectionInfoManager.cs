namespace Sandbox.Network;

internal class ConnectionInfoManager
{
	public readonly Dictionary<Guid, ConnectionInfo> All = new();
	public StringTable StringTable { get; } = new( "ConnectionInfo", true );
	public NetworkSystem NetworkSystem { get; }

	public ConnectionInfoManager( NetworkSystem networkSystem )
	{
		NetworkSystem = networkSystem;

		StringTable.OnChangeOrAdd = OnTableEntryUpdated;
		StringTable.OnRemoved = OnTableEntryRemoved;
		StringTable.OnSnapshot = OnTableSnapshot;
		StringTable.PostNetworkUpdate = NetworkSystem.OnConnectionInfoUpdated;
	}

	public ConnectionInfo Add( Connection connection )
	{
		var c = new ConnectionInfo( this )
		{
			ConnectionId = connection.Id,
			State = connection.State
		};

		All[connection.Id] = c;
		return c;
	}

	void OnTableEntryUpdated( StringTable.Entry entry )
	{
		if ( !TryParseEntry( entry, out var id, out var key, out var isUserInfo ) )
			return;

		if ( id == Guid.Empty || string.IsNullOrEmpty( key ) )
			return;

		if ( !All.TryGetValue( id, out var c ) )
		{
			c = All[id] = new( this ) { ConnectionId = id };
		}

		c.FromStringTable( key, entry.Data, isUserInfo );
	}

	void OnTableEntryRemoved( StringTable.Entry entry )
	{
		if ( !TryParseEntry( entry, out var id, out _, out _ ) )
			return;

		if ( id == Guid.Empty )
			return;

		All.Remove( id );
	}

	bool TryParseEntry( StringTable.Entry entry, out Guid id, out string key, out bool isUserInfo )
	{
		id = Guid.Empty;
		key = string.Empty;
		isUserInfo = false;
		string[] split;

		// This is a UserInfo ConVar...
		if ( entry.Name.Contains( "#" ) )
		{
			isUserInfo = true;
			split = entry.Name.Split( '#' );
			id = Guid.Parse( split[0] );
			key = string.Join( '#', split[1..] );

			return true;
		}

		if ( !entry.Name.Contains( ":" ) )
			return false;

		// This is regular data...
		split = entry.Name.Split( ':' );
		id = Guid.Parse( split[0] );
		key = string.Join( ':', split[1..] );

		return true;
	}

	void OnTableSnapshot()
	{
		All.Clear();

		foreach ( var e in StringTable.Entries.Values )
		{
			OnTableEntryUpdated( e );
		}

		NetworkSystem.OnConnectionInfoUpdated();
	}

	/// <summary>
	/// Get info for this connection
	/// </summary>
	internal ConnectionInfo Get( Guid id )
	{
		return All.GetValueOrDefault( id );
	}

	internal void Remove( Guid id )
	{
		if ( All.Remove( id, out var c ) )
		{
			c.Dispose();
		}

		var entries = StringTable.Entries.Keys
			.Where( c => c.StartsWith( $"{id}:" ) || c.StartsWith( $"{id}#" ) )
			.ToArray();

		foreach ( var key in entries )
		{
			StringTable.Remove( key );
		}
	}
}

/// <summary>
/// Information about a connection. The difference between this and the actual connections is that
/// this can be networked between clients, so all clients have the same information about each other.
/// This is going to be required in p2p games, where players need to take over hosting from each other.
/// In a game like Rust, with a dedicated server, this won't need to be networked to other clients.
/// </summary>
internal sealed class ConnectionInfo
{
	private ConnectionInfoManager manager;
	private NetworkSystem NetworkSystem => manager.NetworkSystem;

	public int Ping { get; private set; }
	public Guid ConnectionId { get; internal set; }
	public string Name { get; internal set; }
	public SteamId SteamId { get; internal set; }
	public DateTimeOffset ConnectionTime { get; internal set; }
	public bool CanRefreshObjects { get; internal set; } = true;
	public bool CanSpawnObjects { get; internal set; } = true;
	public bool CanDestroyObjects { get; internal set; } = true;
	public SteamId PartyId { get; internal set; }
	internal Connection.ChannelState State { get; set; }

	internal ConnectionInfo( ConnectionInfoManager connectionInfoManager )
	{
		manager = connectionInfoManager;
		ConnectionTime = DateTime.UtcNow;
		CanSpawnObjects = Sandbox.ProjectSettings.Networking.ClientsCanSpawnObjects;
		CanRefreshObjects = Sandbox.ProjectSettings.Networking.ClientsCanRefreshObjects;
		CanDestroyObjects = Sandbox.ProjectSettings.Networking.ClientsCanDestroyObjects;
	}

	/// <summary>
	/// Key values that come straight from the user. Can't be trusted, they could send anything. Used
	/// for things like preferences, avatar clothing etc.
	/// </summary>
	public Dictionary<string, string> UserData { get; } = new();

	internal void Update( UserInfo userInfo )
	{
		Name = userInfo.Name?.Length > MaxNameLength ? userInfo.Name[..MaxNameLength] : userInfo.Name;
		SteamId = userInfo.SteamId;
		PartyId = userInfo.PartyId;

		UserData.Clear();

		foreach ( var p in userInfo.UserData )
		{
			if ( !IsValidUserData( p.Key, p.Value ) )
				continue;

			UserData.Add( p.Key, p.Value );
		}

		UpdateStringTable();
	}

	internal void UpdatePing( int ping )
	{
		// This is the case for local mock info
		if ( manager is null )
			return;

		// Only the host updates the string table
		if ( !NetworkSystem.IsHost )
			return;

		UpdateStringTable( "ping", ping );
		Ping = ping;
	}

	internal void UpdateStringTable()
	{
		// This is the case for local mock info
		if ( manager is null )
			return;

		// Only the host updates the string table
		if ( !NetworkSystem.IsHost )
			return;

		UpdateStringTable( "ping", Ping );
		UpdateStringTable( "name", Name );
		UpdateStringTable( "state", State );
		UpdateStringTable( "steamid", SteamId );
		UpdateStringTable( "connect", ConnectionTime );
		UpdateStringTable( "canSpawnObjects", CanSpawnObjects );
		UpdateStringTable( "canRefreshObjects", CanRefreshObjects );
		UpdateStringTable( "canDestroyObjects", CanDestroyObjects );
		UpdateStringTable( "party", PartyRoom.Current?.Id ?? new( 0 ) );

		foreach ( var (k, v) in UserData )
		{
			UpdateStringTable( k, v, true );
		}
	}

	void UpdateStringTable<T>( string key, T value, bool isUserInfo = false )
	{
		var stringTableKey = isUserInfo ? $"{ConnectionId}#{key}" : $"{ConnectionId}:{key}";
		var data = NetworkSystem.TypeLibrary.ToBytes( value );

		if ( manager.StringTable.Entries.TryGetValue( stringTableKey, out var entry ) )
		{
			// The value is the same, no need to update it.
			if ( data.SequenceEqual( entry.Data ) )
				return;
		}

		manager.StringTable.Set( stringTableKey, data );
	}

	internal void FromStringTable( string key, byte[] data, bool isUserInfo )
	{
		var value = NetworkSystem.TypeLibrary.FromBytes<object>( data );

		if ( isUserInfo )
		{
			UserData[key] = (string)value;
			return;
		}

		switch ( key )
		{
			case "name":
				Name = (string)value;
				break;
			case "ping":
				Ping = (int)value;
				break;
			case "steamid":
				SteamId = (SteamId)value;
				break;
			case "connect":
				ConnectionTime = (DateTimeOffset)value;
				break;
			case "canSpawnObjects":
				CanSpawnObjects = (bool)value;
				break;
			case "canRefreshObjects":
				CanRefreshObjects = (bool)value;
				break;
			case "canDestroyObjects":
				CanDestroyObjects = (bool)value;
				break;
			case "party":
				PartyId = (SteamId)value;
				break;
			case "state":
				State = (Connection.ChannelState)value;
				break;
		}
	}

	internal void Dispose()
	{
		manager = null;
	}

	internal void UpdateUserData( Dictionary<string, string> userData )
	{
		UserData.Clear();

		foreach ( var (k, v) in userData )
		{
			if ( !IsValidUserData( k, v ) )
				continue;

			UserData.Add( k, v );
			UpdateStringTable( k, v, true );
		}
	}

	// The host replicates user data to every peer, so only take small values for UserInfo convars
	internal static bool IsValidUserData( string key, string value )
	{
		if ( key is null || value?.Length > MaxUserDataLength )
			return false;

		var command = ConVarSystem.Find( key );
		return command is not null && command.IsUserInfo;
	}

	internal const int MaxUserDataLength = 16 * 1024;
	internal const int MaxNameLength = 256;

	internal void SetUserData( string key, string value )
	{
		UpdateStringTable( key, value, true );
	}

	internal string GetUserData( string key )
	{
		return UserData.GetValueOrDefault( key );
	}

	/// <summary>
	/// When a user isn't connected to a server, or hosting a server, the
	/// client info table doesn't exist. So we provide info here for the
	/// local connection.
	/// </summary>
	static ConnectionInfo _localMock;
	static int _localMockConVars;

	internal static ConnectionInfo GetLocalMock()
	{
		if ( _localMock is null )
		{
			_localMock = new( null );
			_localMockConVars = ConVarSystem.Version - 1;
		}

		_localMock.UpdateLocal( ref _localMockConVars );
		return _localMock;
	}

	/// <summary>
	/// The local mock's view of <see cref="UserInfo.Local"/>, without building one: it's read whenever something asks the
	/// local connection for its name or id (UI does every frame), so the user data is read from the ConVars again only
	/// when they've changed since <paramref name="conVarsVersion"/>.
	/// </summary>
	void UpdateLocal( ref int conVarsVersion )
	{
		var name = Utility.Steam.PersonaName;
		Name = name?.Length > MaxNameLength ? name[..MaxNameLength] : name;
		SteamId = Utility.Steam.SteamId;
		PartyId = PartyRoom.Current?.Id ?? new( 0 );

		var version = ConVarSystem.Version;
		if ( version == conVarsVersion )
			return;

		conVarsVersion = version;
		UserData.Clear();
		foreach ( var convar in ConVarSystem.Members.Values )
		{
			if ( !convar.IsUserInfo )
				continue;

			var value = convar.Value;
			if ( IsValidUserData( convar.Name, value ) )
				UserData[convar.Name] = value;
		}
	}
}

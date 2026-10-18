using Sandbox.Network;

namespace Sandbox;

public abstract partial class Connection
{
	/// <summary>
	/// This is a "fake" connection for the local player. It is passed to RPCs when calling them
	/// locally etc.
	/// </summary>
	[ActionGraphInclude]
	public static Connection Local { get; internal set; } = new LocalConnection( Guid.NewGuid() );

	/// <summary>
	/// A list of connections that are currently on this server. If you're not on a server
	/// this will return only one connection (Connection.Local). Some games restrict the 
	/// connection list - in which case you will get an empty list.
	/// </summary>
	[ActionGraphInclude]
	public static IReadOnlyList<Connection> All
	{
		get
		{
			var l = new List<Connection>( 32 );

			if ( Networking.System is not null )
			{
				foreach ( var c in Networking.System.ConnectionInfo.All )
				{
					var connection = Find( c.Key );
					if ( connection is not null )
						l.Add( connection );
				}
			}
			else
			{
				if ( Local is not null )
					l.Add( Local );
			}

			return l.AsReadOnly<Connection>();
		}
	}

	/// <summary>
	/// The connection of the current network host.
	/// </summary>
	[ActionGraphInclude]
	public static Connection Host
	{
		get
		{
			if ( Networking.System is null )
				return Local;

			if ( Networking.System.IsHost )
				return Local;

			return Networking.System.HostConnection;
		}
	}

	/// <summary>
	/// Find a <see cref="Connection"/> for a Connection Id.
	/// </summary>
	[ActionGraphInclude]
	public static Connection Find( Guid id )
	{
		if ( id == Guid.Empty )
			return default;

		// Is this us?
		if ( Local.Id == id )
			return Local;

		if ( Networking.System is null )
			return default;

		// Is this someone we're directly connected to?
		var c = Networking.System.FindConnection( id );
		if ( c is not null ) return c;

		// Is this someone we have connection information about?
		var info = FindConnectionInfo( id );
		if ( info is null ) return default;

		return FindOrCreateMockConnection( info );
	}

	internal static ConnectionInfo FindConnectionInfo( Guid id )
	{
		if ( Networking.System is null )
			return Local.Id == id ? ConnectionInfo.GetLocalMock() : null;

		return Networking.System.ConnectionInfo.Get( id );
	}

	/// <summary>
	/// Reset any static members to their defaults or clear them.
	/// </summary>
	internal static void Reset()
	{
		Networking.System?.MockConnections.Clear();
	}

	private static Connection FindOrCreateMockConnection( ConnectionInfo info )
	{
		// Mocks route through their system's host, so each system keeps its own
		var mocks = Networking.System.MockConnections;
		if ( mocks.TryGetValue( info.ConnectionId, out var connection ) )
			return connection;

		connection = new MockConnection( info.ConnectionId );
		connection.InitializeSystem( Networking.System );

		mocks[info.ConnectionId] = connection;
		return connection;
	}
}

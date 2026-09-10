using Sandbox.Network;

namespace Editor.Mcp;

[McpToolset( "network", "Multiplayer from the editor - host a lobby, spawn local client instances, watch connections and trigger a host handoff" )]
public static class NetworkTools
{
	/// <summary>
	/// Who we are on the network: whether we're connected, hosting or a client, and every connection
	/// we know about with its state. Use this to confirm a local instance joined or a handoff finished.
	/// </summary>
	[McpTool.ReadOnly( "network_status" )]
	public static NetworkState NetworkStatus()
	{
		return new NetworkState
		{
			IsActive = Networking.IsActive,
			IsHost = Networking.IsActive && Networking.IsHost,
			IsClient = Networking.IsClient,
			IsConnecting = Networking.IsConnecting,
			LocalId = Connection.Local?.Id ?? default,
			HostId = Connection.Host?.Id ?? default,
			Connections = Connection.All.Select( c => new ConnectionState
			{
				Id = c.Id,
				Name = c.Name,
				IsHost = c.IsHost,
				IsLocal = c == Connection.Local,
				IsActive = c.IsActive,
				IsConnecting = c.IsConnecting,
				Ping = c.Ping
			} ).ToArray()
		};
	}

	/// <summary>
	/// Host a lobby from the editor. Enters play mode first if needed. Local instances spawned with
	/// network_spawn_instance join this lobby.
	/// </summary>
	[McpTool( "network_start_hosting" )]
	public static NetworkState StartHosting()
	{
		if ( EditorUtility.Network.Active )
			throw new Exception( "Already connected - network_disconnect first" );

		EditorUtility.Network.StartHosting();
		return NetworkStatus();
	}

	/// <summary>
	/// Leave the current game. If we're the host and other players are connected, the game is handed
	/// to one of them first, which is how to exercise host migration from the editor. Check the
	/// spawned instance's log for "Becoming the host".
	/// </summary>
	[McpTool( "network_disconnect" )]
	public static NetworkState Disconnect()
	{
		if ( !EditorUtility.Network.Active )
			throw new Exception( "Not connected" );

		EditorUtility.Network.Disconnect();
		return NetworkStatus();
	}

	/// <summary>
	/// Hand the game to a fresh local instance in one step: spawns it, waits until it has joined, then
	/// disconnects so it becomes the host. Blocks for up to two minutes while the instance boots. Read the
	/// instance's log afterwards to see what your game did when the host changed.
	/// </summary>
	[McpTool( "network_migrate_to_new_instance" )]
	public static async Task<NetworkState> MigrateToNewInstance()
	{
		if ( !EditorUtility.Network.Hosting )
			throw new Exception( "Not hosting - network_start_hosting first" );

		if ( !await LocalInstances.MigrateHostAsync( windowed: true ) )
			throw new Exception( "The instance didn't join in time" );

		return NetworkStatus();
	}

	/// <summary>
	/// Launch another sbox.exe on this machine that joins the editor's game as a client. Takes a
	/// while to boot; poll network_status until it appears in the connections. Returns the process
	/// id and the folder the instance logs into, so you can read what happened on its side.
	/// </summary>
	/// <param name="windowed">Run the instance in a small window instead of fullscreen.</param>
	[McpTool( "network_spawn_instance" )]
	public static SpawnedInstance SpawnInstance( bool windowed = true )
	{
		if ( !EditorUtility.Network.Hosting )
			throw new Exception( "Not hosting - network_start_hosting first" );

		return new SpawnedInstance
		{
			ProcessId = LocalInstances.Spawn( windowed ),
			LogsDirectory = System.IO.Path.Combine( Environment.CurrentDirectory, "logs" )
		};
	}

	public class NetworkState
	{
		public bool IsActive { get; set; }
		public bool IsHost { get; set; }
		public bool IsClient { get; set; }
		public bool IsConnecting { get; set; }

		public Guid LocalId { get; set; }
		public Guid HostId { get; set; }
		public ConnectionState[] Connections { get; set; }
	}

	public class ConnectionState
	{
		public Guid Id { get; set; }
		public string Name { get; set; }
		public bool IsHost { get; set; }
		public bool IsLocal { get; set; }
		public bool IsActive { get; set; }
		public bool IsConnecting { get; set; }
		public float Ping { get; set; }
	}

	public class SpawnedInstance
	{
		public int ProcessId { get; set; }

		/// <summary>The folder game instances log into. The newest sbox log that isn't the editor's is this instance.</summary>
		public string LogsDirectory { get; set; }
	}
}

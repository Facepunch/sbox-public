namespace Sandbox.Network;

public abstract class NetworkSocket
{
	internal Action<Connection> OnClientConnect;
	internal Action<Connection> OnClientDisconnect;

	/// <summary>
	/// Whether this socket should be disposed automatically when the network system
	/// it belongs to is disconnected.
	/// </summary>
	internal bool AutoDispose { get; set; } = true;

	/// <summary>
	/// Whether peers on this socket can take over as host when the host leaves.
	/// </summary>
	internal virtual bool SupportsHostMigration => false;

	/// <summary>
	/// Move whatever represents the host on this transport, e.g. the Steam lobby owner.
	/// </summary>
	internal virtual void OnHostChanged( Connection newHost )
	{

	}

	internal abstract void Dispose();
	internal abstract void GetIncomingMessages( NetworkSystem.MessageHandler handler );

	/// <summary>
	/// This is called on a worker thread and should handle any threaded processing of messages.
	/// </summary>
	internal abstract void ProcessMessagesInThread();

	/// <summary>
	/// Called when everything has just been hooked up to the network system.
	/// </summary>
	internal virtual void Initialize( NetworkSystem networkSystem )
	{
		// If the socket already has connections, we should call OnClientConnect here to 
		// add them to the network system
	}

	/// <summary>
	/// ConnectionInfo table has been updated
	/// </summary>
	internal virtual void OnConnectionInfoUpdated( NetworkSystem networkSystem )
	{

	}

	/// <summary>
	/// Called when a session has failed with a user. Steam Networking Messages will invoke this callback
	/// if an attempt to send a message to a user failed because of a broken session.
	/// </summary>
	internal virtual void OnSessionFailed( SteamId steamId )
	{

	}

	/// <summary>
	/// Set data about this socket. For example, this might be used to change whether a lobby
	/// should be visible for players depending on the game state.
	/// </summary>
	internal virtual void SetData( string key, string value )
	{

	}

	/// <summary>
	/// Set the name of the server. This will be displayed to other players when they
	/// query servers.
	/// </summary>
	internal virtual void SetServerName( string name )
	{

	}

	/// <summary>
	/// Set the current map name. This will be displayed to other players when they
	/// query servers.
	/// </summary>
	internal virtual void SetMapName( string name )
	{

	}

	/// <summary>
	/// Called once a second
	/// </summary>
	internal virtual void Tick( NetworkSystem networkSystem )
	{

	}
}


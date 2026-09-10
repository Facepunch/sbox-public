namespace Sandbox.Network;

internal partial class NetworkSystem
{
	internal Connection Connection { get; private set; }

	public bool IsClient => !IsHost;

	/// <summary>
	/// True if we're currently connecting to the server. We're not yet spawned etc.
	/// </summary>
	public bool IsConnecting => Connection?.IsConnecting ?? false;

	internal void Connect( Connection connection )
	{
		Connection = connection;
		Connection.InitializeSystem( this );
		SetHostConnection( connection );
	}
}

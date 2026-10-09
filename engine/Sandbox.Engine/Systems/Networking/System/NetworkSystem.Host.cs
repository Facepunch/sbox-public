namespace Sandbox.Network;

internal partial class NetworkSystem
{
	public void InitializeHost()
	{
		IsHost = true;
		HostConnection = null;
		InstallStringTables();

		// Conna: if we're the host then set our state as Connected.
		Connection.Local.State = Connection.ChannelState.Connected;

		if ( !Application.IsDedicatedServer )
		{
			// Add connection info for the local connection
			var localConnectionInfo = ConnectionInfo.Add( Connection.Local );
			localConnectionInfo.Update( UserInfo.Local );
		}

		InitializeGameSystem();
	}
}



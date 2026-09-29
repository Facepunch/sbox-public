namespace Sandbox.Network;

internal sealed class InProcessClientEnvironment : INetworkClientEnvironment
{
	readonly NetworkSystem _host;

	public InProcessClientEnvironment( NetworkSystem host )
	{
		_host = host;
	}

	public bool ReadsReplicatedConVars => false;

	public Task<bool> LoadGamePackageAsync( NetworkSystem system, ServerInfo msg ) => Task.FromResult( true );

	public Task<bool> MountMapAsync( NetworkSystem system, ServerInfo msg ) => Task.FromResult( true );

	public void InstallNetworkTables( NetworkSystem system )
	{
		foreach ( var table in _host.Tables )
		{
			if ( system.Tables.Any( x => x.Name == table.Name ) )
				continue;

			system.InstallTable( new StringTable( table.Name, table.Compressed ) );
		}
	}

	public Task<bool> LoadNetworkTablesAsync( NetworkSystem system ) => Task.FromResult( true );

	public Task InitializeGameSystemAsync( NetworkSystem system )
	{
		system.GameSystem = new SceneNetworkSystem( system.TypeLibrary, system );
		system.GameSystem.OnInitialize();
		return Task.CompletedTask;
	}

	public void SetLoadingTitle( string title )
	{
	}

	public void OnActivated( NetworkSystem system )
	{
	}

	public void Disconnect( NetworkSystem system, string reason )
	{
		if ( !system.IsDisconnected )
			system.Disconnect();
	}
}

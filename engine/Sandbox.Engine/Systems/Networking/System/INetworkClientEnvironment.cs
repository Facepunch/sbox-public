namespace Sandbox.Network;

internal interface INetworkClientEnvironment
{
	bool ReadsReplicatedConVars { get; }

	Task<bool> LoadGamePackageAsync( NetworkSystem system, ServerInfo msg );
	Task<bool> MountMapAsync( NetworkSystem system, ServerInfo msg );
	void InstallNetworkTables( NetworkSystem system );
	Task<bool> LoadNetworkTablesAsync( NetworkSystem system );
	Task InitializeGameSystemAsync( NetworkSystem system );
	void SetLoadingTitle( string title );
	void OnActivated( NetworkSystem system );
	void Disconnect( NetworkSystem system, string reason );
}

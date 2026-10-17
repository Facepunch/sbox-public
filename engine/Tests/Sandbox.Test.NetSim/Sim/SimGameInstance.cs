using Sandbox.Engine;
using Sandbox.Internal;
using Sandbox.Network;
using System.Threading;

namespace NetSim;

/// <summary>
/// Just enough of a game instance for the real join handshake: no package to load, no network tables
/// to wait on, and the game network system is the one the current peer already made.
/// </summary>
internal sealed class SimGameInstance : IGameInstanceDll
{
	readonly TypeLibrary _typeLibrary;

	public List<string> Disconnects { get; } = new();

	public SimGameInstance( TypeLibrary typeLibrary )
	{
		_typeLibrary = typeLibrary;
	}

	public TypeLibrary TypeLibrary => _typeLibrary;

	public Task<bool> LoadGamePackageAsync( string ident, GameLoadingFlags flags, CancellationToken ct ) => Task.FromResult( true );
	public Task<bool> LoadNetworkTables( NetworkSystem system ) => Task.FromResult( true );
	public GameNetworkSystem CreateGameNetworking( NetworkSystem system ) => SimPeer.Current?.SceneSystem;
	public Task<GameNetworkSystem> CreateGameNetworkingAsync( NetworkSystem system ) => Task.FromResult<GameNetworkSystem>( SimPeer.Current?.SceneSystem );
	public void InstallNetworkTables( NetworkSystem system ) { }
	public ScenePushScope PushScope() => Game.ActiveScene is { } scene ? scene.PushScope() : default;

	public void Disconnect( string message = null ) => Disconnects.Add( $"{SimPeer.Current}: {message}" );

	public void Bootstrap() { }
	public Task Initialize() => Task.CompletedTask;
	public void Tick() { }
	public void Exiting() { }
	public void OnRender( SwapChainHandle_t swapChain ) { }
	public void FinishLoadingAssemblies() { }
	public void OnProjectConfigChanged( Package package ) { }
	public void ClosePopups( object panelClickedOn ) { }
	public void SimulateUI() { }
	public bool EditorPlay() => false;
	public void CloseGame() { }
	public void ResetSceneListenerMetrics() { }
	public object GetSceneListenerMetrics() => null;
	public bool TryGetReplicatedVarValue( string name, out string value ) { value = null; return false; }
	public Task LoadPackageAssembliesAsync( Package package ) => Task.CompletedTask;
}

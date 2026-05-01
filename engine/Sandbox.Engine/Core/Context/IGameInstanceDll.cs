using Sandbox.Internal;
using Sandbox.Network;
using System.Threading;

namespace Sandbox.Engine;

internal unsafe interface IGameInstanceDll
{
	public static IGameInstanceDll Current { get; set; }

	public void Bootstrap();
	public Task Initialize();
	public void Tick();
	public void Exiting();

	public InputContext InputContext => default;

	public void OnRender( SwapChainHandle_t swapChain );
	public void FinishLoadingAssemblies();
	public TypeLibrary TypeLibrary { get; }
	public void OnProjectConfigChanged( Package package );

	//
	// UI
	//
	public void ClosePopups( object panelClickedOn );
	public void SimulateUI();


	//
	// Game Menu Shit
	//
	public Task<bool> LoadGamePackageAsync( string ident, GameLoadingFlags flags, CancellationToken ct );

	//
	// Scene
	//
	public IDisposable PushScope();
	public void EditorPlay(); // play game button pressed in editor

	//
	// Network
	//

	GameNetworkSystem CreateGameNetworking( NetworkSystem system );
	public void InstallNetworkTables( NetworkSystem system );
	public Task LoadNetworkTables( NetworkSystem system );

	/// <summary>
	/// Called when the "disconnect" command is ran.
	/// </summary>
	public void Disconnect( string message = null );

	/// <summary>
	/// Closes the current GameInstance immediately
	/// </summary>
	public void CloseGame();

	void ResetSceneListenerMetrics();
	object GetSceneListenerMetrics();

	/// <summary>
	/// Get the replicated var value from the host
	/// </summary>
	public bool TryGetReplicatedVarValue( string name, out string value );

	/// <summary>
	/// Load the assemblies from this package into the current game instance
	/// </summary>
	public Task LoadPackageAssembliesAsync( Package package );
}

[Flags]
public enum GameLoadingFlags
{
	/// <summary>
	/// Set if we're loading a game as a result of joining a server
	/// </summary>
	Remote = 1,

	/// <summary>
	/// Set if we're the hosting as the result of starting our own server
	/// </summary>
	Host = 2,

	/// <summary>
	/// Set if we want to reload the game, even if it's already loaded
	/// </summary>
	Reload = 4,

	/// <summary>
	/// Set if this is a developer session. It started from an editor session and as such we shouldn't load
	/// assemblies from the package, they should be loaded from the Network Tables instead.
	/// </summary>
	Developer = 8
}

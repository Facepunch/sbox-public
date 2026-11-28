namespace Sandbox.Engine;

internal interface IMenuDll
{
	public static IMenuDll Current { get; set; }

	public void Bootstrap();
	public Task Initialize();
	public void Tick();

	/// <summary>
	/// Handles a Steam launch request received while the menu is running.
	/// </summary>
	void OnNewSteamUrlLaunchParameters();

	/// <summary>
	/// Updates the menu when a friend changes persona or rich presence.
	/// </summary>
	void OnSteamFriendChanged( Friend friend );

	/// <summary>
	/// Joins a game from a Steam rich presence invite.
	/// </summary>
	void OnSteamGameJoinRequested( string connect );

	/// <summary>
	/// Joins a lobby from a Steam invite.
	/// </summary>
	void OnSteamLobbyJoinRequested( SteamId lobbyId );

	public void RunEvent( string name );
	public void RunEvent( string name, object argument );
	public void RunEvent( string name, object arg0, object arg1 );
	public void RunEvent<T>( Action<T> action );
	public void Exiting();

	public InputContext InputContext => default;

	public void SimulateUI();
	public void ClosePopups( object p );
	bool HasOverlayMouseInput();
	void OnRender( SwapChainHandle_t swapChain );
	void LateTick();
	void Reset();

	IDisposable PushScope();
}

using Sandbox.Engine;

namespace Sandbox;

public static partial class MenuUtility
{
	static CancellationTokenSource gameLoadingCts = new CancellationTokenSource();

	/// <summary>
	/// Close the current game.
	/// </summary>
	public static void CloseGame()
	{
		// Editor only: just exit playmode
		if ( IToolsDll.Current is not null )
		{
			IToolsDll.Current.ExitPlaymode();
			return;
		}

		gameLoadingCts?.Cancel();

		if ( IGameInstance.Current is not null )
		{
			IGameInstance.Current?.Close();
		}
		else
		{
			// Conna: game instance will call disconnect. If we don't have a game instance then we
			// need to call it ourselves.
			Networking.Disconnect();

			Application.ClearGame();
		}

		LaunchArguments.Reset();
	}

	/// <summary>
	/// A game has been opened. Load the game. If allowLaunchOverride then special launch conditions will be obeyed.
	/// For example, we might join a lobby instead of loading the game, or we might open the launcher.
	/// </summary>
	public static void OpenGame( string ident, bool allowLaunchOverride = true, Dictionary<string, string> gameSettings = null )
	{
		CloseAllModals();

		gameLoadingCts?.Cancel();
		gameLoadingCts = new CancellationTokenSource();

		if ( gameSettings is not null ) LaunchArguments.GameSettings = gameSettings;
		_ = LoadAsync( ident, allowLaunchOverride, gameLoadingCts.Token );
	}

	/// <summary>
	/// A game has been opened. Load the game.
	/// </summary>
	public static void OpenGameWithMap( string gameident, string mapName, Dictionary<string, string> gameSettings = null )
	{
		LaunchArguments.Map = mapName;
		if ( gameSettings is not null ) LaunchArguments.GameSettings = gameSettings;

		OpenGame( gameident, false );
	}

	static async Task LoadAsync( string ident, bool allowLaunchOverride, CancellationToken ct )
	{
		ThreadSafe.AssertIsMainThread();
		LoadingScreen.IsVisible = true;
		LoadingScreen.Media = null;
		LoadingScreen.Title = "Loading Game..";

		var package = await Package.FetchAsync( ident, false );
		if ( package is not null )
		{
			LoadingScreen.Title = package.Title;
			LoadingScreen.Media = package.LoadingScreen.MediaUrl;
		}

		var flags = GameLoadingFlags.Host | GameLoadingFlags.Reload;
		if ( Application.IsEditor ) flags |= GameLoadingFlags.Developer; // todo - is the package we're loading a local package

		await IGameInstanceDll.Current.LoadGamePackageAsync( ident, flags, ct );
	}

	static bool _isJoiningLobby;

	/// <summary>
	/// Try to join any lobby for this game.
	/// </summary>
	public static async Task<bool> TryJoinLobby( string ident )
	{
		if ( _isJoiningLobby )
			return false;

		try
		{
			_isJoiningLobby = true;

			var lobbies = await Networking.QueryLobbies( ident );

			var orderedLobbies = lobbies.OrderBy( lobby => lobby.ContainsFriends )
				.ThenByDescending( lobby => lobby.Members );

			foreach ( var lobby in orderedLobbies )
			{
				if ( lobby.IsFull ) continue;

				// We might be in a game now
				if ( Game.InGame ) return false;

				Log.Info( $"Attempting to join available lobby {lobby.LobbyId}" );

				// Try to join this one
				if ( await Networking.TryConnectSteamId( lobby.LobbyId ) )
				{
					CloseAllModals();
					return true;
				}
			}

			Log.Info( $"Couldn't join a lobby - making a game" );
			return false;
		}
		finally
		{
			_isJoiningLobby = false;
		}
	}
}

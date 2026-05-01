using Sandbox.Engine;

namespace Sandbox;

public static partial class MenuUtility
{
	/// <summary>
	/// A game has been opened. Load the game. If allowLaunchOverride then special launch conditions will be obeyed.
	/// For example, we might join a lobby instead of loading the game, or we might open the launcher.
	/// </summary>
	public static void OpenGame( string ident, bool allowLaunchOverride = true, Dictionary<string, string> gameSettings = null )
	{
		CloseAllModals();

		if ( gameSettings is not null ) LaunchArguments.GameSettings = gameSettings;
		_ = LoadAsync( ident, allowLaunchOverride );
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

	static async Task LoadAsync( string ident, bool allowLaunchOverride, CancellationToken ct = default )
	{
		ThreadSafe.AssertIsMainThread();
		LoadingScreen.IsVisible = true;
		LoadingScreen.Media = null;
		LoadingScreen.Title = null;

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

		using var scope = Networking.MatchmakingScope();

		try
		{
			_isJoiningLobby = true;

			Log.Info( "Searching for games.." );
			var lobbies = await Networking.QueryLobbies( ident );
			Log.Info( $"..found {lobbies.Count} available matches" );

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

			return false;
		}
		finally
		{
			_isJoiningLobby = false;
		}
	}
}

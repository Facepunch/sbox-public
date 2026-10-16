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
		Api.Activity.GameRequested( new( "menu", ident ), replace: false );

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

		// What the search found and where it ended up, so an empty or failed search can be told apart
		// from a join. Without a join the caller usually starts its own server.
		var report = new Api.Events.EventRecord( "quickplay" );
		report.SetValue( "ident", ident );
		report.StartTimer( "ms" );
		var tried = 0;
		int? joinedMembers = null;

		try
		{
			_isJoiningLobby = true;
			Api.Activity.GameRequested( new( "quickplay", ident ), replace: false );

			// Leave the game we're in before looking, the loading screen stays up for the search
			if ( Game.InGame )
			{
				IGameInstanceDll.Current.CloseGame();
				LoadingScreen.IsVisible = true;
			}

			Log.Info( "Searching for games.." );
			var lobbies = await Networking.QueryLobbies( ident );
			Log.Info( $"..found {lobbies.Count} available matches" );

			report.SetValue( "found", lobbies.Count );
			report.SetValue( "open", lobbies.Count( x => !x.IsFull ) );

			var orderedLobbies = lobbies.OrderBy( lobby => lobby.ContainsFriends )
				.ThenByDescending( lobby => lobby.Members );

			foreach ( var lobby in orderedLobbies )
			{
				if ( lobby.IsFull ) continue;

				// We might be in a game now
				if ( Game.InGame ) return false;

				Log.Info( $"Attempting to join available lobby {lobby.LobbyId}" );

				// Try to join this one
				tried++;
				if ( await Networking.TryConnectSteamId( lobby.LobbyId ) )
				{
					joinedMembers = lobby.Members;
					CloseAllModals();
					return true;
				}
			}

			return false;
		}
		finally
		{
			_isJoiningLobby = false;

			report.FinishTimer( "ms" );
			report.SetValue( "tried", tried );
			report.SetValue( "joined", joinedMembers is not null );
			if ( joinedMembers is { } members ) report.SetValue( "members", members );
			report.Submit();
		}
	}
}

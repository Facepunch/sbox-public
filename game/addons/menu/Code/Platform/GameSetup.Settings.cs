using Sandbox;
using Sandbox.Modals;

namespace MenuProject.Platform;

/// <summary>
/// Defaults shared by game setup and party launching.
/// </summary>
public sealed partial class GameSetup
{
	internal static CreateGameResults GetInitialSettings( Package package, int maxMembers = 0 )
	{
		var maximum = Math.Max( 1, package.Info.MaxPlayers );
		var minimum = Math.Clamp( package.Info.MinPlayers, 1, maximum );
		var defaultPlayers = Math.Clamp( maxMembers > 0 ? maxMembers : maximum, minimum, maximum );
		return Game.Cookies.Get<CreateGameResults>( CookieNameOf( package.FullIdent ), new()
		{
			MaxPlayers = defaultPlayers,
			ServerName = DefaultServerName
		} );
	}

	static string CookieNameOf( string ident ) => $"{ident}_create_game_config";

	/// <summary>
	/// Forgets a game's saved map when it couldn't be loaded, so its setup starts from the default map.
	/// </summary>
	[Event( "game.mapfailed" )]
	static void ForgetSavedMap( string ident, string map )
	{
		var saved = Game.Cookies.Get<CreateGameResults>( CookieNameOf( ident ), new() );
		if ( saved.Map != map ) return;

		saved.Map = null;
		Game.Cookies.Set( CookieNameOf( ident ), saved );
	}

	internal static int ClampServerSlots( Package package, int slots )
	{
		return Math.Clamp( slots, 1, Math.Max( 1, package.Info.MaxPlayers ) );
	}
}

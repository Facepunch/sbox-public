using Sandbox;
using Sandbox.Modals;

namespace MenuProject.Modals.GameModalComponents;

/// <summary>
/// Defaults and comparisons shared by game setup and party launching.
/// </summary>
public sealed partial class GameSetup
{
	internal static CreateGameResults GetInitialSettings( Package package, int maxMembers = 0 )
	{
		var maximum = Math.Max( 1, package.Info.MaxPlayers );
		var minimum = Math.Clamp( package.Info.MinPlayers, 1, maximum );
		var defaultPlayers = Math.Clamp( maxMembers > 0 ? maxMembers : maximum, minimum, maximum );
		return Game.Cookies.Get<CreateGameResults>( $"{package.FullIdent}_create_game_config", new()
		{
			MaxPlayers = defaultPlayers,
			ServerName = DefaultServerName
		} );
	}

	internal static int ClampServerSlots( Package package, int slots )
	{
		return Math.Clamp( slots, 1, Math.Max( 1, package.Info.MaxPlayers ) );
	}

	internal static bool SettingsEqual( CreateGameResults? left, CreateGameResults? right )
	{
		if ( left is not { } a || right is not { } b ) return left.HasValue == right.HasValue;

		return a.MaxPlayers == b.MaxPlayers && a.ServerName == b.ServerName && a.Privacy == b.Privacy && a.Map == b.Map
			&& (a.GameSettings?.Count ?? 0) == (b.GameSettings?.Count ?? 0)
			&& (a.GameSettings is null || a.GameSettings.All( x => b.GameSettings is not null && b.GameSettings.TryGetValue( x.Key, out var value ) && value == x.Value ));
	}
}

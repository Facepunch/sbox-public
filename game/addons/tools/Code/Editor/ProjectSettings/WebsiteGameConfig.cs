namespace Editor.ProjectSettingPages;

/// <summary>
/// Player counts, play modes and map settings used to be set on the website. Until a project declares
/// them itself, start from the website values so the first save doesn't wipe them. The settings window and
/// the publish wizard fetch the package first, so this reads it from the cache.
/// </summary>
internal static class WebsiteGameConfig
{
	static Package Fetched( Project project ) => Package.TryGetCached( project.Config.FullIdent, out var package, false ) ? package : null;

	public static void FillPlayers( Project project, PlayerSettings settings )
	{
		var package = Fetched( project );
		if ( package is null )
			return;

		settings.Coop = package.GetValue( "Coop", false );
		settings.Versus = package.GetValue( "Versus", false );
		settings.RecommendedMinPlayers = package.GetValue( "RecommendedMinPlayers", 0 );
		settings.RecommendedMaxPlayers = package.GetValue( "RecommendedMaxPlayers", 0 );

		if ( settings.RecommendedMinPlayers > 0 || settings.RecommendedMaxPlayers > 0 )
			return;

		// No range on the website - turn the old single/multi player checkboxes into one.
		var single = package.GetValue( "SinglePlayer", false );
		var multi = package.GetValue( "MultiPlayer", false );

		if ( single )
		{
			settings.RecommendedMinPlayers = 1;
			settings.RecommendedMaxPlayers = multi ? 0 : 1;
		}
		else if ( multi )
		{
			settings.RecommendedMinPlayers = 2;
		}
	}

	public static MapSettings Maps( Project project )
	{
		if ( project.Config.TryGetMeta( "MapSettings", out MapSettings settings ) )
			return settings;

		var package = Fetched( project );

		return new MapSettings
		{
			UseCreateGameModal = package?.GetValue( "UseCreateGameModal", false ) ?? false,
			NeedsMap = package?.GetValue( "NeedsMap", false ) ?? false,
			ShowChangeMap = package?.GetValue( "ShowChangeMap", false ) ?? false,
			DefaultMap = package?.GetValue<string>( "DefaultMap", null ),
			MapTarget = package?.GetValue<string>( "MapTarget", null ),
		};
	}
}

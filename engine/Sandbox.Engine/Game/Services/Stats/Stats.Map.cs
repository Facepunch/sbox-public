namespace Sandbox.Services;

/// <summary>
/// Allows access to stats for the current game. Stats are defined by the game's author
/// and can be used to track anything from player actions to performance metrics. They are
/// how you submit data to leaderboards.
/// </summary>
public static partial class Stats
{
	/// <summary>
	/// Stats for the current map
	/// </summary>
	public static class Map
	{
		/// <summary>
		/// Get the stats for the local player
		/// </summary>
		public static PlayerStats Local => Stats.GetLocalPlayerStats( Application.MapPackage?.FullIdent ?? "local.map" );

		/// <summary>
		/// Get the global stats
		/// </summary>
		[Title( "Get Global Map Stats" )]
		[Category( "Services/Stats" )]
		[Icon( "emoji_events" )]
		[ActionGraphNode( "services.stats.map.getglobal" )]
		public static GlobalStats Global => Stats.GetGlobalStats( Application.MapPackage?.FullIdent ?? "local.map" );

		/// <summary>
		/// Add a stat value for this package
		/// </summary>
		[Title( "Set Map Stat" )]
		[Category( "Services/Stats" )]
		[Icon( "emoji_events" )]
		[ActionGraphNode( "services.stats.map.set" )]
		public static void SetValue( string name, double amount, Dictionary<string, object> data = default )
		{
			var package = Application.MapPackage;
			if ( package is not null )
			{
				var ident = package.FullIdent;
				Api.Stats.AddIncrement( ident, name, amount, GetObjectDictionary( data ) );
			}

			Local?.Predict( name, amount );
		}

		/// <summary>
		/// Get a stat for the local player
		/// </summary>
		[Title( "Get Local" )]
		[Category( "Services/Stats" )]
		[Icon( "emoji_events" )]
		[ActionGraphNode( "services.stats.map.getlocal" ), Pure]
		public static PlayerStat GetLocal( string name ) => Local.Get( name );

		/// <summary>
		/// Get a stat for the local player
		/// </summary>
		[Title( "Get Local" )]
		[Category( "Services/Stats" )]
		[Icon( "emoji_events" )]
		[ActionGraphNode( "services.stats.map.getglobal" ), Pure]
		public static GlobalStat GetGlobal( string name ) => Global.Get( name );
	}
}


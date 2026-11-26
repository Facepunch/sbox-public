namespace Sandbox.Services;

public static partial class Stats
{
	/// <summary>
	/// Compatibility API for retired map stats.
	/// </summary>
	[Obsolete( "Map stats have been removed. Use Sandbox.Services.Stats for game stats." )]
	public static class Map
	{
		/// <summary>
		/// Returns an empty collection of local player stats.
		/// </summary>
		[Obsolete]
		public static PlayerStats Local { get; } = new( "local.map", 0 );

		/// <summary>
		/// Returns an empty collection of global stats.
		/// </summary>
		[Obsolete]
		[Title( "Get Global Map Stats" )]
		[Category( "Services/Stats" )]
		[Icon( "emoji_events" )]
		[ActionGraphNode( "services.stats.map.getglobal" )]
		public static GlobalStats Global { get; } = new( "local.map" );

		/// <summary>
		/// Retained for compatibility. Stat writes are ignored.
		/// </summary>
		[Obsolete]
		[Title( "Set Map Stat" )]
		[Category( "Services/Stats" )]
		[Icon( "emoji_events" )]
		[ActionGraphNode( "services.stats.map.set" )]
		public static void SetValue( string name, double amount, Dictionary<string, object> data = default )
		{
		}

		/// <summary>
		/// Returns an empty player stat.
		/// </summary>
		[Obsolete]
		[Title( "Get Local" )]
		[Category( "Services/Stats" )]
		[Icon( "emoji_events" )]
		[ActionGraphNode( "services.stats.map.getlocal" ), Pure]
		public static PlayerStat GetLocal( string name ) => default;

		/// <summary>
		/// Returns an empty global stat.
		/// </summary>
		[Obsolete]
		[Title( "Get Local" )]
		[Category( "Services/Stats" )]
		[Icon( "emoji_events" )]
		[ActionGraphNode( "services.stats.map.getglobal" ), Pure]
		public static GlobalStat GetGlobal( string name ) => default;
	}
}


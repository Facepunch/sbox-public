namespace Sandbox.Services;

public static partial class Achievements
{
	/// <summary>
	/// Compatibility API for retired map achievements.
	/// </summary>
	[Obsolete( "Map achievements have been removed. Use Sandbox.Services.Achievements for game achievements." )]
	public static class Map
	{
		/// <summary>
		/// Retained for compatibility. Unlock requests are ignored.
		/// </summary>
		[Obsolete]
		[Title( "Unlock Map Achievement" )]
		[Category( "Services/Achievements" )]
		[Icon( "emoji_events" )]
		[ActionGraphNode( "services.achievements.map.unlock" )]
		public static void Unlock( string name )
		{
		}

		/// <summary>
		/// Returns an empty achievement collection.
		/// </summary>
		[Obsolete]
		public static IEnumerable<Achievement> All => [];

		/// <summary>
		/// Returns null for every map achievement.
		/// </summary>
		[Obsolete]
		[Title( "Get Achievement" )]
		[Category( "Services/Achievements" )]
		[Icon( "emoji_events" )]
		[ActionGraphNode( "services.achievements.map.get" ), Pure]
		public static Achievement Get( string name ) => null;
	}
}


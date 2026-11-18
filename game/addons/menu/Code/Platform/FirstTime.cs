using Sandbox;

namespace MenuProject.Platform;

/// <summary>
/// Tracks which parts of the menu the player has used at least once, so the dock can light up what they haven't.
/// Stored in the menu's cookies.
/// </summary>
public static class FirstTime
{
	public const string Discover = "platform.seen.discover";
	public const string Avatar = "platform.seen.avatar";

	static readonly Dictionary<string, bool> seen = new();

	public static bool Seen( string key )
	{
		if ( !seen.TryGetValue( key, out var value ) )
			seen[key] = value = Game.Cookies.Get( key, false );

		return value;
	}

	public static void MarkSeen( string key )
	{
		if ( Seen( key ) ) return;

		seen[key] = true;
		Game.Cookies.Set( key, true );
	}
}

namespace Sandbox.Services;

/// <summary>
/// Everything the platform knows about how a game is played and launched. Shared DTO (website +
/// game engine), carried on listings too, so menu cards, parties and lobbies can use it without
/// fetching the full package. Combines the project settings the version was published with
/// (enforced limits, launch mode) and the creator's website config (how they present the game,
/// map launch options). Rebuilt on publish, version switch and config save.
/// </summary>
public class GameMetaData : BaseMetaData
{
	//
	// Players
	//

	/// <summary>
	/// The lobby limit from the project settings, enforced when a lobby is created. Most projects leave
	/// the default (1 - 64), so don't show this to players - use <see cref="RecommendedMinPlayers"/>.
	/// </summary>
	public int MinPlayers { get; set; } = 1;

	/// <inheritdoc cref="MinPlayers"/>
	public int MaxPlayers { get; set; } = 1;

	/// <summary>
	/// How many players the creator says the game is meant for, within the lobby limit. This is the count
	/// to show and to match parties against. Null when the creator hasn't said.
	/// </summary>
	public int? RecommendedMinPlayers { get; set; }

	/// <summary>Upper end of <see cref="RecommendedMinPlayers"/>. Null means no limit ("4+").</summary>
	public int? RecommendedMaxPlayers { get; set; }

	/// <summary>
	/// Can be played alone - the recommended range starts at 1. Falls back to the old website setting,
	/// then to the lobby limit, when the creator gave no range.
	/// </summary>
	public bool Singleplayer { get; set; }

	/// <summary>Can be played together - the recommended range goes above 1. Same fallbacks as <see cref="Singleplayer"/>.</summary>
	public bool Multiplayer { get; set; }

	/// <summary>Players work together.</summary>
	public bool Coop { get; set; }

	/// <summary>Players compete against each other.</summary>
	public bool Versus { get; set; }

	//
	// Platform
	//

	/// <summary>Can be played in VR.</summary>
	public bool Vr { get; set; }

	/// <summary>Can only be played in VR.</summary>
	public bool VrOnly { get; set; }

	/// <summary>Uses the streamer integration.</summary>
	public bool Streamer { get; set; }

	//
	// Launch
	//

	/// <summary>The project's launch mode, e.g. "default", "quickplay", "dedicatedserveronly".</summary>
	public string LaunchMode { get; set; }

	/// <summary>Declares settings players can change before starting.</summary>
	public bool HasGameSettings { get; set; }

	/// <summary>Pressing play opens the create game popup (max players, privacy, map).</summary>
	public bool UseCreateGameModal { get; set; }

	//
	// Maps
	//

	/// <summary>Players must pick a map before playing.</summary>
	public bool NeedsMap { get; set; }

	/// <summary>Offer "Change Map" in the escape menu.</summary>
	public bool ShowChangeMap { get; set; }

	/// <summary>Map package selected by default.</summary>
	public string DefaultMap { get; set; }

	/// <summary>
	/// Comma separated games whose maps this game can load, or "*" for any. Null means maps made
	/// for this game.
	/// </summary>
	public string MapTarget { get; set; }

	public override void GetAutoTags( HashSet<string> tags )
	{
		SetTag( tags, "streamergame", Streamer );
		SetTag( tags, "quickplay", string.Equals( LaunchMode, "quickplay", StringComparison.OrdinalIgnoreCase ) );
		SetTag( tags, "dedicatedserver", string.Equals( LaunchMode, "dedicatedserveronly", StringComparison.OrdinalIgnoreCase ) );
		SetTag( tags, "vronly", VrOnly );
		SetTag( tags, "hasgamesettings", HasGameSettings );
		SetTag( tags, "singleplayer", Singleplayer );

		// Creators have long tagged these by hand, so only ever add them.
		if ( Multiplayer ) tags.Add( "multiplayer" );
		if ( Coop ) tags.Add( "coop" );
		if ( Versus ) tags.Add( "pvp" );
	}
}

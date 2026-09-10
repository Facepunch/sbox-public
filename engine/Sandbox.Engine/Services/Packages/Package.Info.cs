using Sandbox.DataModel;

namespace Sandbox;

public partial class Package
{
	/// <summary>
	/// Strongly-typed shortcuts for the well-known data values the menu/launcher reads via
	/// <see cref="GetValue{T}(string, T)"/>. <see cref="PackageInfo"/> is a thin readonly view over this
	/// package, so <c>package.Info.IsStreamerGame</c> allocates nothing and keeps the magic string keys
	/// and their defaults in one place.
	/// </summary>
	public PackageInfo Info => new( this );
}

/// <summary>
/// A thin readonly view over a <see cref="Package"/>'s data values, exposing the well-known launcher
/// flags by name so callers don't repeat the string keys and defaults. Holds nothing but a reference to
/// the package; get one from <see cref="Package.Info"/>. Tolerates a default/empty instance by returning
/// each flag's default.
/// </summary>
public readonly struct PackageInfo
{
	private readonly Package _package;

	internal PackageInfo( Package package )
	{
		_package = package;
	}

	/// <summary>
	/// Does this game opt into the streamer integration features (the stream connection button, etc.)?
	/// </summary>
	public bool IsStreamerGame => _package?.GetMeta( "UsesStreamerFeatures", false ) ?? false;

	/// <summary>
	/// Should the pause menu show the player list button? Defaults to true.
	/// </summary>
	public bool ShowsPlayerList => _package?.GetValue( "ShowPlayerList", true ) ?? true;

	/// <summary>
	/// Should the pause menu offer a "change map" button? Defaults to false.
	/// </summary>
	public bool ShowsChangeMap => _package?.GetValue( "ShowChangeMap", false ) ?? false;

	/// <summary>
	/// Does this game require a map to be chosen before it can launch?
	/// </summary>
	public bool NeedsMap => _package?.GetValue( "NeedsMap", false ) ?? false;

	/// <summary>
	/// Should launching this game go through the create-game modal rather than starting directly?
	/// </summary>
	public bool UsesCreateGameModal => _package?.GetValue( "UseCreateGameModal", false ) ?? false;

	/// <summary>
	/// The map this game launches with by default, or empty if none is configured.
	/// </summary>
	public string DefaultMap => _package?.GetValue( "DefaultMap", "" ) ?? "";

	/// <summary>
	/// The package whose maps this game pulls from, defaulting to the game's own ident.
	/// </summary>
	public string MapTarget => _package?.GetValue( "MapTarget", _package.FullIdent );

	/// <summary>
	/// The maximum number of players this game supports. Defaults to 1.
	/// </summary>
	public int MaxPlayers => _package?.GetMeta( "MaxPlayers", 1 ) ?? 1;

	/// <summary>
	/// The minimum number of players needed to start this game. Defaults to 1.
	/// </summary>
	public int MinPlayers => _package?.GetMeta( "MinPlayers", 1 ) ?? 1;

	/// <summary>
	/// The raw launch mode (e.g. "quickplay", "dedicatedserveronly", "launcher"), or "default".
	/// </summary>
	public string LaunchMode => _package?.GetMeta( "LaunchMode", "default" ) ?? "default";

	/// <summary>
	/// Does this game launch straight into matchmaking (<see cref="LaunchMode"/> "quickplay")?
	/// </summary>
	public bool IsQuickPlay => string.Equals( LaunchMode, "quickplay", StringComparison.OrdinalIgnoreCase );

	/// <summary>
	/// Is this game playable only on a dedicated server (<see cref="LaunchMode"/> "dedicatedserveronly")?
	/// </summary>
	public bool IsDedicatedServerOnly => string.Equals( LaunchMode, "dedicatedserveronly", StringComparison.OrdinalIgnoreCase );

	/// <summary>
	/// Can this game only be played in VR?
	/// </summary>
	public bool IsVrOnly => _package?.GetMeta<ControlModeSettings>( "ControlModes" )?.IsVROnly ?? false;

	/// <summary>
	/// The configurable game settings shown in the create-game modal, or null if none are defined.
	/// </summary>
	public List<GameSetting> GameSettings => _package?.GetMeta<List<GameSetting>>( "GameSettings", null );

	/// <summary>
	/// Does this game define any configurable <see cref="GameSetting"/>s?
	/// </summary>
	public bool HasGameSettings => GameSettings is { Count: > 0 };

	/// <summary>
	/// The ident of this package's parent package, or null if it has none.
	/// </summary>
	public string ParentPackage => _package?.GetMeta<string>( "ParentPackage", null );
}

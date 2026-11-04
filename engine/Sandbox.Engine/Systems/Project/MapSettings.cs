using System.ComponentModel.DataAnnotations;

namespace Sandbox;

/// <summary>
/// How the menu launches a game with maps. Stored in the project meta under "MapSettings".
/// </summary>
[Expose]
public class MapSettings
{
	/// <summary>
	/// When pressing play, show a popup to configure max players, lobby privacy and the map.
	/// </summary>
	[Display( Name = "Create Game Popup" )]
	public bool UseCreateGameModal { get; set; }

	/// <summary>
	/// Players have to choose a map before they can play.
	/// </summary>
	[Display( Name = "Show Map Select" )]
	public bool NeedsMap { get; set; }

	/// <summary>
	/// Show a Change Map option in the escape menu. Relaunches the game with the new map.
	/// </summary>
	public bool ShowChangeMap { get; set; }

	/// <summary>
	/// The map package selected by default, e.g. facepunch.flatgrass. Leave empty to make players choose.
	/// </summary>
	public string DefaultMap { get; set; }

	/// <summary>
	/// Leave empty to show maps made for this game. Or list the games whose maps you can load, e.g.
	/// facepunch.sandbox, facepunch.walker - or * for all maps.
	/// </summary>
	public string MapTarget { get; set; }
}

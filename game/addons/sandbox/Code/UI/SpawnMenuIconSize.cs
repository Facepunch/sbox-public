using Sandbox.UI;

namespace Sandbox;

/// <summary>
/// How big tiles are in spawn menu grids.
/// </summary>
public enum SpawnMenuIconSize
{
	/// <summary>
	/// Compact tiles with an icon only.
	/// </summary>
	[Title( "#ui.icon_size.small" )]
	Small,
	/// <summary>
	/// Standard tiles with an icon and title.
	/// </summary>
	[Title( "#ui.icon_size.medium" )]
	Medium,
	/// <summary>
	/// Large tiles with an icon and title.
	/// </summary>
	[Title( "#ui.icon_size.large" )]
	Large
}

/// <summary>
/// Tile sizes and the picker used by spawn menu grids.
/// </summary>
public static class SpawnMenuIconSizeExtensions
{
	const string Cookie = "spawnmenu.icon_size";

	/// <summary>
	/// The user's chosen tile size, persisted between sessions.
	/// </summary>
	public static SpawnMenuIconSize Current
	{
		get => _current ??= Game.Cookies.Get( Cookie, SpawnMenuIconSize.Medium );
		set
		{
			_current = value;
			Game.Cookies.Set( Cookie, value );
		}
	}

	static SpawnMenuIconSize? _current;

	/// <summary>
	/// Cell size to give a <see cref="Sandbox.UI.VirtualGrid"/> for this tile size.
	/// </summary>
	public static Vector2 ItemSize( this SpawnMenuIconSize size ) => size switch
	{
		SpawnMenuIconSize.Small => new Vector2( 75, 75 ),
		SpawnMenuIconSize.Large => new Vector2( 171, 171 ),
		_ => new Vector2( 123, 123 )
	};

	/// <summary>
	/// CSS class to put on the grid so tiles can adapt their text to the size.
	/// </summary>
	public static string CssClass( this SpawnMenuIconSize size ) => $"size-{size.ToString().ToLowerInvariant()}";

	/// <summary>
	/// Material icon representing this size in the toolbar and its menu.
	/// </summary>
	public static string Icon( this SpawnMenuIconSize size ) => size switch
	{
		SpawnMenuIconSize.Small => "view_comfy",
		SpawnMenuIconSize.Large => "crop_square",
		_ => "grid_view"
	};

	/// <summary>
	/// Open the menu that picks a tile size, under the mouse.
	/// </summary>
	public static void OpenMenu( Panel source, Action<SpawnMenuIconSize> onPicked, Action onListPicked = null )
	{
		OpenMenu( source, Current, onPicked, onListPicked );
	}

	/// <summary>
	/// Open the picker with the active tile size highlighted. A null size selects list view.
	/// </summary>
	public static void OpenMenu( Panel source, SpawnMenuIconSize? selectedSize, Action<SpawnMenuIconSize> onPicked, Action onListPicked = null )
	{
		var menu = new Sandbox.UI.Menu();
		if ( onListPicked is not null )
		{
			var option = menu.AddOption( "#ui.icon_size.list", "view_list", onListPicked );
			option.SetClass( "selected", selectedSize is null );
		}

		foreach ( var (size, info) in DisplayInfo.ForEnumValues<SpawnMenuIconSize>() )
		{
			var option = menu.AddOption( info.Name, size.Icon(), () => onPicked( size ) );
			option.SetClass( "selected", size == selectedSize );
		}

		menu.Open( source, Popup.PositionMode.UnderMouse );
	}
}

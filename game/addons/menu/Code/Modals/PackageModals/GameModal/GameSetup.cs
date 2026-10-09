using Sandbox;
using Sandbox.DataModel;
using Sandbox.Modals;
using Sandbox.Network;
using Sandbox.UI;
using MenuProject.Settings;

namespace MenuProject.Modals.GameModalComponents;

/// <summary>
/// Setting up a new game of a package before starting it - where, who can join, and the game's own
/// settings. Starts from what you picked last time for it, and remembers what you start with. Each
/// choice is a <see cref="SettingItem"/>, so the setup view draws them with the settings page's own
/// rows (SettingsRow) - the map's the one it draws itself.
/// </summary>
public sealed partial class GameSetup
{
	public Package Package { get; }

	/// <summary>
	/// Once it's started, with everything picked. Without one it's launched the usual way.
	/// </summary>
	public Action<CreateGameResults> OnComplete { get; }

	/// <summary>
	/// The play button that asked for it (gamepage, hovercard, hero) - the play's reported under it when
	/// it's started. Null for a setup nobody pressed play for (a game changing its own map), not a play.
	/// </summary>
	public string Via { get; init; }

	/// <summary>
	/// The tile that play button was on, for the play's surface and shelf.
	/// </summary>
	public Panel Source { get; init; }

	/// <summary>
	/// Where it starts - a package's ident, or a mounted scene's path.
	/// </summary>
	public string Map
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			_ = ResolveMap( value );
		}
	}

	/// <summary>
	/// The map's name to show - its package's title or its scene's, once it's looked up.
	/// </summary>
	public string MapTitle { get; private set; }

	/// <summary>
	/// A picture of the map, once it's looked up - null if it hasn't got one.
	/// </summary>
	public Texture MapThumb { get; private set; }

	public SettingItem ServerNameItem { get; }
	public SettingItem PrivacyItem { get; }
	public SettingItem PlayersItem { get; }

	/// <summary>
	/// The game's own settings, each with its row.
	/// </summary>
	readonly List<(GameSetting Setting, SettingItem Item)> GameItems = new();

	public string ServerName => ServerNameItem.Value?.ToString();
	public LobbyPrivacy Privacy => PrivacyItem.As<LobbyPrivacy>();
	public int MaxPlayers => (int)PlayersItem.FloatValue;

	/// <summary>
	/// The smallest supported server size, within the game's valid player range.
	/// </summary>
	public int MinPlayerCount => Math.Clamp( Package.Info.MinPlayers, 1, MaxPlayerCount );

	/// <summary>
	/// The game's maximum server size, with at least one player slot.
	/// </summary>
	public int MaxPlayerCount => Math.Max( 1, Package.Info.MaxPlayers );

	public bool IsMultiplayer => Package.Tags.Contains( "multiplayer" ) || Package.Info.MaxPlayers > 1;

	/// <summary>
	/// A player count to pick - only when the game lets it be more than one number.
	/// </summary>
	public bool HasPlayerRange => MinPlayerCount != MaxPlayerCount;

	public bool NeedsMap => Package.Info.NeedsMap || string.Equals( Package.Info.LaunchMode, "launcher", StringComparison.OrdinalIgnoreCase );

	string CookieName => $"{Package.FullIdent}_create_game_config";

	static string DefaultServerName => $"{Sandbox.Utility.Steam.PersonaName}'s game";

	/// <summary>
	/// Past this many choices they go in a dropdown rather than side by side.
	/// </summary>
	const int MaxSideBySide = 4;

	static readonly List<Option> OffOn = [new( "Off", false ), new( "On", true )];

	/// <summary>
	/// Start from the supplied setup, or restore the player's previous choices for this game.
	/// </summary>
	public GameSetup( Package package, Action<CreateGameResults> onComplete = null, CreateGameResults? initialSettings = null )
	{
		Package = package;
		OnComplete = onComplete;

		// What was picked last time, or the defaults - then made sense of, in case the game's changed since
		var saved = initialSettings ?? GetInitialSettings( package );

		var serverName = string.IsNullOrEmpty( saved.ServerName ) ? DefaultServerName : saved.ServerName;
		var players = Math.Clamp( saved.MaxPlayers, MinPlayerCount, MaxPlayerCount );

		ServerNameItem = Loaded( new SettingItem
		{
			Id = "server.name",
			Title = "Name",
			Kind = SettingKind.Custom,
			CreateControl = TextControl,
			Read = () => serverName
		} );

		PrivacyItem = Loaded( new SettingItem
		{
			Id = "server.privacy",
			Title = "Who Can Join",
			Options = Enum.GetValues<LobbyPrivacy>().Select( x => new Option( TitleOf( x ), x ) { Icon = IconOf( x ) } ).ToList(),
			Read = () => saved.Privacy
		} );

		PlayersItem = Loaded( new SettingItem
		{
			Id = "server.players",
			Title = "Max Players",
			Kind = SettingKind.Slider,
			Min = MinPlayerCount,
			Max = MaxPlayerCount,
			Step = 1,
			NumberFormat = "0",
			Read = () => (float)players
		} );

		var values = new Dictionary<string, string>( saved.GameSettings ?? new(), StringComparer.OrdinalIgnoreCase );

		foreach ( var setting in package.Info.GameSettings ?? new List<GameSetting>() )
		{
			values.TryGetValue( setting.Name, out var value );
			GameItems.Add( (setting, ItemFor( setting, value )) );
		}

		Map = saved.Map ?? package.Info.DefaultMap;
	}

	static SettingItem Loaded( SettingItem item )
	{
		item.Revert();
		return item;
	}

	/// <summary>
	/// A game setting's row - its choices side by side (a dropdown past a few), an on and off, a slider
	/// for a number with a top to it, or something to type in. Starting from what it was last time,
	/// or the game's default.
	/// </summary>
	static SettingItem ItemFor( GameSetting setting, string value )
	{
		var title = setting.Title.ToTitleCase();

		if ( HasOptions( setting ) )
		{
			var option = setting.Options.FirstOrDefault( x => string.Equals( x.Name, value ?? setting.Default, StringComparison.OrdinalIgnoreCase ) );
			var picked = !string.IsNullOrEmpty( option.Name ) ? option.Name : setting.Options[0].Name ?? "";

			return Loaded( new SettingItem
			{
				Id = setting.Name,
				Title = title,
				Kind = setting.Options.Count <= MaxSideBySide ? SettingKind.Options : SettingKind.Dropdown,
				Options = setting.Options.Select( x => new Option( x.Name.ToTitleCase(), x.Name ) { Icon = x.Icon } ).ToList(),
				Read = () => picked
			} );
		}

		value ??= setting.Default ?? "";

		if ( IsToggle( setting ) )
		{
			return Loaded( new SettingItem { Id = setting.Name, Title = title, Options = OffOn, Read = () => value.ToBool() } );
		}

		if ( IsNumber( setting ) && setting.Max is not null )
		{
			return Loaded( new SettingItem
			{
				Id = setting.Name,
				Title = title,
				Kind = SettingKind.Slider,
				Min = setting.Min ?? 0,
				Max = setting.Max ?? 100,
				Step = setting.Step ?? 1,
				Read = () => value.ToFloat()
			} );
		}

		return Loaded( new SettingItem { Id = setting.Name, Title = title, Kind = SettingKind.Custom, CreateControl = TextControl, Read = () => value } );
	}

	/// <summary>
	/// Something to type in, for a row - the settings page has no need of one of its own.
	/// </summary>
	static Panel TextControl( SettingItem item )
	{
		var entry = new TextEntry { Text = item.Value?.ToString() ?? "" };
		entry.OnTextEdited = x => item.Value = x;
		return entry;
	}

	/// <summary>
	/// Just the rows of one group of the game's settings - null or "General" for the ungrouped ones.
	/// </summary>
	public IEnumerable<SettingItem> ItemsIn( string group ) => GameItems
		.Where( x => IsGeneral( group ) ? IsGeneral( x.Setting.Group ) : string.Equals( x.Setting.Group, group, StringComparison.OrdinalIgnoreCase ) )
		.OrderBy( x => x.Item.Title, StringComparer.OrdinalIgnoreCase )
		.ThenBy( x => x.Item.Id, StringComparer.OrdinalIgnoreCase )
		.Select( x => x.Item );

	/// <summary>
	/// The groups the game puts its settings in, other than the general one, by name.
	/// </summary>
	public IEnumerable<string> OtherGroups => GameItems
		.Select( x => x.Setting.Group )
		.Where( x => !IsGeneral( x ) )
		.Distinct( StringComparer.OrdinalIgnoreCase )
		.OrderBy( x => x, StringComparer.OrdinalIgnoreCase );

	static bool IsGeneral( string group ) => string.IsNullOrEmpty( group ) || string.Equals( group, "General", StringComparison.OrdinalIgnoreCase );

	static bool HasOptions( GameSetting setting ) => setting.Options is { Count: > 0 };

	/// <summary>
	/// A 0 or 1 with no range, or a true or false - an on and off.
	/// </summary>
	static bool IsToggle( GameSetting setting )
	{
		var d = setting.Default ?? "";

		if ( (d == "0" || d == "1") && setting.Min is null && setting.Max is null && setting.Step is null )
			return true;

		return bool.TryParse( d, out _ );
	}

	static bool IsNumber( GameSetting setting ) => float.TryParse( setting.Default, out _ );

	/// <summary>
	/// A row's value as the text the game gets - an on and off back the way the game wrote its default
	/// (a 1 or a true), a number without trailing noise.
	/// </summary>
	static string ToText( GameSetting setting, SettingItem item )
	{
		if ( item.Options == OffOn )
		{
			var on = item.As<bool>();
			return setting.Default is "0" or "1" ? (on ? "1" : "0") : (on ? "true" : "false");
		}

		if ( item.Kind == SettingKind.Slider )
			return item.FloatValue.ToString( System.Globalization.CultureInfo.InvariantCulture );

		return item.Value?.ToString() ?? "";
	}

	static string TitleOf( LobbyPrivacy privacy ) => privacy switch
	{
		LobbyPrivacy.Public => "Public",
		LobbyPrivacy.Private => "Private",
		_ => "Friends Only"
	};

	static string IconOf( LobbyPrivacy privacy ) => privacy switch
	{
		LobbyPrivacy.Public => "public",
		LobbyPrivacy.Private => "lock",
		_ => "people"
	};

	public string PrivacyTitle => TitleOf( Privacy );
	public string PrivacyIcon => IconOf( Privacy );

	/// <summary>
	/// Look up the map's name and picture - a mounted scene's own, or its package's. Its short ident
	/// meanwhile, and after if it can't be found.
	/// </summary>
	async Task ResolveMap( string map )
	{
		MapTitle = string.IsNullOrWhiteSpace( map ) ? "Default map" : MapName( map );
		MapThumb = null;

		if ( string.IsNullOrWhiteSpace( map ) ) return;

		if ( Sandbox.Mounting.Directory.GetMetadata( map ) is { } meta )
		{
			MapTitle = meta.Name;
			MapThumb = meta.Thumbnail;
			return;
		}

		var package = await Package.Fetch( map, true );

		// Picked another while it was looking
		if ( package is null || map != Map ) return;

		MapTitle = package.Title;
		MapThumb = Texture.Load( package.ThumbWide );
	}

	/// <summary>
	/// A map's short name - a package's name without its org, or a scene's file without its folders.
	/// </summary>
	static string MapName( string map )
	{
		map = map.Split( '#' )[0];

		if ( map.Contains( '/' ) )
			return System.IO.Path.GetFileNameWithoutExtension( map );

		var dot = map.IndexOf( '.' );
		return dot >= 0 ? map[(dot + 1)..] : map;
	}

	/// <summary>
	/// Everything picked, remembered for next time.
	/// </summary>
	public CreateGameResults Finish()
	{
		var results = new CreateGameResults
		{
			Map = Map,
			MaxPlayers = MaxPlayers,
			ServerName = ServerName,
			Privacy = Privacy,
			GameSettings = GameItems.ToDictionary( x => x.Setting.Name, x => ToText( x.Setting, x.Item ) ),
		};

		Game.Cookies.Set( CookieName, results );
		return results;
	}
}

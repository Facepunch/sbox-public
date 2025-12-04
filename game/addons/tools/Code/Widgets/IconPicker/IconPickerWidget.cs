using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Editor;

/// <summary>
/// Picker mode - Material Icons or Emojis
/// </summary>
public enum IconPickerMode
{
	MaterialIcons,
	Emojis
}

/// <summary>
/// A widget for picking Material Icons or Emojis with categories, favorites and recently used tracking.
/// </summary>
[Icon( "search" )]
sealed class IconPickerWidget : Widget
{
	public Action<string> ValueChanged { get; set; }
	public Action<Color> ColorChanged { get; set; }
	public int IconsPerPage = 56;
	public int IconsPerRow = 7;

	const string RecentlyUsedCookieKey = "IconPicker.RecentlyUsed";
	const string RecentlyUsedEmojiCookieKey = "IconPicker.RecentlyUsedEmoji";
	const string FavoritesCookieKey = "IconPicker.Favorites";
	const string FavoritesEmojiCookieKey = "IconPicker.FavoritesEmoji";
	const int MaxRecentIcons = 16;
	const int MaxFavorites = 32;

	string _currentCategory = "Main";
	IconPickerMode _currentMode = IconPickerMode.MaterialIcons;

	string _icon;
	public string Icon
	{
		get => _icon;
		set
		{
			_icon = value;
			AddToRecentlyUsed( _icon, _currentMode );
			Rebuild();
			ValueChanged?.Invoke( _icon );
		}
	}

	int CurrentPage = 0;
	Label HeaderLabel;
	Label CategoryLabel;
	GridLayout ContentLayout;
	Layout CategoryLayout;
	IconButton MaterialIconButton;
	IconButton EmojiButton;
	IconButton ColorButton;
	LineEdit SearchBox;
	string _searchText = "";

	static readonly Dictionary<string, string[]> EmojiCategories;
	static readonly Dictionary<string, List<string>> EmojiToNames; // Maps emoji to their searchable names

	static string[] SmileyEmojis;
	static string[] PeopleEmojis;
	static string[] NatureEmojis;
	static string[] FoodEmojis;
	static string[] ActivityEmojis;
	static string[] TravelEmojis;
	static string[] ObjectEmojis;
	static string[] SymbolEmojis;
	static string[] FlagEmojis;

	static IconPickerWidget()
	{
		// Load emojis from JSON file
		Dictionary<string, string[]> emojiData = null;

		try
		{
			// Try loading from addons/tools/Data path (where this code lives)
			var path = FileSystem.Root.GetFullPath( "/addons/tools/Data/emojis/emojis.json" );
			if ( !string.IsNullOrEmpty( path ) && System.IO.File.Exists( path ) )
			{
				var json = System.IO.File.ReadAllText( path );
				emojiData = JsonSerializer.Deserialize<Dictionary<string, string[]>>( json );
			}
		}
		catch { }

		// Fallback defaults if JSON not found
		emojiData ??= new Dictionary<string, string[]>();

		SmileyEmojis = emojiData.GetValueOrDefault( "Smileys" ) ?? [];
		PeopleEmojis = emojiData.GetValueOrDefault( "People" ) ?? [];
		NatureEmojis = emojiData.GetValueOrDefault( "Nature" ) ?? [];
		FoodEmojis = emojiData.GetValueOrDefault( "Food" ) ?? [];
		ActivityEmojis = emojiData.GetValueOrDefault( "Activities" ) ?? [];
		TravelEmojis = emojiData.GetValueOrDefault( "Travel" ) ?? [];
		ObjectEmojis = emojiData.GetValueOrDefault( "Objects" ) ?? [];
		SymbolEmojis = emojiData.GetValueOrDefault( "Symbols" ) ?? [];
		FlagEmojis = emojiData.GetValueOrDefault( "Flags" ) ?? [];

		EmojiCategories = new Dictionary<string, string[]>()
		{
			["Recently Used"] = Array.Empty<string>(),
			["Favorites"] = Array.Empty<string>(),
			["Smileys"] = SmileyEmojis,
			["People"] = PeopleEmojis,
			["Nature"] = NatureEmojis,
			["Food"] = FoodEmojis,
			["Activities"] = ActivityEmojis,
			["Travel"] = TravelEmojis,
			["Objects"] = ObjectEmojis,
			["Symbols"] = SymbolEmojis,
			["Flags"] = FlagEmojis,
			["All Emojis"] = SmileyEmojis
				.Concat( PeopleEmojis )
				.Concat( NatureEmojis )
				.Concat( FoodEmojis )
				.Concat( ActivityEmojis )
				.Concat( TravelEmojis )
				.Concat( ObjectEmojis )
				.Concat( SymbolEmojis )
				.Concat( FlagEmojis )
				.Distinct()
				.ToArray()
		};

		// Build reverse mapping from emojis to searchable names
		// Uses reflection to get the Emoji.Entries dictionary from Sandbox.UI namespace
		EmojiToNames = new Dictionary<string, List<string>>();
		try
		{
			var emojiType = typeof( Sandbox.UI.Emoji );
			var entriesField = emojiType.GetField( "Entries", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic );
			if ( entriesField?.GetValue( null ) is Dictionary<string, string> entries )
			{
				foreach ( var kvp in entries )
				{
					var name = kvp.Key.Trim( ':' ); // Remove colons from ":heart:" -> "heart"
					var emoji = kvp.Value;

					if ( !EmojiToNames.ContainsKey( emoji ) )
						EmojiToNames[emoji] = new List<string>();

					EmojiToNames[emoji].Add( name );
				}
			}
		}
		catch { }
	}

	public IconPickerWidget( Widget parent = null ) : base( parent )
	{
		Layout = Layout.Column();
		FocusMode = FocusMode.Click;

		// Remove tab widget, just use the icons tab directly
		var iconsTab = new Widget( this );
		iconsTab.Layout = Layout.Row();
		Layout.Add( iconsTab );

		// Left sidebar for categories
		var sidebarLayout = Layout.Column();
		sidebarLayout.Margin = 4;
		sidebarLayout.Spacing = 2;
		sidebarLayout.Alignment = TextFlag.Top; // Icons start at the top
		iconsTab.Layout.Add( sidebarLayout );

		CategoryLayout = sidebarLayout;
		BuildCategorySidebar();

		// Separator
		var separator = new Widget( iconsTab );
		separator.FixedWidth = 1;
		separator.MinimumHeight = 100;
		separator.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground.Lighten( 0.1f ) );
			Paint.DrawRect( separator.LocalRect );
			return true;
		};
		iconsTab.Layout.Add( separator );

		// Right content area - wrapped in a widget for background painting
		var contentAreaWrapper = new Widget( iconsTab );
		var contentArea = Layout.Column();
		contentArea.Margin = 4;
		contentAreaWrapper.Layout = contentArea;
		iconsTab.Layout.Add( contentAreaWrapper );

		// Add background for content area for better contrast
		contentAreaWrapper.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground.Darken( 0.05f ) );
			Paint.DrawRect( contentAreaWrapper.LocalRect, 4 );
			return false; // Continue painting children
		};

		// Category header - right aligned
		CategoryLabel = contentArea.Add( new Label( iconsTab ) );
		CategoryLabel.Alignment = TextFlag.Right;
		CategoryLabel.SetStyles( "font-weight: bold; font-size: 11px; color: #888;" );

		// Search box
		SearchBox = contentArea.Add( new LineEdit( iconsTab ) );
		SearchBox.PlaceholderText = "Search icons...";
		SearchBox.TextEdited += ( text ) =>
		{
			_searchText = text;
			CurrentPage = 0;
			Rebuild();
		};

		// Page navigation
		var headerLayout = Layout.Row();
		headerLayout.Margin = 4;
		headerLayout.Spacing = 2;
		contentArea.Add( headerLayout );

		var buttonLeft = headerLayout.Add( new IconButton( "chevron_left", ButtonLeft, iconsTab ) );
		buttonLeft.ToolTip = "Previous Page";

		HeaderLabel = headerLayout.Add( new Label( iconsTab ) );
		HeaderLabel.Alignment = TextFlag.Center;

		var buttonRight = headerLayout.Add( new IconButton( "chevron_right", ButtonRight, iconsTab ) );
		buttonRight.ToolTip = "Next Page";

		// Icon grid
		ContentLayout = Layout.Grid();
		ContentLayout.Margin = 4;
		ContentLayout.Spacing = 4;
		ContentLayout.Alignment = TextFlag.LeftTop; // Icons start at top-left
		contentArea.Add( ContentLayout );

		contentArea.AddStretchCell( 1 );

		// Right sidebar for small mode buttons (icons / emoji) and color picker
		var rightSeparator = new Widget( iconsTab );
		rightSeparator.FixedWidth = 1;
		rightSeparator.MinimumHeight = 100;
		rightSeparator.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground.Lighten( 0.1f ) );
			Paint.DrawRect( separator.LocalRect );
			return true;
		};
		iconsTab.Layout.Add( rightSeparator );

		var rightSidebar = Layout.Column();
		rightSidebar.Margin = 4;
		rightSidebar.Spacing = 10; // Increased spacing for better separation
		rightSidebar.Alignment = TextFlag.Top | TextFlag.Right; // Align to top right
		iconsTab.Layout.Add( rightSidebar );

		// Mode buttons as small icons in the right sidebar
		MaterialIconButton = new IconButton( "apps", () => SetMode( IconPickerMode.MaterialIcons ), iconsTab );
		MaterialIconButton.ToolTip = "Material Icons";
		MaterialIconButton.IconSize = 14;
		MaterialIconButton.FixedSize = 26;
		rightSidebar.Add( MaterialIconButton );

		// Use a material icon for emoji instead of emoji glyph
		EmojiButton = new IconButton( "emoji_emotions", () => SetMode( IconPickerMode.Emojis ), iconsTab );
		EmojiButton.ToolTip = "Emojis";
		EmojiButton.IconSize = 14;
		EmojiButton.FixedSize = 26;
		rightSidebar.Add( EmojiButton );

		// Color button - opens color picker popup
		ColorButton = new IconButton( "palette", () =>
		{
			var lastColor = EditorCookie.Get<Color?>( "IconPicker.LastColor", null ) ?? Color.White;
			ColorPicker.OpenColorPopup( lastColor, c =>
			{
				EditorCookie.Set( "IconPicker.LastColor", c );
				ColorChanged?.Invoke( c );
			} );
		}, iconsTab );
		ColorButton.ToolTip = "Icon Color";
		ColorButton.IconSize = 14;
		ColorButton.FixedSize = 26;
		rightSidebar.Add( ColorButton );

		UpdateModeButtons();

		Rebuild();
	}

	void SetMode( IconPickerMode mode )
	{
		if ( _currentMode == mode ) return;
		_currentMode = mode;
		_currentCategory = mode == IconPickerMode.MaterialIcons ? "Main" : "Smileys";
		_searchText = "";
		if ( SearchBox is not null )
		{
			SearchBox.Text = "";
			SearchBox.PlaceholderText = mode == IconPickerMode.MaterialIcons ? "Search icons..." : "Search emojis...";
		}
		CurrentPage = 0;
		UpdateModeButtons();
		BuildCategorySidebar();
		Rebuild();
	}

	void UpdateModeButtons()
	{
		var activeColor = Theme.Primary;
		var inactiveColor = Theme.ControlBackground;

		MaterialIconButton.SetStyles( _currentMode == IconPickerMode.MaterialIcons
			? $"background-color: {activeColor.Hex}; color: white;"
			: $"background-color: {inactiveColor.Hex};" );

		EmojiButton.SetStyles( _currentMode == IconPickerMode.Emojis
			? $"background-color: {activeColor.Hex}; color: white;"
			: $"background-color: {inactiveColor.Hex};" );

		// Update color button visibility: only for material icons and when an icon is selected
		if ( ColorButton is not null )
		{
			ColorButton.Visible = _currentMode == IconPickerMode.MaterialIcons && !string.IsNullOrEmpty( Icon );
		}
	}

	void BuildCategorySidebar()
	{
		CategoryLayout.Clear( true );

		var categories = _currentMode == IconPickerMode.MaterialIcons ? IconCategories : EmojiCategories;

		foreach ( var category in categories.Keys )
		{
			var isActive = category == _currentCategory;
			var categoryInfo = GetCategoryInfo( category );

			var btn = new IconButton( categoryInfo.Icon, () => SetCategory( category ), this );
			btn.ToolTip = categoryInfo.Name;
			btn.IconSize = 18;
			btn.FixedSize = 28;

			if ( isActive )
			{
				btn.OnPaintOverride = () =>
				{
					Paint.ClearPen();
					Paint.SetBrush( Theme.Primary.WithAlpha( 0.3f ) );
					Paint.DrawRect( btn.LocalRect, 4 );
					Paint.SetPen( Theme.Primary );
					Paint.DrawIcon( btn.LocalRect, categoryInfo.Icon, 18 );
					return true;
				};
			}

			CategoryLayout.Add( btn );
		}
	}

	void SetCategory( string category )
	{
		if ( _currentCategory == category ) return;
		_currentCategory = category;
		_searchText = "";
		if ( SearchBox is not null ) SearchBox.Text = "";
		CurrentPage = 0;
		BuildCategorySidebar();
		Rebuild();
	}

	(string Icon, string Name) GetCategoryInfo( string category )
	{
		return category switch
		{
			// Material Icon categories
			"Recently Used" => ("history", "Recently Used"),
			"Favorites" => ("star", "Favorites"),
			"Main" => ("apps", "Common"),
			"All Icons" => ("widgets", "All Icons"),
			// Emoji categories
			"Smileys" => ("emoji_emotions", "Smileys & Emotion"),
			"People" => ("emoji_people", "People & Body"),
			"Nature" => ("emoji_nature", "Animals & Nature"),
			"Food" => ("emoji_food_beverage", "Food & Drink"),
			"Activities" => ("emoji_events", "Activities"),
			"Travel" => ("emoji_transportation", "Travel & Places"),
			"Objects" => ("emoji_objects", "Objects"),
			"Symbols" => ("emoji_symbols", "Symbols"),
			"Flags" => ("emoji_flags", "Flags"),
			"All Emojis" => ("apps", "All Emojis"),
			_ => ("help", category)
		};
	}

	void Rebuild()
	{
		var currentPage = CurrentPage;
		int totalPages = GetTotalPages();
		if ( totalPages == 0 ) totalPages = 1;
		if ( currentPage >= totalPages ) currentPage = Math.Max( 0, totalPages - 1 );
		if ( currentPage < 0 ) currentPage = 0;
		CurrentPage = currentPage;

		var categoryInfo = GetCategoryInfo( _currentCategory );
		CategoryLabel.Text = categoryInfo.Name.ToUpperInvariant();
		HeaderLabel.Text = $"{currentPage + 1} / {totalPages}";

		var iconNames = GetAvailableIconNames();

		ContentLayout.Clear( true );
		int startingIndex = currentPage * IconsPerPage;
		int endingIndex = Math.Min( startingIndex + IconsPerPage, iconNames.Length );
		int x = 0;
		int y = 0;
		for ( int i = startingIndex; i < endingIndex; i++ )
		{
			var icon = iconNames[i];
			var isFavorite = IsFavorite( icon, _currentMode );
			var iconButton = new IconButton( icon );
			iconButton.IconSize = 18;
			iconButton.FixedSize = 26;
			iconButton.ToolTip = icon + (isFavorite ? " ?" : "");
			iconButton.OnClick = () => Icon = icon;

			// Right-click context menu for favorites
			var capturedIcon = icon;
			var capturedMode = _currentMode;
			iconButton.MouseRightClick = () =>
			{
				var menu = new Menu( iconButton );
				if ( IsFavorite( capturedIcon, capturedMode ) )
				{
					menu.AddOption( "Remove from Favorites", "star_border", () =>
					{
						RemoveFromFavorites( capturedIcon, capturedMode );
						Rebuild();
					} );
				}
				else
				{
					menu.AddOption( "Add to Favorites", "star", () =>
					{
						AddToFavorites( capturedIcon, capturedMode );
						Rebuild();
					} );
				}
				menu.OpenAtCursor();
			};

			if ( icon == Icon )
			{
				iconButton.OnPaintOverride = () =>
				{
					Paint.ClearPen();
					Paint.SetBrush( Theme.Primary.WithAlpha( 0.8f ) );
					Paint.DrawRect( iconButton.LocalRect, 3 );
					Paint.SetPen( Color.White );
					Paint.DrawIcon( iconButton.LocalRect, icon, 18 );
					return true;
				};
			}
			else if ( isFavorite )
			{
				iconButton.OnPaintOverride = () =>
				{
					Paint.ClearPen();
					Paint.SetBrush( Theme.Yellow.WithAlpha( 0.2f ) );
					Paint.DrawRect( iconButton.LocalRect, 3 );
					Paint.SetPen( Theme.TextControl );
					Paint.DrawIcon( iconButton.LocalRect, icon, 18 );
					return true;
				};
			}

			ContentLayout.AddCell( x, y, iconButton );
			x++;
			if ( x >= IconsPerRow )
			{
				x = 0;
				y++;
			}
		}

		Update();
		UpdateModeButtons();
	}

	void ButtonLeft()
	{
		CurrentPage--;
		if ( CurrentPage < 0 )
			CurrentPage = GetTotalPages() - 1;
		Rebuild();
	}

	void ButtonRight()
	{
		CurrentPage++;
		if ( CurrentPage >= GetTotalPages() )
			CurrentPage = 0;
		Rebuild();
	}

	string[] GetAvailableIconNames()
	{
		string[] icons;

		// Get base icon list
		if ( _currentCategory == "Recently Used" )
		{
			icons = GetRecentlyUsedIcons( _currentMode );
		}
		else if ( _currentCategory == "Favorites" )
		{
			icons = GetFavorites( _currentMode );
		}
		else
		{
			var categories = _currentMode == IconPickerMode.MaterialIcons ? IconCategories : EmojiCategories;
			if ( categories.TryGetValue( _currentCategory, out var categoryIcons ) )
			{
				icons = categoryIcons;
			}
			else
			{
				icons = _currentMode == IconPickerMode.MaterialIcons ? MainIcons : SmileyEmojis;
			}
		}

		// Apply search filter
		if ( !string.IsNullOrWhiteSpace( _searchText ) )
		{
			if ( _currentMode == IconPickerMode.Emojis )
			{
				// Search emojis by their names (e.g., "heart" finds ??, ??, ??, etc.)
				icons = icons.Where( emoji =>
				{
					// Check if the emoji itself contains the search text (unlikely but possible)
					if ( emoji.Contains( _searchText, StringComparison.OrdinalIgnoreCase ) )
						return true;

					// Check if any of the emoji's names contain the search text
					if ( EmojiToNames.TryGetValue( emoji, out var names ) )
					{
						return names.Any( name => name.Contains( _searchText, StringComparison.OrdinalIgnoreCase ) );
					}

					return false;
				} ).ToArray();
			}
			else
			{
				// Material icons: search by icon name
				icons = icons.Where( i => i.Contains( _searchText, StringComparison.OrdinalIgnoreCase ) ).ToArray();
			}
		}

		return icons;
	}

	int GetTotalPages()
	{
		var icons = GetAvailableIconNames();
		if ( icons.Length == 0 ) return 1;
		return (int)MathF.Ceiling( icons.Length / (float)IconsPerPage );
	}

	// Recently used methods
	static string[] GetRecentlyUsedIcons( IconPickerMode mode )
	{
		var key = mode == IconPickerMode.MaterialIcons ? RecentlyUsedCookieKey : RecentlyUsedEmojiCookieKey;
		return EditorCookie.Get( key, Array.Empty<string>() );
	}

	static void AddToRecentlyUsed( string icon, IconPickerMode mode )
	{
		if ( string.IsNullOrEmpty( icon ) ) return;

		var key = mode == IconPickerMode.MaterialIcons ? RecentlyUsedCookieKey : RecentlyUsedEmojiCookieKey;
		var recent = GetRecentlyUsedIcons( mode ).ToList();
		recent.Remove( icon );
		recent.Insert( 0, icon );

		if ( recent.Count > MaxRecentIcons )
			recent = recent.Take( MaxRecentIcons ).ToList();

		EditorCookie.Set( key, recent.ToArray() );
	}

	// Favorites methods
	static string[] GetFavorites( IconPickerMode mode )
	{
		var key = mode == IconPickerMode.MaterialIcons ? FavoritesCookieKey : FavoritesEmojiCookieKey;
		return EditorCookie.Get( key, Array.Empty<string>() );
	}

	static bool IsFavorite( string icon, IconPickerMode mode )
	{
		return GetFavorites( mode ).Contains( icon );
	}

	static void AddToFavorites( string icon, IconPickerMode mode )
	{
		if ( string.IsNullOrEmpty( icon ) ) return;

		var key = mode == IconPickerMode.MaterialIcons ? FavoritesCookieKey : FavoritesEmojiCookieKey;
		var favorites = GetFavorites( mode ).ToList();
		if ( !favorites.Contains( icon ) )
		{
			favorites.Insert( 0, icon );
			if ( favorites.Count > MaxFavorites )
				favorites = favorites.Take( MaxFavorites ).ToList();
			EditorCookie.Set( key, favorites.ToArray() );
		}
	}

	static void RemoveFromFavorites( string icon, IconPickerMode mode )
	{
		if ( string.IsNullOrEmpty( icon ) ) return;

		var key = mode == IconPickerMode.MaterialIcons ? FavoritesCookieKey : FavoritesEmojiCookieKey;
		var favorites = GetFavorites( mode ).ToList();
		favorites.Remove( icon );
		EditorCookie.Set( key, favorites.ToArray() );
	}

	/// <summary>
	/// Open an Icon Picker popup
	/// </summary>
	public static void OpenPopup( Widget parent, string icon, Action<string> onChange, bool showEmojis = true, Action<Color> colorChanged = null )
	{
		var popup = new PopupWidget( parent );
		popup.Visible = false;
		popup.FixedWidth = 280;
		popup.Layout = Layout.Column();
		popup.Layout.Margin = 4;

		var editor = popup.Layout.Add( new IconPickerWidget( popup ), 1 );

		// Hide emoji toggle if not needed
		if ( !showEmojis )
		{
			editor.EmojiButton.Visible = false;
			editor.MaterialIconButton.Visible = false;
		}

		editor.ColorChanged = colorChanged;

		// Start with recently used if available, otherwise main
		var recentIcons = GetRecentlyUsedIcons( IconPickerMode.MaterialIcons );
		editor._currentCategory = recentIcons.Length > 0 ? "Recently Used" : "Main";
		editor.BuildCategorySidebar();

		// Find which category contains the current icon and navigate to it
		if ( !string.IsNullOrEmpty( icon ) )
		{
			// Check Material Icons first
			foreach ( var kvp in IconCategories )
			{
				var idx = Array.IndexOf( kvp.Value, icon );
				if ( idx >= 0 )
				{
					editor._currentMode = IconPickerMode.MaterialIcons;
					editor._currentCategory = kvp.Key;
					editor.CurrentPage = idx / editor.IconsPerPage;
					editor.UpdateModeButtons();
					editor.BuildCategorySidebar();
					break;
				}
			}

			// Check Emojis if not found in Material Icons
			if ( showEmojis )
			{
				foreach ( var kvp in EmojiCategories )
				{
					var idx = Array.IndexOf( kvp.Value, icon );
					if ( idx >= 0 )
					{
						editor._currentMode = IconPickerMode.Emojis;
						editor._currentCategory = kvp.Key;
						editor.CurrentPage = idx / editor.IconsPerPage;
						editor.UpdateModeButtons();
						editor.BuildCategorySidebar();
						break;
					}
				}
			}
		}

		editor._icon = icon;
		editor.ValueChanged = onChange;
		editor.Rebuild();

		popup.OpenAtCursor();
	}

	// Main/Common icons - most frequently used
	static readonly string[] MainIcons = new[]
	{
		// Files & Organization
		"folder", "folder_open", "home", "settings", "search", "apps", "widgets", "view_module", "grid_view", "list",
		// Editing & Actions
		"add", "remove", "edit", "delete", "save", "close", "check", "clear", "refresh", "sync",
		"undo", "redo", "content_copy", "content_paste", "content_cut",
		// Visibility & Security
		"visibility", "visibility_off", "lock", "lock_open",
		// Favorites & Bookmarks
		"star", "star_border", "favorite", "favorite_border", "bookmark", "bookmark_border",
		// Information & Help
		"info", "warning", "error", "help", "help_outline", "lightbulb",
		// Development
		"code", "terminal", "bug_report", "build", "extension",
		// Navigation
		"arrow_back", "arrow_forward", "arrow_upward", "arrow_downward", "expand_more", "expand_less",
		"chevron_left", "chevron_right", "first_page", "last_page", "menu", "more_vert", "more_horiz",
		// Media Controls
		"play_arrow", "pause", "stop", "skip_next", "skip_previous", "fast_forward", "fast_rewind",
		"loop", "shuffle", "volume_up", "volume_down", "volume_mute", "volume_off"
	};

	// Action icons
	static readonly string[] ActionIcons = new[]
	{
		"search", "home", "settings", "done", "info", "delete", "shopping_cart", "check_circle",
		"favorite", "visibility", "lock", "schedule", "language", "face", "help", "history",
		"highlight_off", "account_circle", "alarm", "build", "code", "date_range", "event",
		"explore", "extension", "fingerprint", "grade", "label", "launch", "perm_identity",
		"power_settings_new", "print", "question_answer", "receipt", "room", "settings_input_component",
		"spellcheck", "subject", "supervisor_account", "swap_horiz", "swap_vert", "thumb_up",
		"thumb_down", "timeline", "toc", "today", "track_changes", "translate", "trending_up",
		"trending_down", "update", "verified_user", "view_list", "work", "zoom_in", "zoom_out",
		"add_circle", "remove_circle", "add_box", "indeterminate_check_box", "check_box",
		"check_box_outline_blank", "radio_button_checked", "radio_button_unchecked"
	};

	// Navigation icons
	static readonly string[] NavigationIcons = new[]
	{
		"arrow_back", "arrow_forward", "arrow_upward", "arrow_downward", "arrow_drop_down",
		"arrow_drop_up", "arrow_left", "arrow_right", "cancel", "check", "chevron_left",
		"chevron_right", "close", "expand_less", "expand_more", "first_page", "fullscreen",
		"fullscreen_exit", "last_page", "menu", "more_horiz", "more_vert", "refresh", "subdirectory_arrow_left",
		"subdirectory_arrow_right", "unfold_less", "unfold_more", "apps", "arrow_back_ios",
		"arrow_forward_ios", "double_arrow", "east", "north", "south", "west", "north_east",
		"north_west", "south_east", "south_west", "home", "menu_open", "pivot_table_chart"
	};

	// Content icons
static readonly string[] ContentIcons = new[]
	{
		"add", "add_box", "add_circle", "add_circle_outline", "archive", "backspace", "block",
		"clear", "content_copy", "content_cut", "content_paste", "create", "delete_sweep",
		"drafts", "filter_list", "flag", "font_download", "forward", "gesture", "how_to_reg",
		"inbox", "link", "link_off", "low_priority", "mail", "markunread", "move_to_inbox",
		"next_week", "outlined_flag", "redo", "remove", "remove_circle", "remove_circle_outline",
		"reply", "reply_all", "report", "save", "save_alt", "select_all", "send", "sort",
		"text_format", "unarchive", "undo", "weekend", "where_to_vote", "ballot", "file_copy",
		"how_to_vote", "waves", "add_link", "attach_email", "calculate", "dynamic_feed"
	};

	// Communication icons
	static readonly string[] CommunicationIcons = new[]
	{
		"call", "chat", "chat_bubble", "comment", "contact_mail", "contact_phone", "contacts",
		"email", "forum", "import_contacts", "live_help", "location_off", "location_on",
		"mail_outline", "message", "no_sim", "phone", "portable_wifi_off", "present_to_all",
		"ring_volume", "rss_feed", "screen_share", "speaker_phone", "stay_current_landscape",
		"stay_current_portrait", "stop_screen_share", "swap_calls", "textsms", "voicemail",
		"vpn_key", "call_end", "call_made", "call_merge", "call_missed", "call_received",
		"call_split", "cell_wifi", "clear_all", "dialer_sip", "dialpad", "duo", "import_export",
		"invert_colors_off", "list_alt", "mobile_screen_share", "nat", "person_add_disabled",
		"phonelink_erase", "phonelink_lock", "phonelink_ring", "phonelink_setup", "print_disabled"
	};

	// Editor icons
	static readonly string[] EditorIcons = new[]
	{
		"attach_file", "attach_money", "border_all", "border_bottom", "border_clear", "border_color",
		"border_horizontal", "border_inner", "border_left", "border_outer", "border_right",
		"border_style", "border_top", "border_vertical", "bubble_chart", "drag_handle",
		"format_align_center", "format_align_justify", "format_align_left", "format_align_right",
		"format_bold", "format_clear", "format_color_fill", "format_color_reset", "format_color_text",
		"format_indent_decrease", "format_indent_increase", "format_italic", "format_line_spacing",
		"format_list_bulleted", "format_list_numbered", "format_paint", "format_quote",
		"format_shapes", "format_size", "format_strikethrough", "format_textdirection_l_to_r",
		"format_textdirection_r_to_l", "format_underlined", "functions", "height", "highlight",
		"insert_chart", "insert_comment", "insert_drive_file", "insert_emoticon", "insert_invitation",
		"insert_link", "insert_photo", "linear_scale", "merge_type", "mode_comment", "monetization_on",
		"money_off", "multiline_chart", "notes", "pie_chart", "publish", "scatter_plot", "score",
		"short_text", "show_chart", "space_bar", "strikethrough_s", "table_chart", "text_fields",
		"title", "vertical_align_bottom", "vertical_align_center", "vertical_align_top", "wrap_text"
	};

	// File icons
	static readonly string[] FileIcons = new[]
	{
		"folder", "folder_open", "folder_shared", "folder_special", "create_new_folder",
		"attachment", "cloud", "cloud_circle", "cloud_done", "cloud_download", "cloud_off",
		"cloud_queue", "cloud_upload", "file_download", "file_upload", "drive_file_move",
		"drive_file_rename_outline", "drive_folder_upload", "file_download_done", "file_present",
		"folder_delete", "folder_zip", "grid_view", "request_page", "snippet_folder", "source",
		"topic", "upload_file", "workspaces", "description", "insert_drive_file", "note_add",
		"post_add", "file_copy", "save", "save_alt", "save_as", "restore_page", "difference",
		"document_scanner", "draft", "drafts", "feed", "inventory_2", "newspaper", "note",
		"rule_folder", "snippet_folder", "task", "text_snippet"
	};

	// Hardware icons
	static readonly string[] HardwareIcons = new[]
	{
		"cast", "cast_connected", "computer", "desktop_mac", "desktop_windows", "developer_board",
		"device_hub", "device_unknown", "devices_other", "dock", "gamepad", "headset", "headset_mic",
		"keyboard", "keyboard_arrow_down", "keyboard_arrow_left", "keyboard_arrow_right",
		"keyboard_arrow_up", "keyboard_backspace", "keyboard_capslock", "keyboard_hide",
		"keyboard_return", "keyboard_tab", "keyboard_voice", "laptop", "laptop_chromebook",
		"laptop_mac", "laptop_windows", "memory", "mouse", "phone_android", "phone_iphone",
		"phonelink", "phonelink_off", "power_input", "router", "scanner", "security", "sim_card",
		"smartphone", "speaker", "speaker_group", "tablet", "tablet_android", "tablet_mac",
		"toys", "tv", "videogame_asset", "watch", "browser_not_supported", "browser_updated",
		"cable", "cast_for_education", "connected_tv", "developer_board_off", "earbuds"
	};

	// Social icons
	static readonly string[] SocialIcons = new[]
	{
		"cake", "domain", "group", "group_add", "location_city", "mood", "mood_bad", "notifications",
		"notifications_active", "notifications_none", "notifications_off", "notifications_paused",
		"pages", "party_mode", "people", "people_outline", "person", "person_add", "person_outline",
		"plus_one", "poll", "public", "school", "sentiment_dissatisfied", "sentiment_satisfied",
		"sentiment_very_dissatisfied", "sentiment_very_satisfied", "share", "thumb_down_alt",
		"thumb_up_alt", "whatshot", "emoji_emotions", "emoji_events", "emoji_flags", "emoji_food_beverage",
		"emoji_nature", "emoji_objects", "emoji_people", "emoji_symbols", "emoji_transportation"
	};

	// Toggle icons
	static readonly string[] ToggleIcons = new[]
	{
		"check_box", "check_box_outline_blank", "indeterminate_check_box", "radio_button_checked",
		"radio_button_unchecked", "star", "star_border", "star_half", "toggle_off", "toggle_on"
	};

	// Maps icons
	static readonly string[] MapsIcons = new[]
	{
		"add_location", "beenhere", "directions", "directions_bike", "directions_boat",
		"directions_bus", "directions_car", "directions_railway", "directions_run",
		"directions_subway", "directions_transit", "directions_walk", "edit_location",
		"ev_station", "flight", "hotel", "layers", "layers_clear", "local_activity",
		"local_airport", "local_atm", "local_bar", "local_cafe", "local_car_wash",
		"local_convenience_store", "local_dining", "local_drink", "local_florist",
		"local_gas_station", "local_grocery_store", "local_hospital", "local_hotel",
		"local_laundry_service", "local_library", "local_mall", "local_movies",
		"local_offer", "local_parking", "local_pharmacy", "local_phone", "local_pizza",
		"local_play", "local_post_office", "local_printshop", "local_see", "local_shipping",
		"local_taxi", "map", "my_location", "navigation", "near_me", "person_pin",
		"person_pin_circle", "pin_drop", "place", "rate_review", "restaurant", "restaurant_menu",
		"satellite", "store_mall_directory", "streetview", "subway", "terrain", "traffic",
		"train", "tram", "transfer_within_a_station", "zoom_out_map"
	};

	static readonly Dictionary<string, string[]> IconCategories = new()
	{
		["Recently Used"] = Array.Empty<string>(),
		["Favorites"] = Array.Empty<string>(),
		["Main"] = MainIcons,
		["All Icons"] = MainIcons
			.Concat( ActionIcons )
			.Concat( NavigationIcons )
			.Concat( ContentIcons )
			.Concat( CommunicationIcons )
			.Concat( EditorIcons )
			.Concat( FileIcons )
			.Concat( HardwareIcons )
			.Concat( SocialIcons )
			.Concat( ToggleIcons )
			.Concat( MapsIcons )
			.Distinct()
			.OrderBy( x => x )
			.ToArray()
	};

	// Emoji categories are loaded from Data/emojis/emojis.json at runtime.
	// This avoids embedding large emoji literals in source files which can be mangled by file encoding.
}


using System.Collections.Generic;
using System.Linq;

namespace Editor;

/// <summary>
/// A widget for picking Material Icons with categories and recently used tracking.
/// </summary>
[Icon( "search" )]
sealed class IconPickerWidget : Widget
{
    public Action<string> ValueChanged { get; set; }
    public int IconsPerPage = 56;
    public int IconsPerRow = 7;

    const string RecentlyUsedCookieKey = "IconPicker.RecentlyUsed";
    const int MaxRecentIcons = 16;

    string _currentCategory = "Main";

    string _icon;
    public string Icon
    {
        get => _icon;
        set
        {
            _icon = value;
            AddToRecentlyUsed( _icon );
            Rebuild();
            ValueChanged?.Invoke( _icon );
        }
    }

    int CurrentPage = 0;
    Label HeaderLabel;
    Label CategoryLabel;
    GridLayout ContentLayout;
    Layout CategoryLayout;

    public IconPickerWidget( Widget parent = null ) : base( parent )
    {
        Layout = Layout.Row();
        FocusMode = FocusMode.Click;

        // Left sidebar for categories
        var sidebarLayout = Layout.Column();
        sidebarLayout.Margin = 4;
        sidebarLayout.Spacing = 2;
        Layout.Add( sidebarLayout );

        CategoryLayout = sidebarLayout;
        BuildCategorySidebar();

        // Separator
        var separator = new Widget( this );
        separator.FixedWidth = 1;
        separator.MinimumHeight = 100;
        separator.OnPaintOverride = () =>
        {
            Paint.ClearPen();
            Paint.SetBrush( Theme.ControlBackground.Lighten( 0.1f ) );
            Paint.DrawRect( separator.LocalRect );
            return true;
        };
        Layout.Add( separator );

        // Right content area
        var contentArea = Layout.Column();
        contentArea.Margin = 4;
        Layout.Add( contentArea );

        // Category header
        CategoryLabel = contentArea.Add( new Label( this ) );
        CategoryLabel.Alignment = TextFlag.Left;
        CategoryLabel.SetStyles( "font-weight: bold; font-size: 11px; color: #888;" );

        // Page navigation
        var headerLayout = Layout.Row();
        headerLayout.Margin = 4;
        headerLayout.Spacing = 2;
        contentArea.Add( headerLayout );

        var buttonLeft = headerLayout.Add( new IconButton( "chevron_left", ButtonLeft, this ) );
        buttonLeft.ToolTip = "Previous Page";

        HeaderLabel = headerLayout.Add( new Label( this ) );
        HeaderLabel.Alignment = TextFlag.Center;

        var buttonRight = headerLayout.Add( new IconButton( "chevron_right", ButtonRight, this ) );
        buttonRight.ToolTip = "Next Page";

        // Icon grid
        ContentLayout = Layout.Grid();
        ContentLayout.Margin = 4;
        ContentLayout.Spacing = 4;
        ContentLayout.Alignment = TextFlag.LeftTop;
        contentArea.Add( ContentLayout );

        contentArea.AddStretchCell( 1 );

        Rebuild();
    }

    void BuildCategorySidebar()
    {
        CategoryLayout.Clear( true );

        foreach ( var category in IconCategories.Keys )
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
        CurrentPage = 0;
        BuildCategorySidebar();
        Rebuild();
    }

    (string Icon, string Name) GetCategoryInfo( string category )
    {
        return category switch
        {
            "Recently Used" => ("history", "Recently Used"),
            "Main" => ("star", "Main Icons"),
            "Action" => ("touch_app", "Action"),
            "Navigation" => ("explore", "Navigation"),
            "Content" => ("content_copy", "Content"),
            "Communication" => ("chat", "Communication"),
            "Editor" => ("edit", "Editor"),
            "File" => ("folder", "Files & Folders"),
            "Hardware" => ("devices", "Hardware"),
            "Social" => ("people", "Social"),
            "Toggle" => ("toggle_on", "Toggle"),
            "Maps" => ("map", "Maps"),
            "All Icons" => ("apps", "All Icons"),
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
            var iconButton = new IconButton( icon );
            iconButton.IconSize = 18;
            iconButton.FixedSize = 26;
            iconButton.ToolTip = icon;
            iconButton.OnClick = () => Icon = icon;

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

            ContentLayout.AddCell( x, y, iconButton );
            x++;
            if ( x >= IconsPerRow )
            {
                x = 0;
                y++;
            }
        }

        Update();
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
        if ( _currentCategory == "Recently Used" )
        {
            return GetRecentlyUsedIcons();
        }

        if ( IconCategories.TryGetValue( _currentCategory, out var icons ) )
        {
            return icons;
        }

        return MainIcons;
    }

    int GetTotalPages()
    {
        var icons = GetAvailableIconNames();
        if ( icons.Length == 0 ) return 1;
        return (int)MathF.Ceiling( icons.Length / (float)IconsPerPage );
    }

    static string[] GetRecentlyUsedIcons()
    {
        return EditorCookie.Get( RecentlyUsedCookieKey, Array.Empty<string>() );
    }

    static void AddToRecentlyUsed( string icon )
    {
        if ( string.IsNullOrEmpty( icon ) ) return;

        var recent = GetRecentlyUsedIcons().ToList();
        recent.Remove( icon );
        recent.Insert( 0, icon );

        if ( recent.Count > MaxRecentIcons )
            recent = recent.Take( MaxRecentIcons ).ToList();

        EditorCookie.Set( RecentlyUsedCookieKey, recent.ToArray() );
    }

    /// <summary>
    /// Open an Icon Picker popup
    /// </summary>
    public static void OpenPopup( Widget parent, string icon, Action<string> onChange )
    {
        var popup = new PopupWidget( parent );
        popup.Visible = false;
        popup.FixedWidth = 280;
        popup.Layout = Layout.Column();
        popup.Layout.Margin = 4;

        var editor = popup.Layout.Add( new IconPickerWidget( popup ), 1 );

        // Start with recently used if available, otherwise main
        var recentIcons = GetRecentlyUsedIcons();
        editor._currentCategory = recentIcons.Length > 0 ? "Recently Used" : "Main";
        editor.BuildCategorySidebar();

        // Find which category contains the current icon and navigate to it
        if ( !string.IsNullOrEmpty( icon ) )
        {
            foreach ( var kvp in IconCategories )
            {
                var idx = Array.IndexOf( kvp.Value, icon );
                if ( idx >= 0 )
                {
                    editor._currentCategory = kvp.Key;
                    editor.CurrentPage = idx / editor.IconsPerPage;
                    editor.BuildCategorySidebar();
                    break;
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
        "folder", "folder_open", "home", "settings", "search", "add", "remove", "edit", "delete",
        "save", "close", "check", "clear", "refresh", "sync", "undo", "redo", "content_copy", "content_paste",
        "content_cut", "visibility", "visibility_off", "lock", "lock_open", "star", "star_border",
        "favorite", "favorite_border", "bookmark", "bookmark_border", "label", "info", "warning",
        "error", "help", "help_outline", "lightbulb", "code", "terminal", "bug_report", "build",
        "extension", "widgets", "view_module", "grid_view", "list", "apps", "menu", "more_vert",
        "more_horiz", "arrow_back", "arrow_forward", "arrow_upward", "arrow_downward", "expand_more",
        "expand_less", "chevron_left", "chevron_right", "first_page", "last_page", "play_arrow",
        "pause", "stop", "skip_next", "skip_previous", "fast_forward", "fast_rewind", "loop",
        "shuffle", "volume_up", "volume_down", "volume_mute", "volume_off"
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
		["Main"] = MainIcons,
		["Action"] = ActionIcons,
		["Navigation"] = NavigationIcons,
		["Content"] = ContentIcons,
		["Communication"] = CommunicationIcons,
		["Editor"] = EditorIcons,
		["File"] = FileIcons,
		["Hardware"] = HardwareIcons,
		["Social"] = SocialIcons,
		["Toggle"] = ToggleIcons,
		["Maps"] = MapsIcons,
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
}

namespace Editor.Preferences;

internal class EditorPreferencesWindow : BaseWindow
{
	NavigationView container;

	[Menu( "Editor", "Edit/Preferences", "room_preferences", Priority = -1 )]
	public static EditorPreferencesWindow OpenEditorPreferences()
	{
		var prefs = new EditorPreferencesWindow();
		prefs.Show();
		return prefs;
	}

	internal void SwitchPage<T>() where T : Widget => container.SwitchPage<T>();

	public EditorPreferencesWindow()
	{
		SetModal( true, true );
		Size = new Vector2( 740, 440 );
		MinimumSize = Size;
		TranslucentBackground = true;
		NoSystemBackground = true;

		WindowTitle = "Editor Settings";
		SetWindowIcon( "room_preferences" );

		Layout = Layout.Column();
		Layout.Margin = 4;
		Layout.Spacing = 4;

		container = new NavigationView( this );
		Layout.Add( container );

		container.AddSectionHeader( "Editor" );

		container.AddPage( "General", "tune", new PageGeneral( this ) );
		container.AddPage( "Notifications", "notifications", new PageNotifications( this ) );
		container.AddPage( "Scene View", "videocam", new PageSceneView( this ) );
		container.AddPage( "Editor Keybinds", "keyboard", new PageKeybinds( this ) );
		container.AddPage( "Networking", "wifi", new PageNetworking( this ) );

		EditorEvent.Run( "editor.preferences", container );
	}
}

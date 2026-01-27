using NativeEngine;

namespace Editor;

public class EditorMainWindow : DockWindow
{
	internal static EditorMainWindow Current;

	Menu FileMenu { get; init; }
	public Menu AppsMenu { get; init; }
	public Menu ViewsMenu { get; init; }
	public Menu GameMenu { get; init; }
	Menu RecentScenesMenu { get; init; }
	Menu EditMenu { get; init; }

	internal ConsoleWidget Console => ConsoleWidget.Instance;

	protected override bool OnClose()
	{
		// Check the editor scenes for one with unsaved changes
		if ( GetUnsavedScenes().Any() || GetUnsavedResources().Any() )
		{
			ShowCloseDialog();
			return false;
		}

		ProjectCookie?.Set( $"gizmo.settings", EditorScene.GizmoSettings );

		return true;
	}

	private static List<SceneEditorSession> GetUnsavedScenes()
	{
		return SceneEditorSession.All.Where( x => x.HasUnsavedChanges ).ToList();
	}

	private static List<GameResource> GetUnsavedResources()
	{
		return ResourceLibrary.GetAll<GameResource>().Where( x =>
		{
			var asset = AssetSystem.FindByPath( x.ResourcePath );
			var compiledFile = asset?.GetCompiledFile( true );
			var isCloud = compiledFile != null && compiledFile.Contains( ".sbox/cloud/" );
			return x.HasUnsavedChanges && !isCloud;
		}
		).ToList();
	}

	public void ShowCloseDialog()
	{
		var popup = new PopupDialogWidget( "💾" );
		var saveableScenes = GetUnsavedScenes();
		var saveableResources = GetUnsavedResources();
		popup.FixedWidth = 512;
		popup.WindowTitle = "Unsaved Changes";
		popup.MessageLabel.Text = $"Do you want to save the changes you made?";
		if ( saveableScenes.Any() )
		{
			popup.MessageLabel.Text += $"\n\nScenes:\n\n{string.Join( "\n", saveableScenes.Select( x => x.Scene.Name ) )}";
		}
		if ( saveableResources.Any() )
		{
			popup.MessageLabel.Text += $"\n\nResources:\n\n{string.Join( "\n", saveableResources.Select( x => x.ResourceName ) )}";
		}

		popup.ButtonLayout.Spacing = 4;
		popup.ButtonLayout.AddStretchCell();
		popup.ButtonLayout.Add( new Button( "Save" )
		{
			Clicked = () =>
			{
				// Save all the scenes
				saveableScenes.ForEach( x => x.Save( false ) );
				saveableResources.ForEach( x =>
				{
					var asset = AssetSystem.FindByPath( x.ResourcePath );
					asset.SaveToDisk( x );
					asset.Compile( false );
				} );

				popup.Destroy();
				Destroy();

				foreach ( var session in saveableScenes )
				{
					session.Destroy();
				}
			}
		} );

		popup.ButtonLayout.Add( new Button( "Don't Save" ) { Clicked = () => { Destroy(); popup.Destroy(); } } );
		popup.ButtonLayout.Add( new Button( "Cancel" ) { Clicked = () => { EditorMainWindow.showLauncherOnExit = false; popup.Destroy(); } } );

		popup.SetModal( true, true );
		popup.Hide();
		popup.Show();
	}

	static bool isEngineLoggingVerbose;

	private Option save;
	private Option saveAs;
	private Option saveAll;
	private Option discard;
	private Option undoOption;
	private Option redoOption;

	internal EditorMainWindow()
	{
		Current = this;
		Visible = false;
		Enabled = false;
		WindowTitle = "s&box editor";
		DeleteOnClose = true;
		FullScreenManager = new();
		DockManager.OnLayoutLoaded += OnDockLayoutLoaded;

		{
			FileMenu = MenuBar.AddMenu( "File" );
			FileMenu.AddOption( "New Scene", "note_add", EditorScene.NewScene, "editor.new" );
			FileMenu.AddOption( "Open", "file_open", EditorScene.Open, "editor.open" );
			RecentScenesMenu = FileMenu.AddMenu( "Open Recent", "restore" );
			RecentScenesMenu.AboutToShow += BuildRecentScenes;
			FileMenu.AddSeparator();
			save = FileMenu.AddOption( "Save", "save", EditorScene.SaveSession, "editor.save" );
			saveAs = FileMenu.AddOption( "Save As..", "save_as", EditorScene.SaveSessionAs, "editor.save-as" );
			saveAll = FileMenu.AddOption( "Save All", null, EditorScene.SaveAllSessions, "editor.save-all" );
			discard = FileMenu.AddOption( "Discard Changes", "auto_delete", EditorScene.Discard );
			FileMenu.AddSeparator();
			FileMenu.AddOption( "Close Project", "disabled_by_default", () => { showLauncherOnExit = true; Quit(); } );
			FileMenu.AddOption( "Quit", "logout", Quit, "editor.quit" );
			FileMenu.AboutToShow += OnFileMenuAboutToShow;
		}

		{
			EditMenu = MenuBar.AddMenu( "Edit" );

			undoOption = EditMenu.AddOption( "Undo", "undo", Undo, "editor.undo" );
			redoOption = EditMenu.AddOption( "Redo", "redo", Redo, "editor.redo" );

			EditMenu.AddSeparator();
			EditMenu.AddOption( "Cut", "cut", EditorScene.Cut, "editor.cut" );
			EditMenu.AddOption( "Copy", "copy", EditorScene.Copy, "editor.copy" );
			EditMenu.AddOption( "Paste", "paste", EditorScene.Paste, "editor.paste" );
			EditMenu.AddOption( "Paste As Child", null, EditorScene.PasteAsChild, "editor.paste-as-child" );
			EditMenu.AboutToShow += OnEditMenuAboutToShow;
		}

		{
			ViewsMenu = MenuBar.AddMenu( "View" );
			ViewsMenu.AboutToShow += OnViewsMenuAboutToShow;
		}

		{
			var projectMenu = MenuBar.AddMenu( "Project" );
			projectMenu.AddOption( "Play", "play_arrow", EditorScene.TogglePlay, "editor.toggle-play" );

			projectMenu.AddOption( new Option()
			{
				Checkable = true,
				Checked = EditorScene.PlayMode,
				Toggled = ( b ) => EditorScene.PlayMode = b,
				Text = "Play in Game Mode",
				Icon = "sports_esports"
			} );

			projectMenu.AddSeparator();
			projectMenu.AddOption( "Open Project Folder", "folder", () => EditorUtility.OpenFolder( Project.Current.GetRootPath() ) );
			projectMenu.AddOption( "Open Solution", "integration_instructions", OpenSolution, "editor.open-solution" );
		}

		{
			MenuBar.AddMenu( "Scene" );
			AppsMenu = MenuBar.AddMenu( "Tools" );
		}

		{
			MenuBar.AddMenu( "Settings" );
			var debug = MenuBar.AddMenu( "Debug" );

			debug.AddOption( "Widget Debugger", null, () => g_pBindSystemGlobalHotkeys.Cmd_ShowWidgetDebugger() );
			debug.AddOption( "Input Debugger", null, () => g_pBindSystemGlobalHotkeys.Cmd_ShowInputDebugger() );

			debug.AddSeparator();

			var help = MenuBar.AddMenu( "Help" );

			help.AddOption( "Open Log Folder", "source", () => EditorUtility.OpenFolder( FileSystem.Root.GetFullPath( "/logs/" ) ) );
			help.AddOption( "Developer Documentation", "article", () => EditorUtility.OpenFolder( "https://sbox.game/dev/" ) );
			help.AddOption( "Report a Bug", "bug_report", () => EditorUtility.OpenFolder( "https://github.com/Facepunch/sbox-public/issues" ) );

			help.AddSeparator();
			help.AddOption( "About s&box editor", "info", () =>
			{
				var aboutWidget = new AboutWidget();
				aboutWidget.SetModal( true, true );
				aboutWidget.Show();
			} );

			help.AddSeparator();

			var di = DisplayInfo.ForEnumValues<LogLevel>();
			var rootRule = Logging.GetDefaultLevel();

			{
				var o = help.AddOption( "Trace Logging" );
				o.Checkable = true;
				o.FetchCheckedState = () => Logging.GetDefaultLevel() == LogLevel.Trace;
				o.Toggled = ( b ) =>
				{
					Logging.SetRule( "*", b ? LogLevel.Trace : LogLevel.Info );
					EditorCookie.Set( "DefaultLoggingLevel", b ? LogLevel.Trace : LogLevel.Info );
				};
			}

			{
				var o = help.AddOption( "Verbose Engine Logging" );
				o.Checkable = true;
				o.FetchCheckedState = () => isEngineLoggingVerbose;
				o.Toggled = ( b ) =>
				{
					EngineGlue.SetEngineLoggingVerbose( b );
					isEngineLoggingVerbose = b;
				};
			}

			{
				var o = help.AddOption( "Verbose Hotload Logging" );
				o.Checkable = true;
				o.FetchCheckedState = () => HotloadManager.hotload_log == 2;
				o.Toggled = ( b ) =>
				{
					HotloadManager.hotload_log = b ? 2 : 0;
				};
			}
		}

		EditorWindow = this;
	}

	[Shortcut( "editor.open-solution", "CTRL+P", ShortcutType.Window )]
	void OpenSolution()
	{
		CodeEditor.OpenSolution();
	}

	[Shortcut( "editor.undo", "CTRL+Z", ShortcutType.Window )]
	static void Undo()
	{
		using ( SceneEditorSession.Scope() )
		{
			if ( SceneEditorSession.Active.IsUndoScopeOpen )
			{
				if ( EditorPreferences.UndoSounds )
				{
					EditorUtility.PlayRawSound( "sounds/editor/fail.wav" );
				}

				return;
			}
			SceneEditorSession.Active.UndoSystem.Undo();
		}
	}

	[Shortcut( "editor.redo", "CTRL+Y", ShortcutType.Window )]
	static void Redo()
	{
		using ( SceneEditorSession.Scope() )
		{
			if ( SceneEditorSession.Active.IsUndoScopeOpen )
			{
				if ( EditorPreferences.UndoSounds )
				{
					EditorUtility.PlayRawSound( "sounds/editor/fail.wav" );
				}

				return;
			}
			SceneEditorSession.Active.UndoSystem.Redo();
		}
	}

	[Shortcut( "editor.quit", "CTRL+Q", ShortcutType.Window )]
	static void Quit()
	{
		Current?.Close();
	}

	[Shortcut( "editor.video", "F6", ShortcutType.Window )]
	static void ToggleVideo()
	{
		if ( Game.IsPlaying ) return;
		ConVarSystem.Run( "video" );
	}

	protected override void OnPaint()
	{
		if ( Game.IsPlaying )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.Overlay );
			Paint.DrawRect( LocalRect );
			return;
		}

		base.OnPaint();
	}

	internal void OnStartupLoadingFinished()
	{
		// Load gizmo settings
		EditorScene.RestoreState();

		// Register our menu bar and dock options, doesn't open anything
		MenuBar.RegisterNamed( "Editor", MenuBar );
		DockAttribute.RegisterWindow( "Editor", this );

		// This will attempt to restore the last used layout (or default layout if first time)
		// Which means it will create dock widgets and move them around
		// This also involves creating SceneDocks which open scenes
		StateCookie = "SboxSceneEditor";

		// fucking horrible
		string geometryCookie = EditorCookie.GetString( $"Window.{StateCookie}.Geometry", null );
		if ( geometryCookie is null )
		{
			// no saved geometry, so default to center
			Center();
		}

		EditorEvent.Run( "editor.created", this );

		RebuildApps();

		SetVisible( true );

		// Register the main editor window as an SDL window and tell the input system it's the main window
		// We need this for focusing and relative mouse capture mode
		NativeEngine.InputSystem.RegisterWindowWithSDL( _widget.winId() );
		NativeEngine.InputSystem.SetEditorMainWindow( _widget.winId() );
	}

	record struct LayoutFile( string Name, string Json );

	protected override void RestoreDefaultDockLayout()
	{
		var layout = FileSystem.Config.ReadJsonOrDefault<LayoutFile>( $"/editor/layout/default.json", default );
		if ( layout.Name is null ) return;
		if ( layout.Json is null ) return;

		DockManager.State = layout.Json;
	}

	/// <summary>
	/// Called when the layout is loaded. We want to force all the scene views to be visible!
	/// </summary>
	void OnDockLayoutLoaded()
	{
		SceneEditorSession.OnEditorWindowRestoreLayout();
	}

	/// <summary>
	/// Called when the console key is pressed while the game is focused. Should
	/// do everything possible to switch to the actual console.
	/// </summary>
	internal void ConsoleFocus()
	{
		// focus the editor window instead of the game window
		EditorWindow.Blur();
		EditorWindow.Focus( true );

		// focus the console input if it's visible. The console key
		// is used to switch between game and editor too, so don't be
		// heavy handed by forcing the console to be visible etc.
		if ( Console?.Visible ?? false )
		{
			Console.Input.Focus();
		}
	}

	internal static bool showLauncherOnExit = false;
	public override void OnDestroyed()
	{
		// Unsubscribe from events
		if ( DockManager != null ) DockManager.OnLayoutLoaded -= OnDockLayoutLoaded;
		if ( RecentScenesMenu != null ) RecentScenesMenu.AboutToShow -= BuildRecentScenes;
		if ( FileMenu != null ) FileMenu.AboutToShow -= OnFileMenuAboutToShow;
		if ( ViewsMenu != null ) ViewsMenu.AboutToShow -= OnViewsMenuAboutToShow;
		if ( EditMenu != null ) EditMenu.AboutToShow -= OnEditMenuAboutToShow;

		base.OnDestroyed();

		if ( Sandbox.Internal.GlobalToolsNamespace.EditorWindow != this )
			return;

		Sandbox.Internal.GlobalToolsNamespace.EditorWindow = null;
		EditorUtility.Quit( showLauncherOnExit );
	}

	/// <summary>
	/// Called once to create the editor
	/// </summary>
	internal void Startup()
	{
		Size = new Vector2( 1920, 1080 );

		g_pToolFramework2.SetStallMonitorMainThreadWindow( _widget );

		OnStartupLoadingFinished();
	}

	[Event( "refresh" )]
	void RebuildApps()
	{
		AppsMenu?.Clear();

		foreach ( var tool in EngineTools.All )
		{
			var option = AppsMenu.AddOption( tool.Name, tool.Icon, () => EngineTools.ShowTool( tool.Name ) );
			option.StatusTip = tool.Description;
			option.ToolTip = $"{tool.Name} - {tool.Description}";
		}

		AppsMenu.AddSeparator();

		foreach ( var tool in EditorTypeLibrary.GetAttributes<EditorAppAttribute>().OrderBy( x => x.Title ) )
		{
			var option = AppsMenu.AddOption( tool.Title, tool.Icon, () => tool.Open() );
			option.StatusTip = tool.Description;
			option.ToolTip = $"{tool.Title} - {tool.Description}";
		}

		// Force a repaint
		Update();
	}

	FullScreenManager FullScreenManager { get; set; }

	/// <summary>
	/// Is a widget currently the fullscreen widget
	/// </summary>
	public bool IsFullscreen( Widget widget )
	{
		return FullScreenManager.Widget == widget;
	}

	/// <summary>
	/// Sets a widget as the fullscreen widget
	/// </summary>
	/// <param name="widget"></param>
	/// <returns>whether or not the widget is now fullscreen</returns>
	public bool SetFullscreen( Widget widget )
	{
		if ( FullScreenManager.Widget == widget || !widget.IsValid() )
		{
			FullScreenManager.Clear();
			return false;
		}

		FullScreenManager.SetWidget( widget );

		return FullScreenManager.Widget == widget;
	}

	[Event( "asset.selected" )]
	public void OnAssetSelected( Asset asset )
	{
		// maybe an option for this if people bitch about it
		EditorUtility.PlayAssetSound( asset );
	}

	public void SetVisible( bool visible )
	{
		if ( visible )
		{
			EditorWindow.Enabled = true;
			EditorWindow.Visible = true;
			EditorWindow.Focus();
		}
		else
		{
			EditorWindow.Enabled = false;
			EditorWindow.Visible = false;
		}

		Update();
	}

	private void OnFileMenuAboutToShow()
	{
		save.Enabled = SceneEditorSession.Active?.HasUnsavedChanges ?? false;
		saveAs.Enabled = SceneEditorSession.Active is not null;
		saveAll.Enabled = SceneEditorSession.All.Any();
		discard.Enabled = SceneEditorSession.Active?.HasUnsavedChanges ?? false;
	}

	private void OnEditMenuAboutToShow()
	{
		UpdateEditMenu( undoOption, redoOption );
	}

	private void OnViewsMenuAboutToShow()
	{
		CreateDynamicViewMenu( ViewsMenu );
	}

	public void UpdateEditorTitle( string title )
	{
		var projectName = Project.Current?.Config.Title ?? "No Project";
		Title = $"{title} - {projectName} - s&box editor{(Global.IsApiConnected ? "" : " - offline")}";
	}

	void BuildRecentScenes()
	{
		RecentScenesMenu.Clear();

		var recentScenes = AssetSystem.All
			.Where( x => x.LastOpened is not null )
			.Where( x => x.AssetType.FileExtension == "scene" || x.AssetType.FileExtension == "prefab" )
			.OrderByDescending( x => x.LastOpened )
			.Take( 20 );

		foreach ( var asset in recentScenes )
		{
			var attribute = EditorTypeLibrary.GetAttributes<AssetTypeAttribute>().Where( x => x.Extension == asset.AssetType.FileExtension ).FirstOrDefault();
			RecentScenesMenu.AddOption( $"{asset.Name} ({asset.Path})", string.Empty, () => { asset.OpenInEditor(); } );
		}
	}

	/// <summary>
	/// Updates Undo/Redo states and text
	/// </summary>
	void UpdateEditMenu( Option undoOption, Option redoOption )
	{
		if ( SceneEditorSession.Active?.UndoSystem.Back.TryPeek( out var undoEntry ) ?? false )
		{
			undoOption.Enabled = true;
			undoOption.Text = $"Undo {undoEntry.Name}";
		}
		else
		{
			undoOption.Enabled = false;
			undoOption.Text = "Undo";
		}

		if ( SceneEditorSession.Active?.UndoSystem.Forward.TryPeek( out var redoEntry ) ?? false )
		{
			redoOption.Enabled = true;
			redoOption.Text = $"Redo {redoEntry.Name}";
		}
		else
		{
			redoOption.Enabled = false;
			redoOption.Text = "Redo";
		}
	}
}

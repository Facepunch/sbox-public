namespace Sandbox.PanelGallery;

/// <summary>
/// A mock scene editor built entirely out of panels - custom title bar, menus, a tree, a few
/// thousand rows of data, an inspector and a console. This is the thing that tells us whether the
/// panel system is up to being the editor.
/// </summary>
public class EditorWindow : Panel
{
	/// <summary>
	/// The class that turns the light theme on. The light rules in gallery.scss are scoped under it.
	/// </summary>
	public const string LightModeClass = "editor-light-mode";

	readonly PanelWindow Window;

	Panel hierarchyPane;
	Panel inspectorPane;
	ContentTabs content;
	Hierarchy hierarchy;
	Inspector inspector;

	Sandbox.UI.Label statusSelection;
	Sandbox.UI.Label statusCounts;
	Sandbox.UI.Label statusFps;
	Sandbox.UI.Label statusInfo;
	Sandbox.UI.Label statusWarn;
	Sandbox.UI.Label statusError;

	Sandbox.UI.Label maximizeIcon;
	Sandbox.UI.Label windowTitle;
	Sandbox.UI.Label themeIcon;
	bool lightMode;

	/// <summary>
	/// The mock editor in a window of its own.
	/// </summary>
	public static PanelWindow Open()
	{
		var window = new PanelWindow( "Panel Gallery", new Vector2( 1500, 940 ), new Vector2( -1, -1 ), true );
		window.Root.AddChild( new EditorWindow( window ) );
		return window;
	}

	public EditorWindow( PanelWindow window )
	{
		Window = window;

		StyleSheet.Load( "/styles/gallery.scss" );

		BuildTitleBar();
		BuildBody();
		BuildStatusBar();

		ApplyTheme();
	}

	//
	// Title bar. The `window-drag` class is what tells the OS this bit moves the window - see
	// PanelWindow.HitTest. Buttons opt out with `window-nodrag`.
	//
	void BuildTitleBar()
	{
		var bar = Add.Panel( "titlebar window-drag" );

		bar.Add.Label( "s&", "brand" );

		// Menus live in the title bar, same as the editor's
		var menus = bar.AddChild( new Sandbox.UI.MenuBar() );
		menus.AddClass( "window-nodrag" );

		var file = menus.AddMenu( "File" );
		Option( file, "New Scene", "Ctrl+N" );
		Option( file, "Open Scene...", "Ctrl+O" );
		Option( file, "Save Scene", "Ctrl+S", () => SceneEditorSession.Active?.Save( false ) );
		Option( file, "Save Scene As...", "Ctrl+Shift+S", () => SceneEditorSession.Active?.Save( true ) );
		file.AddSeparator();
		Option( file, "Quit", "Alt+F4", Window.Dispose );

		var edit = menus.AddMenu( "Edit" );
		Option( edit, "Undo", "Ctrl+Z", () => SceneEditorSession.Active?.UndoSystem.Undo() );
		Option( edit, "Redo", "Ctrl+Y", () => SceneEditorSession.Active?.UndoSystem.Redo() );
		edit.AddSeparator();
		Option( edit, "Cut", "Ctrl+X" );
		Option( edit, "Copy", "Ctrl+C" );
		Option( edit, "Paste", "Ctrl+V" );
		edit.AddSeparator();
		Option( edit, "Delete", "Del", () => { if ( SceneEditorSession.Active is { } session ) DeleteSelected( session ); } );

		var view = menus.AddMenu( "View" );
		view.AddOption( "Hierarchy", on => hierarchyPane.Style.Display = on ? DisplayMode.Flex : DisplayMode.None ).Checked = true;
		view.AddOption( "Inspector", on => inspectorPane.Style.Display = on ? DisplayMode.Flex : DisplayMode.None ).Checked = true;
		Option( view, "Console" );
		Option( view, "Asset Browser" );
		view.AddSeparator();
		Option( view, "Full Screen", "F11", () => { Window.ToggleMaximized(); UpdateMaximizeIcon(); } );

		var game = menus.AddMenu( "Game" );
		Option( game, "Play", "F5" );
		Option( game, "Play From Here" );
		game.AddSeparator();
		Option( game, "Project Settings" );

		var scene = menus.AddMenu( "Scene" );
		Option( scene, "Create Object", null, CreateObject );
		Option( scene, "Create Camera" );
		Option( scene, "Create Light" );
		scene.AddSeparator();
		Option( scene, "Scene Settings" );

		var tools = menus.AddMenu( "Tools" );
		Option( tools, "Asset Browser" );
		Option( tools, "Shader Graph" );
		tools.AddSeparator();
		Option( tools, "Panel UI" );

		var help = menus.AddMenu( "Help" );
		Option( help, "Documentation" );
		Option( help, "About" );

		bar.Add.Panel( "grow" );

		// Centred over the bar, so the menus on the left don't push it off centre
		var titleWrap = bar.Add.Panel( "titlewrap" );
		windowTitle = titleWrap.Add.Label( "", "title" );

		var project = bar.Add.Panel( "project window-nodrag" );
		project.Add.Label( ProjectInitial, "chip" );
		project.Add.Label( ProjectTitle, "name" );

		themeIcon = WindowButton( bar, "light_mode", "theme", ToggleTheme );

		WindowButton( bar, "remove", null, Window.Minimize );
		maximizeIcon = WindowButton( bar, "crop_square", null, () =>
		{
			Window.ToggleMaximized();
			UpdateMaximizeIcon();
		} );
		WindowButton( bar, "close", "close", Window.Dispose );

		Add.Panel( "accentline" );

		UpdateTitle();
	}

	static string ProjectTitle => Project.Current?.Config?.Title ?? "No Project";
	static string ProjectInitial => ProjectTitle.Length > 0 ? ProjectTitle[..1].ToUpperInvariant() : "?";

	/// <summary>
	/// Scene name and editor, the way the editor titles its window.
	/// </summary>
	void UpdateTitle()
	{
		var session = SceneEditorSession.Active;
		var name = session?.Scene?.Name ?? "Untitled";
		var dirty = session?.HasUnsavedChanges == true ? "*" : "";

		windowTitle.Text = $"{name}{dirty} - s&box editor";
	}
	Sandbox.UI.Label WindowButton( Panel parent, string icon, string classname, Action onClick )
	{
		var button = parent.Clickable( "windowbutton window-nodrag", onClick );
		if ( classname is not null ) button.AddClass( classname );
		return button.Icon( icon );
	}

	/// <summary>
	/// Swap the palette. Both themes are in the sheet already - the light rules are scoped under
	/// one class on this panel, so flipping it just changes which rules match and every transition
	/// in the window animates the change.
	/// </summary>
	void ToggleTheme()
	{
		lightMode = !lightMode;
		ApplyTheme();
	}

	void ApplyTheme()
	{
		SetClass( LightModeClass, lightMode );

		themeIcon.Text = lightMode ? "dark_mode" : "light_mode";
		Window.BackgroundColor = lightMode ? Color.FromBytes( 238, 240, 246 ) : Color.FromBytes( 15, 17, 23 );
	}

	void UpdateMaximizeIcon()
	{
		maximizeIcon.Text = Window.IsMaximized ? "filter_none" : "crop_square";
	}

	/// <summary>
	/// A row with a shortcut shown beside it. Rows with nothing to do are just there to look at.
	/// </summary>
	static Sandbox.UI.Menu Option( Sandbox.UI.Menu menu, string text, string shortcut = null, Action action = null )
	{
		var option = menu.AddOption( text, action );
		option.Shortcut = shortcut;
		return option;
	}

	//
	// Shortcuts. The editor's own keys, doing the editor's own actions - so undo here is the same
	// undo stack the rest of the editor pushes to.
	//
	public override void OnButtonEvent( ButtonEvent e )
	{
		if ( e.Pressed && HandleShortcut( e ) )
		{
			e.StopPropagation = true;
			return;
		}

		base.OnButtonEvent( e );
	}

	bool HandleShortcut( ButtonEvent e )
	{
		if ( SceneEditorSession.Active is not { } session ) return false;

		if ( e.HasCtrl )
		{
			switch ( e.Button )
			{
				case "z": if ( e.HasShift ) session.UndoSystem.Redo(); else session.UndoSystem.Undo(); return true;
				case "y": session.UndoSystem.Redo(); return true;
				case "s": session.Save( e.HasShift ); return true;
				case "d": Duplicate( session ); return true;
			}

			return false;
		}

		switch ( e.Button )
		{
			case "delete": DeleteSelected( session ); return true;
			case "f": FrameSelection( session ); return true;
		}

		return false;
	}

	static void DeleteSelected( SceneEditorSession session )
	{
		var items = session.Selection.OfType<GameObject>().Where( x => x.IsValid() ).ToArray();
		if ( items.Length == 0 ) return;

		using ( session.UndoScope( items.Length > 1 ? $"Delete {items.Length} Objects" : $"Delete {items[0].Name}" ).WithGameObjectDestructions( items ).Push() )
		{
			foreach ( var item in items ) item.Destroy();
		}

		session.Selection.Clear();
	}

	static void Duplicate( SceneEditorSession session )
	{
		var items = session.Selection.OfType<GameObject>().Where( x => x.IsValid() ).ToArray();
		if ( items.Length == 0 ) return;

		var copies = new List<GameObject>();

		using ( session.UndoScope( "Duplicate" ).Push() )
		{
			foreach ( var item in items )
			{
				var copy = item.Clone();
				copy.Parent = item.Parent;
				copy.Name = item.Name;
				copies.Add( copy );
			}
		}

		session.Selection.Set( copies );
	}

	static void FrameSelection( SceneEditorSession session )
	{
		var items = session.Selection.OfType<GameObject>().Where( x => x.IsValid() ).ToArray();
		if ( items.Length == 0 ) return;

		var bounds = items[0].GetBounds();

		foreach ( var item in items.Skip( 1 ) )
		{
			bounds = bounds.AddBBox( item.GetBounds() );
		}

		session.FrameTo( bounds );
	}

	//
	// Three panes with draggable splitters
	//
	void BuildBody()
	{
		var body = Add.Panel( "body" );

		hierarchyPane = body.Add.Panel( "pane left" );
		hierarchy = BuildHierarchy( hierarchyPane );

		body.AddChild( new Splitter( hierarchyPane, 160, 460 ) );

		var centre = body.Add.Panel( "pane centre" );
		content = centre.AddChild( new ContentTabs() );
		content.OnPicked = item => hierarchy.Select( item );

		// The right hand splitter sits before the pane it resizes, so dragging left grows it
		var rightSplitter = body.AddChild( new Splitter( null, 240, 680 ) { Inverted = true } );

		inspectorPane = body.Add.Panel( "pane right" );
		rightSplitter.Target = inspectorPane;

		var inspectorHeader = inspectorPane.Add.Panel( "paneheader" );
		inspectorHeader.Icon( "tune" );
		inspectorHeader.Add.Label( "Inspector" );
		inspectorHeader.Add.Panel( "grow" );

		inspector = inspectorPane.AddChild( new Inspector() );
		inspector.Show( null );
	}

	Sandbox.UI.Label hierarchyCount;

	Hierarchy BuildHierarchy( Panel pane )
	{
		var header = pane.Add.Panel( "paneheader" );
		header.Icon( "list" );
		header.Add.Label( "Hierarchy" );
		header.Add.Panel( "grow" );
		hierarchyCount = header.Add.Label( "0", "badge" );

		var tree = pane.AddChild( new Hierarchy() );

		// The row of tools above the tree, same as the editor's
		var tools = pane.Add.Panel( "panetools" );
		pane.SetChildIndex( tools, 1 );

		tools.Clickable( "toolbutton", CreateObject ).Icon( "add" );

		var search = tools.AddChild( new TextInput( "Search", "search" ) );
		search.OnChange = value => tree.SetFilter( value );

		tree.OnSelected = OnSelected;
		return tree;
	}

	/// <summary>
	/// Make an empty object in the open scene, under whatever's selected.
	/// </summary>
	void CreateObject()
	{
		if ( SceneEditorSession.Active is not { } session ) return;
		if ( session.Scene is not { } activeScene ) return;

		using ( activeScene.Push() )
		{
			var item = new GameObject( true, "Object" );

			if ( session.Selection.FirstOrDefault() is GameObject parent && parent.IsValid() )
				item.Parent = parent;

			session.Selection.Set( item );
		}
	}

	void OnSelected( GameObject item )
	{
		inspector.Show( item );

		statusSelection.Text = item.IsValid()
			? $"{item.Name}  ({item.Components.Count} components)"
			: "Nothing selected";
	}

	//
	// Status bar
	//
	Panel graph;
	readonly List<Panel> graphBars = new();

	void BuildStatusBar()
	{
		var bar = Add.Panel( "statusbar" );

		bar.Add.Panel( "dot" );
		statusSelection = bar.Add.Label( "Nothing selected" );

		bar.Add.Panel( "grow" );

		graph = bar.Add.Panel( "graph" );
		for ( int i = 0; i < 28; i++ )
		{
			var graphBar = graph.Add.Panel( "bar" );
			graphBar.Style.Height = 3;
			graphBars.Add( graphBar );
		}

		bar.Add.Panel( "sep" );

		statusInfo = LogCount( bar, "comment", "info" );
		statusWarn = LogCount( bar, "warning", "warn" );
		statusError = LogCount( bar, "error", "error" );

		bar.Add.Panel( "sep" );
		statusCounts = bar.Add.Label( "", "mono" );
		bar.Add.Panel( "sep" );
		statusFps = bar.Add.Label( "", "mono" );
	}

	/// <summary>
	/// One of the message counters in the corner, same as the editor's.
	/// </summary>
	static Sandbox.UI.Label LogCount( Panel bar, string icon, string className )
	{
		var panel = bar.Add.Panel( "logcount" );
		panel.AddClass( className );
		panel.Icon( icon );
		return panel.Add.Label( "0", "mono" );
	}

	/// <summary>
	/// Push the frame time onto the little graph in the corner, so you can watch it breathe.
	/// </summary>
	void PushGraph( float milliseconds )
	{
		for ( int i = 0; i < graphBars.Count - 1; i++ )
		{
			graphBars[i].Style.Height = graphBars[i + 1].Style.Height;
			graphBars[i].Style.BackgroundColor = graphBars[i + 1].Style.BackgroundColor;
		}

		var last = graphBars[^1];
		var height = MathX.Clamp( milliseconds * 1.6f, 2.0f, 14.0f );

		last.Style.Height = height;
		last.Style.BackgroundColor = milliseconds > 8.0f ? Color.FromBytes( 244, 63, 94, 200 ) : Color.FromBytes( 34, 211, 238, 140 );
	}

	RealTimeSince timeSinceStats;
	RealTimeSince timeSinceGraph;
	float frameTime = 16.0f;

	public override void Tick()
	{
		frameTime = frameTime.LerpTo( RealTime.Delta * 1000.0f, 0.1f );

		if ( timeSinceGraph > 0.06f )
		{
			timeSinceGraph = 0;
			PushGraph( RealTime.Delta * 1000.0f );
		}

		if ( timeSinceStats > 0.25f )
		{
			timeSinceStats = 0;

			statusCounts.Text = $"{CountPanels( this ):n0} panels";
			hierarchyCount.Text = $"{Hierarchy.ActiveScene?.Children.Count ?? 0}";
			statusFps.Text = $"{frameTime:0.0} ms   {1000.0f / MathF.Max( frameTime, 0.01f ):0} fps";

			statusInfo.Text = $"{ConsolePanel.InfoCount:n0}";
			statusWarn.Text = $"{ConsolePanel.WarnCount:n0}";
			statusError.Text = $"{ConsolePanel.ErrorCount:n0}";

			UpdateTitle();

			UpdateMaximizeIcon();
		}
	}

	static int CountPanels( Panel panel )
	{
		var count = 1;

		for ( int i = 0; i < panel.ChildrenCount; i++ )
			count += CountPanels( panel.GetChild( i ) );

		return count;
	}
}

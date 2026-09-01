using Sandbox.UI;
using Sandbox.UI.Construct;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Sandbox.LauncherUI;

/// <summary>
/// The launcher, as panels. A sidebar carries the brand, the page navigation and the utility
/// links; the main column is the current page - projects or samples - with platform news in
/// a rail on the right.
/// </summary>
class LauncherWindow : Panel
{
	readonly Editor.PanelWindow Window;
	readonly Editor.ProjectList ProjectList = new();

	Panel content;
	Panel projectsPanel;
	Sandbox.UI.Label countLabel;

	enum Page { Projects, Samples }
	enum SortMethod { Date, Name, Org }

	Page page = Page.Projects;
	SortMethod sort = SortMethod.Date;
	string filter = "";
	Button sortButton;

	readonly Dictionary<Page, Panel> navItems = new();

	// Load stagger - every section label and row fades in, in turn
	int animIndex;

	// The first project in the list as currently filtered and sorted - what Enter opens
	Project topHit;

	TextEntry searchBox;
	bool searchFocused;

	// Projects with an editor open, and the row showing each project - see WatchRunningEditorsAsync
	HashSet<string> runningProjects = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<Panel, string> rowProjects = new();

	public LauncherWindow( Editor.PanelWindow window )
	{
		Window = window;

		AddClass( "editor-window" );
		SetClass( "style-light", LauncherPreferences.LightTheme );
		StyleSheet.Load( "/styles/editor.scss" );
		StyleSheet.Load( "/styles/launcher.scss" );

		AddBackdrop();
		BuildSidebar();

		var main = this.AddChild<Panel>();
		main.AddClass( "main" );

		BuildTitleBar( main );

		content = main.AddChild<Panel>();
		content.AddClass( "content" );

		ShowHome();

		// Dropping a .sbproj - or a folder holding one - adds it, same as the Add button.
		// While the drag hovers we answer whether we'd take it - the cursor shows it, and
		// the window lights up
		AddEventListener( "ondrop", e =>
		{
			if ( e is not DropEvent drop ) return;

			var accept = drop.Files.Any( CouldBeProject );
			SetClass( "dropping", accept && !drop.IsDrop );

			if ( !accept ) return;

			drop.Action = DropAction.Copy;

			if ( !drop.IsDrop ) return;

			foreach ( var file in drop.Files )
				AddProjectFromPath( file );
		} );

		AddEventListener( "ondragleave", e =>
		{
			if ( e is DropEvent ) SetClass( "dropping", false );
		} );

		_ = WatchRunningEditorsAsync();
	}

	/// <summary>
	/// Keep an eye on which projects have an editor open and mark their rows. The processes
	/// themselves are the source, so editors the launcher didn't start still light up.
	/// </summary>
	async Task WatchRunningEditorsAsync()
	{
		while ( this.IsValid() )
		{
			var running = await Task.RunInThreadAsync( RunningEditors.Scan );

			if ( !running.SetEquals( runningProjects ) )
			{
				runningProjects = running;

				foreach ( var (row, path) in rowProjects )
				{
					if ( row.IsValid() ) row.SetClass( "running", running.Contains( path ) );
				}
			}

			await Task.Delay( 2000 );
		}
	}

	Panel backdrop;

	/// <summary>
	/// The background - the latest news artwork once it arrives, softened so the UI stays
	/// readable. The window's ground colour shows until then.
	/// </summary>
	void AddBackdrop()
	{
		backdrop = this.AddChild<Panel>();
		backdrop.AddClass( "backdrop" );
	}

	//
	// Sidebar - brand, navigation, resources, settings
	//

	void BuildSidebar()
	{
		var sidebar = this.AddChild<Panel>();
		sidebar.AddClass( "sidebar window-drag" );

		BuildLockup( sidebar );

		navItems[Page.Projects] = NavItem( sidebar, "Projects", "sports_esports", () => SetPage( Page.Projects ) );
		navItems[Page.Samples] = NavItem( sidebar, "Samples", "school", () => SetPage( Page.Samples ) );

		SetNavActive();

		var resources = sidebar.AddChild<Panel>();
		resources.AddClass( "sectionlabel" );
		resources.Add.Label( "RESOURCES", "text" );
		resources.AddChild<Panel>().AddClass( "rule" );

		LinkItem( sidebar, "Documentation", "menu_book", "https://sbox.game/dev/doc/" );
		LinkItem( sidebar, Global.BackendTitle, "public", Global.BackendUrl );
		LinkItem( sidebar, "API Reference", "data_object", $"{Global.BackendUrl}/api" );

		var gameFolder = Environment.CurrentDirectory;
		LinkItem( sidebar, "Engine Folder", "folder", gameFolder );
		LinkItem( sidebar, "Logs", "receipt_long", $"{gameFolder}/logs" );

		sidebar.AddChild<Panel>().AddClass( "grow" );

		BuildCloseToggle( sidebar );

		sidebar.Add.Label( $"Version {Application.Version}", "version" );
	}

	/// <summary>
	/// The brand lockup: the marque, the wordmark, what this app is underneath.
	/// </summary>
	void BuildLockup( Panel sidebar )
	{
		var lockup = sidebar.AddChild<Panel>();
		lockup.AddClass( "lockup" );

		var brand = lockup.AddChild<Panel>();
		brand.AddClass( "brand" );

		var marque = brand.AddChild<Panel>();
		marque.AddClass( "marque" );
		marque.Add.Label( "s&" );

		brand.Add.Label( "box", "wordmark" );

		lockup.Add.Label( "EDITOR", "tagline" );
	}

	Panel NavItem( Panel sidebar, string title, string icon, Action onClick )
	{
		var item = sidebar.Add.Panel( "navitem window-nodrag" );
		item.AddEventListener( "onclick", onClick );
		Stagger( item );

		item.Add.Icon( icon, "icon" );
		item.Add.Label( title, "label" );

		return item;
	}

	void LinkItem( Panel sidebar, string title, string icon, string target )
	{
		var item = sidebar.Add.Panel( "navitem window-nodrag" );
		item.AddEventListener( "onclick", () => Editor.EditorUtility.OpenFolder( target ) );
		Stagger( item );

		item.Add.Icon( icon, "icon" );
		item.Add.Label( title, "label" );
		item.Add.Panel( "grow" );
		item.Add.Icon( "north_east", "icon external" );
	}

	void SetPage( Page newPage )
	{
		if ( page == newPage ) return;

		page = newPage;
		filter = "";
		SetNavActive();
		ShowHome();
	}

	void SetNavActive()
	{
		foreach ( var (p, item) in navItems )
		{
			var active = p == page;
			item.SetClass( "active", active );

			var bar = item.Children.FirstOrDefault( x => x.HasClass( "bar" ) );

			if ( active && bar is null )
				item.AddChild<Panel>().AddClass( "bar" );
			else if ( !active && bar is not null )
				bar.Delete( true );
		}
	}

	void BuildCloseToggle( Panel parent )
	{
		var toggle = parent.AddChild<SwitchControl>();
		toggle.AddClass( "window-nodrag" );

		toggle.Label = "Close On Launch";
		toggle.Value = LauncherPreferences.CloseOnLaunch;

		toggle.OnValueChanged = value =>
		{
			LauncherPreferences.CloseOnLaunch = value;
			LauncherPreferences.Save();
		};
	}

	//
	// Title bar - just the fps and the window buttons, the sidebar owns the brand
	//

	void BuildTitleBar( Panel main )
	{
		var bar = main.AddChild<Panel>();
		bar.AddClass( "titlebar window-drag" );

		fpsLabel = bar.Add.Label( "", "fps" );

		themeButton = WindowButton( bar, LauncherPreferences.LightTheme ? "dark_mode" : "light_mode", null, ToggleTheme );

		WindowButton( bar, "remove", null, Window.Minimize );

		if ( Window.CanMaximize )
			WindowButton( bar, "crop_square", null, Window.ToggleMaximized );

		WindowButton( bar, "close", "close", Window.Dispose );
	}

	Sandbox.UI.Label fpsLabel;
	int frameCount;
	readonly Stopwatch fpsTimer = Stopwatch.StartNew();

	/// <summary>
	/// Tick runs once per presented frame, so counting them is the fps.
	/// </summary>
	public override void Tick()
	{
		frameCount++;

		// The box is ready to type into the moment the window is up. Focusing can't happen in
		// the constructor - the panels aren't attached to the window's UI system yet
		if ( !searchFocused && searchBox.IsValid() )
		{
			searchFocused = true;
			searchBox.Focus();
		}

		if ( fpsTimer.ElapsedMilliseconds < 500 ) return;

		fpsLabel.Text = $"{frameCount * 1000 / fpsTimer.ElapsedMilliseconds} fps";
		frameCount = 0;
		fpsTimer.Restart();
	}

	Button themeButton;

	/// <summary>
	/// Swap between the dark and light themes - a class on the root, so the styles re-resolve
	/// in place.
	/// </summary>
	void ToggleTheme()
	{
		LauncherPreferences.LightTheme = !LauncherPreferences.LightTheme;
		LauncherPreferences.Save();

		SetClass( "style-light", LauncherPreferences.LightTheme );
		themeButton.Icon = LauncherPreferences.LightTheme ? "dark_mode" : "light_mode";
	}

	Button WindowButton( Panel bar, string icon, string className, Action onClick )
	{
		var button = bar.AddChild( new Button( null, icon, "windowbutton window-nodrag", onClick ) );
		if ( className is not null ) button.AddClass( className );
		return button;
	}

	//
	// The current page - header and the project list
	//

	void ShowHome()
	{
		content.DeleteChildren( true );
		animIndex = 0;

		var header = content.AddChild<Panel>();
		header.AddClass( "header" );

		header.Add.Label( page == Page.Samples ? "Samples" : "Projects", "pagetitle" );
		countLabel = header.Add.Label( "", "count" );

		header.AddChild<Panel>().AddClass( "grow" );

		var search = header.AddChild<TextEntry>();
		search.AddClass( "searchbox" );
		search.Placeholder = "Search";
		search.Icon = "search";
		search.OnTextEdited = value => { filter = value ?? ""; RefreshProjects(); };

		// The keyboard path is the fastest path - typing filters, Enter opens the top hit
		search.AddEventListener( "onsubmit", () => { if ( topHit is not null ) OpenProject( topHit ); } );
		searchBox = search;
		searchFocused = false;

		sortButton = header.AddChild( new Button( null, SortIcon(), "iconbutton", OpenSortMenu ) );

		header.AddChild( new Button( "Add", "folder_open", "flatbutton", () => _ = AddExistingProjectAsync() ) );
		header.AddChild( new Button( "New Project", "add", "primarybutton", ShowCreator ) );

		var body = content.AddChild<Panel>();
		body.AddClass( "body" );

		var projectColumn = body.AddChild<Panel>();
		projectColumn.AddClass( "project-column" );

		var projectLabel = projectColumn.AddChild<Panel>();
		projectLabel.AddClass( "sectionlabel" );
		projectLabel.Add.Label( page == Page.Samples ? "SAMPLES" : "LOCAL PROJECTS", "text" );
		projectLabel.AddChild<Panel>().AddClass( "rule" );

		projectsPanel = projectColumn.AddChild<Panel>();
		projectsPanel.AddClass( "projects" );

		_ = FillNewsAsync( body );

		RefreshProjects();
	}

	/// <summary>
	/// The rail down the right hand side - platform news, once the backend is up. It only
	/// exists at all when there's news to put in it, so offline costs no space.
	/// </summary>
	async Task FillNewsAsync( Panel body )
	{
		if ( newsCache is null )
		{
			await PanelAppSystem.ApiReady;

			try
			{
				newsCache = await Backend.News.GetPlatformNews( 4, 0 );
			}
			catch ( Exception )
			{
				// Offline is fine - there's just no rail
				return;
			}
		}

		var posts = newsCache;
		if ( !body.IsValid() || posts is null || posts.Length == 0 ) return;

		// The freshest artwork becomes the room's lighting
		var media = posts.FirstOrDefault( x => !string.IsNullOrEmpty( x.Media ) )?.Media;

		if ( media is not null && backdrop.IsValid() )
		{
			backdrop.Style.Set( "background-image", $"url( {media} )" );
			backdrop.AddClass( "visible" );
		}

		var rail = body.AddChild<Panel>();
		rail.AddClass( "rail" );

		var label = rail.AddChild<Panel>();
		label.AddClass( "sectionlabel" );
		label.Add.Label( "LATEST NEWS", "text" );
		label.AddChild<Panel>().AddClass( "rule" );

		var newsList = rail.AddChild<Panel>();
		newsList.AddClass( "news-list" );

		foreach ( var post in posts )
		{
			var url = post.Url;
			if ( url is not null && !url.StartsWith( "http" ) ) url = $"{Global.BackendUrl}{url}";
			if ( url is not null ) url += url.Contains( '?' ) ? "&utm_source=launcher" : "?utm_source=launcher";

			var card = newsList.Add.Panel( "newscard" );
			card.AddEventListener( "onclick", () => Editor.EditorUtility.OpenFolder( url ) );

			if ( !string.IsNullOrEmpty( post.Media ) )
			{
				var image = card.AddChild<Panel>();
				image.AddClass( "image" );
				image.Style.Set( "background-image", $"url( {post.Media} )" );
			}

			var text = card.AddChild<Panel>();
			text.AddClass( "text" );
			text.Add.Label( post.Title, "title" );
			text.Add.Label( RelativeTime( post.Created.LocalDateTime ), "date" );
		}
	}

	static Sandbox.Services.NewsPostDto[] newsCache;

	string SortIcon() => sort switch
	{
		SortMethod.Name => "sort_by_alpha",
		SortMethod.Org => "groups",
		_ => "calendar_month",
	};

	void OpenSortMenu()
	{
		PopupMenu.Open( Window, new PopupMenu.Item[]
		{
			new( "Most Recent", () => SetSort( SortMethod.Date ), "calendar_month" ),
			new( "Name", () => SetSort( SortMethod.Name ), "sort_by_alpha" ),
			new( "Organization", () => SetSort( SortMethod.Org ), "groups" ),
		}, sortButton );
	}

	void SetSort( SortMethod method )
	{
		sort = method;
		sortButton.Icon = SortIcon();
		RefreshProjects();
	}

	/// <summary>
	/// Rebuild the list for the current page - projects shows pinned then local, samples shows
	/// the sample projects that ship with the engine.
	/// </summary>
	void RefreshProjects()
	{
		if ( !projectsPanel.IsValid() ) return;

		projectsPanel.DeleteChildren( true );
		rowProjects.Clear();
		animIndex = 0;

		ProjectList.Refresh();

		var projects = ProjectList.GetAll().Where( x => !x.IsBuiltIn ).ToList();

		// Samples ride along without being saved to the list
		var samples = FindSamples();

		projects = sort switch
		{
			SortMethod.Name => projects.OrderBy( x => x.Config.Title ).ToList(),
			SortMethod.Org => projects.OrderBy( x => x.Package?.Org.Title ).ToList(),
			_ => projects.OrderByDescending( x => x.LastOpened ).ToList(),
		};

		if ( filter.Length > 0 )
		{
			bool Matches( Project x ) =>
				(x.Config.Title?.Contains( filter, StringComparison.OrdinalIgnoreCase ) ?? false) ||
				(x.Package?.Org.Title?.Contains( filter, StringComparison.OrdinalIgnoreCase ) ?? false);

			projects = projects.Where( Matches ).ToList();
			samples = samples.Where( Matches ).ToList();
		}

		var local = projects.Where( x => !samples.Any( y => x.ConfigFilePath == y.ConfigFilePath ) ).ToList();

		if ( page == Page.Samples )
		{
			countLabel.Text = samples.Count.ToString();
			topHit = samples.FirstOrDefault();

			AddGroup( null, samples );
		}
		else
		{
			countLabel.Text = local.Count.ToString();
			topHit = local.OrderByDescending( x => x.Pinned ).FirstOrDefault();

			AddGroup( "Pinned", local.Where( x => x.Pinned ) );
			AddGroup( null, local.Where( x => !x.Pinned ) );
		}

		if ( projectsPanel.ChildrenCount == 0 )
			AddEmptyState();
	}

	/// <summary>
	/// The sample projects that ship beside the engine. They are content rather than part of
	/// the saved list, and the launcher has to come up without them - an install that never
	/// got the content depot has no samples folder at all.
	/// </summary>
	List<Project> FindSamples()
	{
		var samples = new List<Project>();

		try
		{
			foreach ( var dir in System.IO.Directory.EnumerateDirectories( "samples/" ) )
			{
				var file = System.IO.Directory.EnumerateFiles( dir, "*.sbproj" ).FirstOrDefault();
				if ( file is null ) continue;

				var sample = ProjectList.TryAddFromFile( file );
				if ( sample is not null ) samples.Add( sample );
			}
		}
		catch ( Exception e )
		{
			Log.Info( $"Couldn't read the samples folder: {e.Message}" );
		}

		return samples;
	}

	void AddGroup( string title, IEnumerable<Project> projects )
	{
		var list = projects.ToList();
		if ( list.Count == 0 ) return;

		if ( title is not null )
		{
			var label = projectsPanel.AddChild<Panel>();
			label.AddClass( "sectionlabel" );
			label.Add.Label( title.ToUpperInvariant(), "text" );
			label.AddChild<Panel>().AddClass( "rule" );
			Stagger( label );
		}

		foreach ( var project in list )
		{
			BuildRow( project );
		}
	}

	void AddEmptyState()
	{
		var empty = projectsPanel.AddChild<Panel>();
		empty.AddClass( "empty" );
		empty.Add.Icon( "rocket_launch", "icon" );

		if ( filter.Length > 0 )
		{
			empty.Add.Label( $"Nothing matches \"{filter}\"", "title" );
			empty.Add.Label( "Try a different search.", "hint" );
		}
		else
		{
			empty.Add.Label( "No projects yet", "title" );
			empty.Add.Label( "Create one to get started.", "hint" );
		}
	}

	void Stagger( Panel panel )
	{
		// Invariant - this is CSS, not something anyone reads. Formatted in a comma-decimal
		// culture the delay would come back out as whole seconds
		panel.Style.Set( "animation-delay", FormattableString.Invariant( $"{animIndex * 0.025f:0.000}s" ) );
		animIndex++;
	}

	void BuildRow( Project project )
	{
		var row = projectsPanel.AddChild<Panel>();
		row.AddClass( "projectrow" );
		Stagger( row );

		// Opening is deliberate: the Open button, a double click, or Enter in search. A stray
		// click on the row does nothing, and right click is the context menu - two of them
		// in a row is two context menus, not a launch
		row.AddEventListener( "ondoubleclick", e =>
		{
			if ( e is MousePanelEvent mouse && mouse.Button != "mouseleft" ) return;
			OpenProject( project );
		} );
		row.AddEventListener( "onrightclick", () => OpenRowMenu( project ) );

		var title = project.Config.Title;

		var thumb = row.AddChild<Panel>();
		thumb.AddClass( "thumb" );
		thumb.Style.Set( "background-image", ChipGradient( project ) );

		var letter = thumb.Add.Label( string.IsNullOrEmpty( title ) ? "?" : title.Substring( 0, 1 ).ToUpperInvariant() );

		var text = row.AddChild<Panel>();
		text.AddClass( "text" );
		text.Add.Label( title, "name" );

		var org = project.Config.Org == "local" ? "Local" : project.Package?.Org.Title ?? project.Config.Org;
		text.Add.Label( $"{org} · {RelativeTime( project.LastOpened.LocalDateTime )}", "sub" );

		// Lit while an editor is open on this project - see WatchRunningEditorsAsync
		var runningTag = row.AddChild<Panel>();
		runningTag.AddClass( "runningtag" );
		runningTag.Add.Icon( "circle", "icon" );
		runningTag.Add.Label( "Running", "running-label" );

		var path = ProjectPath( project );
		rowProjects[row] = path;
		row.SetClass( "running", runningProjects.Contains( path ) );

		var stats = row.AddChild<Panel>();
		stats.AddClass( "statswrap" );

		_ = LoadPackageAsync( thumb, letter, stats, project );

		var modes = project.Config.GetMetaOrDefault( "ControlModes", new ControlModeSettings() );
		if ( modes.VR )
			row.Add.Panel( "badge" ).Add.Icon( "panorama_photosphere", "icon" );

		if ( project.Pinned )
			row.Add.Panel( "badge pin" ).Add.Icon( "push_pin", "icon" );

		row.AddChild( new Button( "Open", "play_arrow", "open", () => OpenProject( project ) ) );
	}

	// What the backend told us about each published project, for stats and the context menu
	readonly Dictionary<string, Package> packages = new();

	/// <summary>
	/// A published project has a life on the backend - a real thumbnail, ratings, players.
	/// Fill them into the row when they arrive.
	/// </summary>
	async Task LoadPackageAsync( Panel thumb, Panel letter, Panel stats, Project project )
	{
		if ( project.Config.Org is null or "local" ) return;

		var ident = $"{project.Config.Org}.{project.Config.Ident}";

		await PanelAppSystem.ApiReady;

		Package package;

		try
		{
			package = await Package.FetchAsync( ident, partial: true );
		}
		catch ( Exception )
		{
			return;
		}

		if ( package is null ) return;

		packages[ident] = package;

		if ( !string.IsNullOrEmpty( package.Thumb ) && thumb.IsValid() )
		{
			thumb.Style.Set( "background-image", $"url( {package.Thumb} )" );
			letter.Delete();
		}

		if ( !stats.IsValid() ) return;

		var votes = package.VotesUp + package.VotesDown;
		if ( votes > 0 )
			Stat( stats, "thumb_up", $"{package.VotesUp * 100 / votes}%", "rating" );

		if ( package.Usage.UsersNow > 0 )
			Stat( stats, "person", $"{Compact( package.Usage.UsersNow )} playing", "playing" );
		else if ( package.Usage.Total.Users > 0 )
			Stat( stats, "group", $"{Compact( package.Usage.Total.Users )} players", "players" );
	}

	static void Stat( Panel stats, string icon, string text, string className )
	{
		var stat = stats.AddChild<Panel>();
		stat.AddClass( $"stat {className}" );
		stat.Add.Icon( icon, "icon" );
		stat.Add.Label( text );
	}

	static string Compact( long value ) => value switch
	{
		>= 1_000_000 => $"{value / 1_000_000.0:0.#}m",
		>= 10_000 => $"{value / 1_000}k",
		>= 1_000 => $"{value / 1_000.0:0.#}k",
		_ => value.ToString(),
	};

	/// <summary>
	/// Every project gets a chip of its own - a gradient keyed off its identity, so the same
	/// project always wears the same colors.
	/// </summary>
	static string ChipGradient( Project project )
	{
		var hash = StableHash( project.Config.Ident ?? project.Config.Title ?? "?" );

		// Knuth spread - neighbouring hashes land far apart on the wheel
		var hue = (hash * 2654435761u) % 360;
		Color from = new ColorHsv( hue, 0.50f, 0.48f );
		Color to = new ColorHsv( (hue + 45) % 360, 0.60f, 0.28f );

		return $"linear-gradient( 45deg, {to.Hex} 0%, {from.Hex} 100% )";
	}

	/// <summary>
	/// FNV-1a. string.GetHashCode is randomized per run, and the chips shouldn't reshuffle
	/// every launch.
	/// </summary>
	static uint StableHash( string text )
	{
		var hash = 2166136261u;

		foreach ( var c in text )
		{
			hash ^= c;
			hash *= 16777619u;
		}

		return hash;
	}

	static string RelativeTime( DateTime time )
	{
		if ( time.Year <= 2000 ) return "never opened";

		var span = DateTime.Now - time;

		if ( span.TotalDays < 1 ) return "today";
		if ( span.TotalDays < 2 ) return "yesterday";
		if ( span.TotalDays < 31 ) return $"{(int)span.TotalDays} days ago";
		if ( span.TotalDays < 62 ) return "last month";
		if ( time.Year == DateTime.Now.Year ) return "this year";

		return time.Year.ToString();
	}

	void OpenRowMenu( Project project )
	{
		var items = new List<PopupMenu.Item>();

		// The row's normal opening is blocked while an editor is on the project - this is the
		// deliberate way to get a second instance
		if ( runningProjects.Contains( ProjectPath( project ) ) )
			items.Add( new( "Launch Another Instance", () => LaunchProject( project ), "rocket_launch" ) );

		items.Add( new( project.Pinned ? "Unpin" : "Pin", () =>
		{
			project.Pinned = !project.Pinned;
			ProjectList.SaveList();
			RefreshProjects();
		}, "push_pin" ) );

		items.Add( new( "Open Folder", () => Editor.EditorUtility.OpenFolder( System.IO.Path.GetDirectoryName( project.ConfigFilePath ) ), "folder" ) );

		// Published projects have a page on the backend
		if ( packages.TryGetValue( $"{project.Config.Org}.{project.Config.Ident}", out var package ) && package.Url is not null )
		{
			items.Add( new( $"View on {Global.BackendTitle}", () => Editor.EditorUtility.OpenFolder( package.Url ), "open_in_new" ) );
		}

		items.Add( null );
		items.Add( new( "Remove From List", () =>
		{
			ProjectList.Remove( project );
			ProjectList.SaveList();
			RefreshProjects();
		}, "delete" ) );

		PopupMenu.Open( Window, items );
	}

	/// <summary>
	/// Add a project that already exists on disk - pick its .sbproj and it joins the list.
	/// </summary>
	async Task AddExistingProjectAsync()
	{
		var path = await Window.PickOpenFile( null, "Project files|sbproj" );
		if ( string.IsNullOrEmpty( path ) ) return;

		AddProjectFromPath( path );
	}

	/// <summary>
	/// Whether this path could join the list - a .sbproj, or a folder that might hold one.
	/// </summary>
	static bool CouldBeProject( string path )
		=> path.EndsWith( ".sbproj", StringComparison.OrdinalIgnoreCase ) || System.IO.Directory.Exists( path );

	/// <summary>
	/// Add the project at this path to the list. A folder counts when there's a .sbproj inside.
	/// </summary>
	void AddProjectFromPath( string path )
	{
		if ( System.IO.Directory.Exists( path ) )
			path = System.IO.Directory.EnumerateFiles( path, "*.sbproj" ).FirstOrDefault();

		if ( path is null || !path.EndsWith( ".sbproj", StringComparison.OrdinalIgnoreCase ) ) return;

		var project = ProjectList.TryAddFromFile( path );
		if ( project is null ) return;

		ProjectList.SaveList();

		// Land where the new row is
		SetPage( Page.Projects );
		RefreshProjects();
	}

	static string ProjectPath( Project project ) => System.IO.Path.GetFullPath( project.ConfigFilePath );

	/// <summary>
	/// Open a project - unless an editor is already on it, in which case this does nothing.
	/// A second instance is almost never what anyone wants; right click has Launch Another
	/// Instance for when it really is.
	/// </summary>
	void OpenProject( Project project )
	{
		if ( runningProjects.Contains( ProjectPath( project ) ) )
			return;

		LaunchProject( project );
	}

	/// <summary>
	/// Launch the editor on a project - same as the Qt launcher, hand off to sbox-dev.
	/// </summary>
	void LaunchProject( Project project )
	{
		project.LastOpened = DateTimeOffset.Now;
		ProjectList.SaveList();

		var info = new ProcessStartInfo( NetCore.GetExecutablePath( "sbox-dev" ), $"{Environment.CommandLine} -project \"{project.ConfigFilePath}\"" );

		// Only let the shell start it on Windows - on Linux UseShellExecute goes through
		// xdg-open, which opens the editor in a web browser rather than running it.
		info.UseShellExecute = OperatingSystem.IsWindows();
		info.CreateNoWindow = true;
		info.WorkingDirectory = Environment.CurrentDirectory;

		Process.Start( info );

		// Mark it running right away rather than waiting on the next scan
		runningProjects.Add( ProjectPath( project ) );

		if ( LauncherPreferences.CloseOnLaunch )
		{
			Window.Dispose();
			return;
		}

		RefreshProjects();
	}

	//
	// New project
	//

	void ShowCreator()
	{
		content.DeleteChildren( true );

		var creator = content.AddChild( new ProjectCreatorPanel() );

		creator.OnDone = configPath =>
		{
			if ( configPath is not null )
			{
				var project = ProjectList.TryAddFromFile( configPath );
				ProjectList.SaveList();

				if ( project is not null )
				{
					ShowHome();
					OpenProject( project );
					return;
				}
			}

			ShowHome();
		};
	}
}

using System.Diagnostics;

namespace Editor;

public class HomeWidget : Widget
{
	enum SortMethod
	{
		Date,
		Name,
		Org
	}

	private SortMethod _sort;
	private SortMethod Sort
	{
		get => _sort;
		set
		{
			if ( SortButton.IsValid() )
			{
				SortButton.Icon = value switch
				{
					SortMethod.Date => "calendar_month",
					SortMethod.Name => "sort_by_alpha",
					SortMethod.Org => "groups",
					_ => ""
				};
			}

			if ( _sort != value )
			{
				_sort = value;
				RefreshLocalProjects();
			}
		}
	}

	private string _filter;
	private string Filter
	{
		get => _filter;
		set
		{
			_filter = value;
			RefreshLocalProjects();
		}
	}

	private Layout LocalProjectLayout { get; set; }

	private ProjectList ProjectList { get; }

	private IconButton SortButton;

	public HomeWidget( Widget parent = null ) : base( parent )
	{
		AcceptDrops = true;

		Layout = Layout.Column();
		Layout.Spacing = 4;

		ProjectList = new ProjectList();

		//
		// Menu bar - filters etc.
		//
		{
			var menuRow = Layout.AddRow();
			menuRow.Spacing = 4;
			menuRow.Margin = new Sandbox.UI.Margin( 16, 16, 16, 0 );

			SortButton = menuRow.Add( new IconButton( "sort_by_alpha" ) { OnClick = OpenPopup, ToolTip = "Sort by" } );
			Sort = _sort;

			{
				var search = menuRow.Add( new LineEdit() { PlaceholderText = "⌕  Search" }, 1 );
				search.SetStyles( $"border: 1px solid {Theme.ButtonBackground.Hex};" );
				search.TextChanged += _ =>
				{
					Filter = search.Value;
					search.Focus();
				};
				search.Blur();
			}

			//menuRow.Add( new IconButton( "cloud_download" )
			//{
			//	OnClick = ProjectDownload.OpenWindow,
			//	ToolTip = $"Clone a project from {Global.BackendTitle}"
			//} );
			menuRow.Add( new IconButton( "create_new_folder" )
			{
				OnClick = AddProjectFromFile,
				ToolTip = $"Add a project from a folder"
			} );
			menuRow.Add( new Button.Primary( "New Project...", "add" )
			{
				FixedHeight = Theme.RowHeight,
				Clicked = CreateProject,
				ToolTip = $"Create a new project"
			} );
		}

		Layout.AddSpacingCell( 8.0f );

		//
		// Launchpad - contains recent projects
		//
		{
			var Scroller = Layout.Add( new ScrollArea( this ), 1 );
			Scroller.Canvas = new Widget( Scroller )
			{
				Layout = Layout.Column(),
				VerticalSizeMode = SizeMode.CanGrow | SizeMode.Expand
			};

			LocalProjectLayout = Scroller.Canvas.Layout.Add( Layout.Column() );
			Scroller.Canvas.Layout.AddStretchCell();

			Scroller.Canvas.OnPaintOverride = () =>
			{
				Paint.ClearPen();
				Paint.SetBrush( Theme.WindowBackground );
				Paint.DrawRect( Scroller.Canvas.LocalRect );
				return true;
			};

			RefreshLocalProjects();
		}

	}

	private void RefreshLocalProjects()
	{
		ProjectList.Refresh();

		// Collect projects
		var allProjects = ProjectList.GetAll()
			.AsEnumerable()
			.Where( x => !x.IsBuiltIn )
			.ToList();

		UpdateProjectList( allProjects );

		/*

		// Collect packages
		var cloudProjects = new List<Package>();

		var sandboxProject = await Package.Fetch( "facepunch.sandbox", true );
		cloudProjects.Add( sandboxProject );

		// Fetch all packages belonging to all orgs that the user belongs to,
		// but only show them if the ident doesn't match an addon we already
		// have locally
		var orgs = EditorUtility.Account.Memberships.ToList();

		foreach ( var org in orgs )
		{
			var findResult = await Package.FindAsync( $"org:{org.Ident}" );

			var games = findResult.Packages
				.Where( x => !x.Archived )
				.Where( x => x.PackageType == Package.Type.Gamemode )
				.Where( x => !string.IsNullOrWhiteSpace( x.Source ) )
				.Where( x => !cloudProjects.Any( y => x.FullIdent == y.FullIdent ) );

			cloudProjects.AddRange( games );
		}

		// Skip games that we already have cloned
		cloudProjects = cloudProjects.Where( x => !allProjects.Any( y => x.FullIdent == y.Config.FullIdent ) ).ToList();

		// Fetching complete, let's replace our loading label with content
		UpdateCloudProjects( cloudProjects );
		*/
	}


	private void UpdateProjectList( List<Project> projects )
	{
		using var suspend = SuspendUpdates.For( this );

		List<Project> sampleProjects = new List<Project>();
		foreach ( var dir in System.IO.Directory.EnumerateDirectories( "samples/" ) )
		{
			var projFile = System.IO.Directory.EnumerateFiles( dir, "*.sbproj" ).FirstOrDefault();
			if ( projFile is null ) continue;
			var project = ProjectList.TryAddFromFile( projFile );
			if ( project is null ) continue;

			sampleProjects.Add( project );
		}

		//
		// Sort everything
		//
		switch ( Sort )
		{
			case SortMethod.Name:
				projects = projects.OrderBy( x => x.Config.Title ).ToList();
				break;
			case SortMethod.Org:
				projects = projects.OrderBy( x => x.Package.Org.Title ).ToList();
				break;
			case SortMethod.Date:
			default:
				projects = projects.OrderByDescending( x => x.LastOpened ).ToList();
				break;
		}

		//
		// Filter everything
		//
		var filter = Filter?.ToLower();
		if ( !string.IsNullOrEmpty( filter ) )
		{
			bool ContainsFilter( string str ) => str.ToLower().Contains( filter );
			bool PackageContainsFilter( Package x ) => ContainsFilter( x.Title ) || ContainsFilter( string.Join( " ", x.Tags ) ) || ContainsFilter( x.Org.Title ) || ContainsFilter( x.Org.Ident );

			projects = projects.Where( x => PackageContainsFilter( x.Package ) ).ToList();
		}


		//
		// Create UI
		//

		{
			LocalProjectLayout.Clear( true );
			LocalProjectLayout.Margin = new Sandbox.UI.Margin( 16, 0, 16, 16 );

			var nonSample = projects.Where( x => !sampleProjects.Any( y => x.ConfigFilePath == y.ConfigFilePath ) );
			if ( nonSample.Any( x => x.Pinned ) )
			{
				var l = LocalProjectLayout.Add( new Label.Subtitle( "Pinned Projects" ) { ContentMargins = 8, VerticalSizeMode = SizeMode.Expand } );
				CreateItemRows( nonSample.Where( x => x.Pinned ), LocalProjectLayout.AddColumn() );
			}

			if ( nonSample.Any( x => !x.Pinned ) )
			{
				var l = LocalProjectLayout.Add( new Label.Subtitle( "Local Projects" ) { ContentMargins = 8, VerticalSizeMode = SizeMode.Expand } );
				CreateItemRows( nonSample.Where( x => !x.Pinned ), LocalProjectLayout.AddColumn() );
			}

			// Sample Projects
			{
				var l = LocalProjectLayout.Add( new Label.Subtitle( "Sample Projects" ) { ContentMargins = 8, VerticalSizeMode = SizeMode.Expand } );
				CreateItemRows( sampleProjects, LocalProjectLayout.AddColumn() );
			}
		}

	}

	ContextMenu popup;

	void OpenPopup()
	{
		popup = new ContextMenu( this );
		popup.Layout = Layout.Column();
		popup.Layout.Margin = 8;
		popup.Size = new Vector2( 160, 30 );

		popup.AddOption( "Most Recent", "calendar_month", () => Sort = SortMethod.Date );
		popup.AddOption( "Name", "sort_by_alpha", () => Sort = SortMethod.Name );
		popup.AddOption( "Organization", "groups", () => Sort = SortMethod.Org );

		popup.Position = ScreenRect.TopLeft + new Vector2( 16, 48 );
		popup.Show();
		//popup.OpenAtCursor( false );
	}

	public override void OnDragHover( DragEvent ev )
	{
		if ( !ev.Data.HasFileOrFolder )
			return;

		//if ( !ProjectList.IsAcceptableAddonPath( ev.Data.FileOrFolder ) )
		//	return;

		ev.Action = DropAction.Link;
	}

	public override void OnDragDrop( DragEvent ev )
	{
		if ( !ev.Data.HasFileOrFolder )
			return;

		//if ( !ProjectList.IsAcceptableAddonPath( ev.Data.FileOrFolder ) )
		//	return;

		try
		{
			ProjectList.TryAddFromFile( ev.Data.FileOrFolder );
			ProjectList.SaveList();
			RefreshLocalProjects();
		}
		catch ( Exception )
		{
			//	Log.Warning( ex, $"Couldn't add project from disk: {ex.Message}" );
		}

		ev.Action = DropAction.Link;
	}

	private void CreateItemRows( IEnumerable<Project> list, Layout layout )
	{
		var grid = new GridLayout();
		grid.Spacing = 1;
		grid.HorizontalSpacing = 16;

		layout.Add( grid );

		int i = 0;
		foreach ( var item in list )
		{
			var widget = new ProjectRow( item, this );

			widget.OnPinStateChanged = () =>
			{
				ProjectList.SaveList(); // save changes
				RefreshLocalProjects(); // build projects
			};

			widget.OnProjectRemove = () =>
			{
				ProjectList.Remove( item );
				ProjectList.SaveList();
				RefreshLocalProjects(); // build projects
			};

			widget.OnProjectOpen = ( args ) =>
			{
				item.LastOpened = DateTime.Now;
				ProjectList.SaveList();
				RefreshLocalProjects();

				OpenProject( item, args );
			};

			grid.AddCell( 0, i, widget );
			i++;
		}
	}

	public void AddProjectFromFile()
	{
		var fd = new FileDialog( null );
		//	fd.Directory = EditorPreferences.AddonLocation;

		// Make sure to make the directory if it does not exist yet.
		if ( !System.IO.Directory.Exists( fd.Directory ) ) System.IO.Directory.CreateDirectory( fd.Directory );

		fd.Title = "Find project file";
		fd.SetNameFilter( "*.sbproj" );

		if ( fd.Execute() )
		{
			try
			{
				ProjectList.TryAddFromFile( fd.SelectedFile );
				ProjectList.SaveList();
				RefreshLocalProjects();
			}
			catch ( Exception )
			{
			}
		}
	}

	public void CreateProject()
	{
		var creatorWindow = new ProjectCreator();

		creatorWindow.OnProjectCreated = config =>
		{
			var project = ProjectList.TryAddFromFile( config );
			project.LastOpened = DateTime.Now; // Set the last opened time to now
			ProjectList.SaveList();
			RefreshLocalProjects();

			if ( project != null )
				OpenProject( project );
		};

		creatorWindow.Show();
	}

	public void OpenProject( Project project, string args = null )
	{
		ProcessStartInfo info = new ProcessStartInfo( "sbox-dev.exe", $"{Environment.CommandLine} -project \"{project.ConfigFilePath}\" {args ?? ""}" );
		info.UseShellExecute = true;
		info.CreateNoWindow = true;
		info.WorkingDirectory = System.Environment.CurrentDirectory;

		Process.Start( info );

		GetAncestor<StartupWindow>()?.OnSuccessfulLaunch();
	}

	protected override void OnPaint()
	{
		Paint.ClearPen();
		Paint.SetBrush( Theme.WindowBackground );
		Paint.DrawRect( LocalRect );
	}
}


using System.IO;

namespace Editor.LibraryManager;

[Dock( "Editor", "Library Manager", "extension" )]
public class LibraryManagerDock : Widget
{
	SegmentedControl PageSelect;
	LibraryList ListLocal;
	LibraryList ListGlobal;
	Widget GlobalControls;
	List<FacetDropdown> FacetDropdowns = new();

	internal Action<string> OnValueChanged;

	public LibraryManagerDock( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Margin = 4;
		Layout.Spacing = 4;

		FocusMode = FocusMode.TabOrClickOrWheel;
	}

	void Rebuild()
	{
		Layout.Clear( true );

		{
			var rowWidget = Layout.Add( new Widget( this ) );
			rowWidget.VerticalSizeMode = SizeMode.CanShrink;
			rowWidget.OnPaintOverride = () =>
			{
				Paint.SetBrushAndPen( Theme.SidebarBackground, Color.Transparent, 0 );
				Paint.DrawRect( rowWidget.LocalRect, 4 );
				return false;
			};
			var row = rowWidget.Layout = Layout.Row();
			row.Margin = 4;
			row.Spacing = 8;

			var textEdit = row.Add( new LineEdit( this ) { FixedHeight = Theme.RowHeight, PlaceholderText = "⌕  Search", ToolTip = "Search" } );
			textEdit.TextChanged += ( txt ) =>
			{
				OnValueChanged?.Invoke( txt );
			};
			textEdit.SetStyles( "background-color: #000; max-width: 200px;" );

			row.AddStretchCell();

			PageSelect = row.Add( new SegmentedControl( this ) );
			PageSelect.AddOption( "Browse", "language" );
			PageSelect.AddOption( "Installed", "folder" );
			PageSelect.HorizontalSizeMode = SizeMode.Flexible;
			PageSelect.OnSelectedChanged = _ =>
			{
				RebuildGlobalControls();

				// Make the appropriate list visible and re-select the highlighted library so it updates the LibraryDetail
				var index = PageSelect.SelectedIndex;
				if ( ListGlobal is not null )
				{
					ListGlobal.Visible = index == 0;
					if ( ListGlobal.Visible )
					{
						ListGlobal.OnLibrarySelected?.Invoke( ListGlobal.SelectedLibrary );
					}
				}
				if ( ListLocal is not null )
				{
					ListLocal.Visible = index == 1;
					if ( ListLocal.Visible )
					{
						ListLocal.OnLibrarySelected?.Invoke( ListLocal.SelectedLibrary );
					}
				}
			};

			row.AddStretchCell();

			row.Add( new Button( "Open Folder", this ) { Icon = "folder", ToolTip = "Open Library Folder", Clicked = () => EditorUtility.OpenFolder( FileSystem.Libraries.GetFullPath( "/" ) ) } );
			row.Add( new Button( "New Library", this ) { Icon = "add_circle", ToolTip = "Create New Library", Clicked = CreateNewLibrary } );
		}

		{
			var splitter = Layout.Add( new Splitter( this ) );
			splitter.IsHorizontal = true;

			var listWidget = new Widget( this );
			listWidget.Layout = Layout.Column();
			splitter.AddWidget( listWidget );
			splitter.SetStretch( 0, 10 );

			var content = new Widget( this );
			content.Layout = Layout.Row();
			content.MinimumWidth = 300;
			content.Width = 400;
			splitter.AddWidget( content );
			splitter.SetStretch( 1, 4 );

			GlobalControls = new Widget();
			GlobalControls.Layout = Layout.Row();
			GlobalControls.Layout.Spacing = 4;
			GlobalControls.ContentMargins = new Sandbox.UI.Margin( 0, 0, 0, 4 );
			listWidget.Layout.Add( GlobalControls );

			RebuildGlobalControls();

			// Global and Local library lists
			{
				ListGlobal = new LibraryList( this )
				{
					ShowAvailable = true,
					OnLibrarySelected = ( library ) =>
					{
						content.Layout.Clear( true );
						content.Layout.Add( new LibraryDetail( library ) );
					}
				};
				ListGlobal.OnListLoaded += () =>
				{
					RebuildGlobalControls();
				};

				listWidget.Layout.Add( ListGlobal );
				if ( PageSelect.SelectedIndex != 0 )
				{
					ListGlobal.Visible = false;
				}

				ListLocal = new LibraryList( this )
				{
					ShowInstalled = true,
					OnLibrarySelected = ( library ) =>
					{
						content.Layout.Clear( true );
						content.Layout.Add( new LibraryDetail( library ) );
					}
				};
				listWidget.Layout.Add( ListLocal );
				if ( PageSelect.SelectedIndex != 1 )
				{
					ListLocal.Visible = false;
				}
			}

			// Filter the results when we search
			OnValueChanged = ( val ) =>
			{
				if ( ListGlobal.Visible )
				{
					ListGlobal.Filter = val;
				}
				if ( ListLocal.Visible )
				{
					ListLocal.Filter = val;
				}
			};
		}
	}

	void RebuildGlobalControls()
	{
		GlobalControls.Layout.Clear( true );
		FacetDropdowns.Clear();

		// Only build the controls if we're on the global page
		if ( PageSelect.SelectedIndex != 0 || ListGlobal?.LastResult?.Facets is null )
		{
			return;
		}

		var parts = ListGlobal.Query.Split( ' ' ).Select( x => x.Split( ':' ) );
		var facetTags = parts.Where( x => x.Length > 1 )
			.GroupBy( x => x[0] ) // avoid duplicates
			.ToDictionary( g => g.Key, g => g.First()[1] );

		// Category Facets
		foreach ( var facet in ListGlobal.LastResult.Facets )
		{
			string selection = null;
			facetTags.TryGetValue( facet.Name, out selection );

			var dropdown = GlobalControls.Layout.Add( new FacetDropdown( facet, selection, this ) );
			dropdown.OnChanged += UpdateGlobalQuery;
			dropdown.IncludeChildren = true;
			FacetDropdowns.Add( dropdown );
		}

		// Sort Order
		{
			var sortEntries = new List<Package.Facet.Entry>();
			foreach ( var order in ListGlobal.LastResult.Orders )
			{
				sortEntries.Add( new Package.Facet.Entry( order.Name, order.Title, order.Icon, 0, [] ) );
			}
			var sortFacet = new Package.Facet( "sort", "Sort By", sortEntries.ToArray() );

			var dropdown = GlobalControls.Layout.Add( new FacetDropdown( sortFacet, ListGlobal.SortMode, this ) );
			dropdown.ShowCount = false;
			dropdown.OnChanged += () =>
			{
				var name = dropdown.Selected?.Name.ToLower();
				ListGlobal.SortMode = string.IsNullOrEmpty( name ) ? "updated" : name;
			};
		}
	}

	void UpdateGlobalQuery()
	{
		if ( ListGlobal is null ) return;

		var query = "";

		foreach ( var dropdown in FacetDropdowns )
		{
			if ( dropdown.Selected != null && !query.Contains( $"{dropdown.Facet.Name}:" ) )
			{
				query += $" {dropdown.GetFilter()}";
			}
		}
		query = query.Trim();

		ListGlobal.Query = query;
	}

	void CreateNewLibrary()
	{
		Dialog.AskString( CreateNewLibrary, "What would you like to call your new library?", "Create", title: "Create a Library", minLength: 2 );
	}

	void CreateNewLibrary( string libraryName )
	{
		// make libraryName folder safe
		var folderName = string.Concat( libraryName.Where( x => char.IsAsciiLetterOrDigit( x ) ) );
		if ( string.IsNullOrWhiteSpace( folderName ) )
			return;

		void FinalizeLibrary( string name )
		{
			FileSystem.Libraries.CreateDirectory( name );
			var dir = FileSystem.Libraries.GetFullPath( name );

			CopyFolder( FileSystem.Root.GetFullPath( "templates/library.minimal" ), dir, libraryName, name.ToLower() );

			_ = LibrarySystem.Add( name, default );
		}

		// Check to make sure the ident doesn't match the game's ident or any other library
		bool sharesIdent = folderName.ToLower() == Project.Current.Package.Ident.ToLower();
		if ( !sharesIdent ) sharesIdent = FileSystem.Libraries.FindDirectory( "", folderName )?.Count() > 0;
		if ( sharesIdent )
		{
			var newIdentNum = 2;
			var newFolderName = $"{folderName}_{newIdentNum}";
			while ( (FileSystem.Libraries.FindDirectory( "", newFolderName )?.Count() ?? 0) > 0 )
			{
				newIdentNum++;
				newFolderName = $"{folderName}_{newIdentNum}";
			}
			folderName = newFolderName;
			Dialog.AskConfirm( () => { FinalizeLibrary( newFolderName ); }, $"The library cannot share it's Ident with another Package. Would you like to continue with the ident \"{newFolderName}\"?", "Error creating Library", "OK", "Cancel" );
			return;
		}

		FinalizeLibrary( folderName );
	}

	static void CopyFolder( string sourceDir, string targetDir, string libraryName, string libraryFolder )
	{
		if ( sourceDir.Contains( "\\.", StringComparison.OrdinalIgnoreCase ) )
		{
			return;
		}

		System.IO.Directory.CreateDirectory( targetDir );

		foreach ( var file in Directory.GetFiles( sourceDir ) )
		{
			CopyAndProcessFile( file, targetDir, libraryName, libraryFolder );
		}

		foreach ( var directory in Directory.GetDirectories( sourceDir ) )
		{
			CopyFolder( directory, Path.Combine( targetDir, Path.GetFileName( directory ) ), libraryName, libraryFolder );
		}
	}

	static void CopyAndProcessFile( string file, string targetDir, string libraryName, string libraryFolder )
	{
		var targetname = Path.Combine( targetDir, Path.GetFileName( file ) );

		// Replace $ident with our ident in file name
		targetname = targetname.Replace( "$title", libraryName );
		targetname = targetname.Replace( "$ident", libraryFolder );

		if ( file.EndsWith( ".cs" ) || file.EndsWith( ".json" ) || file.EndsWith( ".sbproj" ) )
		{
			var txt = System.IO.File.ReadAllText( file );
			txt = txt.Replace( "$title", libraryName );
			txt = txt.Replace( "$ident", libraryFolder );
			System.IO.File.WriteAllText( targetname, txt );
		}
		else
		{
			File.Copy( file, targetname );
		}
	}

	[EditorEvent.Frame]
	public void Frame()
	{
		if ( SetContentHash( ContentHash, 0.5f ) )
		{
			Rebuild();
		}
	}

	int ContentHash() => HashCode.Combine( 0 );

}

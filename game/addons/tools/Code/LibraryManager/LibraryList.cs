using Editor.Wizards;

namespace Editor.LibraryManager;

class LibraryList : ListView
{
	public Action<Package> OnLibrarySelected { get; set; }
	public Action OnListLoaded { get; set; }
	public Package SelectedLibrary { get; private set; }

	public bool ShowInstalled { get; set; }
	public bool ShowAvailable { get; set; }

	public string Filter { get; set; } = "";
	public string Query { get; set; } = "";
	public string SortMode { get; set; } = "updated";
	public Package.FindResult LastResult { get; set; }

	public LibraryList( Widget parent ) : base( parent )
	{
		ItemContextMenu = ShowItemContext;
		ItemSelected = OnItemClicked;

		ItemSize = new Vector2( -1, 64 );
	}

	public void OnItemClicked( object value )
	{
		if ( value is LibraryProject lib )
		{
			OnLibrarySelected?.Invoke( lib.Project.Package );
			SelectedLibrary = lib.Project.Package;
		}

		if ( value is Package p )
		{
			OnLibrarySelected?.Invoke( p );
			SelectedLibrary = p;
		}
	}

	private void ShowItemContext( object obj )
	{
		LibraryProject project = obj as LibraryProject;
		Package package = obj as Package ?? project?.Project.Package;

		var m = new ContextMenu();

		if ( package is not null && package.Org.Ident != "local" )
		{
			m.AddOption( "View in Browser", "public", () => EditorUtility.OpenFolder( package.Url ) );
			m.AddSeparator();
		}

		if ( project is not null )
		{
			m.AddOption( "Project Properties", "tune", () => ProjectSettingsWindow.OpenForProject( project.Project ) );
			m.AddSeparator();
			m.AddOption( "Publish Project", "upload_file", () => PublishWizard.Open( project.Project ) );
			m.AddSeparator();
			m.AddOption( "Show in Explorer", "folder", () => EditorUtility.OpenFolder( project.Project.RootDirectory.FullName ) );
		}

		m.OpenAtCursor();
	}

	protected override void PaintItem( VirtualWidget item )
	{
		if ( item.Object is LibraryProject c )
			PaintItem( item, c.Project.Package );

		if ( item.Object is Package p )
			PaintItem( item, p );
	}

	private void PaintItem( VirtualWidget item, Package c )
	{
		var rect = item.Rect;

		if ( Paint.HasPressed )
			rect = rect.Shrink( 2, 2, 0, 0 );

		var library = LibrarySystem.All.Where( x => x.Project.Package.Ident == c.Ident && x.Project.Package.Org.Ident == c.Org.Ident ).FirstOrDefault();

		var pen = Theme.Text.WithAlpha( 0.6f );

		if ( Paint.HasMouseOver || Paint.HasSelected )
		{
			Paint.SetBrush( Theme.Primary.WithAlpha( Paint.HasMouseOver ? 0.8f : 0.5f ) );
			Paint.ClearPen();
			Paint.DrawRect( rect, 4 );

			pen = Theme.Text.WithAlpha( Paint.HasMouseOver ? 1 : 0.9f );
			Paint.ClearBrush();
		}

		rect = rect.Shrink( 4 );

		// Icon
		{
			var iconRect = rect;
			iconRect.Width = iconRect.Height;
			iconRect = iconRect.Shrink( 8 );

			Paint.SetBrushAndPen( Theme.Border.WithAlpha( 0.4f ) );

			if ( !string.IsNullOrWhiteSpace( c.Thumb ) && !c.Thumb.StartsWith( "/" ) )
			{
				Paint.Draw( iconRect, c.Thumb, borderRadius: 4 );
			}
			else
			{
				Paint.DrawRect( iconRect, 4 );
			}

			rect.Left = iconRect.Right + 8;
		}

		//header
		{
			rect.Top += 8;

			Paint.SetHeadingFont( 11, 450 );
			Paint.Pen = pen;
			var r = Paint.DrawText( rect, c.Title, TextFlag.LeftTop );

			r.Left = r.Right + 8;
			r.Right = rect.Right;
			r.Bottom -= 1;

			rect.Top = r.Bottom + 4;

			var installedVersion = library is not null ? $"• v{library?.Version?.Build}" : "";

			Paint.Pen = pen.WithAlphaMultiplied( 0.6f );
			Paint.SetDefaultFont();
			var drawnRect = Paint.DrawText( r, $"{c.Org.Title} {installedVersion}", TextFlag.LeftBottom );
			if ( !string.IsNullOrEmpty( installedVersion ) )
			{
				Paint.DrawIcon( Rect.FromPoints( drawnRect.TopRight, r.BottomRight ), "download_done", 12, TextFlag.LeftCenter );
			}
		}

		// body text
		{
			Paint.Pen = pen.WithAlphaMultiplied( 0.6f );
			Paint.SetDefaultFont();
			Paint.DrawText( rect, c.Summary, TextFlag.LeftTop );
		}
	}

	protected override void OnPaint()
	{
		Paint.ClearPen();
		Paint.SetBrush( Theme.ControlBackground );
		Paint.DrawRect( LocalRect, 4 );

		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;

		base.OnPaint();
	}

	[EditorEvent.Frame]
	public void Frame()
	{
		if ( !Visible ) return;

		if ( SetContentHash( GetContentHash(), 0.5f ) )
		{
			_ = UpdateAsync();
		}
	}

	async Task UpdateAsync()
	{
		if ( ShowInstalled )
		{
			// Get installed libraries, but fetch their cloud information as well.
			var packages = LibrarySystem.All.Where( x => string.IsNullOrEmpty( Filter ) || x.Project.Config.Title.Contains( Filter, StringComparison.InvariantCultureIgnoreCase ) );
			SetItems( packages );

			var firstLocalPackage = packages.FirstOrDefault();
			if ( firstLocalPackage is not null )
			{
				SelectItem( firstLocalPackage );
			}

			OnListLoaded?.Invoke();
			return;
		}

		// Fetch available libraries from cloud
		var search = "";
		if ( !string.IsNullOrEmpty( Filter ) ) search = Filter + " ";
		var query = $"{search}type:library sort:{SortMode} {Query}".Trim();
		var result = await Package.FindAsync( query, 200, 0 );
		SetItems( result.Packages );

		var firstPackage = result.Packages.FirstOrDefault();
		if ( firstPackage is not null )
		{
			SelectItem( firstPackage );
		}

		LastResult = result;
		OnListLoaded?.Invoke();
	}

	int GetContentHash()
	{
		return HashCode.Combine( Filter, Query, SortMode );
	}

}

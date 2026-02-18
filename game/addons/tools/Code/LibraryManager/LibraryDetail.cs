using System.Text;

namespace Editor.LibraryManager;

class LibraryDetail : Widget
{
	/// <summary>
	/// The package holding the description for this library
	/// </summary>
	Package Package;

	/// <summary>
	/// If this is installed, this will be the installed project
	/// </summary>
	LibraryProject Installed;

	/// <summary>
	/// List of versions
	/// </summary>
	ComboBox VersionList;

	/// <summary>
	/// The "install" button
	/// </summary>
	Button ActionButton;

	/// <summary>
	/// The "uninstall" button
	/// </summary>
	Button UninstallButton;

	/// <summary>
	/// The "properties" button
	/// </summary>
	Button PropertiesButton;

	/// <summary>
	/// The revision currently selected in the version list
	/// </summary>
	Package.IRevision SelectedRevision;

	public LibraryDetail( Package package ) : base()
	{
		Package = package;

		Layout = Layout.Column();
		Layout.Margin = 8;
		Layout.Spacing = 4;

		_ = FetchAndBuild();
	}

	async Task FetchAndBuild()
	{
		if ( !this.IsValid() ) return;

		Layout.Clear( true );

		if ( Package is null )
			return;

		if ( !Sandbox.Package.TryParseIdent( Package.FullIdent, out var ident ) )
			return;

		Installed = LibrarySystem.All.FirstOrDefault( x => x.Project.Package.Ident == Package.Ident && x.Project.Package.Org == Package.Org );

		// do we have this package installed? what is the version?
		var isLocalPackage = ident.org == "local";
		if ( !isLocalPackage )
		{
			Package = await Package.FetchAsync( $"{ident.org}.{ident.package}", false ) ?? Package;
			if ( !this.IsValid() ) return;
		}

		var membership = EditorUtility.Account.Memberships.FirstOrDefault( x => x.Ident == ident.org );

		{
			var header = Layout.AddRow();
			header.Spacing = 4;

			var icon = header.Add( new Widget() );
			icon.FixedWidth = 42;
			icon.FixedHeight = icon.FixedWidth;
			icon.OnPaintOverride = () =>
			{
				Paint.SetBrushAndPen( Theme.Border.WithAlpha( 0.4f ) );

				var rect = new Rect( 0, icon.Size );
				if ( !string.IsNullOrWhiteSpace( Package.Thumb ) && !Package.Thumb.StartsWith( "/" ) )
				{
					Paint.Draw( rect, Package.Thumb, borderRadius: 4 );
				}
				else
				{
					Paint.DrawRect( rect, 4 );
				}

				return true;
			};

			var headerDetails = header.AddColumn();

			headerDetails.Add( new Label( $"<h2>{Package.Title}</h2>" ) );

			// org icon and title
			var orgText = (Package.Org.Ident == "local") ? "Local Package" : $"From <strong>{Package.Org.Title}</strong>";
			headerDetails.Add( new Label( orgText ) );
		}

		// Control Row - Version List, Action Button, Uninstall Button
		{
			var controlRow1 = Layout.AddRow();
			controlRow1.Spacing = 4;

			VersionList = new ComboBox( this );
			controlRow1.Add( VersionList );

			ActionButton = new Button( "Install", "get_app", this ) { Pressed = () => _ = OnActionPressed() };
			ActionButton.FixedWidth = 100;
			controlRow1.Add( ActionButton );

			// If it's a local package, include the Properties & Publish buttons here since the other controls get hidden
			if ( membership is not null || isLocalPackage )
			{
				PropertiesButton = new Button( "Open Properties", "tune" )
				{
					Pressed = () =>
					{
						var project = LibrarySystem.All.FirstOrDefault( x => x.Project.Package.Ident == Package.Ident && x.Project.Package.Org == Package.Org )?.Project;
						if ( project is null ) return;
						ProjectSettingsWindow.OpenForProject( project );
					}
				};
				if ( !isLocalPackage )
				{
					// If this is not a local package, make it a small icon button
					PropertiesButton.Text = "";
					PropertiesButton.Icon = " " + PropertiesButton.Icon;
					PropertiesButton.FixedWidth = 26;
				}
				controlRow1.Add( PropertiesButton );
			}

			UninstallButton = new Button( "", " delete" )
			{
				FixedWidth = 26,
				Pressed = () => Dialog.AskConfirm( OnUninstall, $"Are you sure you want to remove {Package.Title}?", "Remove Package", "Uninstall", "Cancel" )
			};
			controlRow1.Add( UninstallButton );
			UninstallButton.Visible = Installed is not null;
		}

		var pageControl = Layout.Add( new SegmentedControl( this ) );
		pageControl.AddOption( "README", "description" );
		pageControl.AddOption( "Package Details", "history" );
		pageControl.Visible = !isLocalPackage;

		// Scroll Area
		var scrollArea = Layout.Add( new ScrollArea( this ) );
		scrollArea.Canvas = new Widget();
		scrollArea.Canvas.Layout = Layout.Column();
		scrollArea.Canvas.ContentMargins = new Sandbox.UI.Margin( 0, 0, 8, 0 ); // Leave room so the scrollbar doesn't overlap
		scrollArea.Canvas.VerticalSizeMode = SizeMode.Flexible;
		scrollArea.Canvas.HorizontalSizeMode = SizeMode.Flexible;
		scrollArea.HorizontalScrollbarMode = ScrollbarMode.Off;
		scrollArea.MaximumHeight = Height;

		// Description label, formatted xml
		var desc = ParseDescription( Package.Description );
		var descriptionLabel = scrollArea.Canvas.Layout.Add( new Label( desc ) );
		descriptionLabel.WordWrap = true;
		descriptionLabel.SetStyles( "figure { width: 64px; }" );

		// Package Details
		var packageDetails = CreatePackageDetails();
		scrollArea.Canvas.Layout.Add( packageDetails );
		scrollArea.Canvas.Layout.AddStretchCell();

		// Setup Page Control
		pageControl.OnSelectedChanged = _ =>
		{
			descriptionLabel.Visible = pageControl.SelectedIndex == 0;
			packageDetails.Visible = pageControl.SelectedIndex == 1;
		};

		Layout.AddStretchCell();

		if ( ident.org == "local" )
		{
			VersionList.Visible = false;
			ActionButton.Visible = false;
			return;
		}

		var versions = await Package.FetchVersions( $"{ident.org}.{ident.package}" );
		if ( !this.IsValid() ) return;

		if ( versions is not null )
		{
			if ( Installed is not null )
			{
				SelectedRevision = versions.Where( x => x.VersionId == Installed.Version.Build ).FirstOrDefault();
			}

			SelectedRevision ??= versions.FirstOrDefault();

			UpdateVersionList( versions );
			OnVersionSelected( SelectedRevision );
		}
	}

	Widget CreatePackageDetails()
	{
		var parent = new Widget();
		parent.Layout = Layout.Column();
		var headerLabel = parent.Layout.Add( new Label( $"<h3>Summary</h3>\n{Package.Summary}" ) );
		headerLabel.WordWrap = true;
		parent.Layout.AddSpacingCell( 8 );

		var grid = Layout.Grid();
		grid.Spacing = 4;
		parent.Layout.Add( grid );
		grid.AddCell( 0, 0, new Label( "<strong>Latest Version</strong>" ) );
		grid.AddCell( 1, 0, new Label( $"v{Package?.Revision?.VersionId}" ) );

		grid.AddCell( 0, 1, new Label( "<strong>Author</strong>" ) );
		grid.AddCell( 1, 1, new Label( $"<a href=\"{Global.BackendUrl}/{Package?.Org?.Ident}\">{Package?.Org?.Title}</a>" ) );

		grid.AddCell( 0, 2, new Label( "<strong>Date Published</strong>" ) );
		grid.AddCell( 1, 2, new Label( $"{Package.Created.ToString( "G" )}" ) );

		grid.AddCell( 0, 3, new Label( "<strong>Last Updated</strong>" ) );
		grid.AddCell( 1, 3, new Label( $"{Package.Updated.ToString( "G" )}" ) );

		grid.AddCell( 0, 4, new Label( "<strong>Project URL</strong>" ) );
		grid.AddCell( 1, 4, new Label( $"<a href=\"{Package?.Url}\">{Package?.Url}</a>" ) );

		var tags = string.Join( ", ", Package.Tags );
		if ( string.IsNullOrEmpty( tags ) ) tags = "<i>None</i>";
		grid.AddCell( 0, 5, new Label( "<strong>Tags</strong>" ) );
		grid.AddCell( 1, 5, new Label( tags ) );

		// TODO: Add downloads count here?

		parent.Layout.AddStretchCell();
		parent.Visible = false;
		return parent;
	}

	void UpdateVersionList( List<Package.IRevision> versions )
	{
		VersionList.Clear();
		int index = 0;
		var installed = LibrarySystem.All.FirstOrDefault( x => x.Project.Package.Ident == Package.Ident && x.Project.Package.Org == Package.Org );
		foreach ( var v in versions )
		{
			var version = v;
			var name = $"{v.Created.DateTime} - {v.VersionId}";
			string icon = null;
			if ( installed is not null )
			{
				if ( installed.Version.Build == version.VersionId )
				{
					icon = "download_done";
				}
				else if ( index == 0 )
				{
					icon = "fiber_new";
				}
			}
			VersionList.AddItem( name, icon, () => OnVersionSelected( v ), v.Summary, SelectedRevision == version );
			if ( !string.IsNullOrWhiteSpace( icon ) )
			{
				VersionList.TrySelectNamed( name );
			}
			index++;
		}
	}

	void OnVersionSelected( Package.IRevision revision )
	{
		SelectedRevision = revision;

		// Is this fucker installed?
		var installed = LibrarySystem.All.FirstOrDefault( x => x.Project.Package.Ident == Package.Ident && x.Project.Package.Org == Package.Org );
		if ( installed is not null )
		{
			// is this the installed version?
			if ( revision.VersionId == installed.Version.Build )
			{
				ActionButton.Enabled = false;
				ActionButton.Icon = "folder";
				ActionButton.Text = "Installed";
			}
			else
			{
				ActionButton.Enabled = true;
				ActionButton.Icon = "sync";
				ActionButton.Text = $"Update";
			}
			UninstallButton.Visible = true;
		}
		else
		{
			// it's not installed
			ActionButton.Enabled = true;
			ActionButton.Icon = "get_app";
			ActionButton.Text = "Install";
			UninstallButton.Visible = false;
		}

	}

	string ParseDescription( string description )
	{
		if ( string.IsNullOrEmpty( description ) )
			return "<i>No description provided.</i>";

		if ( description.StartsWith( "[" ) )
		{
			try
			{
				var json = System.Text.Json.JsonSerializer.Deserialize<List<TextBlock>>( description );
				if ( json != null )
				{
					var html = new System.Text.StringBuilder();
					foreach ( var block in json )
					{
						// Process block based on attributes
						if ( block.attributes?.Contains( "heading1" ) == true )
						{
							html.Append( "<h1>" );
							AppendText( html, block.text );
							html.Append( "</h1>" );
						}
						else if ( block.attributes?.Contains( "heading2" ) == true )
						{
							html.Append( "<h2>" );
							AppendText( html, block.text );
							html.Append( "</h2>" );
						}
						else if ( block.attributes?.Contains( "heading3" ) == true )
						{
							html.Append( "<h3>" );
							AppendText( html, block.text );
							html.Append( "</h3>" );
						}
						else
						{
							AppendText( html, block.text );
						}
					}
					return html.ToString();
				}
			}
			catch ( System.Exception e )
			{
				Log.Warning( $"Failed to parse JSON description: {e.Message}" );
			}
		}

		return description;
	}

	private void AppendText( StringBuilder html, List<TextElement> textElements )
	{
		if ( textElements == null ) return;

		foreach ( var element in textElements )
		{
			if ( element.type == "string" )
			{
				string text = element.@string;

				// Block breaks
				if ( element.attributes?.blockBreak == true )
				{
					if ( text == "\n" )
					{
						html.Append( "<br />" );
					}
					continue;
				}

				// Links
				if ( element.attributes?.href != null )
				{
					html.Append( $"<a href=\"{element.attributes.href}\">{text}</a>" );
				}
				else
				{
					html.Append( text );
				}
			}
		}
	}

	async Task OnActionPressed()
	{
		await LibrarySystem.Install( Package.FullIdent, SelectedRevision.VersionId );

		// window closed
		if ( !IsValid ) return;

		await FetchAndBuild();
	}

	void OnUninstall()
	{
		UninstallButton.Enabled = false;
		Installed.RemoveAndDelete();
		_ = FetchAndBuild();
	}

	protected override Vector2 SizeHint()
	{
		return new Vector2( 300, 100 );
	}


	// Classes to deserialize JSON description
	private class TextBlock
	{
		public List<TextElement> text { get; set; }
		public List<string> attributes { get; set; }
		public Dictionary<string, object> htmlAttributes { get; set; }
	}

	private class TextElement
	{
		public string type { get; set; }
		public TextAttributes attributes { get; set; }
		public string @string { get; set; }
		public Dictionary<string, object> attachment { get; set; }
	}

	private class TextAttributes
	{
		public bool blockBreak { get; set; }
		public string href { get; set; }
	}
}

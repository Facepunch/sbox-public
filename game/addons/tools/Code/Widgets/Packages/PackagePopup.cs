namespace Editor.Widgets.Packages;

/// <summary>
/// Compact popup shown when right-clicking a package in the cloud browser.
/// Single-column layout: info header → stats → actions.
/// For collection packages the favourite action is shown as a labelled
/// "Add to My Collections" row so the sidebar link is obvious.
/// </summary>
public partial class PackagePopup : PopupWidget
{
	public Package Package { get; set; }

	Package.IRevision Latest;
	bool isInstalling;
	Button installButton;

	public PackagePopup( Package package, Widget parent ) : base( parent )
	{
		Package = package;
		MinimumSize = new Vector2( 340, 0 );
		MaximumSize = new Vector2( 340, 900 );
		Layout = Layout.Column();
		Layout.Margin = 0;
		Layout.Spacing = 0;

		Rebuild();
	}

	public void Rebuild()
	{
		Layout.Clear( true );
		installButton = null;

		BuildInfoSection();
		Layout.Add( new SeparatorLine() );
		BuildActionsSection();
	}

	void BuildInfoSection()
	{
		var panel = new Widget( this );
		panel.Layout = Layout.Column();
		panel.Layout.Margin = new Margin( 14, 12, 14, 10 );
		panel.Layout.Spacing = 0;
		Layout.Add( panel );

		var titleRow = panel.Layout.AddRow();
		titleRow.Spacing = 6;

		var titleCol = titleRow.AddColumn( 1 ); // stretch fills remaining space
		titleCol.Spacing = 2;
		titleCol.Add( new Label( Package.Title ) { WordWrap = true } )
			.SetStyles( "font-family: Poppins; font-size: 13px; font-weight: bold;" );
		titleCol.Add( new Label( $"by {Package.Org.Title}" ) )
			.SetStyles( "color: #777;" );

		titleRow.Add( new TypePill( Package.TypeName ) );

		if ( !string.IsNullOrWhiteSpace( Package.Summary ) )
		{
			panel.Layout.AddSpacingCell( 6 );
			panel.Layout.Add( new Label( Package.Summary ) { WordWrap = true } )
				.SetStyles( "color: #888;" );
		}

		panel.Layout.AddSpacingCell( 8 );

		var stats = panel.Layout.AddRow();
		stats.Spacing = 4;

		// Favourites count (non-interactive — action lives below)
		// Favourites count (non-interactive — action lives below)
		var favorite = stats.Add( new Button.Clear( $"{Package.Favourited:n0}", "favorite" ) );
		favorite.FixedHeight = 18;
		favorite.SetProperty( "is-active", Package.Interaction.Favourite );
		stats.AddSpacingCell( 6 );

		// Thumbs up / down (interactive)
		var voteUp = stats.Add( new Button.Clear( $"{Package.VotesUp:n0}", "thumb_up" ) );
		voteUp.FixedHeight = 18;
		voteUp.SetProperty( "is-active", Package.Interaction.Rating == 0 );
		voteUp.MouseClick = () => _ = Package.SetVoteAsync( true );

		var voteDn = stats.Add( new Button.Clear( $"{Package.VotesDown:n0}", "thumb_down" ) );
		voteDn.FixedHeight = 18;
		voteDn.SetProperty( "is-active", Package.Interaction.Rating == 1 );
		voteDn.MouseClick = () => _ = Package.SetVoteAsync( false );

		stats.AddStretchCell();

		stats.Add( new Label( $"{Package.Usage.Total.Users:n0} users" ) )
			.SetStyles( "color: #888;" );
	}

	void BuildActionsSection()
	{
		var panel = new Widget( this );
		panel.Layout = Layout.Column();
		panel.Layout.Margin = new Margin( 0, 4, 0, 6 );
		panel.Layout.Spacing = 0;
		Layout.Add( panel );

		if ( Package.TypeName == "collection" )
		{
			// Labelled collection-specific favourite toggle
			panel.Layout.Add( new CollectionFavouriteRow( Package, this ) );
		}
		else
		{
			// Generic favourite for non-collection packages
			bool isFav = Package.Interaction.Favourite;
			var favBtn = panel.Layout.Add( new Button.Clear(
				isFav ? "Remove Favourite" : "Add to Favourites",
				isFav ? "favorite" : "favorite_border" ) );
			favBtn.SetProperty( "is-active", isFav );
			favBtn.MouseClick = () => _ = Package.SetFavouriteAsync( !Package.Interaction.Favourite );
		}

		panel.Layout.Add( new Button.Clear( "View Online", "link" )
		{ MouseClick = () => EditorUtility.OpenFolder( Package.Url ) } );

		panel.Layout.Add( new Button.Clear( "Copy Ident", "content_copy" )
		{ MouseClick = () => { EditorUtility.Clipboard.Copy( Package.FullIdent ); Close(); } } );

		if ( AssetSystem.CanCloudInstall( Package ) )
		{
			installButton = panel.Layout.Add( new Button.Clear( "", "" )
			{ MouseClick = () => _ = Install(), Enabled = !isInstalling } );
			CheckForUpdate();
		}

		if ( CanOpenInEditor )
		{
			panel.Layout.Add( new Button.Clear( "Open in Editor", "input" )
			{ MouseClick = () => _ = OpenInEditor() } );
		}
	}

	/// <summary>
	/// Custom-painted row for the collection favourite toggle.
	/// Uses the same "grading" icon as the sidebar section and shows explicit
	/// text so the user understands favouriting adds it to the sidebar.
	/// </summary>
	sealed class CollectionFavouriteRow : Widget
	{
		readonly Package _package;
		bool _hovered;

		public CollectionFavouriteRow( Package package, Widget parent ) : base( parent )
		{
			_package = package;
			FixedHeight = 46;
			MouseTracking = true;
			Cursor = CursorShape.Finger;
		}

		protected override void OnMouseEnter() { _hovered = true; Update(); }
		protected override void OnMouseLeave() { _hovered = false; Update(); }

		protected override void OnMousePress( MouseEvent e )
		{
			if ( e.LeftMouseButton )
				_ = _package.SetFavouriteAsync( !_package.Interaction.Favourite );
		}

		protected override void OnPaint()
		{
			bool isFav = _package.Interaction.Favourite;

			// Hover / active background
			if ( _hovered || isFav )
			{
				Paint.ClearPen();
				var bgAlpha = isFav ? (_hovered ? 0.12f : 0.07f) : 0.05f;
				Paint.SetBrush( Theme.Blue.WithAlpha( bgAlpha ) );
				Paint.DrawRect( LocalRect );
			}

			// Left accent strip when active
			if ( isFav )
			{
				Paint.ClearPen();
				Paint.SetBrush( Theme.Blue.WithAlpha( 0.8f ) );
				Paint.DrawRect( new Rect( 0, 4, 3, LocalRect.Height - 8 ), 1 );
			}

			const float iconSize = 17;
			const float iconLeft = 14;
			var iconRect = new Rect( iconLeft, (LocalRect.Height - iconSize) * 0.5f, iconSize, iconSize );

			// Icon — blue when active, muted when not
			Paint.SetPen( isFav ? Theme.Blue : Theme.TextLight.WithAlpha( 0.6f ) );
			Paint.DrawIcon( iconRect, "grading", iconSize, TextFlag.Center );

			float tx = iconLeft + iconSize + 10;
			float tw = LocalRect.Width - tx - 14;
			float midY = LocalRect.Height * 0.5f;

			// Primary label
			Paint.SetPen( isFav ? Theme.Blue : Theme.Text );
			Paint.SetHeadingFont( 8, 500 );
			Paint.DrawText( new Rect( tx, midY - 12, tw, 14 ),
				isFav ? "In My Collections" : "Add to My Collections",
				TextFlag.LeftCenter );

			// Subtitle hint
			Paint.SetPen( Theme.TextLight.WithAlpha( isFav ? 0.55f : 0.4f ) );
			Paint.SetDefaultFont( 8 );
			Paint.DrawText( new Rect( tx, midY + 2, tw, 12 ),
				isFav ? "Currently shown in the sidebar" : "Will appear in the sidebar",
				TextFlag.LeftCenter );
		}
	}

	/// <summary>
	/// Small pill showing the package type (model, material, collection, …)
	/// in the top-right corner of the info header.
	/// </summary>
	sealed class TypePill : Widget
	{
		readonly string _typeName;

		public TypePill( string typeName )
		{
			_typeName = typeName?.ToLowerInvariant() ?? "";
			FixedHeight = 16;
			FixedWidth = 68; // wide enough for "Collection"
		}

		protected override void OnPaint()
		{
			if ( string.IsNullOrEmpty( _typeName ) ) return;

			string display = _typeName switch
			{
				"model" => "Model",
				"material" => "Material",
				"map" => "Map",
				"game" => "Game",
				"addon" => "Addon",
				"library" => "Library",
				"collection" => "Collection",
				_ => _typeName.ToTitleCase()
			};

			Paint.SetDefaultFont( 7 );
			var measured = Paint.MeasureText( LocalRect, display, TextFlag.Center );

			var pill = new Rect( LocalRect.Right - measured.Width - 10, LocalRect.Top, measured.Width + 10, LocalRect.Height );

			Paint.ClearPen();
			Paint.SetBrush( Color.White.WithAlpha( 0.07f ) );
			Paint.DrawRect( pill, 3 );
			Paint.ClearBrush();

			Paint.SetPen( Theme.TextLight.WithAlpha( 0.7f ) );
			Paint.DrawText( pill, display, TextFlag.Center );
		}
	}

	sealed class SeparatorLine : Widget
	{
		public SeparatorLine()
		{
			FixedHeight = 1;
			MinimumWidth = 0;
		}

		protected override void OnPaint()
		{
			Paint.ClearPen();
			Paint.SetBrush( Color.White.WithAlpha( 0.07f ) );
			Paint.DrawRect( new Rect( 10, 0, LocalRect.Width - 20, 1 ) );
		}
	}

	[Event( "package.changed" )]
	void OnPackageChanged( Package package )
	{
		if ( Package.FullIdent != package.FullIdent ) return;
		Package = package;
		Rebuild();
	}

	async void CheckForUpdate()
	{
		var local = AssetSystem.GetInstalledRevision( Package.FullIdent );

		if ( local is null )
		{
			if ( installButton.IsValid() ) installButton.Visible = false;
			return;
		}

		if ( !installButton.IsValid() ) return;
		installButton.Visible = true;
		installButton.Text = "Checking for update";
		installButton.Icon = "refresh";
		installButton.TransparentForMouseEvents = true;

		Latest = (await Package.FetchVersions( Package.FullIdent ))?.FirstOrDefault();

		if ( !installButton.IsValid() ) return;

		bool updateAvailable = Latest is not null && Latest.VersionId != local.VersionId;
		installButton.Text = updateAvailable ? "Update" : "Up to Date";
		installButton.Icon = updateAvailable ? "file_download" : "check";
		installButton.TransparentForMouseEvents = !updateAvailable;
	}

	bool CanOpenInEditor => Package.TypeName is "model" or "material";

	async Task OpenInEditor()
	{
		(await AssetSystem.InstallAsync( Package.FullIdent ))?.OpenInEditor();
	}

	async Task Install()
	{
		if ( isInstalling ) return;
		isInstalling = true;
		if ( installButton.IsValid() ) installButton.Text = "Installing..";

		await AssetSystem.InstallAsync( Package.FullIdent, false );

		if ( !IsValid ) return;
		isInstalling = false;
		Rebuild();
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.SetPen( Theme.WidgetBackground.Lighten( 0.3f ), 2 );
		Paint.SetBrush( Theme.WindowBackground );
		Paint.DrawRect( LocalRect.Shrink( 2 ), 2 );

		Paint.SetPen( Theme.WindowBackground.Darken( 0.8f ), 1 );
		Paint.ClearBrush();
		Paint.DrawRect( LocalRect.Shrink( 1 ), 2 );
	}
}

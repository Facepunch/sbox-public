namespace Sandbox.PanelGallery;

/// <summary>
/// The middle of the window - the open scenes, the scene view, and the docked asset browser and
/// console underneath it.
/// </summary>
public class ContentTabs : Panel
{
	Panel sceneTabs;
	Panel tabBar;
	Panel content;
	string filter = "";
	int tab;

	/// <summary>
	/// Something was clicked in the 3D view.
	/// </summary>
	public Action<GameObject> OnPicked { get; set; }

	public ContentTabs()
	{
		ConsolePanel.Hook();

		Style.FlexGrow = 1;
		Style.FlexDirection = FlexDirection.Column;
		Style.Overflow = OverflowMode.Hidden;

		BuildSceneTabs();

		AddChild( new ViewportToolbar() );

		var viewport = AddChild( new Viewport() );
		viewport.OnPicked = x => OnPicked?.Invoke( x );

		// Drag this to stretch the scene view up and down
		AddChild( new Splitter( viewport, 120, 1400, true ) );

		BuildTabs();
		BuildContent();
	}

	/// <summary>
	/// One tab per open scene, like the editor's. These are real sessions - clicking one makes it
	/// the active scene everywhere, not just here.
	/// </summary>
	void BuildSceneTabs()
	{
		sceneTabs = Add.Panel( "scenetabs" );
		RebuildSceneTabs();
	}

	void RebuildSceneTabs()
	{
		sceneTabs.DeleteChildren( true );

		foreach ( var session in SceneEditorSession.All )
		{
			var tab = sceneTabs.Add.Panel( "scenetab" );
			tab.SetClass( "active", session == SceneEditorSession.Active );
			tab.Icon( session.Scene?.IsEditor == true ? "grid_on" : "movie" );
			tab.Add.Label( session.Scene?.Name ?? "Untitled" );

			if ( session.HasUnsavedChanges ) tab.Add.Label( "*", "dirty" );

			tab.AddEventListener( "onclick", () =>
			{
				session.MakeActive();
				RebuildSceneTabs();
			} );
		}

		sceneTabs.Add.Panel( "grow" );
	}

	int openScenes = -1;

	void BuildTabs()
	{
		tabBar = Add.Panel( "tabbar" );

		AddTab( 0, "grid_view", "Asset Browser", Assets().Count );
		AddTab( 1, "text_snippet", "Console", ConsolePanel.Count );
	}

	readonly Dictionary<int, Sandbox.UI.Label> tabCounts = new();

	void AddTab( int index, string icon, string title, int count )
	{
		var button = tabBar.Add.Panel( "tab" );
		button.SetClass( "active", index == tab );
		button.Icon( icon );
		button.Add.Label( title );

		tabCounts[index] = button.Add.Label( $"{count:n0}", "count" );

		button.AddEventListener( "onclick", () =>
		{
			tab = index;

			foreach ( var child in tabBar.Children )
				child.SetClass( "active", child == button );

			BuildContent();
		} );
	}

	/// <summary>
	/// Filter the asset browser. Comes from the search box in its tab.
	/// </summary>
	public void SetFilter( string value )
	{
		filter = value ?? "";
		filterDirty = true;
		timeSinceFilter = 0;
	}

	bool filterDirty;
	RealTimeSince timeSinceFilter;

	void BuildContent()
	{
		content?.Delete( true );

		content = tab switch
		{
			1 => AddChild( new ConsolePanel() ),
			_ => BuildAssets(),
		};
	}

	RealTimeSince timeSinceScenesChecked;

	public override void Tick()
	{
		// Refilter a moment after typing stops - rebuilding the grid per keystroke would take the
		// search box down with it
		if ( filterDirty && timeSinceFilter > 0.25f )
		{
			filterDirty = false;
			if ( tab == 0 ) RefreshAssets();
		}

		if ( timeSinceScenesChecked < 0.5f ) return;
		timeSinceScenesChecked = 0;

		// The console fills up whether we're looking at it or not
		if ( tabCounts.TryGetValue( 1, out var consoleCount ) ) consoleCount.Text = $"{ConsolePanel.Count:n0}";

		// Scenes open and close from everywhere - notice when they do
		var hash = SceneEditorSession.All.Count * 31 + (SceneEditorSession.Active?.GetHashCode() ?? 0);

		if ( hash == openScenes ) return;

		openScenes = hash;
		RebuildSceneTabs();
	}

	/// <summary>
	/// The project's real assets. Type gives us the colour and the icon - thumbnails are Pixmaps,
	/// which the panel system can't draw, so they're not in here.
	/// </summary>
	Panel BuildAssets()
	{
		var root = Add.Panel( "assets" );

		var bar = root.Add.Panel( "bar" );

		var search = bar.AddChild( new TextInput( "Search assets..", "search" ) );
		search.OnChange = SetFilter;

		bar.Add.Panel( "grow" );
		bar.Add.Label( $"{Assets().Count:n0} assets", "count" );

		var scroll = root.Add.Panel( "scroll" );
		assetGrid = scroll.Add.Panel( "assetgrid" );

		FillAssets();

		return root;
	}

	Panel assetGrid;

	/// <summary>
	/// Refill the grid without touching the rest of the tab - the search box lives in there.
	/// </summary>
	void RefreshAssets()
	{
		if ( !assetGrid.IsValid() ) return;

		assetGrid.DeleteChildren( true );
		FillAssets();
	}

	void FillAssets()
	{
		var grid = assetGrid;
		var index = 0;

		foreach ( var asset in Assets() )
		{
			if ( filter.Length > 0 && !asset.Name.Contains( filter, StringComparison.OrdinalIgnoreCase ) ) continue;

			var card = grid.Add.Panel( "assetcard" );
			if ( index < 60 ) card.Style.Set( "transition-delay", FormattableString.Invariant( $"{index * 0.014f:0.000}s" ) );
			index++;

			var thumb = card.Add.Panel( "thumb" );
			thumb.Style.BackgroundColor = asset.AssetType.Color;
			thumb.Add.Panel( "gloss" );
			thumb.Icon( IconForAsset( asset ) );

			card.Add.Label( asset.Name, "label" );

			card.AddEventListener( "onclick", () =>
			{
				foreach ( var child in grid.Children ) child.SetClass( "selected", child == card );
			} );

			// The grid is here to be looked at, not to be a full browser
			if ( index >= 400 ) break;
		}

		assetCount = index;

		if ( index == 0 ) grid.Add.Label( "Nothing matches", "label" );
	}

	int assetCount;
	List<Asset> assets;

	/// <summary>
	/// Sorting ten thousand assets on every keystroke would be daft - do it once.
	/// </summary>
	List<Asset> Assets()
	{
		return assets ??= AssetSystem.All
			.Where( x => x.AssetType is not null && !x.AssetType.HiddenByDefault )
			.OrderBy( x => x.Name )
			.ToList();
	}

	static string IconForAsset( Asset asset ) => asset.AssetType.FileExtension switch
	{
		"vmdl" => "view_in_ar",
		"vmat" => "palette",
		"vtex" or "png" or "jpg" => "texture",
		"vsnd" or "sound" or "mp3" or "wav" => "volume_up",
		"vpcf" => "auto_awesome",
		"vmap" => "public",
		"scene" => "movie",
		"prefab" => "widgets",
		"shader" => "gradient",
		"vanmgrph" => "directions_run",
		"vfont" => "text_fields",
		_ => "description",
	};

}

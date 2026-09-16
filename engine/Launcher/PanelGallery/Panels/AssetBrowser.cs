namespace Sandbox.PanelGallery;

/// <summary>A dockable asset browser whose search and selection survive tab changes.</summary>
public class AssetBrowser : Panel
{
	string filter = "";

	public AssetBrowser()
	{
		AddClass( "editor-assets-content" );
		BuildAssets();
	}

	public override void Tick()
	{
		base.Tick();
		if ( !filterDirty || timeSinceFilter <= 0.25f ) return;
		filterDirty = false;
		RefreshAssets();
	}

	/// <summary>Filter the asset browser after typing settles.</summary>
	public void SetFilter( string value )
	{
		filter = value ?? "";
		filterDirty = true;
		timeSinceFilter = 0;
	}

	bool filterDirty;
	RealTimeSince timeSinceFilter;

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

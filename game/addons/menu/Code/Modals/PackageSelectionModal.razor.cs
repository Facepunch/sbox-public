using Sandbox;

namespace MenuProject.Modals;

/// <summary>
/// Browses and filters packages for any menu flow that needs a selection.
/// </summary>
public partial class PackageSelectionModal
{
	/// <summary>
	/// Called when a package is selected.
	/// </summary>
	public Action<Package> OnPackageSelected { get; set; }

	/// <summary>
	/// Called with the updated query when the search, sort order, or filters change.
	/// </summary>
	public Action<string> OnFilterChanged { get; set; }

	/// <summary>
	/// The package types, search terms, and filters to start with.
	/// </summary>
	public string PackageQuery
	{
		get => SearchQuery;
		set => SetQuery( value );
	}

	MenuProject.UI.PackageList packageList;
	Package.FindResult Result => packageList?.Result;
	readonly List<string> selectedTags = new();
	readonly Dictionary<string, string> selectedCategories = new();
	readonly List<string> packageTypes = new();
	readonly List<string> requiredFilters = new();
	string sortOrder = "trending";
	string searchText = "";
	string debouncedSearch = "";
	bool searchPending;
	RealTimeSince timeSinceSearchEdited;
	string lastReportedQuery;

	static readonly Package.SortOrder FavouritesSortOrder = new( "favourite", "Your favourites", "bookmarks" );

	static readonly Package.SortOrder[] GameSortOrders =
	[
		new() { Name = "trending", Title = "Trending", Icon = "trending_up" },
		new() { Name = "rankday", Title = "Popular today", Icon = "local_fire_department" },
		new() { Name = "rankweek", Title = "Popular this week", Icon = "date_range" },
		new() { Name = "rankmonth", Title = "Popular this month", Icon = "calendar_month" },
		new() { Name = "quality", Title = "Most loved", Icon = "favorite" },
		new() { Name = "newest", Title = "New releases", Icon = "new_releases" },
		new() { Name = "updated", Title = "Recently updated", Icon = "schedule" },
		new() { Name = "hiddengem", Title = "Hidden gems", Icon = "diamond" },
		FavouritesSortOrder
	];

	static readonly Package.SortOrder[] DefaultSortOrders =
	[
		new() { Name = "trending", Title = "Trending", Icon = "trending_up" }
	];

	bool IsGamePicker => packageTypes.Count == 1 && packageTypes[0] == "game";

	IEnumerable<Package.SortOrder> SortOrders
	{
		get
		{
			if ( IsGamePicker ) return GameSortOrders;

			var orders = Result?.Orders is { Length: > 0 } availableOrders ? availableOrders : DefaultSortOrders;

			if ( packageTypes.Count == 1 && packageTypes[0] == "map" && !orders.Any( order => order.Name == FavouritesSortOrder.Name ) )
			{
				return orders.Append( FavouritesSortOrder );
			}

			return orders;
		}
	}

	string PackageNoun => packageTypes.Count == 1 ? packageTypes[0] switch
	{
		"game" => "game",
		"map" => "map",
		"model" => "model",
		"material" => "material",
		"sound" => "sound",
		"prefab" => "prefab",
		"shader" => "shader",
		"texture" => "texture",
		_ => "package"
	} : "package";

	bool HasFilters => !string.IsNullOrEmpty( searchText ) || selectedTags.Count > 0 || selectedCategories.Count > 0;

	string SearchQuery => $"{string.Join( " ", packageTypes.Select( type => $"type:{type}" ) )} {string.Join( " ", requiredFilters )} sort:{sortOrder} {debouncedSearch} {string.Join( " ", selectedTags.Select( tag => $"+{tag}" ) )} {string.Join( " ", selectedCategories.Select( category => $"{category.Key}:{category.Value}" ) )}".Trim();

	string ResultsTitle => !string.IsNullOrWhiteSpace( debouncedSearch ) ? "Search results" : SortOrders.FirstOrDefault( order => order.Name == sortOrder ).Title ?? Result?.Orders?.FirstOrDefault( order => order.Name == sortOrder ).Title ?? "Results";

	void SetQuery( string query )
	{
		packageTypes.Clear();
		requiredFilters.Clear();
		selectedTags.Clear();
		selectedCategories.Clear();
		sortOrder = "trending";
		searchPending = false;
		var searchTerms = new List<string>();

		foreach ( var token in (query ?? "").Split( ' ', StringSplitOptions.RemoveEmptyEntries ) )
		{
			if ( token.StartsWith( "type:" ) )
			{
				packageTypes.Add( token[5..] );
			}
			else if ( token.StartsWith( "target:" ) )
			{
				// A map chosen for a game must stay compatible when the user clears filters.
				requiredFilters.Add( token );
			}
			else if ( token.StartsWith( "sort:" ) )
			{
				sortOrder = token[5..];
			}
			else if ( token.StartsWith( '+' ) )
			{
				selectedTags.Add( token[1..] );
			}
			else if ( token.Contains( ':' ) )
			{
				var parts = token.Split( ':', 2 );
				selectedCategories[parts[0]] = parts[1];
			}
			else
			{
				searchTerms.Add( token );
			}
		}

		searchText = string.Join( " ", searchTerms );
		debouncedSearch = searchText;
		lastReportedQuery = SearchQuery;
	}

	void OnSearchEdited( string text )
	{
		searchText = text;

		if ( IsMountedSource )
		{
			RefreshMountedMaps();
			StateHasChanged();
			return;
		}

		searchPending = true;
		timeSinceSearchEdited = 0;
	}

	void ToggleMultiplayer()
	{
		if ( selectedTags.Contains( "multiplayer" ) )
		{
			selectedTags.Remove( "multiplayer" );
		}
		else
		{
			selectedTags.Add( "multiplayer" );
		}
	}

	void ClearFilters()
	{
		searchText = "";
		debouncedSearch = "";
		searchPending = false;

		if ( IsMountedSource )
		{
			RefreshMountedMaps();
			return;
		}

		selectedTags.Clear();
		selectedCategories.Clear();
	}

	/// <summary>
	/// Debounces searches and reports changes from the picker controls.
	/// </summary>
	public override void Tick()
	{
		base.Tick();

		if ( IsMountedSource ) return;

		if ( searchPending && timeSinceSearchEdited >= 0.3f )
		{
			searchPending = false;
			debouncedSearch = searchText;
			StateHasChanged();
		}

		var query = SearchQuery;
		if ( query == lastReportedQuery ) return;

		lastReportedQuery = query;
		OnFilterChanged?.Invoke( query );
	}

	void SelectPackage( Package package )
	{
		CloseModal( true );
		OnPackageSelected?.Invoke( package );
	}

	protected override int BuildHash() => HashCode.Combine( Result, SearchQuery, searchText, packageList?.FoundPackages, ShowMapSources, selectedMount, mountedMaps );
}

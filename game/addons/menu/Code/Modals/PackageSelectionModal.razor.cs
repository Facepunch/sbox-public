using MenuProject.UI;
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

	bool IsGamePicker => packageTypes.Count == 1 && packageTypes[0] == "game";

	/// <summary>
	/// The Games page's sorts, whatever it's picking - a row of icons that fits the sidebar
	/// </summary>
	static IEnumerable<Package.SortOrder> SortOrders => GamePackageSortOrder.Orders;

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

	string ResultsTitle => packageSource == PackageSource.Subscribed ? Language.GetPhrase( "menu.workshop.subscribed_packages", new() { ["noun"] = PackageNoun } )
		: !string.IsNullOrWhiteSpace( debouncedSearch ) ? "#menu.games.search_results" : SortOrders.FirstOrDefault( order => order.Name == sortOrder ).Title ?? Result?.Orders?.FirstOrDefault( order => order.Name == sortOrder ).Title ?? "#menu.games.results";

	void SetQuery( string query )
	{
		packageTypes.Clear();
		requiredFilters.Clear();
		selectedTags.Clear();
		selectedCategories.Clear();
		sortOrder = "trending";
		packageSource = PackageSource.Browse;
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

		if ( IsWorkshopSource )
			RefreshWorkshopPackages();

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

	protected override int BuildHash() => HashCode.Combine( Result, SearchQuery, searchText, packageList?.FoundPackages, ShowMapSources, selectedMount, mountedMaps, workshopPackages );
}

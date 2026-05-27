namespace Sandbox.Services;

/// <summary>
/// A package type/category (e.g. "game", "model", "addon"), along with its allowed licenses and counts.
/// The list is fetched once per session via <see cref="LoadAsync"/> — typically on startup — and then
/// accessed synchronously via <see cref="All"/> and <see cref="Get"/>.
/// </summary>
public sealed class PackageType
{
	/// <summary>
	/// Internal name of the type (e.g. "game", "model").
	/// </summary>
	public string Name { get; init; }

	/// <summary>
	/// Display title of the type.
	/// </summary>
	public string Title { get; init; }

	/// <summary>
	/// Icon for this type.
	/// </summary>
	public string Icon { get; init; }

	/// <summary>
	/// Number of packages of this type that are visible.
	/// </summary>
	public int Count { get; init; }

	/// <summary>
	/// Total number of packages of this type, including hidden/archived.
	/// </summary>
	public int TotalCount { get; init; }

	/// <summary>
	/// Whether this type is shown on the index page.
	/// </summary>
	public bool ShowOnIndex { get; init; }

	/// <summary>
	/// Whether packages of this type can offer backend services.
	/// </summary>
	public bool HasServices { get; init; }

	/// <summary>
	/// Licenses applicable to assets within packages of this type.
	/// </summary>
	public IReadOnlyList<PackageLicense> AssetLicenses { get; init; }

	/// <summary>
	/// Licenses applicable to the package itself when it's software.
	/// </summary>
	public IReadOnlyList<PackageLicense> SoftwareLicenses { get; init; }

	static IReadOnlyList<PackageType> _all = Array.Empty<PackageType>();

	/// <summary>
	/// All known package types. Empty until <see cref="LoadAsync"/> has completed.
	/// </summary>
	public static IReadOnlyList<PackageType> All => _all;

	/// <summary>
	/// Find a package type by its <see cref="Name"/>. Returns null if not found, or if
	/// <see cref="LoadAsync"/> hasn't been called yet.
	/// </summary>
	public static PackageType Get( string name )
	{
		if ( string.IsNullOrEmpty( name ) ) return null;

		foreach ( var t in _all )
		{
			if ( string.Equals( t.Name, name, StringComparison.OrdinalIgnoreCase ) )
				return t;
		}

		return null;
	}

	public static PackageType Model => Get( "model" );
	public static PackageType Material => Get( "material" );
	public static PackageType Game => Get( "game" );
	public static PackageType Map => Get( "map" );
	public static PackageType Clothing => Get( "clothing" );
	public static PackageType Sound => Get( "sound" );
	public static PackageType Addon => Get( "addon" );
	public static PackageType Collection => Get( "collection" );
	public static PackageType Library => Get( "library" );
	public static PackageType Prefab => Get( "prefab" );

	/// <summary>
	/// Populate the type list. Reads the last cached copy from disk first so the game has
	/// data immediately (and works offline / when the backend is down), then fetches fresh
	/// data from the API and writes that back to disk. Safe to call again to refresh.
	/// </summary>
	internal static Task LoadAsync()
	{
		return ServiceCache.LoadAsync<PackageTypeOverview[]>(
			"package_types",
			() => Sandbox.Backend.Package.GetTypes( 100 ),
			dtos => _all = dtos.Select( From ).ToArray() );
	}

	internal static PackageType From( PackageTypeOverview x )
	{
		return new PackageType
		{
			Name = x.Name,
			Title = x.Title,
			Icon = x.Icon,
			Count = x.Count,
			TotalCount = x.TotalCount,
			ShowOnIndex = x.ShowOnIndex,
			HasServices = x.HasServices,
			AssetLicenses = x.AssetLicenses ?? Array.Empty<PackageLicense>(),
			SoftwareLicenses = x.SoftwareLicenses ?? Array.Empty<PackageLicense>()
		};
	}
}

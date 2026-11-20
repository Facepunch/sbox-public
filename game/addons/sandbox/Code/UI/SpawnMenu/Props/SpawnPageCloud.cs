namespace Sandbox;

/// <summary>
/// Local props followed by cloud model packages.
/// </summary>
public class SpawnPageCloud : CloudSpawnList
{
	public string Category { get; set; } = "";
	public string LocalCategory { get; set; }
	public bool IncludeLocalProps { get; set; }

	protected override string PackageType => "model";
	protected override string PackageQuery => string.IsNullOrEmpty( Category ) ? null : $"category:{Category}";
	protected override string PackageIdent( Package package ) => $"prop:{package.FullIdent}";

	string LocalFilterCategory => string.IsNullOrWhiteSpace( LocalCategory ) ? Category : LocalCategory;

	protected override IEnumerable<Entry> FindLocalEntries()
	{
		if ( !IncludeLocalProps && string.IsNullOrWhiteSpace( LocalFilterCategory ) ) return [];

		var entries = string.IsNullOrWhiteSpace( LocalFilterCategory )
			? LocalProps.All
			: LocalProps.All.Where( entry => string.Equals( entry.Category, LocalFilterCategory, StringComparison.OrdinalIgnoreCase ) );

		if ( !string.IsNullOrWhiteSpace( Filter ) )
			entries = entries.Where( entry => entry.DisplayName.Contains( Filter, StringComparison.CurrentCultureIgnoreCase ) || entry.Path.Contains( Filter, StringComparison.OrdinalIgnoreCase ) );

		return entries
			.OrderBy( entry => entry.DisplayName, StringComparer.CurrentCultureIgnoreCase ).ThenBy( entry => entry.Path )
			.Select( entry => new Entry( $"prop:{entry.Path}", entry.Title ?? entry.DisplayName ) );
	}
}

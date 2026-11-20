namespace Sandbox;

/// <summary>
/// Local scripted entities followed by cloud entity packages.
/// </summary>
public class EntityListCloud : CloudSpawnList
{
	public string Query { get; set; } = "";
	public string LocalCategory { get; set; }
	public bool IncludeLocalSubcategories { get; set; } = true;
	public bool IncludeLocalEntities { get; set; }
	public IReadOnlyCollection<string> ExcludedLocalCategoryRoots { get; set; } = [];

	protected override string PackageType => "sent";
	protected override string PackageQuery => Query;
	protected override string EmptyTitle => "#spawnmenu.entity.no_results";

	protected override IEnumerable<Entry> FindLocalEntries()
	{
		if ( !IncludeLocalEntities && string.IsNullOrWhiteSpace( LocalCategory ) ) return [];

		var entities = ResourceLibrary.GetAll<ScriptedEntity>()
			.Where( entity => !entity.IsRemote && ( !entity.Developer || ServerSettings.ShowDeveloperEntities ) );

		if ( !string.IsNullOrWhiteSpace( LocalCategory ) )
			entities = entities.Where( entity => SpawnMenuCategory.Matches( entity.Category, LocalCategory, IncludeLocalSubcategories ) );
		if ( ExcludedLocalCategoryRoots?.Count > 0 )
			entities = entities.Where( entity => !ExcludedLocalCategoryRoots.Any( category =>
				string.Equals( SpawnMenuCategory.Root( entity.Category ), category, StringComparison.OrdinalIgnoreCase ) ) );

		if ( !string.IsNullOrWhiteSpace( Filter ) )
			entities = entities.Where( entity => (LocalizedText.Resolve( entity.Title ) ?? "").Contains( Filter, StringComparison.CurrentCultureIgnoreCase ) || entity.ResourcePath.Contains( Filter, StringComparison.OrdinalIgnoreCase ) );

		return entities
			.OrderBy( entity => LocalizedText.Resolve( entity.Title ), StringComparer.CurrentCultureIgnoreCase ).ThenBy( entity => entity.ResourcePath )
			.Select( entity => new Entry( $"entity:{entity.ResourcePath}", entity.Title, entity.Developer ) );
	}
}

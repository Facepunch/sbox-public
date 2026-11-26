namespace Sandbox;

/// <summary>
/// Browses Workshop models in the selected category.
/// </summary>
public class SpawnPageCloud : CloudSpawnList
{
	/// <summary>
	/// Category to search, or an empty string for all Workshop models.
	/// </summary>
	public string Category { get; set; } = "";

	protected override string PackageType => "model";
	protected override string PackageQuery => string.IsNullOrEmpty( Category ) ? null : $"category:{Category}";
	protected override string PackageIdent( Package package ) => $"prop:{package.FullIdent}";
}

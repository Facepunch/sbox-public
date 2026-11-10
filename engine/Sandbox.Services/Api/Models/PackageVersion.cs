namespace Sandbox.Services;

/// <summary>
/// Information about a specific package version
/// </summary>
public class PackageVersion
{
	public long Id { get; set; }
	public string Changes { get; set; }
	public long FileCount { get; set; }
	public long TotalSize { get; set; }
	public long Hash { get; set; }
	public string ManifestUrl { get; set; }
	public DateTimeOffset Created { get; set; }
	public int EngineVersion { get; set; }
	public string Meta { get; set; }

	/// <summary>
	/// Extra metadata extracted from the version's files (model stats etc), or null if not processed.
	/// </summary>
	public BaseMetaData Extra { get; set; }

	// backwards compatibility
	public long AssetVersionId { get; set; }
}

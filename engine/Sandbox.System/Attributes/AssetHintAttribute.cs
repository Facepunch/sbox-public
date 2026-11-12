namespace Sandbox;

/// <summary>
/// Sets the initial search in an asset picker. Wildcards match asset filenames, or relative paths
/// when the pattern contains a slash. The user can edit or clear the search to choose other assets.
/// </summary>
[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field )]
public sealed class AssetHintAttribute : Attribute
{
	/// <summary>
	/// The initial asset search, such as <c>*.water.vmat</c>.
	/// </summary>
	public string Pattern { get; }

	/// <summary>
	/// Suggests assets matching the given search pattern.
	/// </summary>
	public AssetHintAttribute( string pattern )
	{
		Pattern = pattern;
	}
}

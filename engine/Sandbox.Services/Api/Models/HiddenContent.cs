namespace Sandbox.Services;

/// <summary>
/// A player's explicit discovery and search exclusions. Hiding an organization doesn't add its packages
/// to <see cref="Packages"/> - a package whose organization is hidden is hidden too.
/// </summary>
public class HiddenContent
{
	/// <summary>Package idents (org.package).</summary>
	public string[] Packages { get; set; } = [];

	/// <summary>Organization idents.</summary>
	public string[] Organizations { get; set; } = [];
}

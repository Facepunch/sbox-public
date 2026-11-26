namespace Sandbox.Services;

/// <summary>
/// A player's explicit discovery and search exclusions. Organizations are not expanded into packages.
/// </summary>
public class HiddenContent
{
	/// <summary>Package idents (org.package).</summary>
	public string[] Packages { get; set; } = [];

	/// <summary>Organization idents.</summary>
	public string[] Organizations { get; set; } = [];
}

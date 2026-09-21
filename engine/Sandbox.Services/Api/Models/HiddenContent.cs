namespace Sandbox.Services;

/// <summary>
/// A player's explicit discovery and search exclusions. Organization IDs are not expanded into packages.
/// </summary>
public class HiddenContent
{
	public long[] PackageIds { get; set; } = [];
	public long[] OrganizationIds { get; set; } = [];
}

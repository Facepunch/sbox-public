namespace Sandbox.Services;

public struct PackageLicense
{
	public LicenseType Type { get; set; }
	public string Name { get; set; }
	public string Title { get; set; }
	public string Icon { get; set; }
	public string Description { get; set; }
	public string Url { get; set; }
}

namespace Sandbox.Services;

public struct PackageTypeOverview
{
	public string Name { get; set; }
	public string Title { get; set; }
	public string Icon { get; set; }
	public int Count { get; set; }
	public int TotalCount { get; set; }
	public bool ShowOnIndex { get; set; }
	public bool HasServices { get; set; }
	public PackageLicense[] AssetLicenses { get; set; }
	public PackageLicense[] SoftwareLicenses { get; set; }
}

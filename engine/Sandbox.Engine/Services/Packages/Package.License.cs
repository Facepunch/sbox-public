namespace Sandbox;

public partial class Package
{
	/// <summary>
	/// The license covering this package's art/content assets, identified by name (e.g. "CC0",
	/// "CC_BY"). Null when no license is specified. Matches <c>PackageLicense.Name</c> in the
	/// license catalog, so it can be used to look up the full license details.
	/// </summary>
	public string AssetLicense { get; internal set; }

	// Maps the backend license enum to its catalog name (see Sandbox.Services.Licensing.Assets).
	// A switch over literals so it's allocation-free and independent of the enum's member names.
	internal static string LicenseName( Sandbox.LicenseType type )
	{
		return type switch
		{
			Sandbox.LicenseType.CC0 => "CC0",
			Sandbox.LicenseType.CCBYNCND => "CC_BYNCND",
			Sandbox.LicenseType.CCBY => "CC_BY",
			Sandbox.LicenseType.CCBYSA => "CC_BYSA",
			_ => null,
		};
	}
}

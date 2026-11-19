namespace Sandbox;

public partial class Package
{
	/// <summary>
	/// Check if the package is installed and mounted
	/// </summary>
	public bool IsMounted()
	{
		if ( IsWorkshopMap )
			return WorkshopItems.Current?.IsMounted( WorkshopId ) == true;

		return ServerPackages.Get( FullIdent )?.IsMounted == true;
	}
}

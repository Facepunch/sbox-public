namespace Sandbox;

public partial class Package
{
	/// <summary>
	/// Check if the package is installed and mounted
	/// </summary>
	public bool IsMounted()
	{
		var download = ServerPackages.Get( FullIdent );

		// fully good
		if ( download != null && download.IsMounted )
			return true;

		// fully in progress
		if ( download != null && download.IsDownloading )
			return false;

		// fully fucked
		if ( download != null && download.IsErrored )
			return false;

		return false;
	}
}

using Sandbox.Services;
using System.Threading;

namespace Sandbox;

/// <summary>
/// Getting at installed packages when we can't reach the backend
/// </summary>
public partial class Package
{
	/// <summary>
	/// Package information without the backend - what the backend told us when we last installed it, or
	/// subscribed to its workshop item. Null if we've got nothing.
	/// </summary>
	static Package FetchOffline( string packageIdent )
	{
		if ( !ServiceCache.TryReadDisk<PackageDto>( OfflineCacheKey( packageIdent ), out var dto ) )
			return null;

		Log.Info( $"Couldn't reach the backend, using what we knew about {packageIdent} when we installed it" );

		var package = new RemotePackage();
		package.UpdateFromDto( dto );
		return package;
	}

	/// <summary>
	/// Keep what the backend told us about this package, so it can be installed again offline
	/// </summary>
	internal void RememberForOffline()
	{
		if ( this is not RemotePackage { Dto: { } dto } )
			return;

		ServiceCache.WriteDisk( OfflineCacheKey( FullIdent ), dto );
	}

	static string OfflineCacheKey( string packageIdent ) => $"package.{packageIdent}";

	static string StripVersion( string packageIdent )
	{
		var hash = packageIdent.IndexOf( '#' );
		return hash < 0 ? packageIdent : packageIdent[..hash];
	}
}

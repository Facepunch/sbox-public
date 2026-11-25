using Sandbox.Engine;
using Sentry;

namespace Sandbox;

public partial class Package
{
	Package GetMountSource()
	{
		if ( Project.FindByIdent( Org.Ident, Ident ) is { } project ) return project.Package;
		if ( IsRemote ) return this;

		return TryGetCachedRemote( FullIdent, out var published, allowPartial: false ) ? published : null;
	}

	/// <summary>
	/// Mounts the package files and optionally loads its assemblies into the current game.
	/// </summary>
	public async Task<BaseFileSystem> MountAsync( bool withCode = false )
	{
		var source = GetMountSource() ?? await FetchAsync( FullIdent, false );
		return source is null ? null : await source.MountResolvedAsync( withCode );
	}

	/// <summary>
	/// Mounts the selected source. Callers that release a mount retain this package as its owner.
	/// </summary>
	internal async Task<BaseFileSystem> MountResolvedAsync( bool withCode = false )
	{
		SentrySdk.AddBreadcrumb( $"Mounting {FullIdent}", "package.mount" );

		if ( IsWorkshopMap )
			return await MountWorkshopMapAsync();

		var fs = await ServerPackages.DownloadAndMount( GetVersionedIdent() );
		if ( fs is not null && withCode && IGameInstanceDll.Current is { } game )
			await game.LoadPackageAssembliesAsync( this );

		return fs;
	}

	/// <summary>
	/// Check if the package is installed and mounted
	/// </summary>
	public bool IsMounted()
	{
		var source = GetMountSource();
		if ( source is null ) return false;
		if ( source.IsWorkshopMap )
			return WorkshopItems.Current?.IsMounted( source.WorkshopId ) == true;

		var download = ServerPackages.Get( source.GetVersionedIdent() ) ?? ServerPackages.Get( source.FullIdent );
		if ( download is not { IsMounted: true, activePackage.FileSystem: not null } ) return false;

		return (download.activePackage.Package as LocalPackage) == (source as LocalPackage);
	}
}

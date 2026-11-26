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
	/// Mounts the package's content files for the current game.
	/// </summary>
	public async Task<BaseFileSystem> MountAsync()
	{
		var source = GetMountSource() ?? await FetchAsync( FullIdent, false );
		if ( source is null || source.IsRemoteMap ) return null;

		SentrySdk.AddBreadcrumb( $"Mounting {source.FullIdent}", "package.mount" );

		return await ServerPackages.DownloadAndMount( source.GetVersionedIdent() );
	}

	/// <summary>
	/// Mounts the package's content files. The <paramref name="withCode"/> argument is ignored.
	/// </summary>
	[Obsolete( "Use MountAsync(). Mounting a package only loads its content." )]
	public Task<BaseFileSystem> MountAsync( bool withCode ) => MountAsync();

	/// <summary>
	/// Fraction of this revision's content bytes downloaded while mounting it in the current game.
	/// Returns null when the transfer has no byte progress to report.
	/// </summary>
	public float? DownloadFraction => ServerPackages.Get( GetVersionedIdent() ) is { IsDownloading: true } mount ? mount.DownloadFraction : null;

	/// <summary>
	/// Check if the package is installed and mounted
	/// </summary>
	public bool IsMounted()
	{
		var source = GetMountSource();
		if ( source is null ) return false;

		var download = ServerPackages.Get( source.GetVersionedIdent() ) ?? ServerPackages.Get( source.FullIdent );
		if ( download is not { IsMounted: true, activePackage.FileSystem: not null } ) return false;

		return (download.activePackage.Package as LocalPackage) == (source as LocalPackage);
	}
}

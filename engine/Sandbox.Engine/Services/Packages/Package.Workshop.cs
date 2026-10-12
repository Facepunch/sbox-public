using Sandbox.Menu;
using Sandbox.Services;
using System.Threading;

namespace Sandbox;

public partial class Package
{
	/// <summary>
	/// The Steam Workshop item this package's files are installed from, or 0 if they're downloaded from the backend.
	/// </summary>
	public ulong WorkshopId => GetCachedMeta<ulong>( "WorkshopId", 0 );

	/// <summary>
	/// Are this package's files installed from its Steam Workshop item, rather than downloaded from the backend?
	/// Only maps - clothing has a workshop item too, but its files are on the backend.
	/// </summary>
	public bool InstallsFromWorkshop => WorkshopId != 0 && string.Equals( TypeName, "map", StringComparison.OrdinalIgnoreCase );

	/// <summary>
	/// The workshop item this package's files are installed from, or 0 if they aren't, or we can't tell. Search results
	/// don't have the package's meta - for those it's the full package if we've fetched it.
	/// </summary>
	internal ulong WorkshopFilesId
	{
		get
		{
			if ( InstallsFromWorkshop )
				return WorkshopId;

			return TryGetCached( FullIdent, out var full, false ) && full.InstallsFromWorkshop ? full.WorkshopId : 0;
		}
	}

	/// <summary>
	/// Install the latest of this package's workshop item, and mount its files on <paramref name="fs"/> if there is
	/// one. The item's folder is laid out as the package's files are.
	/// </summary>
	async Task<bool> DownloadFromWorkshop( PackageFileSystem fs, CancellationToken token, PackageLoadOptions options )
	{
		var progress = LoadingProgress.Create( $"Downloading '{Title}'" );

		// Its place among the load's downloads - unless another download of it's already filling that in
		var tracked = global::Sandbox.LoadingScreen.TrackDownload( FullIdent, Title );
		if ( tracked is { IsDownloading: true } ) tracked = null;

		void OnProgress( Workshop.State state )
		{
			if ( state.Total == 0 ) return;

			progress.TotalSize = state.Total;
			progress.Fraction = state.Fraction;
			options.Loading?.LoadingProgress( progress );

			if ( tracked is not null )
			{
				tracked.IsDownloading = true;
				tracked.TotalSize = state.Total;
				tracked.Downloaded = state.Downloaded;
			}
		}

		string folder;

		try
		{
			folder = await Workshop.InstallAsync( WorkshopId, OnProgress, token );
		}
		finally
		{
			if ( tracked is not null )
			{
				tracked.IsDownloading = false;
				tracked.IsComplete = true;
			}
		}

		if ( folder is null )
		{
			Log.Warning( $"Couldn't install workshop item {WorkshopId} for {FullIdent}" );
			return false;
		}

		progress.Title = $"Download '{Title}' Complete";
		progress.Fraction = 1;
		options.Loading?.LoadingProgress( progress );

		// Prefetching, it just needed installing
		if ( fs is null )
			return true;

		var files = System.IO.Directory.EnumerateFiles( folder, "*", System.IO.SearchOption.AllDirectories )
			.Select( x => new ManifestSchema.File { Path = System.IO.Path.GetRelativePath( folder, x ).Replace( '\\', '/' ) } )
			.ToArray();

		foreach ( var file in DownloadableFiles( files, options.SkipAssetDownload ) )
		{
			var absolutePath = System.IO.Path.Combine( folder, file.Path );

			AssetDownloadCache.Mount( fs.Redirect, file.Path, absolutePath );
		}

		RememberForOffline();
		return true;
	}
}

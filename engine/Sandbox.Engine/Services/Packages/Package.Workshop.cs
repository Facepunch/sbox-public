using Sandbox.Menu;
using Sandbox.Services;
using System.Diagnostics;
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

		// How much Steam sent - nothing if it already had the latest
		long downloadedSize = 0;

		// When we'd asked Steam for it (progress only comes once we have), and when the first bytes came in
		var sw = Stopwatch.StartNew();
		double? asked = null;
		double? started = null;

		void OnProgress( Workshop.State state )
		{
			asked ??= sw.Elapsed.TotalSeconds;
			if ( state.Downloaded > 0 ) started ??= sw.Elapsed.TotalSeconds;

			if ( state.Total == 0 ) return;

			downloadedSize = Math.Max( downloadedSize, state.Total );
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

		// Steam saying how the download went - what went wrong, or null if it worked
		var ended = new TaskCompletionSource<string>( TaskCreationOptions.RunContinuationsAsynchronously );

		void OnEnded( WorkshopDownload download )
		{
			if ( download.Id == WorkshopId )
				ended.TrySetResult( download.Error is null ? null : $"{download.Error}: {download.ErrorHelp}" );
		}

		//
		// Report how it went, as a backend download reports itself - so the two can be compared. The time's split
		// up: checking the item's up to date until we asked Steam for it, Steam getting going until it started
		// sending, then the transfer - the part to compare against a backend download.
		//
		void Record( string outcome, string reason, double seconds, string installedIn )
		{
			var askedAt = asked ?? seconds;
			var startedAt = started ?? seconds;

			var metric = new Api.Events.EventRecord( "package.download" );
			metric.SetValue( "ident", FullIdent );
			metric.SetValue( "source", "workshop" );
			metric.SetValue( "workshop_id", WorkshopId );
			metric.SetValue( "outcome", outcome );
			if ( reason is not null ) metric.SetValue( "reason", reason );
			metric.SetValue( "size_sum", downloadedSize );
			metric.SetValue( "prefetch", fs is null );
			metric.SetValue( "time", seconds );
			metric.SetValue( "check_time", askedAt );
			metric.SetValue( "wait_time", startedAt - askedAt );
			metric.SetValue( "transfer_time", seconds - startedAt );

			if ( outcome == "ok" )
			{
				var fileCount = System.IO.Directory.EnumerateFiles( installedIn, "*", System.IO.SearchOption.AllDirectories ).Count();
				metric.SetValue( "files", fileCount );
				Api.Activity.CurrentLoad?.Downloaded( downloadedSize, fileCount, seconds );
			}

			metric.Submit();
		}

		string folder;
		double? installSeconds = null;
		string error;

		Workshop.DownloadEnded += OnEnded;

		try
		{
			folder = await Workshop.InstallAsync( WorkshopId, OnProgress, token );
			installSeconds = sw.Elapsed.TotalSeconds;

			// Steam's result can land a moment after the item says it's done
			if ( asked is not null && !ended.Task.IsCompleted )
				await Task.WhenAny( ended.Task, Task.Delay( 1000, token ) );

			error = ended.Task.IsCompleted ? ended.Task.Result : null;
		}
		catch ( OperationCanceledException ) when ( token.IsCancellationRequested )
		{
			// Only a download that got going, not one given up on while checking it's up to date or after it finished
			if ( asked is not null && installSeconds is null )
				Record( "cancelled", null, sw.Elapsed.TotalSeconds, null );

			throw;
		}
		finally
		{
			Workshop.DownloadEnded -= OnEnded;

			if ( tracked is not null )
			{
				tracked.IsDownloading = false;
				tracked.IsComplete = true;
			}
		}

		// Failing to update can still leave the old version installed to carry on with - it's still a failed download
		if ( folder is null || error is not null )
			Record( "failed", error ?? "Steam unavailable", installSeconds.Value, folder );
		else if ( downloadedSize > 0 )
			Record( "ok", null, installSeconds.Value, folder );
		else
			Api.Activity.CurrentLoad?.Downloaded( 0, 0, 0 );

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

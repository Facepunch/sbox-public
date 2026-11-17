using Sandbox.Internal;
using Sandbox.Menu;
using Sandbox.Services;
using System.Diagnostics;
using System.Threading;

namespace Sandbox;

internal static partial class Workshop
{
	/// <summary>
	/// Explains why a client cannot join with different installed Workshop content.
	/// </summary>
	internal static string GetVersionMismatchMessage( IEnumerable<string> titles )
	{
		return $"You can't join this server because its version of {string.Join( ", ", titles )} is different from yours. One of you has an older version - the server needs to update, or yours does.";
	}

	/// <summary>
	/// Installs Workshop content with shared loading progress and download diagnostics.
	/// </summary>
	internal static async Task<string> InstallContentAsync( ulong id, string title, ILoadingInterface loading, CancellationToken token, bool prefetch = false, string ident = null )
	{
		using var textureDownload = TextureWebStreaming.BeginDownload();
		ident ??= id.ToString( System.Globalization.CultureInfo.InvariantCulture );
		var progress = LoadingProgress.Create( $"Downloading '{title}'" );

		// Its place among the load's downloads - unless another download of it's already filling that in
		var tracked = LoadingScreen.TrackDownload( ident, title );
		if ( tracked is { IsDownloading: true } ) tracked = null;

		// How much Steam sent - nothing if it already had the latest
		long downloadedSize = 0;

		// When we'd asked Steam for it (progress only comes once we have), and when the first bytes came in
		var sw = Stopwatch.StartNew();
		double? asked = null;
		double? started = null;

		void OnProgress( State state )
		{
			asked ??= sw.Elapsed.TotalSeconds;
			if ( state.Downloaded > 0 ) started ??= sw.Elapsed.TotalSeconds;

			if ( state.Total == 0 ) return;

			downloadedSize = Math.Max( downloadedSize, state.Total );
			progress.TotalSize = state.Total;
			progress.Fraction = state.Fraction;
			loading?.LoadingProgress( progress );

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
			if ( download.Id == id )
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
			metric.SetValue( "ident", ident );
			metric.SetValue( "source", "workshop" );
			metric.SetValue( "workshop_id", id );
			metric.SetValue( "outcome", outcome );
			if ( reason is not null ) metric.SetValue( "reason", reason );
			metric.SetValue( "size_sum", downloadedSize );
			metric.SetValue( "prefetch", prefetch );
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

		DownloadEnded += OnEnded;

		try
		{
			folder = await InstallAsync( id, OnProgress, token );
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
			DownloadEnded -= OnEnded;

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
			Log.Warning( $"Couldn't install workshop item {id} for {ident}" );
			return null;
		}

		progress.Title = $"Download '{title}' Complete";
		progress.Fraction = 1;
		loading?.LoadingProgress( progress );

		return folder;
	}
}

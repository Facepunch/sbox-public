using Sandbox.Engine;
using Sandbox.Menu;
using Sentry;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace Sandbox;


public partial class Package
{
	struct FileDownloadEntry
	{
		public ManifestSchema.File File;
		public string AbsolutePath;
		public ulong Crc;
	}

	/// <summary>
	/// Don't blindly download every file in the manifest. We can filter them here.
	/// </summary>
	static bool FilterFileDownloads( ManifestSchema.File f )
	{
		if ( !AssetDownloadCache.IsLegalDownload( f.Path ) )
			return false;

		return true;
	}

	/// <summary>
	/// Download a package to a temporary location and return a filesystem with its contents
	/// </summary>
	internal async Task<PackageFileSystem> Download( CancellationToken token = default, PackageLoadOptions options = default )
	{
		// TODO - if we have a download in progress then return, or wait for it (?)
		// The filesystem is technically immutable other than disposing and adding more shit to it
		if ( Revision == null )
			return null;

		var rev = Revision;
		if ( rev == null ) return null;

		options.Loading?.LoadingProgress( LoadingProgress.Create( $"Fetching '{Title}' Information" ) );

		// make sure manifest is downloaded
		await rev.DownloadManifestAsync( token );

		if ( rev.Manifest == null ) return null;

		var entries = rev.Manifest.Files ?? Array.Empty<ManifestSchema.File>();

		// filter out files we're never going to download
		entries = entries.Where( FilterFileDownloads ).ToArray();

		if ( options.SkipAssetDownload )
		{
			entries = entries.Where( x => x.Path.StartsWith( ".bin" ) ).ToArray();
		}

		var downloadQueue = new ConcurrentBag<FileDownloadEntry>();
		var fs = new PackageFileSystem();

		//
		// First check if we have these files in our core content - we can ignore these because they're already mounted etc
		//
		foreach ( var e in entries )
		{
			TryAddToDownloadQueue( e, fs, downloadQueue, token );
		}

		// nothing to download
		if ( downloadQueue.Count <= 0 )
			return fs;

		var progress = LoadingProgress.Create( $"Downloading '{Title}'" );

		var workers = 16;
		var sw = Stopwatch.StartNew();
		long totalSize = downloadQueue.Sum( x => x.File.Size );
		long downloadedSize = 0;
		Log.Trace( $"Downloading {downloadQueue.Count:n0} files ({totalSize.FormatBytes()}).." );
		SentrySdk.AddBreadcrumb( $"Downloading {downloadQueue.Count:n0} files for {FullIdent}", "package.download" );

		progress.Title = $"Downloading '{Title}'";
		progress.TotalSize = totalSize;

		options.Loading?.LoadingProgress( progress );

		var metric = new Api.Events.EventRecord( "package.download" );
		metric.SetValue( "ident", FullIdent );
		metric.SetValue( "files", downloadQueue.Count );
		metric.SetValue( "size_sum", totalSize );
		metric.SetValue( "size_avg", downloadQueue.Average( x => x.File.Size ) );

		Utility.DataProgress.Callback progressCallback = ( p ) => { Interlocked.Add( ref downloadedSize, p.DeltaBytes ); };

		// garry: downloading in a random order so it'll download small files while downloading large ones seems to be the best stategy
		//		  I saw some good results from downloading large files first, but random order beat it every time.

		bool hasError = false;

		//
		// Download any pending files
		//
		var task = downloadQueue
			.OrderBy( x => Guid.NewGuid() )
			.ForEachTaskAsync( async ( e ) =>
			{
				try
				{
					await DownloadFileAsync( e, fs, progressCallback, token );
				}
				catch ( Exception ex )
				{
					Log.Warning( ex, $"Error when downloading {FullIdent}/{e.File.Url}" );
					hasError = true;
				}

			}, workers, token );

		long oldSize = 0;
		while ( !task.IsCompleted )
		{
			token.ThrowIfCancellationRequested();

			if ( downloadedSize != oldSize )
			{
				var speed = downloadedSize / sw.Elapsed.TotalSeconds;
				var mbps = (speed / 1024.0 / 1024.0) * 8;

				oldSize = downloadedSize;
				double frac = ((double)downloadedSize / (double)totalSize).Clamp( 0, 1 );

				progress.Title = $"Downloading '{Title}'";
				progress.Mbps = mbps;
				progress.Fraction = frac;

				options.Loading?.LoadingProgress( progress );
			}

			await Task.Delay( 16 );

			if ( hasError )
			{
				return null;
			}
		}

		progress.Title = $"Download '{Title}' Complete";

		Log.Trace( $"..done in {sw.Elapsed.TotalSeconds:0.00}s" );

		metric.SetValue( "time", sw.Elapsed.TotalSeconds );
		metric.SetValue( "workers", sw.Elapsed.TotalSeconds );
		metric.SetValue( "order", "random" );
		metric.Submit();

		//
		// Done with this
		//
		downloadQueue.Clear();
		downloadQueue = null;


		return fs;
	}

	private void TryAddToDownloadQueue( ManifestSchema.File entry, PackageFileSystem fs, ConcurrentBag<FileDownloadEntry> queue, CancellationToken token )
	{
		ThreadSafe.AssertIsMainThread();

		var crc = Convert.ToUInt64( entry.Crc, 16 );

		if ( AssetDownloadCache.TryMount( fs.Redirect, entry.Path, crc ) )
			return;

		var targetFile = AssetDownloadCache.GetAbsolutePath( entry.Path, crc );
		System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( targetFile ) );

		token.ThrowIfCancellationRequested();

		var fileInfo = new System.IO.FileInfo( targetFile );

		var download = new FileDownloadEntry
		{
			File = entry,
			AbsolutePath = targetFile,
			Crc = crc
		};

		queue.Add( download );
	}

	/// <summary>
	/// Make sure this manifest file entry is what it says it is
	/// </summary>
	private ValueTask<bool> CheckFileCrc( string absoluteFilePath, ManifestSchema.File entry, CancellationToken token )
	{
		var fileInfo = new System.IO.FileInfo( absoluteFilePath );
		if ( !fileInfo.Exists ) return ValueTask.FromResult( false );
		if ( fileInfo.Length != entry.Size ) return ValueTask.FromResult( false );

		// We can be selective on checking the crcs here. I'm turning it off for now
		// because it's a few seconds loadtime. We can probnably skip it on listen servers
		// and probably on serverside etc..

		/*
		using var stream = fileInfo.OpenRead();
		var crc = await Sandbox.Utility.Crc64.FromStreamAsync( stream );
		if ( crc.ToString( "x" ) != entry.Crc ) return false;
		*/

		return ValueTask.FromResult( true );
	}

	static ConcurrentDictionary<string, SemaphoreSlim> activeDownloadLocks = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Download an individual file
	/// </summary>
	private async Task DownloadFileAsync( FileDownloadEntry entry, PackageFileSystem fs, Sandbox.Utility.DataProgress.Callback progress, CancellationToken token )
	{
		var semaphore = activeDownloadLocks.GetOrAdd( entry.AbsolutePath, key => new SemaphoreSlim( 1 ) );

		await semaphore.WaitAsync( token );

		try
		{
			if ( System.IO.File.Exists( entry.AbsolutePath ) )
				return;

			if ( entry.File.Size == 0 )
			{
				await System.IO.File.WriteAllTextAsync( entry.AbsolutePath, "", token );
			}
			else
			{
				var url = $"{entry.File.Url}";

				var success = await Sandbox.Utility.Web.DownloadFile( url, entry.AbsolutePath, token, progress );
				if ( !success ) throw new System.Exception( $"Failed to download file {url} to {entry.AbsolutePath}" );
			}

			//
			// Make sure crc matches
			//
			if ( !await CheckFileCrc( entry.AbsolutePath, entry.File, token ) )
			{
				// we should probably throw exception and abandon here?
				Log.Warning( $"Downloaded file {entry.AbsolutePath} - checkfile failed" );
			}

			AssetDownloadCache.TryMount( fs.Redirect, entry.File.Path, entry.Crc );
		}
		finally
		{
			semaphore.Release();

			if ( semaphore.CurrentCount == 1 )
			{
				activeDownloadLocks.TryRemove( entry.AbsolutePath, out _ );
			}

		}
	}

	/// <summary>
	/// Download and mount this package. If withCode is true we'll try to load the assembly if it exists.
	/// </summary>
	public async Task<BaseFileSystem> MountAsync( bool withCode = false )
	{
		SentrySdk.AddBreadcrumb( $"Mounting {this.FullIdent}", "package.mount" );

		int? version = Revision?.VersionId > 0 ? (int)Revision.VersionId : null;
		var fs = await ServerPackages.Current.DownloadAndMount( FormatIdent( Org.Ident, Ident, version, !IsRemote ) );
		if ( fs is null ) return default;

		if ( withCode )
		{
			await IGameInstanceDll.Current?.LoadPackageAssembliesAsync( this );
		}

		return fs;
	}
}

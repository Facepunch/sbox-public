using Sandbox.Engine;
using Sandbox.Menu;
using Sentry;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace Sandbox;


public partial class Package
{
	/// <summary>
	/// How many files to download at once. Bound by requests in flight, not bandwidth.
	/// </summary>
	internal const int MaxParallelDownloads = 64;

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
	/// The files in a revision's manifest a download fetches - the legal ones, and with
	/// <paramref name="skipAssets"/> only the compiled code.
	/// </summary>
	static ManifestSchema.File[] DownloadableFiles( IRevision rev, bool skipAssets )
	{
		return DownloadableFiles( rev.Manifest?.Files ?? Array.Empty<ManifestSchema.File>(), skipAssets ).ToArray();
	}

	/// <inheritdoc cref="DownloadableFiles(IRevision, bool)"/>
	static IEnumerable<ManifestSchema.File> DownloadableFiles( ManifestSchema.File[] files, bool skipAssets )
	{
		var entries = files.Where( FilterFileDownloads );

		if ( skipAssets )
			entries = entries.Where( x => x.Path.StartsWith( ".bin" ) );

		return entries;
	}

	/// <summary>
	/// How much downloading this package would fetch, in bytes - its files that aren't in the download
	/// cache already. Fetches the manifest if it hasn't been. 0 when it's all cached, -1 when it can't be
	/// told (no revision, no manifest).
	/// </summary>
	internal async Task<long> GetDownloadSizeAsync( bool skipAssets = false, CancellationToken token = default )
	{
		if ( Revision is not { } rev )
			return -1;

		if ( InstallsFromWorkshop )
		{
			var state = Workshop.GetState( WorkshopId );
			if ( state.IsInstalled && !state.NeedsUpdate ) return 0;
			return state.Total > 0 ? state.Total - state.Downloaded : -1;
		}

		await rev.DownloadManifestAsync( token );
		if ( rev.Manifest == null ) return -1;

		var entries = DownloadableFiles( rev, skipAssets );

		// A few file stats each - all at once, off the main thread, like the download's own check
		return await Task.Run( () => entries
			.AsParallel()
			.Where( x => AssetDownloadCache.ResolveCached( x.Path, Convert.ToUInt64( x.Crc, 16 ) ) is null )
			.Sum( x => TextureWebStreaming.DownloadSize( x ) ), token );
	}

	/// <summary>
	/// Download a package to a temporary location and return a filesystem with its contents
	/// </summary>
	internal async Task<PackageFileSystem> Download( CancellationToken token = default, PackageLoadOptions options = default )
	{
		var fs = new PackageFileSystem();
		return await DownloadFiles( fs, token, options ) ? fs : null;
	}

	/// <summary>
	/// Download this package's files into the asset cache without mounting anything, so a later
	/// <see cref="Download(CancellationToken, PackageLoadOptions)"/> finds them all cached.
	/// Downloads of the same file share the per-file lock, so this can run alongside one.
	/// Returns false if the download failed.
	/// </summary>
	internal Task<bool> Prefetch( CancellationToken token, PackageLoadOptions options = default )
	{
		return DownloadFiles( null, token, options );
	}

	/// <summary>
	/// Download the files into the cache, mounting each on <paramref name="fs"/> if there is one
	/// </summary>
	async Task<bool> DownloadFiles( PackageFileSystem fs, CancellationToken token, PackageLoadOptions options )
	{
		// TODO - if we have a download in progress then return, or wait for it (?)
		// The filesystem is technically immutable other than disposing and adding more shit to it
		if ( Revision == null )
			return false;

		var rev = Revision;
		if ( rev == null ) return false;
		using var textureDownload = TextureWebStreaming.BeginDownload();

		// The files are in a workshop item, the backend just has a manifest of them
		if ( InstallsFromWorkshop )
			return await DownloadFromWorkshop( fs, token, options );

		options.Loading?.LoadingProgress( LoadingProgress.Create( $"Fetching '{Title}' Information" ) );

		// make sure manifest is downloaded
		await rev.DownloadManifestAsync( token );

		if ( rev.Manifest == null ) return false;

		// filter out files we're never going to download
		var entries = DownloadableFiles( rev, options.SkipAssetDownload );

		var downloadQueue = new ConcurrentBag<FileDownloadEntry>();

		// Finding each file in the caches is a few file stats, so that runs for all of them at once off the main thread
		var resolved = new string[entries.Length];
		await Task.Run( () => Parallel.For( 0, entries.Length, i => resolved[i] = AssetDownloadCache.ResolveCached( entries[i].Path, Convert.ToUInt64( entries[i].Crc, 16 ) ) ), token );
		token.ThrowIfCancellationRequested();

		var loopSw = Stopwatch.StartNew();
		for ( int i = 0; i < entries.Length; i++ )
		{
			TryAddToDownloadQueue( entries[i], resolved[i], fs, downloadQueue, token );
			if ( loopSw.ElapsedMilliseconds > 8 )
			{
				if ( fs is not null ) global::Sandbox.LoadingScreen.Subtitle = System.IO.Path.GetFileName( entries[i].Path );
				await Task.Yield();
				loopSw.Restart();
			}
		}


		// Its place among the load's downloads - unless another download of it's already filling that in
		var tracked = global::Sandbox.LoadingScreen.TrackDownload( FullIdent, Title );
		if ( tracked is { IsDownloading: true } ) tracked = null;

		var textureCount = entries.Count( x => x.Path.EndsWith( ".vtex_c", StringComparison.OrdinalIgnoreCase ) );
		var cachedTextures = textureCount - downloadQueue.Count( x => x.File.Path.EndsWith( ".vtex_c", StringComparison.OrdinalIgnoreCase ) );
		var streamableTextures = downloadQueue.Count( x => TextureWebStreaming.IsSupported( x.File ) );
		var streamedTextures = 0;
		var streamingFallbacks = 0;
		long deferredTextureBytes = 0;

		void LogTextureStreaming()
		{
			// Prefetch uses this same path; report once when the package is actually mounted.
			if ( fs is null || textureCount == 0 ) return;
			if ( !TextureWebStreaming.Enabled )
			{
				Log.Info( $"Texture web streaming '{FullIdent}': disabled; downloading textures in full." );
				return;
			}
			Log.Info( $"Texture web streaming '{FullIdent}': {streamedTextures:N0} streamed, {deferredTextureBytes.FormatBytes()} deferred; {cachedTextures:N0} fully cached, {textureCount - cachedTextures - streamableTextures:N0} without usable metadata, {streamingFallbacks:N0} fallbacks." );
		}

		// nothing to download
		if ( downloadQueue.Count <= 0 )
		{
			if ( tracked is not null ) tracked.IsComplete = true;
			if ( fs is not null ) RememberForOffline();

			LogTextureStreaming();
			options.Loading?.LoadingProgress( new LoadingProgress { Title = $"Download '{Title}' Complete", Fraction = 1 } );
			Api.Activity.CurrentLoad?.Downloaded( 0, 0, 0 );
			return true;
		}

		var progress = LoadingProgress.Create( $"Downloading '{Title}'" );

		var workers = MaxParallelDownloads;
		var sw = Stopwatch.StartNew();
		long totalSize = downloadQueue.Sum( x => TextureWebStreaming.DownloadSize( x.File ) );
		long downloadedSize = 0;
		Log.Trace( $"Downloading {downloadQueue.Count:n0} files ({totalSize.FormatBytes()}).." );
		SentrySdk.AddBreadcrumb( $"Downloading {downloadQueue.Count:n0} files for {FullIdent}", "package.download" );

		progress.Title = $"Downloading '{Title}'";
		progress.TotalSize = totalSize;

		options.Loading?.LoadingProgress( progress );

		if ( tracked is not null )
		{
			tracked.TotalSize = totalSize;
			tracked.Downloaded = 0;
			tracked.IsComplete = false;
			tracked.IsDownloading = true;
		}

		var metric = new Api.Events.EventRecord( "package.download" );
		metric.SetValue( "ident", FullIdent );
		metric.SetValue( "source", "backend" );
		metric.SetValue( "files", downloadQueue.Count );
		metric.SetValue( "size_sum", totalSize );
		metric.SetValue( "size_avg", downloadQueue.Average( x => x.File.Size ) );
		metric.SetValue( "prefetch", fs is null );

		Utility.DataProgress.Callback progressCallback = ( p ) => { Interlocked.Add( ref downloadedSize, p.DeltaBytes ); };

		// garry: downloading in a random order so it'll download small files while downloading large ones seems to be the best stategy
		//		  I saw some good results from downloading large files first, but random order beat it every time.

		bool hasError = false;

		// Stop the other workers when one fails, they'd keep writing to a filesystem we've thrown away
		using var downloadCancel = CancellationTokenSource.CreateLinkedTokenSource( token );

		// Downloaded files wait here for the main thread to mount them
		var downloaded = new ConcurrentQueue<FileDownloadEntry>();

		void MountDownloaded()
		{
			while ( downloaded.TryDequeue( out var e ) )
			{
				AssetDownloadCache.Mount( fs.Redirect, e.File.Path, e.AbsolutePath );
				TextureWebStreaming.QueuePrefetch( e.AbsolutePath, e.File );
			}
		}

		//
		// Download any pending files. The transfers run on the thread pool: on the main thread every read
		// waits for the next frame's queue drain, which caps throughput by frame rate and stalls the frame.
		//
		var task = Task.Run( () => downloadQueue
			.OrderBy( x => Guid.NewGuid() )
			.ForEachTaskAsync( async ( e ) =>
			{
				try
				{
					var partial = await TextureWebStreaming.TryPrepareAsync( e.File, downloadCancel.Token );
					if ( partial is not null )
					{
						e.AbsolutePath = partial;
						Interlocked.Increment( ref streamedTextures );
						Interlocked.Add( ref deferredTextureBytes, e.File.Size - TextureWebStreaming.DownloadSize( e.File ) );
						progressCallback( new Sandbox.Utility.DataProgress { DeltaBytes = TextureWebStreaming.DownloadSize( e.File ) } );
					}
					else
					{
						if ( TextureWebStreaming.IsSupported( e.File ) ) Interlocked.Increment( ref streamingFallbacks );
						Interlocked.Add( ref totalSize, e.File.Size - TextureWebStreaming.DownloadSize( e.File ) );
						await DownloadFileAsync( e, progressCallback, downloadCancel.Token );
					}
					if ( fs is not null ) downloaded.Enqueue( e );
				}
				// Only swallow if we're the ones who cancelled, a timeout mustn't pass as success
				catch ( OperationCanceledException ) when ( downloadCancel.IsCancellationRequested ) { }
				catch ( Exception ex )
				{
					Log.Warning( ex, $"Error when downloading {FullIdent}/{e.File.Url}" );
					hasError = true;
					downloadCancel.Cancel();
				}

			}, workers ) );

		long oldSize = 0;
		long oldTotalSize = totalSize;
		while ( !task.IsCompleted )
		{
			if ( hasError || token.IsCancellationRequested )
				break;

			if ( fs is not null ) MountDownloaded();

			var currentSize = Interlocked.Read( ref downloadedSize );
			var currentTotalSize = Interlocked.Read( ref totalSize );
			if ( currentSize != oldSize || currentTotalSize != oldTotalSize )
			{
				var speed = currentSize / sw.Elapsed.TotalSeconds;
				var mbps = (speed / 1024.0 / 1024.0) * 8;

				oldSize = currentSize;
				oldTotalSize = currentTotalSize;
				double frac = currentTotalSize > 0 ? ((double)currentSize / currentTotalSize).Clamp( 0, 1 ) : 1;

				progress.Title = $"Downloading '{Title}'";
				progress.TotalSize = currentTotalSize;
				progress.Mbps = mbps;
				progress.Fraction = frac;

				options.Loading?.LoadingProgress( progress );

				if ( tracked is not null )
				{
					tracked.TotalSize = currentTotalSize;
					tracked.Downloaded = downloadedSize;
					tracked.Mbps = mbps;
				}
			}

			await Task.Delay( 16 );
		}

		// Wait for the cancelled workers to finish writing
		await task;

		// Not coming down any more, however it ended
		if ( tracked is not null )
		{
			tracked.IsDownloading = false;
			tracked.Mbps = 0;
		}

		if ( token.IsCancellationRequested || hasError )
		{
			metric.SetValue( "outcome", hasError ? "failed" : "cancelled" );
			metric.SetValue( "time", sw.Elapsed.TotalSeconds );
			metric.SetValue( "downloaded", Interlocked.Read( ref downloadedSize ) );
			metric.Submit();
		}

		token.ThrowIfCancellationRequested();

		if ( hasError )
			return false;

		if ( tracked is not null )
		{
			tracked.TotalSize = totalSize;
			tracked.Downloaded = totalSize;
			tracked.IsComplete = true;
		}

		if ( fs is not null ) MountDownloaded();
		if ( fs is not null ) RememberForOffline();

		progress.Title = $"Download '{Title}' Complete";
		progress.TotalSize = totalSize;
		progress.Fraction = 1;
		options.Loading?.LoadingProgress( progress );
		// Clear subtitle so download stats don't bleed into the next phase (e.g. Compiling).
		if ( fs is not null ) global::Sandbox.LoadingScreen.Subtitle = "";

		Log.Trace( $"..done in {sw.Elapsed.TotalSeconds:0.00}s" );
		LogTextureStreaming();

		metric.SetValue( "outcome", "ok" );
		metric.SetValue( "time", sw.Elapsed.TotalSeconds );
		metric.SetValue( "workers", workers );
		metric.SetValue( "order", "random" );
		metric.Submit();

		Api.Activity.CurrentLoad?.Downloaded( totalSize, downloadQueue.Count, sw.Elapsed.TotalSeconds );

		//
		// Done with this
		//
		downloadQueue.Clear();
		downloadQueue = null;


		return true;
	}

	/// <summary>
	/// Mount the file where <see cref="AssetDownloadCache.ResolveCached"/> found it, or queue its download.
	/// </summary>
	private void TryAddToDownloadQueue( ManifestSchema.File entry, string resolved, PackageFileSystem fs, ConcurrentBag<FileDownloadEntry> queue, CancellationToken token )
	{
		ThreadSafe.AssertIsMainThread();

		var crc = Convert.ToUInt64( entry.Crc, 16 );

		if ( resolved is not null )
		{
			if ( fs is not null ) AssetDownloadCache.Mount( fs.Redirect, entry.Path, resolved );
			return;
		}

		// Web.DownloadFile makes the directory off the main thread. A new one can take a millisecond with a virus scanner watching.
		var targetFile = AssetDownloadCache.GetAbsolutePath( entry.Path, crc );

		token.ThrowIfCancellationRequested();

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
	/// Download an individual file into the cache. Runs off the main thread, the caller mounts it.
	/// Returns once the file is on disk, including when another download of it got there first.
	/// </summary>
	private async Task DownloadFileAsync( FileDownloadEntry entry, Sandbox.Utility.DataProgress.Callback progress, CancellationToken token )
	{
		var semaphore = activeDownloadLocks.GetOrAdd( entry.AbsolutePath, key => new SemaphoreSlim( 1 ) );

		await semaphore.WaitAsync( token );

		try
		{
			if ( System.IO.File.Exists( entry.AbsolutePath ) )
			{
				// Another download (usually prefetch) completed it while we waited for the lock.
				progress?.Invoke( new Sandbox.Utility.DataProgress { DeltaBytes = entry.File.Size } );
				return;
			}

			if ( entry.File.Size == 0 )
			{
				System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( entry.AbsolutePath ) );
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
		var fs = await ServerPackages.DownloadAndMount( FormatIdent( Org.Ident, Ident, version, !IsRemote ) );
		if ( fs is null ) return default;

		if ( withCode )
		{
			await IGameInstanceDll.Current?.LoadPackageAssembliesAsync( this );
		}

		return fs;
	}
}

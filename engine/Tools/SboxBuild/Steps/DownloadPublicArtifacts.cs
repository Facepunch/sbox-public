using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static Facepunch.Constants;

namespace Facepunch.Steps;

/// <summary>
/// Downloads public artifacts that match the current repository commit.
/// </summary>
internal class DownloadPublicArtifacts( string name ) : Step( name )
{
	private const string BaseUrl = "https://artifacts.sbox.game";
	private const int MaxParallelDownloads = 32;
	private const int MaxDownloadRetries = 3;
	private const int MaxFileCopyRetries = 5;
	private const int RetryDelayMs = 500;
	protected override ExitCode RunInternal()
	{
		var temporaryFiles = new ConcurrentBag<string>();
		try
		{
			var commitHash = ResolveCommitHash();
			if ( string.IsNullOrWhiteSpace( commitHash ) )
			{
				Log.Error( "Unable to determine the commit hash to download artifacts for." );
				return ExitCode.Failure;
			}

			Log.Info( $"Downloading public artifacts for commit {commitHash} from {BaseUrl}" );

			using var httpClient = CreateHttpClient();

			var manifest = DownloadManifest( httpClient, BaseUrl, commitHash );
			if ( manifest is null )
			{
				return ExitCode.Failure;
			}

			if ( !string.Equals( manifest.Commit, commitHash, StringComparison.OrdinalIgnoreCase ) )
			{
				Log.Error( $"Manifest commit {manifest.Commit} does not match requested commit {commitHash}." );
				return ExitCode.Failure;
			}

			if ( manifest.Files.Count == 0 )
			{
				Log.Warning( "Manifest does not contain any files to download." );
				return ExitCode.Success;
			}

			var repoRoot = Path.TrimEndingDirectorySeparator( Path.GetFullPath( Directory.GetCurrentDirectory() ) );
			return DownloadArtifacts( httpClient, manifest, repoRoot, temporaryFiles );
		}
		catch ( AggregateException ex )
		{
			foreach ( var inner in ex.Flatten().InnerExceptions )
			{
				Log.Error( $"Artifact download failed: {inner}" );
			}

			return ExitCode.Failure;
		}
		catch ( Exception ex )
		{
			Log.Error( $"Public artifact download failed with error: {ex}" );
			return ExitCode.Failure;
		}
		finally
		{
			CleanupTemporaryFiles( temporaryFiles );
		}
	}

	private static ExitCode DownloadArtifacts( HttpClient httpClient, ArtifactManifest manifest, string repoRoot, ConcurrentBag<string> temporaryFiles )
	{
		var downloadedArtifacts = new ConcurrentDictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		var artifactLocks = new ConcurrentDictionary<string, object>( StringComparer.OrdinalIgnoreCase );
		var updatedCount = 0;
		var skippedCount = 0;
		var failedCount = 0;
		var processedCount = 0;
		var totalFiles = manifest.Files.Count;
		var failedFiles = new ConcurrentBag<string>();
		var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = MaxParallelDownloads };
		var progressLock = new object();
		var lastProgressUpdate = DateTime.MinValue;

		void UpdateProgress( bool force = false )
		{
			lock ( progressLock )
			{
				var now = DateTime.Now;
				// Only update every 100ms to avoid console spam
				if ( !force && ( now - lastProgressUpdate ).TotalMilliseconds < 100 )
					return;

				lastProgressUpdate = now;
				var current = Volatile.Read( ref processedCount );
				var updated = Volatile.Read( ref updatedCount );
				var skipped = Volatile.Read( ref skippedCount );
				var failed = Volatile.Read( ref failedCount );
				var percent = totalFiles > 0 ? (int)( current * 100.0 / totalFiles ) : 100;

				// Build progress bar
				const int barWidth = 30;
				var filledWidth = totalFiles > 0 ? (int)( barWidth * current / (double)totalFiles ) : barWidth;
				var bar = new string( '█', filledWidth ) + new string( '░', barWidth - filledWidth );

				var output = $"\r  [{bar}] {percent,3}% ({current}/{totalFiles}) - Updated: {updated}, Skipped: {skipped}";
				if ( failed > 0 )
					output += $", Failed: {failed}";

				// Pad to clear any previous longer output
				Console.Write( output.PadRight( 100 ) );
			}
		}

		string EnsureArtifactCached( ArtifactFileInfo entry )
		{
			if ( downloadedArtifacts.TryGetValue( entry.Sha256, out var existing ) )
			{
				return existing;
			}

			var artifactLock = artifactLocks.GetOrAdd( entry.Sha256, _ => new object() );
			lock ( artifactLock )
			{
				if ( downloadedArtifacts.TryGetValue( entry.Sha256, out existing ) )
				{
					return existing;
				}

				var tempPath = DownloadArtifactWithRetry( httpClient, BaseUrl, entry )
					?? throw new InvalidOperationException( $"Failed to download artifact {entry.Sha256}." );

				temporaryFiles.Add( tempPath );
				downloadedArtifacts[entry.Sha256] = tempPath;
				return tempPath;
			}
		}

		Log.Info( $"Processing {totalFiles} files..." );

		Parallel.ForEach( manifest.Files, parallelOptions, entry =>
		{
			if ( string.IsNullOrWhiteSpace( entry.Path ) || string.IsNullOrWhiteSpace( entry.Sha256 ) )
			{
				Interlocked.Increment( ref skippedCount );
				Interlocked.Increment( ref processedCount );
				UpdateProgress();
				return;
			}

			var destination = Path.Combine( repoRoot, entry.Path.Replace( '/', Path.DirectorySeparatorChar ) );

			if ( FileMatchesHash( destination, entry.Sha256 ) )
			{
				Interlocked.Increment( ref skippedCount );
				Interlocked.Increment( ref processedCount );
				UpdateProgress();
				return;
			}

			try
			{
				var sourcePath = EnsureArtifactCached( entry );

				var directory = Path.GetDirectoryName( destination );
				if ( !string.IsNullOrEmpty( directory ) )
				{
					Directory.CreateDirectory( directory );
				}

				CopyFileWithRetry( sourcePath, destination );

				if ( !FileMatchesHash( destination, entry.Sha256 ) )
				{
					throw new InvalidOperationException( $"Hash mismatch after writing {entry.Path}." );
				}

				Interlocked.Increment( ref updatedCount );
			}
			catch ( Exception ex )
			{
				// Log errors on a new line to not interfere with progress bar
				Console.WriteLine();
				Log.Error( $"Failed: {entry.Path} - {ex.Message}" );
				failedFiles.Add( entry.Path );
				Interlocked.Increment( ref failedCount );
			}
			finally
			{
				Interlocked.Increment( ref processedCount );
				UpdateProgress();
			}
		} );

		// Final progress update and newline
		UpdateProgress( force: true );
		Console.WriteLine();

		if ( failedCount > 0 )
		{
			Log.Error( $"Completed with errors: {updatedCount} updated, {skippedCount} skipped, {failedCount} failed." );
			return ExitCode.Failure;
		}

		Log.Info( $"Completed: {updatedCount} updated, {skippedCount} unchanged." );
		return ExitCode.Success;
	}

	private static void CleanupTemporaryFiles( ConcurrentBag<string> temporaryFiles )
	{
		foreach ( var temp in temporaryFiles )
		{
			try
			{
				if ( File.Exists( temp ) )
				{
					File.Delete( temp );
				}
			}
			catch ( Exception ex )
			{
				Log.Warning( $"Failed to clean up temporary file '{temp}': {ex.Message}" );
			}
		}
	}

	/// <summary>
	/// Copies a file with retry logic to handle file locking issues.
	/// </summary>
	private static void CopyFileWithRetry( string sourcePath, string destinationPath )
	{
		Exception lastException = null;

		for ( int attempt = 1; attempt <= MaxFileCopyRetries; attempt++ )
		{
			try
			{
				// Try to delete existing file first if it exists
				if ( File.Exists( destinationPath ) )
				{
					try
					{
						File.Delete( destinationPath );
					}
					catch ( IOException )
					{
						// File might be locked, will try to overwrite
					}
				}

				// Use FileStream with explicit sharing to handle locked files better
				// FileShare.Read allows read access during write (antivirus/backup software)
				using ( var sourceStream = new FileStream( sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read ) )
				using ( var destStream = new FileStream( destinationPath, FileMode.Create, FileAccess.Write, FileShare.Read ) )
				{
					sourceStream.CopyTo( destStream );
				}

				return; // Success
			}
			catch ( IOException ex ) when ( attempt < MaxFileCopyRetries )
			{
				lastException = ex;
				Thread.Sleep( RetryDelayMs * attempt ); // Exponential backoff
			}
			catch ( UnauthorizedAccessException ex ) when ( attempt < MaxFileCopyRetries )
			{
				lastException = ex;
				Thread.Sleep( RetryDelayMs * attempt ); // Exponential backoff
			}
		}

		throw new IOException( $"Failed to copy file to '{destinationPath}' after {MaxFileCopyRetries} attempts.", lastException );
	}

	/// <summary>
	/// Downloads an artifact with retry logic for network issues.
	/// </summary>
	private static string DownloadArtifactWithRetry( HttpClient httpClient, string baseUrl, ArtifactFileInfo entry )
	{
		Exception lastException = null;

		for ( int attempt = 1; attempt <= MaxDownloadRetries; attempt++ )
		{
			try
			{
				var result = DownloadArtifact( httpClient, baseUrl, entry );
				if ( result != null )
				{
					return result;
				}

				// DownloadArtifact returned null, meaning it failed but didn't throw
				if ( attempt < MaxDownloadRetries )
				{
					Thread.Sleep( RetryDelayMs * attempt );
				}
			}
			catch ( Exception ex ) when ( attempt < MaxDownloadRetries && IsRetryableException( ex ) )
			{
				lastException = ex;
				Thread.Sleep( RetryDelayMs * attempt );
			}
		}

		if ( lastException != null )
		{
			throw new InvalidOperationException( $"Failed to download artifact '{entry.Path}' after {MaxDownloadRetries} attempts.", lastException );
		}

		return null;
	}

	/// <summary>
	/// Determines if an exception is retryable (network issues, transient errors).
	/// </summary>
	private static bool IsRetryableException( Exception ex )
	{
		// HttpRequestException is typically retryable (network issues)
		if ( ex is HttpRequestException )
			return true;

		// TaskCanceledException can happen on timeouts, worth retrying
		if ( ex is TaskCanceledException )
			return true;

		// Only retry specific IOExceptions that indicate transient issues
		// Don't retry disk full, permission denied, etc.
		if ( ex is IOException ioEx )
		{
			// Check for specific HResult values that indicate transient issues
			// 0x80070020 = ERROR_SHARING_VIOLATION (file in use)
			// 0x80070021 = ERROR_LOCK_VIOLATION (locked portion of file)
			const int ERROR_SHARING_VIOLATION = unchecked((int)0x80070020);
			const int ERROR_LOCK_VIOLATION = unchecked((int)0x80070021);

			return ioEx.HResult == ERROR_SHARING_VIOLATION ||
				   ioEx.HResult == ERROR_LOCK_VIOLATION;
		}

		// Check nested exceptions in AggregateException
		if ( ex is AggregateException agg )
			return agg.InnerExceptions.Any( IsRetryableException );

		return false;
	}

	private static HttpClient CreateHttpClient()
	{
		var handler = new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip
		};

		return new HttpClient( handler )
		{
			Timeout = TimeSpan.FromMinutes( 5 )
		};
	}

	private static string ResolveCommitHash()
	{
		const string branchName = "master";
		string gitCommit = null;
		var success = Utility.RunProcess( "git", $"rev-parse {branchName}", onDataReceived: ( _, e ) =>
		{
			if ( !string.IsNullOrWhiteSpace( e.Data ) )
			{
				gitCommit ??= e.Data.Trim();
			}
		} );

		if ( !success )
		{
			Log.Error( $"Failed to execute git to resolve commit hash for branch '{branchName}'." );
			return null;
		}

		if ( string.IsNullOrWhiteSpace( gitCommit ) )
		{
			Log.Error( $"git returned an empty commit hash for branch '{branchName}'." );
			return null;
		}

		return gitCommit;
	}

	private static ArtifactManifest DownloadManifest( HttpClient httpClient, string baseUrl, string commitHash )
	{
		var manifestUrl = $"{baseUrl.TrimEnd( '/' )}/manifests/{commitHash}.json";

		Log.Info( $"Fetching manifest: {manifestUrl}" );

		using var response = httpClient.GetAsync( manifestUrl, HttpCompletionOption.ResponseHeadersRead ).GetAwaiter().GetResult();
		if ( response.StatusCode == HttpStatusCode.NotFound )
		{
			Log.Error( $"Manifest not found for commit {commitHash}." );
			return null;
		}

		if ( !response.IsSuccessStatusCode )
		{
			Log.Error( $"Failed to download manifest (HTTP {(int)response.StatusCode})." );
			return null;
		}

		using var stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();

		var manifest = JsonSerializer.Deserialize<ArtifactManifest>( stream, new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true
		} );

		if ( manifest is null )
		{
			Log.Error( "Failed to deserialize manifest JSON." );
			return null;
		}

		return manifest;
	}

	private static string DownloadArtifact( HttpClient httpClient, string baseUrl, ArtifactFileInfo entry )
	{
		var hash = entry.Sha256;
		var expectedSize = entry.Size;
		var artifactUrl = $"{baseUrl.TrimEnd( '/' )}/artifacts/{hash}";

		using var response = httpClient.GetAsync( artifactUrl, HttpCompletionOption.ResponseHeadersRead ).GetAwaiter().GetResult();
		if ( response.StatusCode == HttpStatusCode.NotFound )
		{
			return null;
		}

		if ( !response.IsSuccessStatusCode )
		{
			return null;
		}

		var tempPath = Path.Combine( Path.GetTempPath(), $"sbox-public-{hash}-{Guid.NewGuid():N}.bin" );

		using ( var downloadStream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult() )
		using ( var fileStream = File.Open( tempPath, FileMode.Create, FileAccess.Write, FileShare.None ) )
		{
			downloadStream.CopyTo( fileStream );
		}

		if ( expectedSize > 0 )
		{
			var actualSize = new FileInfo( tempPath ).Length;
			if ( actualSize != expectedSize )
			{
				File.Delete( tempPath );
				return null;
			}
		}

		var downloadedHash = Utility.CalculateSha256( tempPath );
		if ( !string.Equals( downloadedHash, hash, StringComparison.OrdinalIgnoreCase ) )
		{
			File.Delete( tempPath );
			return null;
		}

		return tempPath;
	}

	private static bool FileMatchesHash( string path, string expectedHash )
	{
		if ( !File.Exists( path ) )
		{
			return false;
		}

		try
		{
			var hash = Utility.CalculateSha256( path );
			return string.Equals( hash, expectedHash, StringComparison.OrdinalIgnoreCase );
		}
		catch ( Exception ex )
		{
			Log.Warning( $"Failed to compute hash for {path}: {ex.Message}" );
			return false;
		}
	}
}

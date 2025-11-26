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
		var fileLocks = new ConcurrentDictionary<string, object>( StringComparer.OrdinalIgnoreCase );
		var updatedCount = 0;
		var skippedCount = 0;
		var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = MaxParallelDownloads };

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

				var tempPath = DownloadArtifact( httpClient, BaseUrl, entry )
					?? throw new InvalidOperationException( $"Failed to download artifact {entry.Sha256}." );

				temporaryFiles.Add( tempPath );
				downloadedArtifacts[entry.Sha256] = tempPath;
				return tempPath;
			}
		}

		void CopyFileWithRetry( string source, string destination, int maxRetries = 3 )
		{
			for ( int attempt = 0; attempt <= maxRetries; attempt++ )
			{
				try
				{
					File.Copy( source, destination, true );
					return;
				}
				catch ( IOException ) when ( attempt < maxRetries )
				{
					Thread.Sleep( 100 * (attempt + 1) ); // Exponential backoff
				}
			}
		}

		Parallel.ForEach( manifest.Files, parallelOptions, entry =>
		{
			if ( string.IsNullOrWhiteSpace( entry.Path ) || string.IsNullOrWhiteSpace( entry.Sha256 ) )
			{
				Log.Warning( $"Skipping manifest entry with missing path or hash: '{entry.Path ?? "<null>"}'." );
				Interlocked.Increment( ref skippedCount );
				return;
			}

			var destination = Path.Combine( repoRoot, entry.Path.Replace( '/', Path.DirectorySeparatorChar ) );
			var destinationKey = destination.ToLowerInvariant();

			// Lock per destination file to prevent concurrent writes to the same file
			var fileLock = fileLocks.GetOrAdd( destinationKey, _ => new object() );
			lock ( fileLock )
			{
				if ( FileMatchesHash( destination, entry.Sha256 ) )
				{
					Interlocked.Increment( ref skippedCount );
					return;
				}

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

				Log.Info( $"Wrote {entry.Path}" );
				Interlocked.Increment( ref updatedCount );
			}
		} );

		Log.Info( $"Artifact download completed successfully. Updated {updatedCount} file(s), skipped {skippedCount}." );
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

		var targetName = string.IsNullOrWhiteSpace( entry.Path ) ? hash : entry.Path;
		Log.Info( $"Downloading {targetName} from {artifactUrl} ({Utility.FormatSize( expectedSize )})" );

		using var response = httpClient.GetAsync( artifactUrl, HttpCompletionOption.ResponseHeadersRead ).GetAwaiter().GetResult();
		if ( response.StatusCode == HttpStatusCode.NotFound )
		{
			Log.Error( $"Artifact blob {hash} not found." );
			return null;
		}

		if ( !response.IsSuccessStatusCode )
		{
			Log.Error( $"Failed to download artifact {hash} (HTTP {(int)response.StatusCode})." );
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
				Log.Error( $"Downloaded artifact {hash} has size {actualSize}, expected {expectedSize}." );
				File.Delete( tempPath );
				return null;
			}
		}

		var downloadedHash = Utility.CalculateSha256( tempPath );
		if ( !string.Equals( downloadedHash, hash, StringComparison.OrdinalIgnoreCase ) )
		{
			Log.Error( $"Hash mismatch for downloaded artifact {hash}." );
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

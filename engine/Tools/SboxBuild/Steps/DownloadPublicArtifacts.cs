using System.Collections.Generic;
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
	private const int MaxDownloadAttempts = 3;
	private const int MaxManifestLookbackCommits = 512;
	protected override ExitCode RunInternal()
	{
		try
		{
			var commitCandidates = ResolveCommitHistory( MaxManifestLookbackCommits );
			if ( commitCandidates.Count == 0 )
			{
				Log.Error( "Unable to determine the commit hash to download artifacts for." );
				return ExitCode.Failure;
			}

			using var httpClient = CreateHttpClient();

			ArtifactManifest manifest = null;
			foreach ( var candidate in commitCandidates )
			{
				var candidateManifest = DownloadManifest( httpClient, BaseUrl, candidate );
				if ( candidateManifest is null )
				{
					continue;
				}

				if ( !string.Equals( candidateManifest.Commit, candidate, StringComparison.OrdinalIgnoreCase ) )
				{
					Log.Error( $"Manifest commit {candidateManifest.Commit} does not match requested commit {candidate}." );
					return ExitCode.Failure;
				}

				manifest = candidateManifest;
				break;
			}

			if ( manifest is null )
			{
				Log.Error( $"Unable to locate a manifest within the last {commitCandidates.Count} commit(s)." );
				return ExitCode.Failure;
			}

			Log.Info( $"Downloading public artifacts for commit {manifest.Commit} from {BaseUrl}" );

			if ( manifest.Files.Count == 0 )
			{
				Log.Warning( "Manifest does not contain any files to download." );
				return ExitCode.Success;
			}

			var repoRoot = Path.TrimEndingDirectorySeparator( Path.GetFullPath( Directory.GetCurrentDirectory() ) );
			return DownloadArtifacts( httpClient, manifest, repoRoot );
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
	}

	private static ExitCode DownloadArtifacts( HttpClient httpClient, ArtifactManifest manifest, string repoRoot )
	{
		var updatedCount = 0;
		var skippedCount = 0;
		var failedCount = 0;
		var totalCount = manifest.Files.Count;
		var processedCount = 0;
		var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = MaxParallelDownloads };
		var lockObject = new object();

		// Show initial progress
		UpdateProgress( lockObject, 0, totalCount, "Starting downloads...", "" );

		Parallel.ForEach( manifest.Files, parallelOptions, entry =>
		{
			if ( string.IsNullOrWhiteSpace( entry.Path ) || string.IsNullOrWhiteSpace( entry.Sha256 ) )
			{
				Log.Warning( $"Skipping manifest entry with missing path or hash: '{entry.Path ?? "<null>"}'." );
				Interlocked.Increment( ref skippedCount );
				return;
			}

			var destination = Path.Combine( repoRoot, entry.Path.Replace( '/', Path.DirectorySeparatorChar ) );

			if ( FileMatchesHash( destination, entry.Sha256 ) )
			{
				Interlocked.Increment( ref skippedCount );
				UpdateProgress( lockObject, Interlocked.Increment( ref processedCount ), totalCount, "Skipped (up-to-date)", entry.Path ?? entry.Sha256 );
				return;
			}

			var directory = Path.GetDirectoryName( destination );
			if ( !string.IsNullOrEmpty( directory ) )
			{
				Directory.CreateDirectory( directory );
			}

			var dlSuccess = DownloadArtifact( httpClient, BaseUrl, entry, destination );
			if ( dlSuccess )
			{
				Interlocked.Increment( ref updatedCount );
				UpdateProgress( lockObject, Interlocked.Increment( ref processedCount ), totalCount, "Downloaded", entry.Path ?? entry.Sha256 );
			}
			else
			{
				Interlocked.Increment( ref failedCount );
				UpdateProgress( lockObject, Interlocked.Increment( ref processedCount ), totalCount, "Failed", entry.Path ?? entry.Sha256 );
				DeleteIfExists( destination );
			}
		} );

		// Clear progress line and show final result
		Console.Write( "\r" + new string( ' ', GetSafeConsoleWidth() - 1 ) + "\r" );

		if ( failedCount > 0 )
		{
			Log.Error( $"Artifact download failed for {failedCount} file(s)." );
			return ExitCode.Failure;
		}

		Log.Info( $"Artifact download completed successfully. Updated {updatedCount} file(s), skipped {skippedCount}." );
		return ExitCode.Success;
	}

	private static HttpClient CreateHttpClient()
	{
#pragma warning disable CA2000 // Dispose objects before losing scope
		// HttpClient will dispose these handlers when it is disposed.
		var handler = new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip
		};
#pragma warning restore CA2000 // Dispose objects before losing scope

		return new HttpClient( handler )
		{
			Timeout = TimeSpan.FromMinutes( 5 )
		};
	}

	private static IReadOnlyList<string> ResolveCommitHistory( int maxCommits )
	{
		var commits = new List<string>( Math.Max( maxCommits, 1 ) );
		var success = Utility.RunProcess( "git", $"rev-list HEAD --max-count={maxCommits}", onDataReceived: ( _, e ) =>
		{
			if ( !string.IsNullOrWhiteSpace( e.Data ) )
			{
				commits.Add( e.Data.Trim() );
			}
		} );

		if ( !success )
		{
			Log.Error( "Failed to execute git to resolve commit history for the current branch." );
			return Array.Empty<string>();
		}

		if ( commits.Count == 0 )
		{
			Log.Error( "git returned no commits for the current branch." );
		}

		return commits;
	}

	private static ArtifactManifest DownloadManifest( HttpClient httpClient, string baseUrl, string commitHash )
	{
		var manifestUrl = $"{baseUrl.TrimEnd( '/' )}/manifests/{commitHash}.json";

		Log.Info( $"Fetching manifest: {manifestUrl}" );

		using var response = httpClient.GetAsync( manifestUrl, HttpCompletionOption.ResponseHeadersRead ).GetAwaiter().GetResult();
		if ( response.StatusCode == HttpStatusCode.NotFound )
		{
			Log.Warning( $"Manifest not found for commit {commitHash}." );
			return null;
		}

		if ( !response.IsSuccessStatusCode )
		{
			Log.Warning( $"Failed to download manifest for commit {commitHash} (HTTP {(int)response.StatusCode})." );
			return null;
		}

		using var stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();

		var manifest = JsonSerializer.Deserialize<ArtifactManifest>( stream, new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true
		} );

		if ( manifest is null )
		{
			Log.Warning( $"Failed to deserialize manifest JSON for commit {commitHash}." );
			return null;
		}

		return manifest;
	}

	private static bool DownloadArtifact( HttpClient httpClient, string baseUrl, ArtifactFileInfo entry, string destination )
	{
		for ( var attempt = 1; attempt <= MaxDownloadAttempts; attempt++ )
		{
			try
			{
				DownloadArtifactOnce( httpClient, baseUrl, entry, destination );
				return true;
			}
			catch ( Exception ex )
			{
				Log.Warning( $"Download attempt {attempt} for {entry.Path ?? entry.Sha256} failed: {ex.Message}" );
				Thread.Sleep( TimeSpan.FromMilliseconds( 200 * attempt ) );
			}
		}

		return false;
	}

	private static void DownloadArtifactOnce( HttpClient httpClient, string baseUrl, ArtifactFileInfo entry, string destination )
	{
		var hash = entry.Sha256;
		var expectedSize = entry.Size;
		var artifactUrl = $"{baseUrl.TrimEnd( '/' )}/artifacts/{hash}";

		var targetName = string.IsNullOrWhiteSpace( entry.Path ) ? hash : entry.Path;

		using var response = httpClient.GetAsync( artifactUrl, HttpCompletionOption.ResponseHeadersRead ).GetAwaiter().GetResult();
		if ( response.StatusCode == HttpStatusCode.NotFound )
		{
			Log.Error( $"Artifact blob {hash} not found." );
			throw new InvalidOperationException( $"Artifact blob {hash} not found." );
		}

		if ( !response.IsSuccessStatusCode )
		{
			Log.Error( $"Failed to download artifact {hash} (HTTP {(int)response.StatusCode})." );
			throw new InvalidOperationException( $"Failed to download artifact {hash} (HTTP {(int)response.StatusCode})." );
		}

		using ( var downloadStream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult() )
		using ( var fileStream = File.Open( destination, FileMode.Create, FileAccess.Write, FileShare.None ) )
		{
			downloadStream.CopyTo( fileStream );
		}

		if ( expectedSize > 0 )
		{
			var actualSize = new FileInfo( destination ).Length;
			if ( actualSize != expectedSize )
			{
				Log.Error( $"Downloaded artifact {hash} has size {actualSize}, expected {expectedSize}." );
				File.Delete( destination );
				throw new InvalidOperationException( $"Downloaded artifact {hash} has unexpected size." );
			}
		}

		var downloadedHash = Utility.CalculateSha256( destination );
		if ( !string.Equals( downloadedHash, hash, StringComparison.OrdinalIgnoreCase ) )
		{
			Log.Error( $"Hash mismatch for downloaded artifact {hash}." );
			File.Delete( destination );
			throw new InvalidOperationException( $"Hash mismatch for downloaded artifact {hash}." );
		}
	}

	private static void DeleteIfExists( string path )
	{
		try
		{
			if ( File.Exists( path ) )
			{
				File.Delete( path );
			}
		}
		catch ( Exception ex )
		{
			Log.Warning( $"Failed to delete '{path}' during retry cleanup: {ex.Message}" );
		}
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

	private static int GetSafeConsoleWidth()
	{
		try
		{
			return Console.WindowWidth;
		}
		catch ( System.IO.IOException )
		{
			// Console output is redirected or in non-interactive environment
			return 120; // Default width for CI/CD environments
		}
	}

	private static void UpdateProgress( object lockObject, int current, int total, string action, string filePath )
	{
		lock ( lockObject )
		{
			var percentage = (int)((double)current / total * 100);
			var progressBar = new string( '=', percentage / 5 ).PadRight( 20 );
			var baseStatus = $"[{progressBar}] {percentage,3}% ({current}/{total}) - {action}";

			var consoleWidth = GetSafeConsoleWidth();

			// If the base status itself is too long for the console, truncate it
			if ( baseStatus.Length >= consoleWidth - 1 )
			{
				var status = baseStatus.Substring( 0, consoleWidth - 1 ).PadRight( consoleWidth - 1 );
				Console.Write( $"\r{status}" );
				return;
			}

			if ( !string.IsNullOrEmpty( filePath ) )
			{
				var statusWithColon = baseStatus + ": ";
				var maxFileLength = consoleWidth - statusWithColon.Length - 1;

				// Truncate from start if path is too long, keeping filename visible
				var displayFile = filePath;
				if ( maxFileLength > 3 && displayFile.Length > maxFileLength )
				{
					displayFile = "..." + displayFile.Substring( displayFile.Length - maxFileLength + 3 );
				}
				else if ( maxFileLength <= 3 )
				{
					// Not enough space for file path, just show the action
					displayFile = "";
				}
				else
				{
					// File fits without truncation
				}

				if ( !string.IsNullOrEmpty( displayFile ) )
				{
					baseStatus += ": " + displayFile;
				}
			}

			var finalStatus = baseStatus.PadRight( consoleWidth - 1 );
			Console.Write( $"\r{finalStatus}" );
		}
	}
}

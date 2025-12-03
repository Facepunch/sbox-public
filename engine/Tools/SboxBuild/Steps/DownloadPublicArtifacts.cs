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
		var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = MaxParallelDownloads };

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
			}
			else
			{
				Interlocked.Increment( ref failedCount );
				DeleteIfExists( destination );
			}
		} );

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
		var handler = new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip
		};

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
		Log.Info( $"Downloading {targetName} from {artifactUrl} ({Utility.FormatSize( expectedSize )})" );

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
}

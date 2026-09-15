using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using static Facepunch.Constants;

namespace Facepunch.Steps;

/// <summary>
/// Downloads public artifacts that match the current repository commit.
/// </summary>
internal class DownloadPublicArtifacts( bool nativeBinariesOnly = false )
{
	private const string BaseUrl = "https://artifacts.sbox.game";
	private const int MaxParallelDownloads = 32;
	private const int MaxDownloadAttempts = 3;
	private const int MaxManifestLookbackCommits = 128;

	/// <summary>
	/// Size and modification time of every file verified against its manifest hash on the last run.
	/// An unchanged size+mtime is trusted instead of rehashing, which turns the ~10 GB verify pass into
	/// a stat pass; that is what makes running this from a git hook on every checkout bearable.
	/// Under game/bin so both distributions' .gitignore rules already cover it.
	/// </summary>
	private static readonly string StatePath = Path.Combine( "game", "bin", ".sbox-artifacts.json" );

	internal ExitCode Run() => BuildDisplay.Run( "Restore public artifacts", Restore );

	private ExitCode Restore()
	{
		try
		{
			BuildDisplay.Status( "Finding a compatible artifact manifest" );
			// Deepen the shallow PR checkout so rev-list has enough commits to find a
			// matching manifest. Fetch only the current PR branch to avoid pulling down
			// every branch and tag from the remote.
			var headRef = Environment.GetEnvironmentVariable( "GITHUB_HEAD_REF" );
			if ( !string.IsNullOrEmpty( headRef ) )
			{
				Utility.RunProcess( "git", $"fetch --deepen={MaxManifestLookbackCommits} --no-tags origin {headRef}" );
			}

			var commitCandidates = ResolveCommitHistory( MaxManifestLookbackCommits );
			if ( commitCandidates.Count == 0 )
			{
				Log.Error( "Unable to determine the commit hash to download artifacts for." );
				return ExitCode.Failure;
			}

			using var httpClient = CreateHttpClient();
			var requireMatchingNativeInputs = nativeBinariesOnly && !string.IsNullOrEmpty( Environment.GetEnvironmentVariable( "GITHUB_BASE_REF" ) );

			ArtifactManifest manifest = null;
			for ( var candidateIndex = 0; candidateIndex < commitCandidates.Count; candidateIndex++ )
			{
				var candidate = commitCandidates[candidateIndex];
				BuildDisplay.Status( $"Finding manifest ({candidateIndex + 1}/{commitCandidates.Count}): {candidate[..Math.Min( 12, candidate.Length )]}" );
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

				// PR bindings are generated from HEAD, not from the downloaded artifact revision.
				if ( requireMatchingNativeInputs && !Utility.NativeInputsMatch( candidate ) )
				{
					Log.Detail( $"Skipping native artifacts from {candidate}: native inputs differ or could not be verified." );
					continue;
				}

				manifest = candidateManifest;
				break;
			}

			if ( manifest is null )
			{
				Log.Error( $"Unable to locate a compatible manifest within the last {commitCandidates.Count} commit(s)." );
				return ExitCode.Failure;
			}

			if ( !string.Equals( manifest.Commit, commitCandidates[0], StringComparison.OrdinalIgnoreCase ) )
			{
				Log.Info( $"No compatible artifacts for HEAD; falling back to commit {manifest.Commit}." +
					(requireMatchingNativeInputs ? " Native inputs match HEAD." : "") );
			}
			Log.Detail( $"Restoring public artifacts for commit {manifest.Commit} from {BaseUrl}" );

			if ( manifest.Files.Count == 0 )
			{
				Log.Warning( "Manifest does not contain any files to download." );
				Log.Summary( "Public artifacts: no files to restore." );
				return ExitCode.Success;
			}

			var repoRoot = Path.TrimEndingDirectorySeparator( Path.GetFullPath( Directory.GetCurrentDirectory() ) );
			return DownloadArtifacts( httpClient, manifest, repoRoot, nativeBinariesOnly );
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

	private static ExitCode DownloadArtifacts( HttpClient httpClient, ArtifactManifest manifest, string repoRoot, bool nativeBinariesOnly )
	{
		if ( manifest.Files.Any( entry => entry is null || string.IsNullOrWhiteSpace( entry.Path ) || string.IsNullOrWhiteSpace( entry.Sha256 ) ) )
		{
			Log.Error( "Manifest contains an entry with a missing path or hash." );
			return ExitCode.Failure;
		}
		repoRoot = Path.TrimEndingDirectorySeparator( Path.GetFullPath( repoRoot ) );
		var rootPrefix = Path.EndsInDirectorySeparator( repoRoot ) ? repoRoot : repoRoot + Path.DirectorySeparatorChar;
		var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
		var pending = new ConcurrentBag<(ArtifactFileInfo Entry, string Destination)>();
		var progress = new DownloadProgress();
		var skippedCount = 0;
		var cachedCount = 0;
		var checkedCount = 0;
		var knownState = LoadState( repoRoot );
		var newState = new ConcurrentDictionary<string, ArtifactStateEntry>( StringComparer.OrdinalIgnoreCase );
		var platform = NativePlatform.Current.DirectoryName;
		var otherPlatformPrefixes = NativePlatform.All
			.Where( candidate => !candidate.DirectoryName.Equals( platform, StringComparison.OrdinalIgnoreCase ) )
			.Select( candidate => $"game/bin/{candidate.DirectoryName}/" )
			.ToArray();
		Log.Detail( $"Restoring public artifacts for {platform}." );

		BuildDisplay.Status( "Checking local artifacts" );
		var checking = Task.Run( () => Parallel.ForEach( manifest.Files, new ParallelOptions { MaxDegreeOfParallelism = 4 }, entry =>
		{
			try
			{
				var artifactPath = entry.Path.Replace( '\\', '/' );
				if ( otherPlatformPrefixes.Any( prefix => artifactPath.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) )
					|| (nativeBinariesOnly && !artifactPath.StartsWith( "game/bin/", StringComparison.OrdinalIgnoreCase )) )
				{
					// Not this run's concern; keep whatever an earlier full run verified.
					if ( knownState.TryGetValue( entry.Path, out var kept ) )
						newState[entry.Path] = kept;
					Interlocked.Increment( ref skippedCount );
					return;
				}

				var relative = entry.Path.Replace( '/', Path.DirectorySeparatorChar ).Replace( '\\', Path.DirectorySeparatorChar );
				var destination = Path.GetFullPath( Path.Combine( repoRoot, relative ) );
				if ( Path.IsPathRooted( relative ) || relative.Contains( ':' ) || !destination.StartsWith( rootPrefix, pathComparison ) )
					throw new InvalidOperationException( $"Artifact path is outside the repository: '{entry.Path}'." );

				if ( knownState.TryGetValue( entry.Path, out var known ) && IsUnchanged( destination, known, entry ) )
				{
					newState[entry.Path] = known;
					Interlocked.Increment( ref cachedCount );
					Interlocked.Increment( ref skippedCount );
					return;
				}

				if ( FileMatchesHash( destination, entry.Sha256, message => progress.Messages.Enqueue( (true, message) ) )
					&& (entry.Size <= 0 || new FileInfo( destination ).Length == entry.Size) )
				{
					newState[entry.Path] = Snapshot( entry, destination );
					Interlocked.Increment( ref skippedCount );
					return;
				}

				pending.Add( (entry, destination) );
			}
			finally
			{
				Interlocked.Increment( ref checkedCount );
			}
		} ) );

		while ( !checking.IsCompleted )
		{
			ReportMessages();
			BuildDisplay.Progress( Volatile.Read( ref checkedCount ), manifest.Files.Count, $"{Volatile.Read( ref skippedCount )} current or filtered" );
			Thread.Sleep( 100 );
		}
		ReportMessages();
		checking.GetAwaiter().GetResult();

		if ( pending.IsEmpty )
		{
			BuildDisplay.Progress( manifest.Files.Count, manifest.Files.Count, "Local verification complete" );
			SaveState( repoRoot, manifest.Commit, newState.Values );
			Log.Summary( $"Public artifacts up to date: no downloads needed ({skippedCount} current or filtered, {cachedCount} unchanged since the last run)." );
			return ExitCode.Success;
		}

		var totalCount = pending.Count;
		var totalBytes = pending.Sum( item => Math.Max( 0, item.Entry.Size ) );
		var unknownCount = pending.Count( item => item.Entry.Size <= 0 );
		BuildDisplay.Status( $"Downloading {totalCount} public artifacts" );
		var clock = Stopwatch.StartNew();
		var lastTime = 0.0;
		long lastTransferred = 0;
		var speed = 0.0;
		var downloading = Task.Run( () => Parallel.ForEach( pending, new ParallelOptions { MaxDegreeOfParallelism = MaxParallelDownloads }, item =>
		{
			if ( DownloadArtifact( httpClient, BaseUrl, item.Entry, item.Destination, progress ) )
			{
				newState[item.Entry.Path] = Snapshot( item.Entry, item.Destination );
				Interlocked.Increment( ref progress.Verified );
			}
			else
				Interlocked.Increment( ref progress.Failed );
		} ) );

		while ( true )
		{
			var complete = downloading.IsCompleted;
			ReportMessages();
			var elapsed = clock.Elapsed.TotalSeconds;
			var transferred = Interlocked.Read( ref progress.TransferredBytes );
			var verified = Volatile.Read( ref progress.Verified );
			var useful = Interlocked.Read( ref progress.UsefulBytes );
			var interval = elapsed - lastTime;
			if ( interval > 0 )
			{
				var sample = (transferred - lastTransferred) / interval;
				var weight = 1 - Math.Exp( -interval / 3 );
				speed = lastTime == 0 ? sample : speed + weight * (sample - speed);
			}
			lastTime = elapsed;
			lastTransferred = transferred;
			var bytes = unknownCount == 0
				? $"{Utility.FormatSize( useful )} / {Utility.FormatSize( totalBytes )} useful"
				: $"{Utility.FormatSize( useful )} useful ({unknownCount} file sizes unknown)";
			var eta = unknownCount == 0 && speed >= 1 && useful < totalBytes
				? $"ETA {Math.Ceiling( (totalBytes - useful) / speed ):0}s"
				: verified == totalCount ? "Verified" : unknownCount > 0 ? "ETA unknown" : useful >= totalBytes ? "Verifying" : "ETA calculating";
			if ( complete && Volatile.Read( ref progress.Failed ) > 0 )
				eta = $"{Volatile.Read( ref progress.Failed )} failed";
			var detail = $"{bytes} | {Utility.FormatSize( (long)speed )}/s | {eta} | {verified}/{totalCount} verified | " +
				$"{Volatile.Read( ref progress.Active )} active | {Volatile.Read( ref progress.Retrying )} retrying ({Volatile.Read( ref progress.Retries )} retries)";
			// Byte transfer alone is not completion: every file must pass validation and be installed.
			BuildDisplay.Progress( unknownCount > 0 ? verified : verified == totalCount ? totalBytes : Math.Min( useful, totalBytes * 0.99 ),
				unknownCount > 0 ? totalCount : totalBytes, detail );
			if ( complete )
				break;
			Thread.Sleep( 100 );
		}
		downloading.GetAwaiter().GetResult();

		// Failed files are simply absent from the state, so the next run hashes (and refetches) them.
		SaveState( repoRoot, manifest.Commit, newState.Values );

		if ( progress.Failed > 0 )
		{
			Log.Error( $"Artifact download failed for {progress.Failed} file(s)." );
			Log.Summary( $"Public artifacts: {progress.Verified} updated, {skippedCount} current or filtered, {progress.Failed} failed." );
			return ExitCode.Failure;
		}

		Log.Summary( $"Public artifacts restored: {progress.Verified} updated, {skippedCount} current or filtered; " +
			$"{Utility.FormatSize( progress.UsefulBytes )} useful, {Utility.FormatSize( progress.TransferredBytes )} transferred in {clock.Elapsed.TotalSeconds:0.0}s." );
		return ExitCode.Success;

		void ReportMessages()
		{
			while ( progress.Messages.TryDequeue( out var message ) )
			{
				if ( message.Warning )
					Log.Warning( message.Text );
				else
					Log.Detail( message.Text );
			}
		}
	}

	private sealed class DownloadProgress
	{
		public long UsefulBytes;
		public long TransferredBytes;
		public int Verified;
		public int Failed;
		public int Active;
		public int Retrying;
		public int Retries;
		public readonly ConcurrentQueue<(bool Warning, string Text)> Messages = new();
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

		Log.Detail( $"Fetching manifest: {manifestUrl}" );

		using var response = httpClient.GetAsync( manifestUrl, HttpCompletionOption.ResponseHeadersRead ).GetAwaiter().GetResult();
		if ( response.StatusCode == HttpStatusCode.NotFound )
		{
			Log.Detail( $"Manifest not found for commit {commitHash}." );
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

	private static bool DownloadArtifact( HttpClient httpClient, string baseUrl, ArtifactFileInfo entry, string destination, DownloadProgress progress )
	{
		for ( var attempt = 1; attempt <= MaxDownloadAttempts; attempt++ )
		{
			long attemptBytes = 0;
			Interlocked.Increment( ref progress.Active );
			try
			{
				DownloadArtifactOnce( httpClient, baseUrl, entry, destination, progress, bytes =>
				{
					attemptBytes += bytes;
					Interlocked.Add( ref progress.UsefulBytes, bytes );
					Interlocked.Add( ref progress.TransferredBytes, bytes );
				} );
				return true;
			}
			catch ( Exception ex )
			{
				Interlocked.Add( ref progress.UsefulBytes, -attemptBytes );
				progress.Messages.Enqueue( (true, $"Download attempt {attempt} for {entry.Path ?? entry.Sha256} failed: {ex.Message}") );
			}
			finally
			{
				Interlocked.Decrement( ref progress.Active );
			}

			if ( attempt < MaxDownloadAttempts )
			{
				Interlocked.Increment( ref progress.Retries );
				Interlocked.Increment( ref progress.Retrying );
				Thread.Sleep( TimeSpan.FromMilliseconds( 200 * attempt ) );
				Interlocked.Decrement( ref progress.Retrying );
			}
		}

		return false;
	}

	private static void DownloadArtifactOnce( HttpClient httpClient, string baseUrl, ArtifactFileInfo entry, string destination,
		DownloadProgress progress, Action<int> onBytes )
	{
		var hash = entry.Sha256;
		var expectedSize = entry.Size;
		var artifactUrl = $"{baseUrl.TrimEnd( '/' )}/artifacts/{hash}";

		var targetName = string.IsNullOrWhiteSpace( entry.Path ) ? hash : entry.Path;
		progress.Messages.Enqueue( (false, $"Downloading {targetName} from {artifactUrl} ({(expectedSize > 0 ? Utility.FormatSize( expectedSize ) : "size unknown")})") );

		using var response = httpClient.GetAsync( artifactUrl, HttpCompletionOption.ResponseHeadersRead ).GetAwaiter().GetResult();
		if ( response.StatusCode == HttpStatusCode.NotFound )
		{
			throw new InvalidOperationException( $"Artifact blob {hash} not found." );
		}

		if ( !response.IsSuccessStatusCode )
		{
			throw new InvalidOperationException( $"Failed to download artifact {hash} (HTTP {(int)response.StatusCode})." );
		}

		Directory.CreateDirectory( Path.GetDirectoryName( destination ) );
		var temporary = Path.Combine( Path.GetDirectoryName( destination ), $".sbox-artifact-{Guid.NewGuid():N}.tmp" );
		try
		{
			using ( var downloadStream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult() )
			using ( var fileStream = File.Open( temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None ) )
			{
				var buffer = new byte[81920];
				int read;
				while ( (read = downloadStream.Read( buffer, 0, buffer.Length )) > 0 )
				{
					onBytes( read );
					fileStream.Write( buffer, 0, read );
				}
			}

			var actualSize = new FileInfo( temporary ).Length;
			if ( expectedSize > 0 && actualSize != expectedSize )
				throw new InvalidOperationException( $"Downloaded artifact {hash} has size {actualSize}, expected {expectedSize}." );
			if ( response.Content.Headers.ContentLength is { } contentLength && actualSize != contentLength )
				throw new InvalidOperationException( $"Downloaded artifact {hash} has size {actualSize}, HTTP response expected {contentLength}." );

			var downloadedHash = Utility.CalculateSha256( temporary );
			if ( !string.Equals( downloadedHash, hash, StringComparison.OrdinalIgnoreCase ) )
				throw new InvalidOperationException( $"Hash mismatch for downloaded artifact {hash}." );

			File.Move( temporary, destination, overwrite: true );
		}
		finally
		{
			DeleteIfExists( temporary, message => progress.Messages.Enqueue( (true, message) ) );
		}
	}

	private static void DeleteIfExists( string path, Action<string> onWarning )
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
			onWarning( $"Failed to delete '{path}' during retry cleanup: {ex.Message}" );
		}
	}

	private sealed record ArtifactState
	{
		[JsonPropertyName( "commit" )]
		public string Commit { get; init; }

		[JsonPropertyName( "files" )]
		public List<ArtifactStateEntry> Files { get; init; } = new();
	}

	private sealed record ArtifactStateEntry
	{
		[JsonPropertyName( "path" )]
		public string Path { get; init; }

		[JsonPropertyName( "sha256" )]
		public string Sha256 { get; init; }

		[JsonPropertyName( "size" )]
		public long Size { get; init; }

		[JsonPropertyName( "modified" )]
		public long ModifiedTicks { get; init; }
	}

	private static Dictionary<string, ArtifactStateEntry> LoadState( string repoRoot )
	{
		var result = new Dictionary<string, ArtifactStateEntry>( StringComparer.OrdinalIgnoreCase );
		var path = Path.Combine( repoRoot, StatePath );
		if ( !File.Exists( path ) )
			return result;

		try
		{
			var state = JsonSerializer.Deserialize<ArtifactState>( File.ReadAllText( path ) );
			foreach ( var entry in state?.Files ?? [] )
			{
				if ( entry?.Path is not null && entry.Sha256 is not null )
					result[entry.Path] = entry;
			}
		}
		catch ( Exception ex )
		{
			// Corrupt or from an older layout: fall back to hashing everything once.
			Log.Detail( $"Ignoring unreadable artifact state {path}: {ex.Message}" );
		}

		return result;
	}

	/// <summary>
	/// True when the last run verified this exact hash at this path and the file's size and
	/// modification time have not moved since. Anything else gets hashed for real.
	/// </summary>
	private static bool IsUnchanged( string destination, ArtifactStateEntry known, ArtifactFileInfo entry )
	{
		if ( !string.Equals( known.Sha256, entry.Sha256, StringComparison.OrdinalIgnoreCase ) )
			return false;

		var info = new FileInfo( destination );
		if ( !info.Exists || info.Length != known.Size )
			return false;
		if ( entry.Size > 0 && info.Length != entry.Size )
			return false;

		return info.LastWriteTimeUtc.Ticks == known.ModifiedTicks;
	}

	private static ArtifactStateEntry Snapshot( ArtifactFileInfo entry, string destination )
	{
		var info = new FileInfo( destination );
		return new ArtifactStateEntry
		{
			Path = entry.Path,
			Sha256 = entry.Sha256,
			Size = info.Length,
			ModifiedTicks = info.LastWriteTimeUtc.Ticks
		};
	}

	private static void SaveState( string repoRoot, string commit, IEnumerable<ArtifactStateEntry> entries )
	{
		var path = Path.Combine( repoRoot, StatePath );
		try
		{
			Directory.CreateDirectory( Path.GetDirectoryName( path ) );
			var state = new ArtifactState
			{
				Commit = commit,
				Files = entries.OrderBy( entry => entry.Path, StringComparer.Ordinal ).ToList()
			};
			var temporary = path + ".tmp";
			File.WriteAllText( temporary, JsonSerializer.Serialize( state ) );
			File.Move( temporary, path, overwrite: true );
		}
		catch ( Exception ex )
		{
			// Only a cache; losing it costs one full verify next time.
			Log.Warning( $"Failed to write artifact state {path}: {ex.Message}" );
		}
	}

	private static bool FileMatchesHash( string path, string expectedHash, Action<string> onWarning )
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
			onWarning( $"Failed to compute hash for {path}: {ex.Message}" );
			return false;
		}
	}
}

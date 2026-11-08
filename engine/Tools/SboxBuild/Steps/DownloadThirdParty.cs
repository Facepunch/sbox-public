using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using static Facepunch.Constants;

namespace Facepunch.Steps;

/// <summary>
/// Downloads the <see cref="RemoteDeps"/> releases for the platform being built. Each one
/// records the tag it came from, so only a changed version is fetched again.
/// </summary>
internal class DownloadThirdParty( bool force = false )
{
	private const string Repo = "Facepunch/sbox-thirdparty";
	private const int MaxDownloadAttempts = 5;

	internal ExitCode Run() => BuildDisplay.Run( "Restore third-party dependencies", Restore );

	private ExitCode Restore()
	{
		var platform = NativePlatform.Current.DirectoryName;
		var skipped = 0;
		var pending = new List<(RemoteDeps.Dep Dep, string TargetDir, string Marker, string Stamp)>();

		var dependencies = RemoteDeps.All.Where( dep => dep.SupportsPlatform( platform ) ).ToArray();
		BuildDisplay.Status( $"Checking {dependencies.Length} dependencies ({platform})" );

		foreach ( var dep in dependencies )
		{
			var targetDir = Paths.Absolute( dep.Dir );

			// Per dependency, not per directory: several share game/bin. The platform is
			// in the stamp because a dependency's headers can differ between platforms.
			var marker = Path.Combine( targetDir, $".sbox-thirdparty-{dep.Name}" );
			var stamp = $"{dep.Tag} {platform} r{dep.Rebuild}";

			if ( !force && IsCurrent( marker, stamp ) )
			{
				Log.Detail( $"{dep.Tag} ({platform}) is already current." );
				skipped++;
				continue;
			}

			pending.Add( (dep, targetDir, marker, stamp) );
		}

		var fetched = new Fetched[pending.Count];

		try
		{
			using var httpClient = CreateHttpClient();

			// Downloading and extracting is network and decompression into a folder of its own, so the
			// dependencies do it side by side; a clean checkout fetches all of them. Installing writes into
			// shared folders like game/bin, so that stays one at a time and in order.
			var done = 0;
			BuildDisplay.Status( $"Downloading {pending.Count} dependencies ({platform})" );
			Parallel.For( 0, pending.Count, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
			{
				Log.Detail( $"Downloading {pending[i].Dep.Tag} ({platform})..." );
				fetched[i] = Prepare( httpClient, pending[i].Dep, platform );

				var count = Interlocked.Increment( ref done );
				BuildDisplay.Progress( count, pending.Count, $"{count}/{pending.Count} downloaded and extracted" );
			} );

			if ( fetched.Any( f => f is null ) )
				return ExitCode.Failure;

			for ( var i = 0; i < pending.Count; i++ )
			{
				var (dep, targetDir, marker, stamp) = pending[i];
				BuildDisplay.Status( $"Installing {dep.Tag} ({platform})" );
				BuildDisplay.Progress( i, pending.Count, $"{i}/{pending.Count} installed" );

				var written = Install( dep, platform, targetDir, fetched[i].Extracted );
				WriteMarker( marker, stamp, written );
			}
		}
		catch ( Exception ex )
		{
			Log.Error( $"Third party download failed with error: {ex}" );
			return ExitCode.Failure;
		}
		finally
		{
			foreach ( var f in fetched )
			{
				try
				{
					if ( f is not null && Directory.Exists( f.TempRoot ) )
						Directory.Delete( f.TempRoot, recursive: true );
				}
				catch ( IOException )
				{
					// A leftover temp directory is not worth failing the build over.
				}
			}
		}

		Log.Summary( $"Third party dependencies up to date ({pending.Count} downloaded, {skipped} already current)." );
		return ExitCode.Success;
	}

	/// <summary>
	/// True when the stamp matches and every file the release placed is still on disk. The
	/// file list is the point: several dependencies copy runtime binaries into game/bin, where
	/// a clean or a stray delete can remove them while this marker survives, and trusting the
	/// stamp alone then ships a build that silently loads nothing.
	/// </summary>
	private static bool IsCurrent( string marker, string stamp )
	{
		if ( !File.Exists( marker ) )
			return false;

		var lines = File.ReadAllLines( marker );

		// Markers written before the file list existed cannot be verified, so fetch once more.
		if ( lines.Length < 2 || lines[0].Trim() != stamp )
			return false;

		return lines.Skip( 1 )
			.Where( line => !string.IsNullOrWhiteSpace( line ) )
			.All( line => File.Exists( Paths.Absolute( line.Trim() ) ) );
	}

	private static void WriteMarker( string marker, string stamp, List<string> written )
	{
		var manifest = written
			.Select( Paths.ToSrcRelative )
			.Distinct( StringComparer.OrdinalIgnoreCase )
			.Order( StringComparer.OrdinalIgnoreCase );

		File.WriteAllLines( marker, manifest.Prepend( stamp ) );
	}

	/// <summary>A dependency downloaded and extracted into a temporary folder, ready to install.</summary>
	private sealed record Fetched( string TempRoot, string Extracted );

	/// <summary>Downloads and extracts a dependency into a temporary folder of its own. Null on failure.</summary>
	private static Fetched Prepare( HttpClient httpClient, RemoteDeps.Dep dep, string platform )
	{
		// The packaging action archives Windows as .zip and everything else as .tar.gz.
		var isZip = NativePlatform.Current.IsWindows;
		var asset = $"{dep.Name}-{dep.Version}-{platform}.{(isZip ? "zip" : "tar.gz")}";

		// releases/download is served by the CDN: no API call, so nothing counts against the
		// rate limit, and the URL needs no lookup. The API asset id is the fallback, for a
		// private repo where that URL 404s.
		var cdnUrl = $"https://github.com/{Repo}/releases/download/{dep.Tag}/{asset}";

		// If OSX, then use this
		var temp = NativePlatform.Current.IsOsx ? Paths.Absolute( "obj" ) : Path.GetTempPath();
		var tempRoot = Path.Combine( temp, $"sbox-thirdparty-{Guid.NewGuid():N}" );
		var archive = Path.Combine( tempRoot, asset );
		var extracted = Path.Combine( tempRoot, "extracted" );
		var fetched = new Fetched( tempRoot, extracted );

		Directory.CreateDirectory( tempRoot );

		if ( !Download( httpClient, cdnUrl, archive ) )
		{
			Log.Detail( $"CDN download unavailable for {asset}; trying the GitHub release API." );
			var apiUrl = ResolveAssetUrl( httpClient, dep.Tag, asset );

			if ( apiUrl is null || !Download( httpClient, apiUrl, archive ) )
			{
				Log.Error( $"Unable to download {asset} from {Repo}. Has its workflow published {dep.Tag}?" );
				Directory.Delete( tempRoot, recursive: true );
				return null;
			}
		}

		try
		{
			Directory.CreateDirectory( extracted );

			if ( isZip )
			{
				ZipFile.ExtractToDirectory( archive, extracted, overwriteFiles: true );
			}
			else
			{
				using var file = File.OpenRead( archive );
				using var gzip = new GZipStream( file, CompressionMode.Decompress );
				TarFile.ExtractToDirectory( gzip, extracted, overwriteFiles: true );
			}
		}
		catch ( Exception ex )
		{
			Log.Error( $"Extracting {asset} failed: {ex.Message}" );
			Directory.Delete( tempRoot, recursive: true );
			return null;
		}

		return fetched;
	}

	/// <summary>Installs an extracted dependency and returns every file it placed.</summary>
	private static List<string> Install( RemoteDeps.Dep dep, string platform, string targetDir, string extracted )
	{
		var written = new List<string>();

		// Clear the previous release first, or files it had and this one does not will
		// survive.
		foreach ( var stale in new[]
		{
			Path.Combine( targetDir, "lib", platform ),
			Path.Combine( targetDir, "bin", platform ),
		} )
		{
			if ( Directory.Exists( stale ) )
				Directory.Delete( stale, recursive: true );
		}

		// lib and bin nest per platform to match the .build.cs paths. Everything else,
		// include and plugins, is platform neutral.
		foreach ( var directory in Directory.EnumerateDirectories( extracted ) )
		{
			var name = Path.GetFileName( directory );

			// A release that builds more than we ship names the shipped set itself, already
			// laid out the way it lands beside the engine, so take it as it comes.
			if ( name.Equals( dep.RuntimeTree, StringComparison.OrdinalIgnoreCase ) )
			{
				foreach ( var runtimeDir in dep.RuntimeDir )
					CopyDirectory( directory, Path.Combine( Paths.Absolute( runtimeDir ), platform ), written );

				continue;
			}

			var isPlatformSpecific = name.Equals( "lib", StringComparison.OrdinalIgnoreCase )
				|| name.Equals( "bin", StringComparison.OrdinalIgnoreCase );

			var destination = isPlatformSpecific
				? Path.Combine( targetDir, name, platform )
				: Path.Combine( targetDir, name );

			// Split by file, not directory: oidn ships its runtime binary and its import
			// library in the same lib/. A dependency with its own runtime tree has already
			// said what ships, so nothing here is diverted.
			if ( isPlatformSpecific && dep.RuntimeTree is null )
			{
				var runtimeDirs = dep.RuntimeDir
					.Select( d => Path.Combine( Paths.Absolute( d ), platform ) )
					.ToArray();

				CopyDirectory( directory, destination, written,
					f => IsRuntimeBinary( f, dep.RuntimeExecutables ), runtimeDirs );
				continue;
			}

			CopyDirectory( directory, destination, written );
		}

		return written;
	}

	private static string ResolveAssetUrl( HttpClient httpClient, string tag, string asset )
	{
		var releaseUrl = $"https://api.github.com/repos/{Repo}/releases/tags/{tag}";
		Log.Detail( $"Looking up release: {releaseUrl}" );

		using var request = new HttpRequestMessage( HttpMethod.Get, releaseUrl );
		request.Headers.Accept.Add( new MediaTypeWithQualityHeaderValue( "application/vnd.github+json" ) );

		using var response = httpClient.Send( request );

		// A private repo answers 404 rather than 403 when the token is not accepted, so this
		// is not proof the release is missing.
		if ( response.StatusCode == HttpStatusCode.NotFound )
		{
			Log.Error( $"Could not read release {tag} from {Repo}. Either its workflow has not " +
				"published it, or GH_TOKEN/GITHUB_TOKEN is unset or not being accepted." );
			return null;
		}

		if ( !response.IsSuccessStatusCode )
		{
			Log.Error( $"Looking up release {tag} failed (HTTP {(int)response.StatusCode}). A private repo needs GH_TOKEN or GITHUB_TOKEN set." );
			return null;
		}

		using var json = JsonDocument.Parse( response.Content.ReadAsStream() );

		foreach ( var candidate in json.RootElement.GetProperty( "assets" ).EnumerateArray() )
		{
			if ( candidate.GetProperty( "name" ).GetString() == asset )
				return $"https://api.github.com/repos/{Repo}/releases/assets/{candidate.GetProperty( "id" ).GetInt64()}";
		}

		Log.Error( $"Release {tag} has no asset named {asset}." );
		return null;
	}

	/// <summary>
	/// Several of these run at once, so they report through the log rather than the status line, which
	/// the caller keeps for overall progress.
	/// </summary>
	private static bool Download( HttpClient httpClient, string url, string destination )
	{
		var archiveName = Path.GetFileName( destination );
		Log.Detail( $"Downloading {archiveName} from {url}" );
		for ( var attempt = 1; attempt <= MaxDownloadAttempts; attempt++ )
		{
			try
			{
				using var request = new HttpRequestMessage( HttpMethod.Get, url );
				request.Headers.Accept.Add( new MediaTypeWithQualityHeaderValue( "application/octet-stream" ) );

				using var response = httpClient.Send( request, HttpCompletionOption.ResponseHeadersRead );

				// Not an error worth retrying: the caller has another source to try.
				if ( response.StatusCode == HttpStatusCode.NotFound )
					return false;

				if ( RateLimitDelay( response ) is { } delay )
				{
					Log.Warning( $"GitHub rate limited {archiveName}, waiting {delay.TotalSeconds:0}s (attempt {attempt} of {MaxDownloadAttempts})." );
					Thread.Sleep( delay );
					continue;
				}

				response.EnsureSuccessStatusCode();

				using var stream = response.Content.ReadAsStream();
				using var target = File.Create( destination );
				var length = response.Content.Headers.ContentLength;
				var clock = Stopwatch.StartNew();
				stream.CopyTo( target, 81920 );
				target.Flush();

				if ( length.HasValue && target.Length != length.Value )
					throw new IOException( $"Downloaded {archiveName} has {target.Length} bytes, expected {length.Value}." );

				Log.Detail( $"Downloaded {archiveName}: {Utility.FormatSize( target.Length )} in {clock.Elapsed.TotalSeconds:0.0}s" );
				return true;
			}
			catch ( Exception ex )
			{
				Log.Warning( $"Download attempt {attempt} for {archiveName} failed: {ex.Message}" );

				// Backing off in seconds rather than milliseconds, with jitter, so a whole build
				// matrix retrying at once does not line up on the same instant.
				var delay = TimeSpan.FromSeconds( attempt * attempt ) + TimeSpan.FromMilliseconds( Random.Shared.Next( 500 ) );
				Thread.Sleep( delay );
			}
		}

		return false;
	}

	/// <summary>
	/// How long to wait when GitHub refuses a request for rate reasons, or null when the
	/// response is not a rate limit. Unauthenticated callers get 60 requests an hour per
	/// address, which a build matrix on one runner burns through quickly, so say so.
	/// </summary>
	private static TimeSpan? RateLimitDelay( HttpResponseMessage response )
	{
		if ( response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests) )
			return null;

		// Secondary limits answer with Retry-After; the primary limit reports a reset instead.
		if ( response.Headers.RetryAfter?.Delta is { } delta )
			return Clamp( delta );

		if ( response.Headers.TryGetValues( "x-ratelimit-remaining", out var remaining )
			&& remaining.FirstOrDefault() == "0" )
		{
			var reset = TimeSpan.FromMinutes( 1 );

			if ( response.Headers.TryGetValues( "x-ratelimit-reset", out var resetAt )
				&& long.TryParse( resetAt.FirstOrDefault(), out var epoch ) )
			{
				reset = DateTimeOffset.FromUnixTimeSeconds( epoch ) - DateTimeOffset.UtcNow;

				if ( reset > TimeSpan.FromMinutes( 2 ) )
				{
					Log.Error( $"GitHub's rate limit is exhausted until {DateTimeOffset.FromUnixTimeSeconds( epoch ).ToLocalTime():HH:mm}. " +
						"Set GH_TOKEN or GITHUB_TOKEN to raise it from 60 requests an hour to 5000." );
				}
			}

			return Clamp( reset );
		}

		// A plain 403 is a permission problem, not something waiting will fix.
		return null;

		static TimeSpan Clamp( TimeSpan wait ) =>
			wait < TimeSpan.FromSeconds( 1 ) ? TimeSpan.FromSeconds( 1 )
			: wait > TimeSpan.FromSeconds( 30 ) ? TimeSpan.FromSeconds( 30 )
			: wait;
	}

	/// <summary>
	/// True for files a shipped build loads at run time. Executables count only when asked
	/// for, since most of these ship a build time host tool such as dxc.exe or moc.exe.
	/// </summary>
	private static bool IsRuntimeBinary( string file, bool executables )
	{
		var name = Path.GetFileName( file );

		if ( name.EndsWith( ".dll", StringComparison.OrdinalIgnoreCase )
			|| name.EndsWith( ".dylib", StringComparison.OrdinalIgnoreCase ) )
			return true;

		if ( executables && name.EndsWith( ".exe", StringComparison.OrdinalIgnoreCase ) )
			return true;

		// Shared objects carry their version after the suffix, as in libfoo.so.1.4.3.
		return name.Contains( ".so", StringComparison.OrdinalIgnoreCase );
	}

	/// <summary>
	/// Copies <paramref name="source"/> to <paramref name="destination"/>, diverting the
	/// files matching <paramref name="divert"/> to <paramref name="divertTo"/> instead.
	/// </summary>
	private static void CopyDirectory( string source, string destination, List<string> written,
		Func<string, bool> divert = null, string[] divertTo = null )
	{
		if ( !Directory.Exists( source ) )
			return;

		Directory.CreateDirectory( destination );

		var now = DateTime.UtcNow;

		foreach ( var file in Directory.EnumerateFiles( source, "*", SearchOption.AllDirectories ) )
		{
			var relative = Path.GetRelativePath( source, file );
			List<string> targets = [Path.Combine( destination, relative )];

			// Runtime binaries are placed where a loader will look, beside the executable and
			// so flattened, while still staying here: on posix the shared object is also the
			// link input, and Windows only avoids that because it links an import library.
			if ( divert is not null && divert( file ) )
				targets.AddRange( divertTo.Select( d => Path.Combine( d, Path.GetFileName( file ) ) ) );

			foreach ( var target in targets )
			{
				Directory.CreateDirectory( Path.GetDirectoryName( target ) );
				File.Copy( file, target, overwrite: true );

				// Archives carry the timestamps from when CI built them, older than what is
				// already in game/bin, so MSBuild would skip relinking after a version bump.
				File.SetLastWriteTimeUtc( target, now );
				written.Add( target );
			}
		}
	}

	private static HttpClient CreateHttpClient()
	{
#pragma warning disable CA2000 // HttpClient disposes the handler.
		var handler = new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip
		};
#pragma warning restore CA2000

		var client = new HttpClient( handler )
		{
			Timeout = TimeSpan.FromMinutes( 5 )
		};

		// The GitHub API rejects requests without a User-Agent.
		client.DefaultRequestHeaders.UserAgent.Add( new ProductInfoHeaderValue( "sboxbuild", "1.0" ) );

		// Optional. The CDN download needs no token, so this only matters for the API
		// fallback, where anonymous callers get 60 requests an hour per address.
		var token = Environment.GetEnvironmentVariable( "GH_TOKEN" )
			?? Environment.GetEnvironmentVariable( "GITHUB_TOKEN" );

		if ( !string.IsNullOrWhiteSpace( token ) )
			client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue( "Bearer", token );

		return client;
	}
}

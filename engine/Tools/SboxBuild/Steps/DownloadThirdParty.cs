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

	internal ExitCode Run()
	{
		var platform = NativePlatform.Current.DirectoryName;
		var downloaded = 0;
		var skipped = 0;

		try
		{
			using var httpClient = CreateHttpClient();

			foreach ( var dep in RemoteDeps.All )
			{
				if ( !dep.SupportsPlatform( platform ) )
					continue;

				var targetDir = Paths.Absolute( dep.Dir );

				// Per dependency, not per directory: several share game/bin. The platform is
				// in the stamp because a dependency's headers can differ between platforms.
				var marker = Path.Combine( targetDir, $".sbox-thirdparty-{dep.Name}" );
				var stamp = $"{dep.Tag} {platform} r{dep.Rebuild}";

				if ( !force && IsCurrent( marker, stamp ) )
				{
					skipped++;
					continue;
				}

				Log.Info( $"Downloading {dep.Tag} ({platform})..." );

				if ( !Fetch( httpClient, dep, platform, targetDir, out var written ) )
					return ExitCode.Failure;

				WriteMarker( marker, stamp, written );
				downloaded++;
			}
		}
		catch ( Exception ex )
		{
			Log.Error( $"Third party download failed with error: {ex}" );
			return ExitCode.Failure;
		}

		Log.Info( $"Third party dependencies up to date ({downloaded} downloaded, {skipped} already current)." );
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

	private static bool Fetch( HttpClient httpClient, RemoteDeps.Dep dep, string platform, string targetDir,
		out List<string> written )
	{
		written = [];

		// The packaging action archives Windows as .zip and everything else as .tar.gz.
		var isZip = NativePlatform.Current.IsWindows;
		var asset = $"{dep.Name}-{dep.Version}-{platform}.{(isZip ? "zip" : "tar.gz")}";

		// releases/download is served by the CDN: no API call, so nothing counts against the
		// rate limit, and the URL needs no lookup. The API asset id is the fallback, for a
		// private repo where that URL 404s.
		var cdnUrl = $"https://github.com/{Repo}/releases/download/{dep.Tag}/{asset}";

		var tempRoot = Path.Combine( Path.GetTempPath(), $"sbox-thirdparty-{Guid.NewGuid():N}" );
		var archive = Path.Combine( tempRoot, asset );
		var extracted = Path.Combine( tempRoot, "extracted" );

		try
		{
			Directory.CreateDirectory( tempRoot );

			if ( !Download( httpClient, cdnUrl, archive ) )
			{
				var apiUrl = ResolveAssetUrl( httpClient, dep.Tag, asset );

				if ( apiUrl is null || !Download( httpClient, apiUrl, archive ) )
				{
					Log.Error( $"Unable to download {asset} from {Repo}. Has its workflow published {dep.Tag}?" );
					return false;
				}
			}

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

			return true;
		}
		finally
		{
			try
			{
				if ( Directory.Exists( tempRoot ) )
					Directory.Delete( tempRoot, recursive: true );
			}
			catch ( IOException )
			{
				// A leftover temp directory is not worth failing the build over.
			}
		}
	}

	private static string ResolveAssetUrl( HttpClient httpClient, string tag, string asset )
	{
		var releaseUrl = $"https://api.github.com/repos/{Repo}/releases/tags/{tag}";

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

	private static bool Download( HttpClient httpClient, string url, string destination )
	{
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
					Log.Warning( $"GitHub rate limited {url}, waiting {delay.TotalSeconds:0}s (attempt {attempt} of {MaxDownloadAttempts})." );
					Thread.Sleep( delay );
					continue;
				}

				response.EnsureSuccessStatusCode();

				using var stream = response.Content.ReadAsStream();
				using var target = File.Create( destination );
				stream.CopyTo( target );
				return true;
			}
			catch ( Exception ex )
			{
				Log.Warning( $"Download attempt {attempt} for {url} failed: {ex.Message}" );

				// Backing off in seconds rather than milliseconds, with jitter, so a whole build
				// matrix retrying at once does not line up on the same instant.
				Thread.Sleep( TimeSpan.FromSeconds( attempt * attempt ) + TimeSpan.FromMilliseconds( Random.Shared.Next( 500 ) ) );
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

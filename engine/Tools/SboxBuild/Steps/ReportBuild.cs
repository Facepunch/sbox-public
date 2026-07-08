using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static Facepunch.Constants;

namespace Facepunch.Steps;

/// <summary>
/// Reports this build to the backend - where the backend can then do whatever it wants.
/// </summary>
internal class ReportBuild
{
	private const string BuildUrl = "https://public.facepunch.com/sbox/builds/1";

	internal ExitCode Run()
	{
		try
		{
			return ReportAsync().GetAwaiter().GetResult();
		}
		catch ( Exception ex )
		{
			Log.Error( $"Reporting build failed with error: {ex}" );
			return ExitCode.Failure;
		}
	}

	private async Task<ExitCode> ReportAsync()
	{
		var key = Environment.GetEnvironmentVariable( "SBOX_BUILD_KEY" );
		if ( string.IsNullOrEmpty( key ) )
		{
			Log.Warning( "SBOX_BUILD_KEY not set — skipping build report." );
			return ExitCode.Success;
		}

		var commit = Git( "rev-parse HEAD" );
		if ( string.IsNullOrEmpty( commit ) )
		{
			Log.Error( "Unable to determine the commit hash to report." );
			return ExitCode.Failure;
		}

		var payload = new
		{
			Commit = commit,
			Ref = BuildRef(),
			Tag = BuildTag(),
			CommitDate = Git( "show -s --format=%cI HEAD" ),
			PrNumber = PrNumber(),
			Author = Git( "show -s --format=%an HEAD" ),
			Actor = Environment.GetEnvironmentVariable( "GITHUB_ACTOR" ),
			Message = Git( "show -s --format=%s HEAD" ),
			CiUrl = CiUrl(),
			BuiltAt = DateTimeOffset.UtcNow.ToString( "o" ),
			ArtifactUrl = $"{R2.PublicBaseUrl}/builds/{commit}.zip",
			ArtifactBytes = LongEnv( "BUILD_ARTIFACT_BYTES" ),
			// Set by the upload-steam step earlier in this job (null on PRs, which don't push to Steam).
			SteamClientBuildId = LongEnv( "STEAM_CLIENT_BUILD_ID" ),
			SteamServerBuildId = LongEnv( "STEAM_SERVER_BUILD_ID" ),
		};

		Log.Info( $"Reporting build {commit[..Math.Min( 10, commit.Length )]} ({payload.Ref}) to {BuildUrl}" );

		using var client = new HttpClient();
		using var content = new StringContent( JsonSerializer.Serialize( payload ), Encoding.UTF8, "application/json" );
		content.Headers.Add( "X-Build-Key", key );

		var response = await client.PostAsync( BuildUrl, content );
		if ( !response.IsSuccessStatusCode )
		{
			var body = await response.Content.ReadAsStringAsync();
			Log.Error( $"Build report returned {(int)response.StatusCode} {response.StatusCode}: {body}" );
			return ExitCode.Failure;
		}

		Log.Info( "Build reported." );
		return ExitCode.Success;
	}

	// The branch this build came from. For a PR, GITHUB_HEAD_REF is the source branch; otherwise the ref name
	// (the branch on push, or the tag on a tag build).
	private static string BuildRef()
	{
		var head = Environment.GetEnvironmentVariable( "GITHUB_HEAD_REF" );
		if ( !string.IsNullOrEmpty( head ) )
			return head;

		return Environment.GetEnvironmentVariable( "GITHUB_REF_NAME" ) ?? Git( "rev-parse --abbrev-ref HEAD" );
	}

	// The release tag on this commit, if any. Prefer the CI ref (tag builds), fall back to a tag pointing at HEAD.
	private static string BuildTag()
	{
		if ( Environment.GetEnvironmentVariable( "GITHUB_REF_TYPE" ) == "tag" )
			return Environment.GetEnvironmentVariable( "GITHUB_REF_NAME" );

		var tag = Git( "tag --points-at HEAD" );
		return string.IsNullOrWhiteSpace( tag ) ? null : tag;
	}

	// PR number from GITHUB_REF (refs/pull/<N>/merge) on pull_request events; null otherwise.
	private static int? PrNumber()
	{
		var gitRef = Environment.GetEnvironmentVariable( "GITHUB_REF" ) ?? "";
		var m = Regex.Match( gitRef, @"^refs/pull/(\d+)/" );
		return m.Success && int.TryParse( m.Groups[1].Value, out var n ) ? n : null;
	}

	private static string CiUrl()
	{
		var server = Environment.GetEnvironmentVariable( "GITHUB_SERVER_URL" );
		var repo = Environment.GetEnvironmentVariable( "GITHUB_REPOSITORY" );
		var runId = Environment.GetEnvironmentVariable( "GITHUB_RUN_ID" );
		if ( string.IsNullOrEmpty( server ) || string.IsNullOrEmpty( repo ) || string.IsNullOrEmpty( runId ) )
			return null;

		return $"{server}/{repo}/actions/runs/{runId}";
	}

	// Reads an env var as a long (null if unset or unparseable). Used for values handed over by earlier
	// pipeline steps (artifact size, Steam build ids) which simply won't be present on a PR / local run.
	private static long? LongEnv( string name )
	{
		var value = Environment.GetEnvironmentVariable( name );
		return long.TryParse( value, out var n ) ? n : null;
	}

	// Runs git and returns the trimmed first line of output (null if nothing).
	private static string Git( string args )
	{
		string line = null;
		Utility.RunProcess( "git", args, onDataReceived: ( _, e ) =>
		{
			if ( line is null && !string.IsNullOrWhiteSpace( e.Data ) )
				line = e.Data.Trim();
		} );
		return line;
	}
}

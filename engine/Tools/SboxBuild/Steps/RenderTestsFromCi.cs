using System.Text.Json;
using System.Text.RegularExpressions;
using static Facepunch.Constants;

namespace Facepunch.Steps;

/// <summary>
/// Accept CI's captures as golden truth images (<c>render-tests --from-ci [run]</c>). Lavapipe's pixels differ a little
/// between CPU models, so golden truth images come from CI's runners rather than a developer's machine: this downloads a
/// run's <c>render-tests</c> artifact, uploaded when the tests fail, and copies each failing capture over its golden truth
/// image. Without a run it takes the newest artifact for the commit checked out. Needs the GitHub CLI, signed in.
/// </summary>
internal class RenderTestsFromCi( string run, string[] presets )
{
	const string Artifact = "render-tests";
	const string GoldenTruth = "engine/Tests/GoldenTruth";

	internal ExitCode Run() => BuildDisplay.Run( "Golden truth from CI", () =>
	{
		var head = Git( "rev-parse HEAD" );
		var runId = string.IsNullOrWhiteSpace( run ) ? RunForCommit( head ) : ParseRun( run );
		if ( runId is null ) return ExitCode.Failure;

		var download = Path.Combine( Path.GetTempPath(), $"sbox-render-tests-{runId}-{Guid.NewGuid():N}" );
		try
		{
			BuildDisplay.Status( $"Download {Artifact} from run {runId}" );
			if ( !Utility.RunProcess( "gh", $"run download {runId} -n {Artifact} -D \"{download}\"" ) )
			{
				Log.Error( $"Couldn't download the {Artifact} artifact of run {runId}. CI uploads it only when the tests fail, and keeps it 90 days." );
				return ExitCode.Failure;
			}

			var accepted = 0;
			var commits = new HashSet<string>();
			foreach ( var results in Directory.EnumerateFiles( download, "*.results.json" ).Order() )
			{
				using var json = JsonDocument.Parse( File.ReadAllText( results ) );
				var root = json.RootElement;
				if ( Text( root, "outcome" ) == "pass" ) continue;

				var preset = Text( root, "preset" );
				if ( presets.Length > 0 && !presets.Contains( preset, StringComparer.OrdinalIgnoreCase ) ) continue;

				// Its image matched; a new golden truth image won't fix validation messages
				if ( Text( root, "outcome" ) == "validation" )
				{
					Log.Warning( $"{preset}: failed on Vulkan validation messages, not its image. Fix them or allow them in validation.jsonc." );
					continue;
				}

				var capture = Path.ChangeExtension( Path.ChangeExtension( results, null ), ".png" );
				var image = Text( root, "image" );
				var bucket = Text( root, "bucket" );
				if ( !File.Exists( capture ) || image is null || bucket is null )
				{
					Log.Warning( $"{Path.GetFileName( results )}: no capture to accept." );
					continue;
				}

				var target = $"{GoldenTruth}/{bucket}/{image}";
				Directory.CreateDirectory( Path.GetDirectoryName( target ) );
				File.Copy( capture, target, true );
				Log.Info( $"{preset}: {target}" );
				accepted++;
			}

			var sha = RunCommit( runId );
			if ( sha is not null && sha != head ) Log.Warning( $"Run {runId} rendered {sha[..10]}, not the commit checked out ({head[..10]})." );

			Log.Summary( accepted == 0 ? $"Nothing to accept: run {runId} has no failing captures{(presets.Length > 0 ? " for those presets" : "")}." : $"Accepted {accepted} captures from run {runId} into {GoldenTruth}." );
			return ExitCode.Success;
		}
		finally
		{
			try { if ( Directory.Exists( download ) ) Directory.Delete( download, true ); } catch ( IOException ) { }
		}
	} );

	/// <summary>
	/// A run ID, or the ID in a run's URL (<c>.../actions/runs/123/job/456</c>).
	/// </summary>
	static string ParseRun( string run )
	{
		var match = Regex.Match( run.Trim(), @"^(\d+)$|/actions/runs/(\d+)" );
		if ( match.Success ) return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
		Log.Error( $"'{run}' isn't a workflow run ID or URL." );
		return null;
	}

	/// <summary>
	/// The newest run with a render-tests artifact for <paramref name="sha"/>.
	/// </summary>
	static string RunForCommit( string sha )
	{
		var json = Gh( $"api \"repos/{{owner}}/{{repo}}/actions/artifacts?name={Artifact}&per_page=100\"" );
		if ( json is null ) return null;

		using var doc = JsonDocument.Parse( json );
		var found = doc.RootElement.GetProperty( "artifacts" ).EnumerateArray()
			.Where( x => !x.GetProperty( "expired" ).GetBoolean() && x.GetProperty( "workflow_run" ).GetProperty( "head_sha" ).GetString() == sha )
			.OrderByDescending( x => x.GetProperty( "created_at" ).GetDateTime() )
			.Select( x => x.GetProperty( "workflow_run" ).GetProperty( "id" ).GetInt64().ToString() )
			.FirstOrDefault();

		if ( found is null ) Log.Error( $"No {Artifact} artifact for {sha[..10]}: CI hasn't run the render tests on it, or they passed. Push it, or pass a run ID." );
		return found;
	}

	static string RunCommit( string runId ) => Gh( $"run view {runId} --json headSha --jq .headSha" )?.Trim();

	static string Gh( string arguments )
	{
		var output = new System.Text.StringBuilder();
		var ok = Utility.RunProcess( "gh", arguments, onDataReceived: ( _, e ) => { if ( e.Data is not null ) output.AppendLine( e.Data ); } );
		if ( !ok ) Log.Error( "The GitHub CLI (gh) failed: install it and sign in with gh auth login." );
		return ok ? output.ToString() : null;
	}

	static string Git( string arguments )
	{
		var output = "";
		Utility.RunProcess( "git", arguments, onDataReceived: ( _, e ) => { if ( !string.IsNullOrWhiteSpace( e.Data ) ) output = e.Data.Trim(); } );
		return output;
	}

	static string Text( JsonElement root, string name ) => root.TryGetProperty( name, out var value ) ? value.GetString() : null;
}

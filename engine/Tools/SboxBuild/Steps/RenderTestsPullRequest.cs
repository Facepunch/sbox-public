using System.Text.Json;

namespace Facepunch.Steps;

/// <summary>
/// Whether a pull request's CI run renders the tests (<c>borealis-render-tests --pull-request</c>): they take a while, so
/// only pull requests that change rendering get them, or ones with the run label.
/// </summary>
internal static class RenderTestsPullRequest
{
	// The tests run when the PR has this label, which they add themselves when the PR touches rendering, and are skipped
	// when it has the other
	const string RunLabel = "render-tests";
	const string SkipLabel = "skip-render-tests";

	// What can change a frame: the renderer and its device, the components that feed it, shaders and the content the presets
	// use, and the tests themselves. A change elsewhere that still breaks rendering is caught on master, which always runs
	// them; add its path here when that happens.
	static readonly string[] RenderingPaths =
	[
		"engine/Borealis/", "engine/Borealis.Gpu/", "engine/Borealis.Backend/", "engine/Borealis.Source2/", "engine/Sandbox.Rendering/",
		"engine/Sandbox.Engine/Systems/Render/", "engine/Sandbox.Engine/Systems/SceneSystem/",
		"engine/Sandbox.Engine/Scene/Components/Camera/", "engine/Sandbox.Engine/Scene/Components/Effects/",
		"engine/Sandbox.Engine/Scene/Components/IndirectLighting/", "engine/Sandbox.Engine/Scene/Components/Light/",
		"engine/Sandbox.Engine/Scene/Components/Mesh/", "engine/Sandbox.Engine/Scene/Components/Particles/",
		"engine/Sandbox.Engine/Scene/Components/PostProcessing/", "engine/Sandbox.Engine/Scene/Components/Render/",
		"engine/Sandbox.Engine/Scene/Components/Terrain/", "engine/Sandbox.Engine/Scene/Components/Water/",
		"game/core/shaders/", "game/core/materials/", "game/core/textures/",
		"engine/Launcher/BorealisLab/", "engine/Launcher/LabShared/", "engine/Tests/GoldenTruth/",
		"engine/Tools/SboxBuild/Steps/BorealisRenderTests", "engine/Tools/SboxBuild/Steps/RenderTestsPullRequest",
	];

	/// <summary>
	/// Whether a pull request gets the tests: not with the skip label; with the run label; otherwise when it changes a
	/// rendering path, in which case it gets the label too, so the PR shows why and someone can take it off. The labels are
	/// read from GitHub as the step runs, since a run's event (and a re-run's) carries the labels it started with.
	/// </summary>
	public static bool Run()
	{
		var eventPath = Environment.GetEnvironmentVariable( "GITHUB_EVENT_PATH" );
		if ( string.IsNullOrEmpty( eventPath ) || !File.Exists( eventPath ) ) return true;

		using var ev = JsonDocument.Parse( File.ReadAllText( eventPath ) );
		if ( !ev.RootElement.TryGetProperty( "pull_request", out var pr ) ) return true;

		var number = pr.GetProperty( "number" ).GetInt32();
		var labels = LiveLabels( number ) ?? pr.GetProperty( "labels" ).EnumerateArray().Select( x => x.GetProperty( "name" ).GetString() ).ToArray();

		if ( labels.Contains( SkipLabel ) )
		{
			Log.Summary( $"Skipped: the PR has the {SkipLabel} label." );
			GitHubActions.Note( $"skipped ({SkipLabel})" );
			return false;
		}

		if ( labels.Contains( RunLabel ) )
		{
			Log.Info( $"The PR has the {RunLabel} label." );
			return true;
		}

		var touched = ChangedFiles()?.Where( x => RenderingPaths.Any( p => x.StartsWith( p, StringComparison.OrdinalIgnoreCase ) ) ).ToArray();
		if ( touched is { Length: 0 } )
		{
			Log.Summary( $"Skipped: the PR changes nothing that renders. Add the {RunLabel} label to run them anyway." );
			GitHubActions.Note( "skipped (no rendering changes)" );
			return false;
		}

		Log.Info( touched is null ? "Couldn't tell what the PR changes, so running them." : $"The PR changes rendering: {string.Join( ", ", touched.Take( 5 ) )}{(touched.Length > 5 ? ", ..." : "")}" );
		AddLabel( number );
		return true;
	}

	/// <summary>
	/// The files a pull request changes, or null when git can't say.
	/// </summary>
	static string[] ChangedFiles()
	{
		var diffBase = Utility.PrDiffBase();
		if ( diffBase is null ) return null;

		var files = new List<string>();
		var ok = Utility.RunProcess( "git", $"diff --name-only --no-renames --no-ext-diff {diffBase} HEAD", onDataReceived: ( _, e ) =>
		{
			if ( !string.IsNullOrWhiteSpace( e.Data ) ) files.Add( e.Data.Trim() );
		} );
		return ok ? files.ToArray() : null;
	}

	static HttpClient GitHub()
	{
		var http = new HttpClient { BaseAddress = new Uri( "https://api.github.com/" ), Timeout = TimeSpan.FromSeconds( 30 ) };
		http.DefaultRequestHeaders.UserAgent.ParseAdd( "sbox-render-tests" );
		http.DefaultRequestHeaders.Accept.ParseAdd( "application/vnd.github+json" );
		var token = Environment.GetEnvironmentVariable( "GH_TOKEN" ) ?? Environment.GetEnvironmentVariable( "GITHUB_TOKEN" );
		if ( !string.IsNullOrEmpty( token ) ) http.DefaultRequestHeaders.Authorization = new( "Bearer", token );
		return http;
	}

	static string[] LiveLabels( int number )
	{
		try
		{
			using var http = GitHub();
			var json = http.GetStringAsync( $"repos/{Environment.GetEnvironmentVariable( "GITHUB_REPOSITORY" )}/issues/{number}/labels" ).GetAwaiter().GetResult();
			using var labels = JsonDocument.Parse( json );
			return labels.RootElement.EnumerateArray().Select( x => x.GetProperty( "name" ).GetString() ).ToArray();
		}
		catch ( Exception e )
		{
			Log.Warning( $"Couldn't read the PR's labels ({e.Message}); using the ones the run started with." );
			return null;
		}
	}

	static void AddLabel( int number )
	{
		try
		{
			using var http = GitHub();
			using var body = new StringContent( JsonSerializer.Serialize( new { labels = new[] { RunLabel } } ), System.Text.Encoding.UTF8, "application/json" );
			http.PostAsync( $"repos/{Environment.GetEnvironmentVariable( "GITHUB_REPOSITORY" )}/issues/{number}/labels", body ).GetAwaiter().GetResult().EnsureSuccessStatusCode();
		}
		catch ( Exception e )
		{
			// A fork's PR has a read-only token; the tests still run
			Log.Warning( $"Couldn't add the {RunLabel} label ({e.Message})." );
		}
	}
}

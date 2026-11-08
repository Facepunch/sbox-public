using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using static Facepunch.Constants;

namespace Facepunch.Steps;

/// <summary>
/// Render regression tests without a GPU: SceneLab renders presets on Lavapipe, Mesa's software Vulkan driver, and
/// checks each frame against its golden truth image in <c>engine/Tests/GoldenTruth</c> (<c>scenelab -golden-truth</c>),
/// with Vulkan validation on. Lavapipe renders the same pixels on every machine with the same CPU features, so the
/// golden truth images hold on any runner, GPU or not.
/// </summary>
internal class RenderTests( string[] presets, bool update, bool pullRequest = false, bool notifySlack = false )
{
	// On a pull request (--pull-request) the tests run when the PR has this label, which they add themselves when the PR
	// touches rendering, and are skipped when it has the other
	const string RunLabel = "render-tests";
	const string SkipLabel = "skip-render-tests";

	// What can change a frame: the renderer, native and managed, the components that feed it, shaders and the content
	// the presets use, and the tests themselves. A change elsewhere that still breaks rendering is caught on master, which
	// always runs them; add its path here when that happens.
	static readonly string[] RenderingPaths =
	[
		"src/rendersystem/", "src/scenesystem/", "src/materialsystem2/", "src/materialobjects/", "src/meshsystem/",
		"src/texturelib/", "src/vfx/",
		"engine/Sandbox.SceneRenderer/", "engine/Sandbox.Engine/Systems/Render/", "engine/Sandbox.Engine/Systems/SceneSystem/",
		"engine/Sandbox.Engine/Scene/Components/Camera/", "engine/Sandbox.Engine/Scene/Components/Effects/",
		"engine/Sandbox.Engine/Scene/Components/IndirectLighting/", "engine/Sandbox.Engine/Scene/Components/Light/",
		"engine/Sandbox.Engine/Scene/Components/Mesh/", "engine/Sandbox.Engine/Scene/Components/Particles/",
		"engine/Sandbox.Engine/Scene/Components/PostProcessing/", "engine/Sandbox.Engine/Scene/Components/Render/",
		"engine/Sandbox.Engine/Scene/Components/Terrain/", "engine/Sandbox.Engine/Scene/Components/Water/",
		"engine/Definitions/common/Render/", "engine/Definitions/common/SceneSystem/", "engine/Definitions/engine/Render/",
		"engine/Definitions/engine/SceneSystem/", "engine/Definitions/engine/materialsystem2.def",
		"game/core/shaders/", "game/core/materials/", "game/core/textures/",
		"engine/Launcher/SceneLab/", "engine/Tests/GoldenTruth/", "engine/Tools/SboxBuild/Steps/RenderTests",
	];

	// The Mesa version, built with a static LLVM by Facepunch/sbox-thirdparty's lavapipe workflow. The golden truth
	// images are its pixels, so a new version means regenerating them (--update)
	const string LavapipeVersion = "26.2.4";
	const string LavapipeUrl = $"https://github.com/Facepunch/sbox-thirdparty/releases/download/lavapipe-{LavapipeVersion}/lavapipe-{LavapipeVersion}-win64.zip";
	const string GoldenTruth = "engine/Tests/GoldenTruth";

	// The golden truth images were captured at this size, so it's part of the test
	const int Width = 640;
	const int Height = 360;

	// The frame each preset is captured at, and a GameObject preset's frame after its change ten later. Lavapipe takes up
	// to a couple of seconds over a frame with post processing, and the lab presets look the same at 10 as at 30
	const int CaptureFrame = 10;

	/// <summary>
	/// The core renderer: meshes, materials, textures, lights, shadows, fog, reflections, sky, blending, LOD and float
	/// precision, each in a lab preset that builds its world directly. Append new presets; the order is the run order.
	/// </summary>
	public static readonly string[] Core =
	[
		"Box", "Spheres", "Point Lights", "Spot Light", "Sun Shadows", "Point Shadows", "All Shadows",
		"Reflections", "Gradient Fog", "Sky", "Translucency", "Faded Shadows",
		"Cubemap Fog", "Legacy Combined Fog", "Image Based Ambient", "Spot Shadow", "Static Spot Shadow",
		"Point Lights and Spot", "256 Lights", "256 Overlapping Lights", "LOD Test", "Box Grid",
		"Texture Formats", "Texture Filtering", "Non-Uniform Scale", "Far From Origin", "Depth Range", "Material Mix",
		"Shadow Casters", "Shadow Bias",
	];

	/// <summary>
	/// Features built on the renderer: post processing, effects, UI, terrain and the like, in GameObject presets
	/// rendered through the scene, each checked again after its change. Append new presets; the order is the run order.
	/// Terrain and Clutter, Morphs and Model Deformers repeat exactly too, but take three and a half minutes between them.
	/// </summary>
	public static readonly string[] Extended =
	[
		"Post Processing", "Depth of Field", "Bloom Objects", "Blur 0.25", "Blur 0.5", "Blur 0.75", "Highlight Outlines",
		"Ambient Occlusion", "Screen Space Reflections", "Light Cookie", "Decals", "Refraction", "Soft Particles",
		"Sprites and Particles", "Volumetric Fog", "Contact Shadows",
		"Better Fog Distance", "Better Fog Height", "Better Fog Height Only", "Better Fog Sky", "Better Fog Shared Sky",
		"Better Fog Height Skybox", "Better Fog Height Sky", "Better Fog Rotated Sky", "Better Fog Skybox World",
		"Better Fog Sky Selection", "Better Fog Blend", "Better Fog Orthographic", "Better Fog 3D Skybox",
		"Better Fog Overlay Off", "Better Fog Overlay On", "Better Fog Additive Sprites", "Better Fog Lines",
		"Better Fog Water", "Better Fog Text",
		"Scene Object Children", "Screen UI", "World Panel", "Debug Views", "Auto Exposure",
	];

	/// <summary>
	/// The presets of <paramref name="suite"/>: <c>core</c>, <c>extended</c> or <c>all</c>. Null for an unknown one.
	/// </summary>
	public static string[] Suite( string suite ) => suite.ToLowerInvariant() switch
	{
		"core" => Core,
		"extended" => Extended,
		"all" => [.. Core, .. Extended],
		_ => null,
	};

	internal ExitCode Run() => BuildDisplay.Run( "Render tests", () =>
	{
		if ( !OperatingSystem.IsWindows() )
		{
			Log.Error( "render-tests needs Windows for now: it uses the Windows build of Lavapipe." );
			return ExitCode.Failure;
		}

		if ( pullRequest && !RunForPullRequest() ) return ExitCode.Success;

		// An empty list would have SceneLab's parity run compare every preset it has
		if ( presets.Length == 0 )
		{
			Log.Summary( "No presets to render." );
			return ExitCode.Success;
		}

		var root = Directory.GetCurrentDirectory();
		var game = Path.Combine( root, "game" );
		var scenelab = Path.Combine( game, "bin", "managed", "scenelab.exe" );
		if ( !File.Exists( scenelab ) )
		{
			Log.Error( $"SceneLab not found at {scenelab} - build the engine first." );
			return ExitCode.Failure;
		}

		var icd = FindLavapipe( root );
		if ( icd is null ) return ExitCode.Failure;

		// A fresh folder, so a results file from an earlier run can't stand in for a preset this one didn't check
		var output = Path.Combine( game, "screenshots", "scenelab", "render-tests" );
		// Emptied rather than deleted, so a shell or Explorer window open in it doesn't stop the run
		Directory.CreateDirectory( output );
		foreach ( var file in Directory.EnumerateFiles( output ) ) File.Delete( file );
		foreach ( var dir in Directory.EnumerateDirectories( output ) ) Directory.Delete( dir, true );

		// Validation warnings too: without -vulkan_validation_warnings the render system only reports errors
		var arguments = $"-parity \"{string.Join( ';', presets )}\" -parity-out \"{output}\" -golden-truth \"{Path.Combine( root, GoldenTruth )}\"{(update ? " -golden-truth-update" : "")} " +
			$"-vulkan_allow_cpu -vulkan_enable_validation -vulkan_validation_warnings -width {Width} -height {Height} -capture-frame {CaptureFrame} -novsync";

		// Only Lavapipe, and no implicit layers (vendor switchable graphics, overlays) between it and the engine
		var environment = new Dictionary<string, string>
		{
			["VK_DRIVER_FILES"] = icd,
			["VK_LOADER_LAYERS_DISABLE"] = "~implicit~",
		};

		BuildDisplay.Status( $"Rendering {presets.Length} presets on Lavapipe" );
		// About 1.5 minutes for core and 7 for extended here; a hang mustn't hold the runner for long, but a slower runner
		// needs room
		var passed = Utility.RunProcess( scenelab, arguments, game, environment, timeoutMs: Math.Max( 10, presets.Length / 2 ) * 60 * 1000 );

		var table = Path.Combine( output, "parity.txt" );
		if ( File.Exists( table ) )
		{
			foreach ( var line in File.ReadAllLines( table ) ) Log.Info( line );
		}

		var report = RenderTestsReport.Write( output, Path.Combine( root, GoldenTruth ),
			presets.Select( x => (x, Core.Contains( x ) ? "Core" : Extended.Contains( x ) ? "Extended" : "Other") ),
			$"Lavapipe {LavapipeVersion}   {DateTime.Now:yyyy-MM-dd HH:mm}" );
		Log.Info( $"Report: {report}" );

		// Every preset asked for has to have been checked: one that never got to its capture leaves no results
		var notChecked = presets.Where( x => Outcome( output, x ) != "pass" ).ToArray();
		foreach ( var preset in notChecked ) Log.Error( $"{preset}: {Outcome( output, preset ) ?? "no golden truth check (it never captured)"}" );

		if ( !passed || notChecked.Length > 0 )
		{
			Log.Error( $"Render tests failed. Frames, diffs and .results.json are in {output}" );
			if ( notifySlack ) NotifySlack.Run( $":x: Render tests failed on {Environment.GetEnvironmentVariable( "GITHUB_REF_NAME" ) ?? "this build"}: {notChecked.Length} of {presets.Length} presets don't match their golden truth. The run's render-tests artifact has the report." );
			return ExitCode.Failure;
		}

		Log.Summary( update ? $"Wrote golden truth images for {presets.Length} presets to {GoldenTruth}." : $"{presets.Length} presets match their golden truth images." );
		return ExitCode.Success;
	} );

	/// <summary>
	/// Whether a pull request gets the tests: not with the skip label; with the run label; otherwise when it changes a
	/// rendering path, in which case it gets the label too, so the PR shows why and someone can take it off. The labels are
	/// read from GitHub as the step runs, since a run's event (and a re-run's) carries the labels it started with.
	/// </summary>
	static bool RunForPullRequest()
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

	/// <summary>
	/// A preset's outcome from the <c>.results.json</c> beside its capture (named as SceneLab's parity run names it), or
	/// null when there's none. A GameObject preset with a change has a second capture after it, which has to pass too.
	/// </summary>
	static string Outcome( string output, string preset )
	{
		var name = Regex.Replace( preset, "[^A-Za-z0-9]+", "_" ).Trim( '_' ).ToLowerInvariant();
		var outcome = ReadOutcome( Path.Combine( output, $"{name}.results.json" ) );
		if ( outcome != "pass" ) return outcome;

		var changed = Path.Combine( output, $"{name}_changed.results.json" );
		if ( !File.Exists( changed ) ) return outcome;

		var after = ReadOutcome( changed );
		return after == "pass" ? outcome : $"after its change: {after ?? "no outcome"}";
	}

	static string ReadOutcome( string path )
	{
		if ( !File.Exists( path ) ) return null;

		using var json = JsonDocument.Parse( File.ReadAllText( path ) );
		return json.RootElement.TryGetProperty( "outcome", out var outcome ) ? outcome.GetString() : null;
	}

	/// <summary>
	/// LAVAPIPE_ICD, else the pinned release, downloaded the first time: into the runner's tool cache on CI, which outlives
	/// the job's clean of the checkout, else src/thirdparty. Returns the ICD manifest.
	/// </summary>
	static string FindLavapipe( string root )
	{
		var env = Environment.GetEnvironmentVariable( "LAVAPIPE_ICD" );
		if ( !string.IsNullOrEmpty( env ) )
		{
			if ( File.Exists( env ) ) return env;
			Log.Error( $"LAVAPIPE_ICD is set but {env} doesn't exist." );
			return null;
		}

		var cache = Environment.GetEnvironmentVariable( "RUNNER_TOOL_CACHE" );
		var dir = string.IsNullOrEmpty( cache ) ? Path.Combine( root, "src", "thirdparty", "lavapipe", LavapipeVersion ) : Path.Combine( cache, "lavapipe", LavapipeVersion );
		var icd = Path.Combine( dir, "lvp_icd.json" );
		bool Complete() => File.Exists( icd ) && File.Exists( Path.Combine( dir, "vulkan_lvp.dll" ) );
		if ( Complete() ) return icd;

		// Two runners on one machine can both find it missing: one downloads while the other waits on a lock file beside
		// the cache, then finds it there. The download goes through a name of its own and a folder beside the cache's,
		// moved in whole, so a half-extracted cache never looks like a finished one
		Directory.CreateDirectory( Path.GetDirectoryName( dir ) );
		using var lockFile = Lock( $"{dir}.lock" );
		if ( lockFile is null ) return null;
		if ( Complete() ) return icd;

		BuildDisplay.Status( $"Download Lavapipe {LavapipeVersion}" );
		var archive = Path.Combine( Path.GetTempPath(), $"lavapipe-{LavapipeVersion}-{Guid.NewGuid():N}.zip" );
		var staging = $"{dir}.{Guid.NewGuid():N}";
		try
		{
			using ( var http = new HttpClient { Timeout = TimeSpan.FromMinutes( 30 ) } )
			using ( var response = http.GetAsync( LavapipeUrl, HttpCompletionOption.ResponseHeadersRead ).GetAwaiter().GetResult() )
			{
				response.EnsureSuccessStatusCode();
				using var source = response.Content.ReadAsStream();
				using var target = File.Create( archive );
				source.CopyTo( target );
			}

			// What's there is incomplete, so nothing is using it
			ZipFile.ExtractToDirectory( archive, staging );
			if ( Directory.Exists( dir ) ) Directory.Delete( dir, true );
			Directory.Move( staging, dir );
		}
		catch ( Exception e )
		{
			Log.Error( $"Couldn't fetch Lavapipe from {LavapipeUrl}: {e.Message}. Set LAVAPIPE_ICD to an lvp_icd.json instead." );
			return null;
		}
		finally
		{
			try { File.Delete( archive ); } catch ( IOException ) { }
			try { if ( Directory.Exists( staging ) ) Directory.Delete( staging, true ); } catch ( IOException ) { }
		}

		if ( File.Exists( icd ) ) return icd;
		Log.Error( $"{LavapipeUrl} didn't contain lvp_icd.json" );
		return null;
	}

	/// <summary>
	/// Open <paramref name="path"/> for this process alone, waiting while another has it. Null if it never frees up.
	/// </summary>
	static FileStream Lock( string path )
	{
		var waited = System.Diagnostics.Stopwatch.StartNew();
		while ( true )
		{
			try
			{
				return new FileStream( path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None );
			}
			catch ( IOException ) when ( waited.Elapsed < TimeSpan.FromMinutes( 30 ) )
			{
				Thread.Sleep( 1000 );
			}
			catch ( IOException e )
			{
				Log.Error( $"Gave up waiting for {path}: {e.Message}" );
				return null;
			}
		}
	}
}

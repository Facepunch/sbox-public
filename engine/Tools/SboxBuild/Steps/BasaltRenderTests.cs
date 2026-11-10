using System.IO.Compression;
using static Facepunch.Constants;

namespace Facepunch.Steps;

/// <summary>
/// Basalt's own render tests: BasaltLab renders the lab presets on Lavapipe, Mesa's software Vulkan driver, into a texture
/// with no window, and checks each against Basalt's approved images in <c>engine/Tests/GoldenTruth/Basalt</c>
/// (<c>basaltlab -golden-truth</c>), with Vulkan validation on. Lavapipe renders the same pixels on every machine with the
/// same CPU features, so the images hold on any machine, GPU or not.
/// </summary>
internal class BasaltRenderTests( string[] presets, bool update )
{
	// The Mesa version, built with a static LLVM by Facepunch/sbox-thirdparty's lavapipe workflow. The approved images are
	// its pixels, so a new version means writing them again (--update)
	const string LavapipeVersion = "26.2.4";
	const string LavapipeUrl = $"https://github.com/Facepunch/sbox-thirdparty/releases/download/lavapipe-{LavapipeVersion}/lavapipe-{LavapipeVersion}-win64.zip";
	const string GoldenTruth = "engine/Tests/GoldenTruth/Basalt";

	// The images were captured at this size and frame, so they're part of the test
	const int Width = 640;
	const int Height = 360;
	const int CaptureFrame = 10;

	// Lavapipe runs a frame's command buffers on one thread, so a BasaltLab keeps about two cores busy: the presets are
	// split between this many at once
	const int Runs = 4;

	// Lines in BasaltLab's output that mean it didn't shut down cleanly
	static readonly string[] ShutdownProblems = ["NativeResourceCache: Resource still alive", "was not unregistered"];

	internal ExitCode Run() => BuildDisplay.Run( "Basalt render tests", () =>
	{
		if ( !OperatingSystem.IsWindows() )
		{
			Log.Error( "basalt-render-tests needs Windows for now: it uses the Windows build of Lavapipe." );
			return ExitCode.Failure;
		}

		var root = Directory.GetCurrentDirectory();
		var game = Path.Combine( root, "game" );
		var basaltlab = Path.Combine( game, "bin", "managed", "basaltlab.exe" );
		if ( !File.Exists( basaltlab ) )
		{
			Log.Error( $"BasaltLab not found at {basaltlab} - build the engine first." );
			return ExitCode.Failure;
		}

		var icd = FindLavapipe( root );
		if ( icd is null ) return ExitCode.Failure;

		// A fresh folder, so a results file from an earlier run can't stand in for a preset this one didn't check. Emptied
		// rather than deleted, so a shell or Explorer window open in it doesn't stop the run
		var output = Path.Combine( game, "screenshots", "basaltlab", "render-tests" );
		Directory.CreateDirectory( output );
		foreach ( var file in Directory.EnumerateFiles( output ) ) File.Delete( file );
		foreach ( var dir in Directory.EnumerateDirectories( output ) ) Directory.Delete( dir, true );

		// Only Lavapipe, and no implicit layers (vendor switchable graphics, overlays) between it and the engine. Lavapipe's
		// 256-bit code path loses the blue of lit surfaces in single-sample targets, which these are; its 128-bit one renders
		// them right, and the same on any x64 CPU
		var environment = new Dictionary<string, string>
		{
			["VK_DRIVER_FILES"] = icd,
			["VK_LOADER_LAYERS_DISABLE"] = "~implicit~",
			["LP_NATIVE_VECTOR_WIDTH"] = "128",
		};

		// BasaltLab deals the presets round-robin into the runs itself (-shard), so each preset follows the same ones everywhere
		var selection = presets.Length == 0 ? "-suite core" : $"-presets \"{string.Join( ';', presets )}\"";
		var runs = Enumerable.Range( 1, presets.Length == 0 ? Runs : Math.Min( Runs, presets.Length ) )
			.Select( i => (Presets: $"{selection} -shard {i}/{Runs}", Folder: Path.Combine( output, $"run{i}" )) )
			.ToArray();

		var parallel = Math.Clamp( Environment.ProcessorCount / 4, 1, runs.Length );
		BuildDisplay.Status( $"Rendering on Lavapipe, {parallel} BasaltLabs at a time" );

		var results = new bool[runs.Length];
		var problems = new List<string>[runs.Length];
		Parallel.For( 0, runs.Length, new ParallelOptions { MaxDegreeOfParallelism = parallel }, i =>
		{
			// Validation warnings too: without -vulkan_validation_warnings the render system only reports errors
			var arguments = $"{runs[i].Presets} -out \"{runs[i].Folder}\" -golden-truth \"{Path.GetFullPath( Path.Combine( root, GoldenTruth ) )}\"{(update ? " -golden-truth-update" : "")} " +
				$"-vulkan_allow_cpu -vulkan_enable_validation -vulkan_validation_warnings -width {Width} -height {Height} -capture-frame {CaptureFrame}";

			// Each run's output in a file of its own, and anything that says the run didn't end cleanly: a resource still
			// alive at shutdown, a convar left registered
			var log = new List<string>();
			problems[i] = new List<string>();
			void Output( object sender, System.Diagnostics.DataReceivedEventArgs e )
			{
				lock ( log ) log.Add( e.Data );
				if ( ShutdownProblems.Any( x => e.Data.Contains( x, StringComparison.Ordinal ) ) )
					lock ( problems[i] ) problems[i].Add( e.Data.Trim() );
			}

			// A preset takes about 15 s on Lavapipe here; a hang mustn't hold the machine for long
			results[i] = Utility.RunProcess( basaltlab, arguments, game, environment, timeoutMs: 20 * 60 * 1000, onDataReceived: Output );
			lock ( log ) File.WriteAllLines( Path.Combine( output, $"run{i + 1}.log" ), log );
		} );

		// Into the one folder
		var lines = new List<string>();
		foreach ( var run in runs )
		{
			if ( !Directory.Exists( run.Folder ) ) continue;

			var table = Path.Combine( run.Folder, "results.txt" );
			if ( File.Exists( table ) )
			{
				lines.AddRange( File.ReadAllLines( table ).TakeWhile( x => x.Length > 0 ) );
				File.Delete( table );
			}

			foreach ( var file in Directory.EnumerateFiles( run.Folder ) ) File.Move( file, Path.Combine( output, Path.GetFileName( file ) ), true );
			Directory.Delete( run.Folder, true );
		}

		foreach ( var line in lines ) Log.Info( line );

		for ( int i = 0; i < runs.Length; i++ )
		{
			if ( !results[i] ) Log.Error( $"Run {i + 1} ({runs[i].Presets}) failed; its output is in run{i + 1}.log" );
			foreach ( var problem in problems[i].Take( 10 ) ) Log.Error( $"Run {i + 1}: {problem}" );
		}

		if ( !results.All( x => x ) || problems.Any( x => x.Count > 0 ) || lines.Count == 0 )
		{
			Log.Error( $"Basalt render tests failed. Frames, diffs and .results.json are in {output}" );
			return ExitCode.Failure;
		}

		Log.Summary( update ? $"Wrote Basalt's approved images for {lines.Count} presets to {GoldenTruth}." : $"{lines.Count} presets match Basalt's approved images." );
		return ExitCode.Success;
	} );

	/// <summary>
	/// LAVAPIPE_ICD, else the pinned release in src/thirdparty, downloaded the first time. Returns the ICD manifest.
	/// </summary>
	static string FindLavapipe( string root )
	{
		var env = Environment.GetEnvironmentVariable( "LAVAPIPE_ICD" );
		if ( !string.IsNullOrEmpty( env ) )
		{
			// The Vulkan loader takes a Windows path: with forward slashes the driver fails to load (VK_ERROR_OUT_OF_HOST_MEMORY)
			env = Path.GetFullPath( env );
			if ( File.Exists( env ) ) return env;
			Log.Error( $"LAVAPIPE_ICD is set but {env} doesn't exist." );
			return null;
		}

		var dir = Path.Combine( root, "src", "thirdparty", "lavapipe", LavapipeVersion );
		var icd = Path.Combine( dir, "lvp_icd.json" );
		if ( File.Exists( icd ) && File.Exists( Path.Combine( dir, "vulkan_lvp.dll" ) ) ) return icd;

		// Extracted beside the folder and moved in whole, so a half-extracted one never looks finished
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

			ZipFile.ExtractToDirectory( archive, staging );
			if ( Directory.Exists( dir ) ) Directory.Delete( dir, true );
			Directory.CreateDirectory( Path.GetDirectoryName( dir ) );
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
}

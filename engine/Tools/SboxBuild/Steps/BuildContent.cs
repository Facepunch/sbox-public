using Sandbox.ContentBuild;
using System.Text.Json;
using static Facepunch.Constants;

namespace Facepunch.Steps;

/// <summary>
/// Builds the engine and addon content roots as one graph with Sandbox.ContentBuild, with the compilers the editor
/// has: the resource compiler's and the managed ones of the engine and its built-in projects. Records live where the
/// editor's startup build looks for them, so it starts from what this built.
/// </summary>
/// <param name="onlyRoot">A root under game/ to build, with whatever it reads from the others.</param>
/// <param name="force">Compile everything again, current or not.</param>
internal class BuildContent( string onlyRoot = null, bool force = false )
{
	internal ExitCode Run() => BuildDisplay.Run( "Build content", () =>
	{
		var gameDir = Path.Combine( Directory.GetCurrentDirectory(), "game" );
		var worker = Path.Combine( gameDir, "bin", "managed", OperatingSystem.IsWindows() ? "contentworker.exe" : "contentworker" );

		// The projects' compilers, compiled once for every worker, deleted once the workers that load them are gone
		var compilers = Directory.CreateTempSubdirectory( "sbox-compilers" ).FullName;
		ResourceCompiler compiler = null;
		ContentRoot[] roots;
		BuildReport report;
		try
		{
			BuildDisplay.Status( "Compile tools" );
			var rootsFile = Path.Combine( compilers, "roots.json" );
			if ( !Utility.RunProcess( worker, $"-compileprojects \"{compilers}\" -addonroots \"{rootsFile}\"", gameDir ) ) return ExitCode.Failure;

			roots = JsonSerializer.Deserialize<ContentRoot[]>( File.ReadAllText( rootsFile ) );
			var request = new BuildRequest
			{
				Roots = roots.Select( root => root with { PublishTransientsWithAssets = root.Mod != "menu" } ).ToArray(),
				MaxJobs = ResourceCompiler.DefaultMaxJobs,

				// Only the menu publishes here. Other roots ship generated files with their own assets.
				OutputRoots = new Dictionary<string, string> { ["transient"] = Path.Combine( gameDir, "addons", "menu", "Transients" ) },
				Select = onlyRoot is null ? null : [Path.Combine( gameDir, onlyRoot )],
				Force = force,
				CompileProgress = ( finished, activeRoots ) =>
				{
					var count = finished == 1 ? "1 asset compiled" : $"{finished} assets compiled";
					BuildDisplay.Detail( activeRoots.Count == 0 ? count : $"Compiling {string.Join( ", ", activeRoots )} ({count})" );
				},
			};

			compiler = new ResourceCompiler( worker, new AssetTypeTable( Path.Combine( gameDir, "bin", "assettypes.txt" ) ), request.Mounts, compilers );
			BuildDisplay.Status( "Compile" );
			report = ContentBuilder.Build( request, compiler );
		}
		finally
		{
			compiler?.Finished();
			Directory.Delete( compilers, true );
		}

		if ( !BuildDisplay.IsActive || !report.Success ) Log.Info( report.Error ?? report.Log.ToString() );
		foreach ( var root in roots )
		{
			int Count( List<string> list ) => list.Count( x => x.StartsWith( $"{root.Mod}:", StringComparison.OrdinalIgnoreCase ) );
			Log.Info( $"{root.Mod}: {Count( report.Jobs )} compiled, {Count( report.Failed )} failed, {Count( report.Blocked )} blocked, {Count( report.Unsupported )} unsupported, {Count( report.Removed )} removed" );
		}
		Log.Info( $"{report.DescribeMisses} describe misses" );
		Log.Info( $"Phases: {string.Join( ", ", report.Phases.Select( x => $"{x.Phase} {x.Time.TotalSeconds:F1}s" ) )}" );
		Log.Info( $"Job time: {string.Join( ", ", report.JobTime.OrderByDescending( x => x.Value.Time ).Select( x => $"{x.Key} {x.Value.Jobs}x {x.Value.Time.TotalSeconds:F0}s" ) )}" );
		Log.Info( $"{compiler.Launches} content workers started, {compiler.Evicted} stopped for one with other mounts" + (compiler.Ended.Count > 0 ? $", ended early: {string.Join( "; ", compiler.Ended.GroupBy( x => x ).Select( x => $"{x.Count()}x {x.Key}" ) )}" : "") );

		return report.Success ? ExitCode.Success : ExitCode.Failure;
	} );
}

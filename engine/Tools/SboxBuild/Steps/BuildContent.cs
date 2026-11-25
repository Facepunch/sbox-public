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
	private static string FolderName( ContentRoot root )
	{
		var folder = new DirectoryInfo( root.Path );
		return folder.Name.Equals( "Assets", StringComparison.OrdinalIgnoreCase ) ? folder.Parent.Name : folder.Name;
	}

	/// <summary>
	/// Reads the names for the first bootstrap frame, before the content worker is built.
	/// The worker supplies the final list when the Content stage starts compiling.
	/// </summary>
	internal static IEnumerable<BuildDisplay.StageRow> DiscoverRootRows( string gameDir )
	{
		var engineRoots = ContentRoots.Engine( gameDir );
		foreach ( var root in engineRoots )
			yield return new BuildDisplay.StageRow( FolderName( root ) );

		var addons = new DirectoryInfo( Path.Combine( gameDir, "addons" ) );
		if ( !addons.Exists ) yield break;

		var options = new EnumerationOptions { AttributesToSkip = 0 };
		var assetFolders = new EnumerationOptions { AttributesToSkip = 0, MatchCasing = MatchCasing.CaseInsensitive };
		foreach ( var folder in addons.EnumerateDirectories( "*", options ).OrderBy( x => x.Name, StringComparer.OrdinalIgnoreCase ) )
		{
			var assets = Path.Combine( folder.FullName, "Assets" );
			if ( engineRoots.Any( root => string.Equals( root.Path, assets, StringComparison.OrdinalIgnoreCase ) ) ) continue;
			if ( !folder.EnumerateDirectories( "Assets", assetFolders ).Any() ) continue;

			if ( folder.GetFiles( "*.sbproj", options ).Length == 1 )
				yield return new BuildDisplay.StageRow( folder.Name );
		}
	}

	internal ExitCode Run() => BuildDisplay.Run( "Build content", () =>
	{
		var gameDir = Path.Combine( Directory.GetCurrentDirectory(), "game" );
		var worker = Path.Combine( gameDir, "bin", "managed", OperatingSystem.IsWindows() ? "contentworker.exe" : "contentworker" );

		// The projects' compilers, compiled once for every worker, deleted once the workers that load them are gone
		var compilers = Directory.CreateTempSubdirectory( "sbox-compilers" ).FullName;
		ResourceCompiler compiler = null;
		Dictionary<string, string> foldersByMod;
		BuildReport report;
		try
		{
			BuildDisplay.Status( "Compile tools" );
			var rootsFile = Path.Combine( compilers, "roots.json" );
			if ( !Utility.RunProcess( worker, $"-compileprojects \"{compilers}\" -addonroots \"{rootsFile}\"", gameDir ) ) return ExitCode.Failure;

			var roots = JsonSerializer.Deserialize<ContentRoot[]>( File.ReadAllText( rootsFile ) );
			foldersByMod = roots.ToDictionary( root => root.Mod, FolderName, StringComparer.OrdinalIgnoreCase );
			BuildDisplay.SetStageChildren( [.. foldersByMod.Select( root => new BuildDisplay.StageRow( root.Key, root.Value ) )] );

			var request = new BuildRequest
			{
				Roots = roots.Select( root => root with { PublishTransientsWithAssets = root.Mod != "menu" } ).ToArray(),
				MaxJobs = ResourceCompiler.DefaultMaxJobs,

				// Only the menu publishes here. Other roots ship generated files with their own assets.
				OutputRoots = new Dictionary<string, string> { ["transient"] = Path.Combine( gameDir, "addons", "menu", "Transients" ) },
				Select = onlyRoot is null ? null : [Path.Combine( gameDir, onlyRoot )],
				Force = force,
				CompileProgress = progress =>
				{
					var count = $"{progress.Finished}/{progress.Total} assets compiled";
					var detail = progress.Compiling.Count == 0 ? count : $"Compiling {string.Join( ", ", progress.Compiling.Select( mod => foldersByMod[mod] ) )} ({count})";
					BuildDisplay.Detail( detail, progress.Compiling );
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
		foreach ( var (mod, folder) in foldersByMod )
		{
			var prefix = $"{mod}:";
			int Count( List<string> list ) => foldersByMod.Count == 1 ? list.Count : list.Count( x => x.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) );

			var compiled = Count( report.Jobs );
			var removed = Count( report.Removed );
			var failed = Count( report.Failed );
			var blocked = Count( report.Blocked );
			var unsupported = Count( report.Unsupported );

			if ( failed > 0 || unsupported > 0 )
			{
				BuildDisplay.FinishChild( mod, "Failed" );
			}
			else if ( blocked > 0 )
			{
				BuildDisplay.FinishChild( mod, "Blocked" );
			}
			else if ( report.Error is null && !report.Cancelled )
			{
				BuildDisplay.FinishChild( mod, compiled == 0 && removed == 0 ? "Up to date" : "Done" );
			}

			Log.Info( $"{folder}: {compiled} compiled, {failed} failed, {blocked} blocked, {unsupported} unsupported, {removed} removed" );
		}
		Log.Info( $"{report.DescribeMisses} describe misses" );
		Log.Info( $"Phases: {string.Join( ", ", report.Phases.Select( x => $"{x.Phase} {x.Time.TotalSeconds:F1}s" ) )}" );
		Log.Info( $"Job time: {string.Join( ", ", report.JobTime.OrderByDescending( x => x.Value.Time ).Select( x => $"{x.Key} {x.Value.Jobs}x {x.Value.Time.TotalSeconds:F0}s" ) )}" );
		Log.Info( $"{compiler.Launches} content workers started, {compiler.Evicted} stopped for one with other mounts" + (compiler.Ended.Count > 0 ? $", ended early: {string.Join( "; ", compiler.Ended.GroupBy( x => x ).Select( x => $"{x.Count()}x {x.Key}" ) )}" : "") );

		return report.Success ? ExitCode.Success : ExitCode.Failure;
	} );
}

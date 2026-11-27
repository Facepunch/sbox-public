using static Facepunch.Constants;

namespace Facepunch.Steps;

/// <summary>
/// The shader corpus gate (docs/reference/shader-api.md, "The corpus"): <c>ShaderCompiler -slang-corpus</c> compiles every
/// core shader, the BorealisLab surfaces and the corpus folders' published Slang shaders and package <c>.shader_c</c>
/// files with this build's shader library and compiler, and fails when one the baseline
/// (<c>engine/Tests/ShaderCorpus/baseline.tsv</c>) has compiling doesn't now. The corpus folders come from
/// <c>--corpus</c> or the <c>SBOX_SHADER_CORPUS</c> variable (folders separated by <c>;</c>); without them, only the core
/// shaders and the lab's surfaces are checked. On a pull request (<c>--pull-request</c>) it runs only when the shader
/// library or compiler changes. <c>--update</c> writes the results as the new baseline.
/// </summary>
internal class ShaderCorpus( string[] corpus, bool update, bool pullRequest )
{
	/// <summary>What can change whether a shader compiles: the library, the compiler, Slang and the corpus's own files.</summary>
	static readonly string[] CompilerPaths =
	[
		"game/core/shaders/", "engine/Sandbox.ShaderCompiler/", "engine/Borealis.Backend/ShaderCorpus.cs", "engine/Borealis.Backend/OfflineShaders.cs",
		"engine/Launcher/BorealisLab/Surfaces/", "engine/Tests/ShaderCorpus/", "engine/Tools/SboxBuild/Steps/ShaderCorpus.cs",
		"src/thirdparty/slang/", "game/bin/win64/slang", "game/bin/linuxsteamrt64/libslang",
	];

	internal ExitCode Run() => BuildDisplay.Run( "Shader corpus", () =>
	{
		if ( pullRequest && !Changed() ) return ExitCode.Success;

		var root = Directory.GetCurrentDirectory();
		var game = Path.Combine( root, "game" );
		var compiler = Path.Combine( game, "bin", "managed", OperatingSystem.IsWindows() ? "ShaderCompiler.exe" : "ShaderCompiler" );
		if ( !File.Exists( compiler ) )
		{
			Log.Error( $"No shader compiler at {compiler}: build managed first" );
			return ExitCode.Failure;
		}

		var folders = corpus.Length > 0 ? corpus : (Environment.GetEnvironmentVariable( "SBOX_SHADER_CORPUS" ) ?? "").Split( ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );
		if ( folders.Length == 0 ) Log.Warning( "No corpus folders (--corpus or SBOX_SHADER_CORPUS): checking the core shaders and the lab's surfaces only." );

		var baseline = Path.Combine( root, "engine", "Tests", "ShaderCorpus", "baseline.tsv" );
		var results = Path.Combine( game, ".source2", "borealiscorpus", "results.tsv" );
		var arguments = $"-slang-corpus --baseline \"{baseline}\" --results \"{results}\"{string.Concat( folders.Select( f => $" --corpus \"{Path.GetFullPath( f )}\"" ) )}{(update ? " --update" : "")}";
		var success = Utility.RunProcess( compiler, arguments, game, onDataReceived: ( _, e ) =>
		{
			if ( e.Data is null ) return;
			Log.Info( e.Data );
			if ( e.Data.StartsWith( "BREAK ", StringComparison.Ordinal ) ) GitHubActions.Error( "Shader corpus break", e.Data[6..] );
			if ( e.Data.StartsWith( "Shader corpus: ", StringComparison.Ordinal ) && e.Data.Contains( " breaks" ) ) GitHubActions.Note( e.Data["Shader corpus: ".Length..] );
		} );

		if ( success ) return ExitCode.Success;
		Log.Error( $"A shader that compiled no longer does (results in {results}). Fix it, or raise the shader API level with a shim (docs/reference/shader-api.md); if the change is meant, run shader-corpus --update and commit the baseline." );
		return ExitCode.Failure;
	} );

	/// <summary>Whether the pull request changes the shader library or compiler; true when git can't say.</summary>
	static bool Changed()
	{
		var diffBase = Utility.PrDiffBase();
		if ( diffBase is null ) return true;

		var files = new List<string>();
		if ( !Utility.RunProcess( "git", $"diff --name-only --no-renames --no-ext-diff {diffBase} HEAD", onDataReceived: ( _, e ) =>
		{
			if ( !string.IsNullOrWhiteSpace( e.Data ) ) files.Add( e.Data.Trim().Replace( '\\', '/' ) );
		} ) ) return true;

		var touched = files.Where( f => CompilerPaths.Any( p => f.StartsWith( p, StringComparison.OrdinalIgnoreCase ) ) ).ToArray();
		if ( touched.Length == 0 )
		{
			Log.Summary( "Skipped: the PR doesn't change the shader library or compiler." );
			GitHubActions.Note( "skipped (no shader library or compiler changes)" );
			return false;
		}

		Log.Info( $"The PR changes the shader library or compiler: {string.Join( ", ", touched.Take( 5 ) )}{(touched.Length > 5 ? ", ..." : "")}" );
		return true;
	}
}

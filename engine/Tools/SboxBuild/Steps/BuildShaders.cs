using System.Text.RegularExpressions;
using static Facepunch.Constants;

namespace Facepunch.Steps;

internal class BuildShaders( bool forced = false, bool seed = false )
{
	internal ExitCode Run()
	{
		// Compiled shaders aren't committed. Starting from the newest published ones means only the
		// shaders changed since then compile; without them, everything does.
		if ( seed )
		{
			if ( new DownloadPublicArtifacts( shadersOnly: true ).Run() == ExitCode.Success )
				GitHubActions.Note( "seeded from published shaders" );
			else
				Log.Warning( "Couldn't download the published shaders, compiling them all." );
		}

		return Compile();
	}

	private ExitCode Compile() => BuildDisplay.Run( "Build shaders", () =>
	{
		try
		{
			string rootDir = Directory.GetCurrentDirectory();
			string gameDir = Path.Combine( rootDir, "game" );
			// Named for the assembly, which is cased: Windows did not care, Linux does. The apphost
			// carries no extension there either.
			string shaderCompilerPath = Path.Combine( gameDir, "bin", "managed",
				OperatingSystem.IsWindows() ? "ShaderCompiler.exe" : "ShaderCompiler" );

			// Verify shader compiler exists
			if ( !File.Exists( shaderCompilerPath ) )
			{
				Log.Error( $"Error: Shader compiler executable not found at {shaderCompilerPath}" );
				return ExitCode.Failure;
			}

			var result = RunShaderCompiler( shaderCompilerPath, gameDir );
			if ( result == ExitCode.Success ) CompileGpuDriven( shaderCompilerPath, gameDir );
			return result;
		}
		catch ( Exception ex )
		{
			Log.Error( $"Shader compilation failed with error: {ex}" );
			return ExitCode.Failure;
		}
	} );

	/// <summary>
	/// Shaders Borealis.Gpu draws GPU-driven, whose <c>.gpudriven.shader_c</c> variant is built too
	/// (docs/managed/borealis-gpu-driven.md). A variant that fails only warns: Borealis draws that shader the classic way.
	/// </summary>
	static readonly string[] GpuDrivenShaders =
	[
		"core/shaders/depth_only.shader",
		"core/shaders/complex.shader",
		"core/shaders/blendable.shader",
		"core/shaders/foliage.shader",
		"core/shaders/bark.shader",
		"core/shaders/glass.shader",
		"core/shaders/static_overlay.shader",
		"core/shaders/generic.shader",
		"core/shaders/simple.shader",
	];

	void CompileGpuDriven( string shaderCompilerPath, string gameDir )
	{
		BuildDisplay.Status( "Compile GPU-driven variants" );
		var paths = string.Join( " ", GpuDrivenShaders.Select( s => $"\"{Path.GetFullPath( Path.Combine( gameDir, s ) )}\"" ) );
		var arguments = $"{paths} -gpudriven{(forced ? " -f" : "")}";
		var success = Utility.RunProcess( shaderCompilerPath, arguments, gameDir, onDataReceived: ( sender, e ) =>
		{
			if ( e.Data != null && !BuildDisplay.IsActive ) Log.Info( e.Data );
		} );

		if ( !success ) Log.Warning( "Some GPU-driven shader variants failed to compile; Borealis.Gpu draws those shaders the classic way." );
	}

	private ExitCode RunShaderCompiler( string shaderCompilerPath, string workingDirectory )
	{
		// Build arguments - only include -f if forced is true
		string arguments = "*";
		if ( forced )
		{
			arguments += " -f";
		}

		var current = 0;
		var total = 0;
		var compiled = 0;
		var upToDate = 0;
		BuildDisplay.Status( "Compile shaders" );

		bool success = Utility.RunProcess(
			shaderCompilerPath,
			arguments,
			workingDirectory,
			onDataReceived: ( sender, e ) =>
			{
				if ( e.Data != null )
				{
					if ( !BuildDisplay.IsActive ) Log.Info( e.Data );

					var match = Regex.Match( e.Data, @"^\((\d+)/(\d+)\)\s*(.*)$" );
					if ( match.Success && int.TryParse( match.Groups[1].Value, out current )
						&& int.TryParse( match.Groups[2].Value, out total ) )
					{
						BuildDisplay.Detail( match.Groups[3].Value );
						BuildDisplay.Progress( Math.Max( 0, current - 1 ), total );
					}
					else if ( e.Data.Contains( "Compiled successfully in" )
						|| e.Data.Contains( "Skipped, up to date." )
						|| e.Data.Contains( "Compile failed." ) )
					{
						if ( e.Data.Contains( "Compiled successfully in" ) ) compiled++;
						else if ( e.Data.Contains( "Skipped, up to date." ) ) upToDate++;
						BuildDisplay.Progress( current, total );
					}
				}
			}
		);

		GitHubActions.Note( $"{compiled} compiled, {upToDate} up to date" );

		if ( !success )
		{
			Log.Error( $"Shader compiler failed" );
			return ExitCode.Failure;
		}

		return ExitCode.Success;
	}
}

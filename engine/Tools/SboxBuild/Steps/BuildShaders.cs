using System.Diagnostics;
using System.Runtime.InteropServices;
using static Facepunch.Constants;

namespace Facepunch.Steps;

internal class BuildShaders( string name, bool forced = false ) : Step( name )
{
	protected override ExitCode RunInternal()
	{
		try
		{
			string rootDir = Directory.GetCurrentDirectory();
			string gameDir = Path.Combine( rootDir, "game" );

			string shaderCompilerPath;
			if (OperatingSystem.IsWindows()) 
				shaderCompilerPath = Path.Combine( gameDir, "bin", "managed", "shadercompiler.exe" );
			else
				shaderCompilerPath = Path.Combine( gameDir, "bin", "managed", "shadercompiler" );
			// Verify shader compiler exists
			if ( !File.Exists( shaderCompilerPath ) )
			{
				Log.Error( $"Error: Shader compiler executable not found at {shaderCompilerPath}" );
				return ExitCode.Failure;
			}

			return RunShaderCompiler( shaderCompilerPath, gameDir );
		}
		catch ( Exception ex )
		{
			Log.Error( $"Shader compilation failed with error: {ex}" );
			return ExitCode.Failure;
		}
	}

	private ExitCode RunShaderCompiler( string shaderCompilerPath, string workingDirectory )
	{
		// Build arguments - only include -f if forced is true
		string arguments = "*";
		if ( forced )
		{
			arguments += " -f";
		}

		// Track if any shaders were compiled
		var shaderCompiled = false;

		bool success = Utility.RunProcess(
			shaderCompilerPath,
			arguments,
			workingDirectory,
			onDataReceived: ( sender, e ) =>
			{
				if ( e.Data != null )
				{
					Log.Info( e.Data );

					if ( e.Data.Contains( "Compiled successfully in" ) )
					{
						shaderCompiled = true;
					}
				}
			}
		);

		if ( !success )
		{
			Log.Error( $"Shader compiler failed" );
			return ExitCode.Failure;
		}

		if ( shaderCompiled && Utility.IsCi() )
		{
			Log.Error( $"Step Failed because at least one shader had to be recompiled, please compile shaders locally before committing." );
			return ExitCode.Failure;
		}

		return ExitCode.Success;
	}
}

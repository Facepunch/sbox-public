using System;
using static Facepunch.Constants;

namespace Facepunch.Steps;

/// <summary>
/// Step to generate Visual Studio solutions without building them
/// </summary>
internal class GenerateSolutions( string name, BuildConfiguration configuration = BuildConfiguration.Developer ) : Step( name )
{
	private readonly Platform platform = Platform.Create();

	protected override ExitCode RunInternal()
	{
		// Generate solutions based on configuration
		if ( configuration == BuildConfiguration.Retail )
		{
			return GenerateRetailSolutions();
		}
		else
		{
			return GenerateDeveloperSolutions( configuration );
		}
	}

	private ExitCode GenerateRetailSolutions()
	{
		Log.Info( "Generating Retail solutions..." );

		if ( System.OperatingSystem.IsLinux() )
		{
			Log.Warning( "Skipping solution generation on Linux as vpc is not available or supported." );
			return ExitCode.Success;
		}

		string vpcPath = Path.Combine( "src", "devtools", "bin", platform.PlatformID, "vpc" );

		// Generate schemacompiler_all solution
		if ( !Utility.RunProcess( vpcPath, $"/mksln schemacompiler_all /define:BUILDBOT /retail /checkfiles /checkfiles_error /quiet /fi /fc /forceunity /define:PUBLISH /2026 /sbox /{platform.PlatformID} \"@schemacompiler_all\" /defdmacro:SBOX=1", "src" ) )
			return ExitCode.Failure;

		// Generate buildbot_all_win64 solution
		if ( !Utility.RunProcess( vpcPath, $"/mksln buildbot_all_{platform.PlatformID} /define:BUILDBOT /retail /checkfiles /checkfiles_error /quiet /fi /fc /forceunity /define:PUBLISH /2026 /sbox /{platform.PlatformID} +everything +native_everything +sbox_game /defdmacro:SBOX=1", "src" ) )
			return ExitCode.Failure;

		// Generate buildbot_tools_win64 solution
		// Used to rebuild tools on Windows, because sometimes it corrupts which is insane
		if ( platform is WindowsPlatform )
		{
			if ( !Utility.RunProcess( vpcPath, $"/mksln buildbot_tools_{platform.PlatformID} /define:BUILDBOT /retail /checkfiles /checkfiles_error /quiet /fi /fc /forceunity /define:PUBLISH /2026 /{platform.PlatformID} +hammer +modeldoc_editor +animgraph_editor /defdmacro:SBOX=1", "src" ) )
				return ExitCode.Failure;
		}

		return ExitCode.Success;
	}

	private ExitCode GenerateDeveloperSolutions( BuildConfiguration configuration )
	{
		Log.Info( "Generating Developer solutions..." );

		if ( System.OperatingSystem.IsLinux() )
		{
			Log.Warning( "Skipping solution generation on Linux as vpc is not available or supported." );
			return ExitCode.Success;
		}

		string vpcPath = Path.Combine( "src", "devtools", "bin", platform.PlatformID, "vpc" );

		string extraDefines = "";
		if ( configuration == BuildConfiguration.DeveloperMemoryDebug )
		{
			Log.Info( "Using Memory Debug macros" );
			extraDefines = "/define:MEMDEBUG_TRACKING";
		}

		// Generate schemacompiler_all solution
		Log.Info( "Generating Schema Compiler solution" );
		if ( !Utility.RunProcess( vpcPath, $"/mksln schemacompiler_all {extraDefines} /checkfiles /checkfiles_error /quiet /forceunity /2026 /sbox /{platform.PlatformID} \"@schemacompiler_all\" /defdmacro:SBOX=1", "src" ) )
			return ExitCode.Failure;

		// Generate sbox_game solution
		Log.Info( "Generating sbox_game solution" );
		if ( !Utility.RunProcess( vpcPath, $"/mksln sbox_game {extraDefines} /checkfiles /checkfiles_error /quiet /forceunity /2026 /sbox /{platform.PlatformID} +sbox_game /defdmacro:SBOX=1", "src" ) )
			return ExitCode.Failure;

		// Generate developer_all_win64 solution
		Log.Info( "Generating full engine solution" );
		if ( !Utility.RunProcess( vpcPath, $"/mksln developer_all_{platform.PlatformID} {extraDefines} /checkfiles /checkfiles_error /quiet /forceunity /2026 /sbox /{platform.PlatformID} +everything +native_everything +sbox_game /defdmacro:SBOX=1", "src" ) )
			return ExitCode.Failure;

		return ExitCode.Success;
	}
}

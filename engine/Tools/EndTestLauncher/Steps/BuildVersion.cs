using System.Runtime.InteropServices;

namespace Facepunch.EndTestLauncher.Steps;

/// <summary>
/// Builds the project using Bootstrap script or sboxbuild
/// </summary>
internal class BuildVersion( string repository, string workDir, bool skipBuild ) : Step( "Build Version" )
{
	private readonly string repository = repository;
	private readonly string workDir = workDir;
	private readonly bool skipBuild = skipBuild;

	protected override ExitCode RunInternal()
	{
		if ( skipBuild )
		{
			Log.Info( "Skipping build as requested" );
			return ExitCode.Success;
		}

		// Extract owner and repo name
		var parts = repository.Split( '/' );
		if ( parts.Length != 2 )
		{
			Log.Error( $"Invalid repository format: {repository}" );
			return ExitCode.Failure;
		}

		var owner = parts[0];
		var repoName = parts[1];
		var repoPath = Path.Combine( workDir, $"{owner}_{repoName}" );

		if ( !Directory.Exists( repoPath ) )
		{
			Log.Error( $"Repository path does not exist: {repoPath}" );
			return ExitCode.Failure;
		}

		Log.Info( $"Building project in: {repoPath}" );

		// Determine which build script to use based on platform
		string buildScript;
		if ( OperatingSystem.IsWindows() )
		{
			buildScript = "Bootstrap.bat";
		}
		else if ( OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() )
		{
			buildScript = "Bootstrap.sh";
		}
		else
		{
			Log.Error( "Unsupported platform" );
			return ExitCode.Failure;
		}

		var buildScriptPath = Path.Combine( repoPath, buildScript );

		if ( !File.Exists( buildScriptPath ) )
		{
			Log.Warning( $"Bootstrap script not found: {buildScriptPath}" );
			Log.Info( "Trying to use sboxbuild instead..." );
			
			// Try using sboxbuild if available
			var sboxBuildPath = Path.Combine( repoPath, "engine", "Tools", "SboxBuild" );
			if ( Directory.Exists( sboxBuildPath ) )
			{
				Log.Info( "Building with sboxbuild..." );
				if ( !Utility.RunProcess( "dotnet", "run -- build", sboxBuildPath ) )
				{
					Log.Error( "Failed to build with sboxbuild" );
					return ExitCode.Failure;
				}
			}
			else
			{
				Log.Error( "No build script found and sboxbuild not available" );
				return ExitCode.Failure;
			}
		}
		else
		{
			// Make script executable on Unix systems
			if ( OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() )
			{
				Utility.RunProcess( "chmod", $"+x \"{buildScriptPath}\"", repoPath );
			}

			// Run the bootstrap script
			if ( OperatingSystem.IsWindows() )
			{
				if ( !Utility.RunProcess( buildScriptPath, "", repoPath ) )
				{
					Log.Error( "Build failed" );
					return ExitCode.Failure;
				}
			}
			else
			{
				// On Unix, run with bash/sh
				if ( !Utility.RunProcess( "/bin/bash", $"\"{buildScriptPath}\"", repoPath ) )
				{
					Log.Error( "Build failed" );
					return ExitCode.Failure;
				}
			}
		}

		Log.Info( "Build completed successfully" );
		return ExitCode.Success;
	}
}

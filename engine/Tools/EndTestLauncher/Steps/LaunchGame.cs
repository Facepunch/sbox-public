using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Facepunch.EndTestLauncher.Steps;

/// <summary>
/// Launches the game executable
/// </summary>
internal class LaunchGame( string repository, string workDir, string mode ) : Step( "Launch Game" )
{
	private readonly string repository = repository;
	private readonly string workDir = workDir;
	private readonly string mode = mode;

	protected override ExitCode RunInternal()
	{
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
		var gamePath = Path.Combine( repoPath, "game" );

		if ( !Directory.Exists( gamePath ) )
		{
			Log.Error( $"Game directory does not exist: {gamePath}" );
			return ExitCode.Failure;
		}

		// Determine executable name based on platform and mode
		string executableName;
		if ( OperatingSystem.IsWindows() )
		{
			executableName = mode == "dev" ? "sbox-dev.exe" : "sbox.exe";
		}
		else if ( OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() )
		{
			executableName = mode == "dev" ? "sbox-dev" : "sbox";
		}
		else
		{
			Log.Error( "Unsupported platform" );
			return ExitCode.Failure;
		}

		var executablePath = Path.Combine( gamePath, executableName );

		if ( !File.Exists( executablePath ) )
		{
			Log.Error( $"Executable not found: {executablePath}" );
			Log.Info( "Available files in game directory:" );
			if ( Directory.Exists( gamePath ) )
			{
				foreach ( var file in Directory.GetFiles( gamePath ) )
				{
					Log.Info( $"  - {Path.GetFileName( file )}" );
				}
			}
			return ExitCode.Failure;
		}

		Log.Info( $"Launching game: {executablePath}" );
		Log.Info( $"Mode: {mode}" );

		// Launch the game
		try
		{
			var processStartInfo = new ProcessStartInfo
			{
				FileName = executablePath,
				WorkingDirectory = gamePath,
				UseShellExecute = false
			};

			var process = Process.Start( processStartInfo );
			if ( process == null )
			{
				Log.Error( "Failed to start game process" );
				return ExitCode.Failure;
			}

			Log.Info( $"Game launched successfully (PID: {process.Id})" );
			Log.Info( "Game is running. Close this window or press Ctrl+C to stop monitoring." );

			// Wait for the process to exit (optional - could be made configurable)
			process.WaitForExit();
			Log.Info( $"Game process exited with code: {process.ExitCode}" );

			return ExitCode.Success;
		}
		catch ( Exception ex )
		{
			Log.Error( $"Failed to launch game: {ex.Message}" );
			return ExitCode.Failure;
		}
	}
}

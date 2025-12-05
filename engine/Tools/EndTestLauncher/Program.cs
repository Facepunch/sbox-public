using System.CommandLine;
using Facepunch.EndTestLauncher.Steps;

namespace Facepunch.EndTestLauncher;

/// <summary>
/// Sandbox End Test Launcher - Facilitates fetching, building, and launching alternative versions from GitHub
/// </summary>
internal class Program
{
	static int Main( string[] args )
	{
		// Create root command
		var rootCommand = new RootCommand( "endtest-launcher - Fetch, build, and launch alternative s&box versions from GitHub" );

		AddLaunchCommand( rootCommand );

		rootCommand.Invoke( args );
		return Environment.ExitCode;
	}

	private static void AddLaunchCommand( RootCommand rootCommand )
	{
		var launchCommand = new Command( "launch", "Launch a version from GitHub repository" );

		var repoOption = new Option<string>(
			"--repo",
			description: "GitHub repository in format owner/repo (e.g., facepunch/sbox-public)" )
		{
			IsRequired = true
		};

		var branchOption = new Option<string>(
			"--branch",
			description: "Branch name to checkout" );

		var commitOption = new Option<string>(
			"--commit",
			description: "Commit SHA to checkout (alternative to --branch)" );

		var workdirOption = new Option<string>(
			"--workdir",
			description: "Working directory for clones (default: platform-specific user directory)",
			getDefaultValue: () => GetDefaultWorkDirectory() );

		var skipBuildOption = new Option<bool>(
			"--skip-build",
			description: "Skip the build step",
			getDefaultValue: () => false );

		var modeOption = new Option<string>(
			"--mode",
			description: "Launch mode: 'game' or 'dev' (default: 'game')",
			getDefaultValue: () => "game" );

		launchCommand.AddOption( repoOption );
		launchCommand.AddOption( branchOption );
		launchCommand.AddOption( commitOption );
		launchCommand.AddOption( workdirOption );
		launchCommand.AddOption( skipBuildOption );
		launchCommand.AddOption( modeOption );

		launchCommand.SetHandler( ( string repo, string branch, string commit, string workdir, bool skipBuild, string mode ) =>
		{
			// Validate that either branch or commit is provided (but not both)
			bool hasBranch = !string.IsNullOrEmpty( branch );
			bool hasCommit = !string.IsNullOrEmpty( commit );

			if ( !hasBranch && !hasCommit )
			{
				Log.Error( "Error: Either --branch or --commit must be provided" );
				Environment.ExitCode = (int)ExitCode.Failure;
				return;
			}

			if ( hasBranch && hasCommit )
			{
				Log.Error( "Error: --branch and --commit are mutually exclusive. Please provide only one." );
				Environment.ExitCode = (int)ExitCode.Failure;
				return;
			}

			// Validate mode
			if ( mode != "game" && mode != "dev" && mode != "server" )
			{
				Log.Error( $"Invalid mode: {mode}. Must be 'game', 'dev', or 'server'" );
				Environment.ExitCode = (int)ExitCode.Failure;
				return;
			}

			Log.Header( "Sandbox End Test Launcher" );
			Log.Info( $"Repository: {repo}" );
			if ( hasBranch )
			{
				Log.Info( $"Branch: {branch}" );
			}
			else
			{
				Log.Info( $"Commit: {commit}" );
			}
			Log.Info( $"Working directory: {workdir}" );
			Log.Info( $"Mode: {mode}" );
			Log.Info( "" );

			// Create and run steps
			var steps = new List<Step>
			{
				new CloneRepository( repo, workdir )
			};

			// Add checkout step based on whether branch or commit is provided
			if ( hasBranch )
			{
				steps.Add( new CheckoutBranch( repo, branch, workdir ) );
			}
			else
			{
				steps.Add( new CheckoutCommit( repo, commit, workdir ) );
			}

			// Add build and launch steps
			steps.Add( new BuildVersion( repo, workdir, skipBuild ) );
			steps.Add( new LaunchGame( repo, workdir, mode ) );

			foreach ( var step in steps )
			{
				Log.Header( step.Name );
				var result = step.Run();
				if ( result != ExitCode.Success )
				{
					Log.Error( $"Step '{step.Name}' failed. Aborting." );
					Environment.ExitCode = (int)ExitCode.Failure;
					return;
				}
			}

			Log.Header( "Completed Successfully" );
			Environment.ExitCode = (int)ExitCode.Success;
		}, repoOption, branchOption, commitOption, workdirOption, skipBuildOption, modeOption );

		rootCommand.Add( launchCommand );
	}

	/// <summary>
	/// Gets the default working directory based on the platform
	/// </summary>
	private static string GetDefaultWorkDirectory()
	{
		if ( OperatingSystem.IsWindows() )
		{
			var localAppData = Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData );
			return Path.Combine( localAppData, "sbox-endtest" );
		}
		else
		{
			// Linux and macOS
			var home = Environment.GetFolderPath( Environment.SpecialFolder.UserProfile );
			return Path.Combine( home, ".sbox-endtest" );
		}
	}
}

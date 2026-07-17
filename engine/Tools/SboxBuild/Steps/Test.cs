using System.Text;
using static Facepunch.Constants;

namespace Facepunch.Steps;

internal class Test( bool noBuild = true, string filter = null )
{
	/// <summary>
	/// Filter for pull request runs: everything except tests that talk to the live backend,
	/// so a backend hiccup can't fail an unrelated PR. The full suite still runs on release builds.
	/// </summary>
	public const string ExcludeLiveBackend = "TestCategory!=LiveBackend";

	private record TestProject( string Name, bool NeedsEngine );

	/// <summary>
	/// The test projects in execution order - fastest signal first, slowest last, and a
	/// failing tier stops the run. Sandbox.Test.Unit runs with FACEPUNCH_ENGINE removed
	/// from its environment: that tier must work without a built native engine, so a test
	/// that sneaks in a native dependency fails here rather than on someone's bare checkout.
	/// New test projects under engine/Tests/ need to be added to this list.
	/// </summary>
	private static readonly TestProject[] Projects =
	[
		new( "Sandbox.Test.Unit", NeedsEngine: false ),
		new( "Sandbox.Test.Engine", NeedsEngine: true ),
		new( "Sandbox.Test.Integration", NeedsEngine: true ),
	];

	internal ExitCode Run()
	{
		try
		{
			string rootDir = Directory.GetCurrentDirectory();
			string engineDir = Path.Combine( rootDir, "engine" );
			string gameDir = Path.Combine( rootDir, "game" );

			foreach ( var project in Projects )
			{
				if ( !RunTestProject( project, engineDir, gameDir ) )
				{
					Log.Error( $"{project.Name} tests failed!" );
					return ExitCode.Failure;
				}
			}

			Log.Info( "All tests completed successfully!" );
			return ExitCode.Success;
		}
		catch ( Exception ex )
		{
			Log.Error( $"Test operations failed with error: {ex}" );
			return ExitCode.Failure;
		}
	}

	/// <summary>
	/// Runs a single test project with the environment its tier expects, logging a
	/// summary of any failed tests. Returns whether the run passed.
	/// </summary>
	private bool RunTestProject( TestProject project, string engineDir, string gameDir )
	{
		var csproj = Path.Combine( "Tests", project.Name, $"{project.Name}.csproj" );

		// --no-build: BuildManaged already compiled all projects in Sandbox-Engine.slnx (including test projects).
		var noBuildFlag = noBuild ? " --no-build" : string.Empty;
		var filterFlag = string.IsNullOrEmpty( filter ) ? string.Empty : $" --filter \"{filter}\"";
		var testArgs = $"test {csproj} --logger \"console;verbosity=normal;consoleLoggerParameters=ErrorsOnly\" -c Release{noBuildFlag}{filterFlag}";

		Log.Info( "" );
		Log.Info( project.NeedsEngine
			? $"Running {project.Name}..."
			: $"Running {project.Name} (FACEPUNCH_ENGINE removed - this tier must not need the native engine)..." );

		// Track output for failed tests:
		List<string> failedTests = new List<string>();
		StringBuilder currentFailedTestInfo = new();
		var isCollectingFailedTestInfo = false;

		bool success = Utility.RunProcess(
			"dotnet",
			testArgs,
			engineDir,
			new Dictionary<string, string> { { "FACEPUNCH_ENGINE", project.NeedsEngine ? gameDir : null } },
			// A bit hacky but we collect failed tests to get a nicer summary in the end
			onDataReceived: ( sender, e ) =>
			{
				if ( e.Data != null )
				{
					Log.Info( e.Data );

					if ( isCollectingFailedTestInfo && e.Data.TrimStart().StartsWith( "Passed" ) )
					{
						failedTests.Add( currentFailedTestInfo.ToString().Trim( '\n' ) );
						currentFailedTestInfo = currentFailedTestInfo.Clear();
						isCollectingFailedTestInfo = false;
					}
					if ( e.Data.TrimStart().StartsWith( "Failed " ) )
					{
						isCollectingFailedTestInfo = true;
					}
					if ( isCollectingFailedTestInfo )
					{
						currentFailedTestInfo.AppendLine( e.Data );
					}
				}
			}
		);

		if ( !success )
		{
			Log.Info( "" );
			Log.Info( $"Failed Tests Summary ({project.Name}):" );
			Log.Info( "" );

			foreach ( var failedTest in failedTests )
			{
				Log.Info( failedTest );
				Log.Info( "" );
			}
		}

		return success;
	}
}

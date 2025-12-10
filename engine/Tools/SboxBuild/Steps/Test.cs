using System.Text;
using static Facepunch.Constants;

namespace Facepunch.Steps;

internal class Test( string name ) : Step( name )
{
	protected override ExitCode RunInternal()
	{
		try
		{
			string rootDir = Directory.GetCurrentDirectory();
			string engineDir = Path.Combine( rootDir, "engine" );
			string gameDir = Path.Combine( rootDir, "game" );

			var managedTestArgs = "test --logger \"console;verbosity=normal;consoleLoggerParameters=ErrorsOnly\" -c Release --property:OutputPath=bin/test";
			//if ( Utility.IsCi() )
			//{
			// Use cusotm loger for problem matching
			// TODO fix me, add GitHubActions logger to  our projects?
			// managedTestArgs += " --logger GitHubActions";
			//}

			// Track output for failed tests:
			List<string> failedTests = new List<string>();
			StringBuilder currentFailedTestInfo = new();
			var isCollectingFailedTestInfo = false;

			bool managedTestSuccess = Utility.RunProcess(
				"dotnet",
				managedTestArgs,
				engineDir,
				new Dictionary<string, string> { { "FACEPUNCH_ENGINE", gameDir } },
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
			if ( !managedTestSuccess )
			{
				Log.Info( "" );
				Log.Info( "Failed Tests Summary:" );
				Log.Info( "" );

				// Log failed tests
				foreach ( var failedTest in failedTests )
				{
					Log.Info( failedTest );
					Log.Info( "" );
				}

				Log.Error( "Managed tests failed!" );
				return ExitCode.Failure;
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
}

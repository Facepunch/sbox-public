using System.Text;
using System.Xml.Linq;
using static Facepunch.Constants;

namespace Facepunch.Steps;

internal class Test( string name, bool noBuild = true ) : Step( name )
{
	protected override ExitCode RunInternal()
	{
		try
		{
			string rootDir = Directory.GetCurrentDirectory();
			string engineDir = Path.Combine( rootDir, "engine" );
			string gameDir = Path.Combine( rootDir, "game" );
			string testResultsDir = Path.Combine( engineDir, "TestResults", DateTime.UtcNow.ToString( "yyyyMMddTHHmmss" ) );
			Directory.CreateDirectory( testResultsDir );

			// --no-build: BuildManaged already compiled all projects in Sandbox-Engine.slnx (including test projects).
			var noBuildFlag = noBuild ? " --no-build" : string.Empty;
			var managedTestArgs = $"test --logger \"console;verbosity=normal;consoleLoggerParameters=ErrorsOnly\" --logger \"trx;LogFilePrefix=managed-tests\" --results-directory \"{testResultsDir}\" -c Release{noBuildFlag}";
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
				Log.Info( $"Test results saved to: {testResultsDir}" );
				Log.Info( "" );

				var trxFailures = ReadFailedTestsFromTrx( testResultsDir );
				if ( trxFailures.Count > 0 )
				{
					Log.Info( "Failed Tests Summary (from TRX):" );
					Log.Info( "" );

					foreach ( var failedTest in trxFailures )
					{
						Log.Info( failedTest.Name );
						if ( !string.IsNullOrWhiteSpace( failedTest.Message ) )
						{
							Log.Info( failedTest.Message );
						}
						Log.Info( "" );
					}
				}
				else
				{
					Log.Info( "Failed Tests Summary:" );
					Log.Info( "" );

					foreach ( var failedTest in failedTests )
					{
						Log.Info( failedTest );
						Log.Info( "" );
					}
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

	private static List<(string Name, string Message)> ReadFailedTestsFromTrx( string testResultsDir )
	{
		var failures = new List<(string Name, string Message)>();
		if ( !Directory.Exists( testResultsDir ) )
		{
			return failures;
		}

		foreach ( var trxFile in Directory.EnumerateFiles( testResultsDir, "*.trx", SearchOption.AllDirectories ) )
		{
			try
			{
				var document = XDocument.Load( trxFile );

				foreach ( var element in document.Descendants() )
				{
					if ( element.Name.LocalName != "UnitTestResult" )
					{
						continue;
					}

					var outcome = element.Attribute( "outcome" )?.Value;
					if ( !string.Equals( outcome, "Failed", StringComparison.OrdinalIgnoreCase ) )
					{
						continue;
					}

					var testName = element.Attribute( "testName" )?.Value ?? Path.GetFileNameWithoutExtension( trxFile );
					string message = null;

					foreach ( var child in element.Descendants() )
					{
						if ( child.Name.LocalName == "Message" )
						{
							message = child.Value.Trim();
							break;
						}
					}

					failures.Add( (testName, message) );
				}
			}
			catch ( Exception ex )
			{
				Log.Info( $"Failed to parse TRX file '{trxFile}': {ex.Message}" );
			}
		}

		return failures;
	}
}

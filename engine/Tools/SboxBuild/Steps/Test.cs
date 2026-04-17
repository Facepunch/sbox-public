using System.Text;
using System.Xml.Linq;
using static Facepunch.Constants;

namespace Facepunch.Steps;

internal class Test( string name, bool noBuild = true, IEnumerable<string> projects = null ) : Step( name )
{
	private readonly IReadOnlyList<string> projects = projects?.Where( x => !string.IsNullOrWhiteSpace( x ) ).ToArray() ?? [];

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
			var targets = this.projects.Count > 0
				? this.projects.Select( x => ResolveProjectPath( rootDir, engineDir, x ) ).ToArray()
				: [null];

			foreach ( var target in targets )
			{
				var managedTestSuccess = RunManagedTests( engineDir, gameDir, testResultsDir, noBuildFlag, target );
				if ( managedTestSuccess )
				{
					continue;
				}

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
					Log.Info( "Managed tests failed, but no failed tests were parsed from TRX output." );
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

	private bool RunManagedTests( string engineDir, string gameDir, string testResultsDir, string noBuildFlag, string projectPath )
	{
		var projectArg = string.IsNullOrWhiteSpace( projectPath ) ? string.Empty : $" \"{projectPath}\"";
		var logPrefix = string.IsNullOrWhiteSpace( projectPath ) ? "managed-tests" : $"managed-tests-{SanitizeLogPrefix( Path.GetFileNameWithoutExtension( projectPath ) )}";
		var managedTestArgs = $"test{projectArg} --logger \"console;verbosity=normal;consoleLoggerParameters=ErrorsOnly\" --logger \"trx;LogFilePrefix={logPrefix}\" --results-directory \"{testResultsDir}\" -c Release{noBuildFlag}";

		if ( !string.IsNullOrWhiteSpace( projectPath ) )
		{
			Log.Info( $"Running tests for project: {projectPath}" );
		}

		List<string> failedTests = new();
		StringBuilder currentFailedTestInfo = new();
		var isCollectingFailedTestInfo = false;

		var managedTestSuccess = Utility.RunProcess(
			"dotnet",
			managedTestArgs,
			engineDir,
			new Dictionary<string, string> { { "FACEPUNCH_ENGINE", gameDir } },
			onDataReceived: ( sender, e ) =>
			{
				if ( e.Data == null )
				{
					return;
				}

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
		);

		if ( !managedTestSuccess && failedTests.Count > 0 )
		{
			Log.Info( "Failed Tests Summary:" );
			Log.Info( "" );

			foreach ( var failedTest in failedTests )
			{
				Log.Info( failedTest );
				Log.Info( "" );
			}
		}

		return managedTestSuccess;
	}

	private static string ResolveProjectPath( string rootDir, string engineDir, string projectPath )
	{
		if ( Path.IsPathRooted( projectPath ) )
		{
			return projectPath;
		}

		var rootCandidate = Path.Combine( rootDir, projectPath );
		if ( File.Exists( rootCandidate ) )
		{
			return Path.GetFullPath( rootCandidate );
		}

		var engineCandidate = Path.Combine( engineDir, projectPath );
		if ( File.Exists( engineCandidate ) )
		{
			return Path.GetFullPath( engineCandidate );
		}

		return projectPath;
	}

	private static string SanitizeLogPrefix( string name )
	{
		var invalidChars = Path.GetInvalidFileNameChars();
		var sanitized = new string( name.Select( ch => invalidChars.Contains( ch ) ? '_' : ch ).ToArray() );
		return string.IsNullOrWhiteSpace( sanitized ) ? "managed-tests" : sanitized;
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

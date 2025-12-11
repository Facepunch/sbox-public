using static Facepunch.Constants;

namespace Facepunch.Steps;

internal class SentryRelease( string name, string org, string project ) : Step( name )
{
	public string Organization { get; } = org;
	public string Project { get; } = project;

	protected override ExitCode RunInternal()
	{
		try
		{
			string version = Utility.VersionName();
			Log.Info( $"Creating Sentry release for version {version}..." );

			var projectStr = $"--project {Project}";
			bool success;

			// Create new release
			Log.Info( "Creating new release" );
			success = RunSentryCommand( $"releases new \"{version}\" --org \"{Organization}\" {projectStr}" );
			if ( !success ) return ExitCode.Failure;

			// Set commits
			Log.Info( "Setting commits" );
			success = RunSentryCommand( $"releases set-commits \"{version}\" --auto --org \"{Organization}\" {projectStr}" );
			if ( !success ) return ExitCode.Failure;

			// Create deploy
			Log.Info( "Creating deploy" );
			success = RunSentryCommand( $"releases deploys \"{version}\" new -e retail --org \"{Organization}\" {projectStr}" );
			if ( !success ) return ExitCode.Failure;

			// Finalize release
			Log.Info( "Finalizing release" );
			success = RunSentryCommand( $"releases finalize \"{version}\" --org \"{Organization}\" {projectStr}" );
			if ( !success ) return ExitCode.Failure;

			Log.Info( "Successfully created and finalized Sentry release" );
			return ExitCode.Success;
		}
		catch ( Exception ex )
		{
			Log.Error( $"Sentry release creation failed with error: {ex}" );
			return ExitCode.Failure;
		}
	}

	/// <summary>
	/// Runs a Sentry CLI command with the appropriate auth token from environment variables
	/// </summary>
	public static bool RunSentryCommand( string arguments, string workingDirectory = null )
	{
		var token = Environment.GetEnvironmentVariable( "SENTRY_AUTH_TOKEN" );

		if ( string.IsNullOrEmpty( token ) )
		{
			Log.Warning( "SENTRY_AUTH_TOKEN was empty" );
			return false;
		}

		// Create a custom environment variable dictionary for the auth token
		var envVars = new Dictionary<string, string>
		{
			{ "SENTRY_AUTH_TOKEN", token }
		};

		return Utility.RunProcess(
			"sentry-cli.exe",
			arguments,
			workingDirectory,
			envVars,
			1200000
		);
	}
}

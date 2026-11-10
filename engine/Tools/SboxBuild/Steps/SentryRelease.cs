using static Facepunch.Constants;

namespace Facepunch.Steps;

internal class SentryRelease( string org, string project )
{
	public string Organization { get; } = org;
	public string Project { get; } = project;

	internal ExitCode Run()
	{
		try
		{
			string version = Utility.VersionName();
			Log.Info( $"Creating Sentry release for version {version}..." );

			var projectStr = $"--project {Project}";
			bool success;

			// Create new release
			Log.Info( "Creating new release" );
			success = RunSentryCommandWithRetries( $"releases new \"{version}\" --org \"{Organization}\" {projectStr}" );
			if ( !success ) return ExitCode.Failure;

			// Set commits
			Log.Info( "Setting commits" );
			success = RunSentryCommandWithRetries( $"releases set-commits \"{version}\" --auto --org \"{Organization}\" {projectStr}" );
			if ( !success ) return ExitCode.Failure;

			// Create deploy
			Log.Info( "Creating deploy" );
			success = RunSentryCommandWithRetries( $"releases deploys \"{version}\" new -e retail --org \"{Organization}\" {projectStr}" );
			if ( !success ) return ExitCode.Failure;

			// Finalize release
			Log.Info( "Finalizing release" );
			success = RunSentryCommandWithRetries( $"releases finalize \"{version}\" --org \"{Organization}\" {projectStr}" );
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
	/// Sentry's API returns the odd 503 ("Downstream service unavailable"), so give each
	/// command a few goes before failing the step. Repeating one is harmless, at worst a
	/// deploy gets recorded twice.
	/// </summary>
	static bool RunSentryCommandWithRetries( string arguments )
	{
		const int attempts = 4;

		for ( var attempt = 1; attempt <= attempts; attempt++ )
		{
			if ( RunSentryCommand( arguments ) )
				return true;

			if ( attempt == attempts )
				break;

			var backoff = TimeSpan.FromSeconds( 15 * attempt );
			Log.Warning( $"Sentry command failed (attempt {attempt}/{attempts}) - retrying in {backoff.TotalSeconds:0}s" );
			Thread.Sleep( backoff );
		}

		return false;
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

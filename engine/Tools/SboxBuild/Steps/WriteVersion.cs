using static Facepunch.Constants;

namespace Facepunch.Steps;

/// <summary>
/// Step to write version information to a file
/// </summary>
internal class WriteVersion
{
	internal ExitCode Run()
	{
		try
		{
			Log.Info( "Writing version information..." );

			string target = "game/.version";
			string contents = $"{Utility.VersionName()}\n" +
							  $"{Environment.GetEnvironmentVariable( "GITHUB_RUN_ID" )}\n" +
							  $"{Environment.GetEnvironmentVariable( "GITHUB_JOB" )}\n" +
							  $"{Environment.GetEnvironmentVariable( "GITHUB_ACTOR" )}\n" +
							  $"{DateTime.UtcNow}\n";

			File.WriteAllText( target, contents );

			Log.Info( $"Version information written to {target}" );
			return ExitCode.Success;
		}
		catch ( Exception ex )
		{
			Log.Error( $"Failed to write version information: {ex}" );
			return ExitCode.Failure;
		}
	}
}

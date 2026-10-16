using System.Globalization;
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
			string version = Utility.VersionName();
			string contents = $"{version}\n" +
							  $"{Environment.GetEnvironmentVariable( "GITHUB_RUN_ID" )}\n" +
							  $"{Environment.GetEnvironmentVariable( "GITHUB_JOB" )}\n" +
							  $"{Environment.GetEnvironmentVariable( "GITHUB_ACTOR" )}\n" +
							  $"{DateTime.UtcNow.ToString( "dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture )}\n";

			File.WriteAllText( target, contents );

			Log.Info( $"Version information written to {target}" );
			GitHubActions.Note( $"version {version}" );
			return ExitCode.Success;
		}
		catch ( Exception ex )
		{
			Log.Error( $"Failed to write version information: {ex}" );
			return ExitCode.Failure;
		}
	}
}

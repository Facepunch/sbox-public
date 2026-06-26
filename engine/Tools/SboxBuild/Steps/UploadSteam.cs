using System.Text.RegularExpressions;
using static Facepunch.Constants;

namespace Facepunch.Steps;

internal class UploadSteam( string branch )
{
	public string Branch { get; } = branch;

	internal ExitCode Run()
	{
		try
		{
			Log.Info( $"Uploading build to Steam branch: {Branch}" );

			if ( Branch != "staging" && Branch != "release" )
			{
				Log.Error( $"Invalid branch specified: {Branch}. Must be 'staging' or 'release'" );
				return ExitCode.Failure;
			}

			string rootDir = Directory.GetCurrentDirectory();
			string steamworksDir = Path.Combine( rootDir, "steamworks" );
			string steamCmd = @"c:\steam01\steamcmd.bat";

			if ( !Directory.Exists( steamworksDir ) )
			{
				Log.Error( $"Steamworks directory not found at {steamworksDir}" );
				return ExitCode.Failure;
			}

			if ( !File.Exists( steamCmd ) )
			{
				Log.Error( $"SteamCMD not found at {steamCmd}" );
				return ExitCode.Failure;
			}

			// Determine which VDF files to use based on branch
			string clientVdf = Branch == "staging"
				? "app.game.staging.vdf"
				: "app.game.release.vdf";

			string serverVdf = Branch == "staging"
				? "app.server.staging.vdf"
				: "app.server.release.vdf";

			// Upload client build
			Log.Info( $"Uploading client build using {clientVdf}..." );
			if ( !RunAppBuild( steamCmd, steamworksDir, clientVdf, out var clientBuildId ) )
			{
				Log.Error( "Client upload to Steam failed!" );
				return ExitCode.Failure;
			}

			// Upload server build
			Log.Info( $"Uploading server build using {serverVdf}..." );
			if ( !RunAppBuild( steamCmd, steamworksDir, serverVdf, out var serverBuildId ) )
			{
				Log.Error( "Server upload to Steam failed!" );
				return ExitCode.Failure;
			}

			// Hand the build ids to the later report-build step so it can record what this commit was pushed as.
			if ( clientBuildId is not null )
				Utility.SetGitHubEnv( "STEAM_CLIENT_BUILD_ID", clientBuildId );
			if ( serverBuildId is not null )
				Utility.SetGitHubEnv( "STEAM_SERVER_BUILD_ID", serverBuildId );

			Log.Info( $"Successfully uploaded client ({clientBuildId ?? "?"}) and server ({serverBuildId ?? "?"}) builds to Steam branch: {Branch}" );
			return ExitCode.Success;
		}
		catch ( Exception ex )
		{
			Log.Error( $"Steam upload failed with error: {ex}" );
			return ExitCode.Failure;
		}
	}

	// SteamPipe prints the assigned build number as "BuildID <number>" on success; capture the last one we see.
	private static readonly Regex BuildIdRegex = new( @"BuildID\s+(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled );

	/// <summary>
	/// Runs <c>steamcmd +run_app_build</c> for the given app VDF, mirroring its output to the log while scraping
	/// the SteamPipe build id from it. <paramref name="buildId"/> is the parsed id, or null if none was found.
	/// </summary>
	private static bool RunAppBuild( string steamCmd, string steamworksDir, string vdf, out string buildId )
	{
		string captured = null;

		bool success = Utility.RunProcess(
			steamCmd,
			$"+run_app_build \"{Path.Combine( steamworksDir, vdf )}\" +quit",
			steamworksDir,
			timeoutMs: 3600000, // 1 hour timeout
			onDataReceived: ( _, e ) =>
			{
				if ( e.Data is null )
					return;

				Log.Info( e.Data ); // preserve the default line-by-line logging RunProcess would otherwise do

				var m = BuildIdRegex.Match( e.Data );
				if ( m.Success )
					captured = m.Groups[1].Value;
			}
		);

		buildId = captured;

		if ( success && captured is null )
			Log.Warning( $"Steam upload using {vdf} succeeded but no BuildID was found in the output." );

		return success;
	}
}

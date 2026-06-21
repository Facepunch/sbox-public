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
			bool clientSuccess = Utility.RunProcess(
				steamCmd,
				$"+run_app_build \"{Path.Combine( steamworksDir, clientVdf )}\" +quit",
				steamworksDir,
				timeoutMs: 3600000 // 1 hour timeout
			);

			if ( !clientSuccess )
			{
				Log.Error( "Client upload to Steam failed!" );
				return ExitCode.Failure;
			}

			// Upload server build
			Log.Info( $"Uploading server build using {serverVdf}..." );
			bool serverSuccess = Utility.RunProcess(
				steamCmd,
				$"+run_app_build \"{Path.Combine( steamworksDir, serverVdf )}\" +quit",
				steamworksDir,
				timeoutMs: 3600000 // 1 hour timeout
			);

			if ( !serverSuccess )
			{
				Log.Error( "Server upload to Steam failed!" );
				return ExitCode.Failure;
			}

			Log.Info( $"Successfully uploaded client and server builds to Steam branch: {Branch}" );
			return ExitCode.Success;
		}
		catch ( Exception ex )
		{
			Log.Error( $"Steam upload failed with error: {ex}" );
			return ExitCode.Failure;
		}
	}
}

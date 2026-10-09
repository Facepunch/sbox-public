using static Facepunch.Constants;

namespace Facepunch.Steps;

internal class UploadSymbols
{
	internal ExitCode Run()
	{
		try
		{
			Log.Info( "Uploading debug symbols..." );

			string rootDir = Directory.GetCurrentDirectory();
			string gameDir = Path.Combine( rootDir, "game" );
			string steamworksDir = Path.Combine( rootDir, "steamworks" );
			string symbolStoreExe = Path.Combine( steamworksDir, "Facepunch.SymStore.exe" );

			if ( !File.Exists( symbolStoreExe ) )
			{
				Log.Error( $"Symbol store executable not found at {symbolStoreExe}" );
				return ExitCode.Failure;
			}

			// Only upload what we ship: all DLLs, PDBs, and EXEs under the game folder
			bool success = Utility.RunProcess(
				symbolStoreExe,
				"*.dll *.pdb *.exe",
				gameDir,
				timeoutMs: 1800000 // 30 minute timeout
			);

			if ( !success )
			{
				Log.Error( "Symbol upload failed!" );
				return ExitCode.Failure;
			}

			Log.Info( "Symbol upload completed successfully!" );
			return ExitCode.Success;
		}
		catch ( Exception ex )
		{
			Log.Error( $"Symbol upload failed with error: {ex}" );
			return ExitCode.Failure;
		}
	}
}

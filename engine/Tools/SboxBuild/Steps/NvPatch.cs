using static Facepunch.Constants;

namespace Facepunch.Steps;

internal class NvPatch
{
	internal ExitCode Run()
	{
		string rootDir = Directory.GetCurrentDirectory();
		string nvpatchPath = Path.Combine( rootDir, "engine", "ThirdParty", "nvpatch", "nvpatch.exe" );

		try
		{
			if ( !File.Exists( nvpatchPath ) )
			{
				Log.Error( $"Error: NVPatch executable not found at {nvpatchPath}" );
				return ExitCode.Failure;
			}

			Log.Info( "Step 1: Running nvpatch on sbox.exe" );
			string sboxPath = Path.Combine( rootDir, "game", "sbox.exe" );
			if ( !RunNvPatch( nvpatchPath, sboxPath ) )
				return ExitCode.Failure;

			Log.Info( "Step 2: Running nvpatch on sbox-dev.exe" );
			string sboxDevPath = Path.Combine( rootDir, "game", "sbox-dev.exe" );
			if ( !RunNvPatch( nvpatchPath, sboxDevPath ) )
				return ExitCode.Failure;

			Console.WriteLine( "NVPatch operations completed successfully!" );
			return ExitCode.Success;
		}
		catch ( Exception ex )
		{
			Log.Error( $"NVPatch operations failed with error: {ex}" );
			return ExitCode.Failure;
		}
	}

	static bool RunNvPatch( string nvpatchPath, string targetExePath )
	{
		if ( !File.Exists( targetExePath ) )
		{
			Log.Warning( $"Warning: Target executable not found at {targetExePath}" );
			Log.Warning( "Skipping this nvpatch operation." );
			return true;
		}

		return Utility.RunProcess( nvpatchPath, $"--enable \"{targetExePath}\"", null );
	}
}

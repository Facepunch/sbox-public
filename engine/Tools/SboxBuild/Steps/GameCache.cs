using static Facepunch.Constants;

namespace Facepunch.Steps;

internal class GameCache
{
	internal ExitCode Run()
	{
		string rootDir = Directory.GetCurrentDirectory();
		string exePath = Path.Combine( rootDir, "engine", "Tools", "CreateGameCache", "bin", "CreateGameCache.exe" );
		string gameDir = Path.Combine( rootDir, "game" );

		try
		{
			if ( !Utility.RunProcess( exePath, $"\"{gameDir}\"", null ) )
				return ExitCode.Failure;

			Console.WriteLine( "GameCache operations completed successfully!" );
			return ExitCode.Success;
		}
		catch ( Exception ex )
		{
			Log.Error( $"GameCache operations failed with error: {ex}" );
			return ExitCode.Failure;
		}
	}
}

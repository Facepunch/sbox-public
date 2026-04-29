using static Facepunch.Constants;

namespace Facepunch.Steps;

internal class GameCache() : Step( "GameCache" )
{
	protected override ExitCode RunInternal()
	{
		string rootDir = Directory.GetCurrentDirectory();
		string exePath = Path.Combine( rootDir, "engine", "Tools", "CreateGameCache", "bin", "CreateGameCache.exe" );

		try
		{
			Utility.RunProcess( exePath, "--quiet", null );
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

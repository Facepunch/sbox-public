using static Facepunch.Constants;

namespace Facepunch.Steps;

internal class BuildContent( string name ) : Step( name )
{
	protected override ExitCode RunInternal()
	{
		try
		{
			string rootDir = Directory.GetCurrentDirectory();
			string gameDir = Path.Combine( rootDir, "game" );
			string contentBuilderPath = Path.Combine( gameDir, "bin", "win64", "contentbuilder.exe" );

			// Verify content builder exists
			if ( !File.Exists( contentBuilderPath ) )
			{
				if ( OperatingSystem.IsLinux() )
				{
					Log.Warning( $"Warning: Content builder executable not found at {contentBuilderPath}. Skipping content build on Linux." );
					return ExitCode.Success;
				}

				Log.Error( $"Error: Content builder executable not found at {contentBuilderPath}" );
				return ExitCode.Failure;
			}

			bool success = Utility.RunProcess( contentBuilderPath, "-b", gameDir );

			if ( !success )
				return ExitCode.Failure;

			Log.Info( "Content building completed successfully!" );
			return ExitCode.Success;
		}
		catch ( Exception ex )
		{
			Log.Error( $"Content building failed with error: {ex}" );
			return ExitCode.Failure;
		}
	}
}

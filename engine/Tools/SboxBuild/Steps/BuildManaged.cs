using static Facepunch.Constants;

namespace Facepunch.Steps;

internal class BuildManaged( bool clean = false )
{
	internal ExitCode Run()
	{
		string engineDir = Path.Combine( Directory.GetCurrentDirectory(), "engine" );
		string rootDir = Directory.GetCurrentDirectory();

		try
		{
			Log.Info( "Step 1: Dotnet Clean" );
			if ( clean )
			{
				if ( !Utility.RunDotnetCommand( engineDir, "clean" ) )
					return ExitCode.Failure;
			}
			else
			{
				Log.Info( "Skipping dotnet clean as cleanBuild is false." );
			}

			Log.Info( "Step 2: Dotnet Restore" );
			if ( !Utility.RunDotnetCommand( engineDir, "restore" ) )
				return ExitCode.Failure;

			Log.Info( "Step 3: Build CodeGen.exe" );
			if ( !Utility.RunDotnetCommand( engineDir, "build Tools/CodeGen/ -o Tools/CodeGen/bin" ) )
				return ExitCode.Failure;

			Log.Info( "Step 3a: Build CreateGameCache.exe" );
			if ( !Utility.RunDotnetCommand( engineDir, "build Tools/CreateGameCache/ -o Tools/CreateGameCache/bin" ) )
				return ExitCode.Failure;

			Log.Info( "Step 4: Clear managed folder" );
			string managedDir = Path.Combine( rootDir, "game", "bin", "managed" );
			if ( Directory.Exists( managedDir ) )
			{
				try
				{
					Directory.Delete( managedDir, true );
					Directory.CreateDirectory( managedDir ); // Recreate the empty directory
					Log.Info( $"Successfully cleared directory: {managedDir}" );
				}
				catch ( Exception ex )
				{
					Log.Warning( $"Warning: Failed to clear directory: {managedDir}. Error: {ex.Message}" );
					// Continue execution since this is a warning in the original script
				}
			}
			else
			{
				Log.Info( $"Directory does not exist, creating: {managedDir}" );
				Directory.CreateDirectory( managedDir );
			}

			Log.Info( "Step 5: Build Managed" );
			if ( !Utility.RunDotnetCommand( engineDir, "build -c Release Sandbox-Engine.slnx -p:TreatWarningsAsErrors=true" ) )
				return ExitCode.Failure;

			Log.Info( "Build completed successfully!" );
			return ExitCode.Success;
		}
		catch ( Exception ex )
		{
			Log.Error( $"Build failed with error: {ex}" );
			return ExitCode.Failure;
		}
	}
}

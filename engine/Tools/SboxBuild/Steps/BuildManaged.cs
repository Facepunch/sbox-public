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
			RecreateDirectory( Path.Combine( engineDir, "Tools", "CodeGen", "bin" ) );
			if ( !Utility.RunDotnetCommand( engineDir, "build Tools/CodeGen/ -o Tools/CodeGen/bin" ) )
				return ExitCode.Failure;

			Log.Info( "Step 3a: Build CreateGameCache.exe" );
			RecreateDirectory( Path.Combine( engineDir, "Tools", "CreateGameCache", "bin" ) );
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

			var launcherRid = OperatingSystem.IsWindows() ? "win-x64"
				: OperatingSystem.IsLinux() ? "linux-x64"
				: null;
			if ( launcherRid is not null )
			{
				Log.Info( $"Step 6: Publish {launcherRid} framework-dependent single-file launchers" );
				var publishRoot = Path.Combine( Path.GetTempPath(), $"sbox-launchers-{Guid.NewGuid():N}" );
				Directory.CreateDirectory( publishRoot );

				foreach ( var project in new[]
				{
					"Sbox/Sbox.csproj",
					"SboxDev/Sbox-Dev.csproj",
					"StandaloneTest/Sbox-Launcher.csproj",
					"SboxStandalone/Sbox-Standalone.csproj",
					"SboxServer/Sbox-Server.csproj",
					"SboxBench/SboxBench.csproj"
				} )
				{
					var output = Path.Combine( publishRoot, Path.GetFileNameWithoutExtension( project ) );
					var launcherDir = Path.Combine( engineDir, "Launcher" );
					if ( !Utility.RunDotnetCommand( launcherDir,
						$"restore {project} -r {launcherRid} -p:Configuration=Release -p:SelfContained=false -p:RestoreRecursive=false" ) )
						return ExitCode.Failure;

					if ( !Utility.RunDotnetCommand( launcherDir,
						$"publish {project} -c Release -r {launcherRid} -p:SelfContained=false -p:PublishSingleFile=true -p:EnableSingleFileAnalyzer=false -p:BuildProjectReferences=false --no-restore -o \"{output}\"" ) )
						return ExitCode.Failure;

					var name = project switch
					{
						"Sbox/Sbox.csproj" => "sbox",
						"SboxDev/Sbox-Dev.csproj" => "sbox-dev",
						"StandaloneTest/Sbox-Launcher.csproj" => "sbox-launcher",
						"SboxStandalone/Sbox-Standalone.csproj" => "sbox-standalone",
						"SboxServer/Sbox-Server.csproj" => "sbox-server",
						_ => "benchmark"
					};
					var extension = OperatingSystem.IsWindows() ? ".exe" : "";
					File.Copy( Path.Combine( output, name + extension ), Path.Combine( rootDir, "game", name + extension ), true );
				}

				Directory.Delete( publishRoot, true );

				// delete any old .runtimeconfig.json that are hanging around
				foreach ( var name in new[] { "sbox", "sbox-dev", "sbox-launcher", "sbox-standalone", "sbox-server", "benchmark" } )
				{
					foreach ( var extension in new[] { ".dll", ".runtimeconfig.json" } )
					{
						var looseFile = Path.Combine( rootDir, "game", name + extension );
						if ( File.Exists( looseFile ) ) File.Delete( looseFile );
					}
				}
			}

			Log.Info( "Build completed successfully!" );
			return ExitCode.Success;
		}
		catch ( Exception ex )
		{
			Log.Error( $"Build failed with error: {ex}" );
			return ExitCode.Failure;
		}
	}

	private static void RecreateDirectory( string path )
	{
		if ( Directory.Exists( path ) ) Directory.Delete( path, true );
		Directory.CreateDirectory( path );
	}
}

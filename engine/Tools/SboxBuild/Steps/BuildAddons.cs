using System;
using System.IO;
using static Facepunch.Constants;

namespace Facepunch.Steps;

internal class BuildAddons( string name ) : Step( name )
{
	protected override ExitCode RunInternal()
	{
		try
		{
			string rootDir = Directory.GetCurrentDirectory();
			string gameDir = Path.Combine( rootDir, "game" );

			Log.Info( "Step 1: Generate solution" );

			string sboxDevPath = Path.Combine( gameDir, "sbox-dev.exe" );
			if ( !File.Exists( sboxDevPath ) )
			{
				Log.Error( $"Error: sbox-dev.exe not found at {sboxDevPath}" );
				return ExitCode.Failure;
			}

			bool gameTestSuccess = Utility.RunProcess(
				sboxDevPath,
				"-generatesolution",
				gameDir
			);

			if ( !gameTestSuccess )
			{
				Log.Error( "Solution generation failed!" );
				return ExitCode.Failure;
			}

			Log.Info( "Step 2: Building Addons" );

			bool addonsSuccess = Utility.RunDotnetCommand(
				gameDir,
				"build \"s&box.slnx\""
			);

			if ( !addonsSuccess )
			{
				Log.Error( "Addons build failed!" );
				return ExitCode.Failure;
			}

			Log.Info( "Step 3: Building Menu" );

			string menuBuildPath = Path.Combine( gameDir, "bin", "managed", "MenuBuild.exe" );
			if ( !File.Exists( menuBuildPath ) )
			{
				Log.Error( $"Error: MenuBuild.exe not found at {menuBuildPath}" );
				return ExitCode.Failure;
			}

			bool menuSuccess = Utility.RunProcess(
				menuBuildPath,
				"",
				gameDir
			);

			if ( !menuSuccess )
			{
				Log.Error( "Menu build failed!" );
				return ExitCode.Failure;
			}

			Log.Info( "Addons and Menu built successfully!" );
			return ExitCode.Success;
		}
		catch ( Exception ex )
		{
			Log.Error( $"Build operations failed with error: {ex}" );
			return ExitCode.Failure;
		}
	}
}

using System.Text.RegularExpressions;
using static Facepunch.Constants;

namespace Facepunch.Steps;

internal class BuildContent
{
	internal ExitCode Run() => BuildDisplay.Run( "Build content", () =>
	{
		try
		{
			string rootDir = Directory.GetCurrentDirectory();
			string gameDir = Path.Combine( rootDir, "game" );
			// contentbuilder is native, so it sits under the platform directory it was built for.
			string contentBuilderPath = Path.Combine( gameDir, "bin", NativePlatform.Current.DirectoryName,
				OperatingSystem.IsWindows() ? "contentbuilder.exe" : "contentbuilder" );

			// Verify content builder exists
			if ( !File.Exists( contentBuilderPath ) )
			{
				Log.Error( $"Error: Content builder executable not found at {contentBuilderPath}" );
				return ExitCode.Failure;
			}

			if ( !OperatingSystem.IsWindows() )
			{
				// Restore execute permission for downloaded binaries, including cached artifacts.
				var mode = File.GetUnixFileMode( contentBuilderPath );
				if ( (mode & UnixFileMode.UserExecute) == 0 )
					File.SetUnixFileMode( contentBuilderPath, mode | UnixFileMode.UserExecute );
			}

			BuildDisplay.Status( "Compile game content" );
			bool success = Utility.RunProcess( contentBuilderPath, "-b", gameDir,
				onDataReceived: ( _, e ) =>
				{
					if ( e.Data is null ) return;
					if ( !BuildDisplay.IsActive ) Log.Info( e.Data );

					var match = Regex.Match( e.Data, @"(Compiling assets|Merging compile results):\s*(\d+)/(\d+)" );
					if ( match.Success && int.TryParse( match.Groups[2].Value, out var completed )
						&& int.TryParse( match.Groups[3].Value, out var total ) )
					{
						BuildDisplay.Status( match.Groups[1].Value );
						BuildDisplay.Progress( completed, total );
					}
				} );

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
	} );
}

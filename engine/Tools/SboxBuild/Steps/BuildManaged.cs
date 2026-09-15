using static Facepunch.Constants;

namespace Facepunch.Steps;

internal class BuildManaged( bool clean = false )
{
	internal ExitCode Run() => BuildDisplay.Run( "Build managed code", () =>
	{
		string engineDir = Path.Combine( Directory.GetCurrentDirectory(), "engine" );
		string rootDir = Directory.GetCurrentDirectory();

		try
		{
			Log.Info( "Step 1: Dotnet Clean" );
			if ( clean )
			{
				BuildDisplay.Status( "Clean managed projects" );
				if ( !Utility.RunDotnetCommand( engineDir, "clean" ) )
					return ExitCode.Failure;
			}
			else
			{
				Log.Info( "Skipping dotnet clean as cleanBuild is false." );
			}

			Log.Info( "Step 2: Dotnet Restore" );
			BuildDisplay.Status( "Restore managed dependencies" );
			if ( !Utility.RunDotnetCommand( engineDir, "restore" ) )
				return ExitCode.Failure;

			Log.Info( "Step 3: Build CodeGen.exe" );
			BuildDisplay.Status( "Build CodeGen" );
			RecreateDirectory( Path.Combine( engineDir, "Tools", "CodeGen", "bin" ) );
			if ( !Utility.RunDotnetCommand( engineDir, "build Tools/CodeGen/ -o Tools/CodeGen/bin" ) )
				return ExitCode.Failure;

			Log.Info( "Step 3a: Build CreateGameCache.exe" );
			BuildDisplay.Status( "Build CreateGameCache" );
			RecreateDirectory( Path.Combine( engineDir, "Tools", "CreateGameCache", "bin" ) );
			if ( !Utility.RunDotnetCommand( engineDir, "build Tools/CreateGameCache/ -o Tools/CreateGameCache/bin" ) )
				return ExitCode.Failure;

			Log.Info( "Step 4: Clear managed folder" );
			BuildDisplay.Status( "Clear managed output" );
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
			BuildDisplay.Status( "Build managed engine" );
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
				var completedLaunchers = 0;
				BuildDisplay.Status( $"Publish {launcherRid} launchers" );
				BuildDisplay.Progress( completedLaunchers, 6 );

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
					BuildDisplay.Detail( $"Restore {project}" );

					// Linux has no .NET redistributable the way game/_redist covers Windows, so the
					// runtime ships in game/bin/dotnet and the apphost is told to look there
					// relative to itself. Without this it falls through to /usr/share/dotnet and
					// runs against whatever the machine happens to have, or nothing at all. It
					// survives PublishSingleFile, though it has to be on the restore too: publish
					// runs --no-restore, and if only one of the pair sets it the single file
					// bundler is handed no apphost and fails with "Must specify the host binary".
					var relativeDotnet = OperatingSystem.IsLinux() ? " -p:AppHostRelativeDotNet=bin/dotnet" : "";

					if ( !Utility.RunDotnetCommand( launcherDir,
						$"restore {project} -r {launcherRid} -p:Configuration=Release -p:SelfContained=false -p:RestoreRecursive=false{relativeDotnet}" ) )
						return ExitCode.Failure;

					BuildDisplay.Detail( $"Publish {project}" );
					if ( !Utility.RunDotnetCommand( launcherDir,
						$"publish {project} -c Release -r {launcherRid} -p:SelfContained=false -p:PublishSingleFile=true -p:EnableSingleFileAnalyzer=false -p:BuildProjectReferences=false{relativeDotnet} --no-restore -o \"{output}\"" ) )
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
					BuildDisplay.Progress( ++completedLaunchers, 6 );
				}

				Directory.Delete( publishRoot, true );

				if ( OperatingSystem.IsLinux() && !StageLinuxRuntime( rootDir ) )
					return ExitCode.Failure;

				// delete any old .runtimeconfig.json that are hanging around
				BuildDisplay.Status( "Remove old launcher files" );
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
	} );

	/// <summary>
	/// Lays the shared framework the Linux launchers resolve against into game/bin/dotnet.
	///
	/// Taken from the SDK running this build rather than downloaded, so what ships is the runtime
	/// the assemblies were compiled against, and the build needs no network for it. Only
	/// Microsoft.NETCore.App: every launcher's runtimeconfig.json asks for that alone, and
	/// Microsoft.AspNetCore.App would add 27MB nothing loads.
	/// </summary>
	private static bool StageLinuxRuntime( string rootDir )
	{
		// DOTNET_ROOT when something set it, otherwise walk up from the dotnet on PATH, which is
		// where dotnet-install.sh puts it and how CI reaches it.
		var dotnetRoot = Environment.GetEnvironmentVariable( "DOTNET_ROOT" );
		if ( string.IsNullOrEmpty( dotnetRoot ) )
			dotnetRoot = Path.GetDirectoryName( FindDotnetOnPath() );

		if ( string.IsNullOrEmpty( dotnetRoot ) || !Directory.Exists( Path.Combine( dotnetRoot, "shared" ) ) )
		{
			Log.Error( "Could not locate a .NET root to take the shared framework from. Set DOTNET_ROOT." );
			return false;
		}

		var framework = Path.Combine( dotnetRoot, "shared", "Microsoft.NETCore.App" );
		var source = Directory.Exists( framework )
			? Directory.GetDirectories( framework )
				// TryParse, not the constructor: prerelease folder names (10.0.0-rc.1.25451.107) are not a
				// Version and would throw out of the whole enumeration rather than fall through to the
				// error below.
				.Select( d => Version.TryParse( Path.GetFileName( d ), out var v ) ? new { Dir = d, Version = v } : null )
				.Where( x => x is not null && x.Version.Major == 10 )
				.OrderBy( x => x.Version )
				.LastOrDefault()
				?.Dir
			: null;

		if ( source is null )
		{
			Log.Error( $"No Microsoft.NETCore.App found under {framework}." );
			return false;
		}

		var version = Path.GetFileName( source );
		var target = Path.Combine( rootDir, "game", "bin", "dotnet" );
		var icuSource = Path.Combine( AppContext.BaseDirectory, "icu", "linux-x64" );
		// ICU is a separate native dependency, not part of Microsoft.NETCore.App. The SDK
		// container may have it even when the player's Steam runtime does not.
		foreach ( var library in new[] { "libicudata", "libicuuc", "libicui18n" } )
		{
			if ( !Directory.Exists( icuSource ) || Directory.GetFiles( icuSource, $"{library}.so.*" ).Length == 0 )
			{
				Log.Error( $"Missing bundled ICU library {library} in {icuSource}. Restore and rebuild SboxBuild." );
				return false;
			}
		}
		Log.Info( $"Step 7: Stage .NET {version} runtime into game/bin/dotnet" );
		BuildDisplay.Status( $"Stage .NET {version} runtime" );

		RecreateDirectory( target );
		CopyDirectory( source, Path.Combine( target, "shared", "Microsoft.NETCore.App", version ) );
		// App-local ICU is loaded through CoreLib's native search directories, including the
		// shared framework. This also survives our launcher publish filter (no app deps.json).
		CopyDirectory( icuSource, Path.Combine( target, "shared", "Microsoft.NETCore.App", version ) );
		// libhostfxr.so, which the apphost loads first and which then finds the framework above.
		CopyDirectory( Path.Combine( dotnetRoot, "host" ), Path.Combine( target, "host" ) );
		return true;
	}

	/// <summary>The dotnet on PATH, with symlinks followed: distributions commonly put a link in
	/// /usr/bin pointing at the real root, and it is the root we want.</summary>
	private static string FindDotnetOnPath()
	{
		foreach ( var dir in (Environment.GetEnvironmentVariable( "PATH" ) ?? "")
			.Split( Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries ) )
		{
			var candidate = Path.Combine( dir, "dotnet" );
			if ( !File.Exists( candidate ) ) continue;

			return File.ResolveLinkTarget( candidate, returnFinalTarget: true )?.FullName ?? candidate;
		}

		return null;
	}

	private static void CopyDirectory( string source, string destination )
	{
		Directory.CreateDirectory( destination );

		foreach ( var file in Directory.GetFiles( source ) )
			File.Copy( file, Path.Combine( destination, Path.GetFileName( file ) ), true );

		foreach ( var directory in Directory.GetDirectories( source ) )
			CopyDirectory( directory, Path.Combine( destination, Path.GetFileName( directory ) ) );
	}

	private static void RecreateDirectory( string path )
	{
		if ( Directory.Exists( path ) ) Directory.Delete( path, true );
		Directory.CreateDirectory( path );
	}
}

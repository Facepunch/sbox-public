using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Editor;

partial class StandaloneExporter
{
	/// <summary>
	/// The launcher, as built by engine/Launcher/SboxStandalone into bin/managed: an apphost plus a
	/// <c>.dll</c> and <c>.runtimeconfig.json</c> of the same name beside it.
	/// </summary>
	private const string LauncherName = "sbox-standalone";

	/// <summary>
	/// Where, relative to the exported executable, the launcher assembly ends up.
	/// </summary>
	private const string LauncherAssemblyPath = "bin\\managed\\" + LauncherName + ".dll";

	/// <summary>
	/// Size of the assembly path buffer in an apphost (EMBED_MAX in the runtime's corehost).
	/// </summary>
	private const int AppHostPathCapacity = 1024;

	/// <summary>
	/// The game's executable is the standalone launcher's apphost: copied, pointed at the launcher
	/// assembly in bin/managed, and given the game's manifest, project config, version info and
	/// icon as PE resources. Nothing is compiled or bundled per game, and nothing loose in assets/
	/// says what game this is - the runtime reads it back in <see cref="Standalone.LoadFromExecutable"/>.
	/// </summary>
	private void WriteExecutable()
	{
		var launcherDir = Path.Combine( Environment.CurrentDirectory, "bin", "managed" );
		var template = Path.Combine( launcherDir, LauncherName + ".exe" );
		if ( !File.Exists( template ) )
			throw new FileNotFoundException( $"No standalone launcher at {template} - build Sandbox-Engine.slnx (it's the Sbox-Standalone project's output)" );

		// The launcher assembly and its runtime config live with the rest of the engine's managed code
		var managedDir = Path.Combine( _exportConfig.TargetDir, "bin", "managed" );
		Directory.CreateDirectory( managedDir );
		foreach ( var extension in new[] { ".dll", ".runtimeconfig.json" } )
		{
			File.Copy( Path.Combine( launcherDir, LauncherName + extension ), Path.Combine( managedDir, LauncherName + extension ), true );
		}

		// The executable is that apphost, told where its assembly went
		var executablePath = Path.Combine( _exportConfig.TargetDir, $"{_exportConfig.ExecutableName}.exe" );
		var image = File.ReadAllBytes( template );
		SetAppHostAssemblyPath( image, LauncherName + ".dll", LauncherAssemblyPath );
		File.WriteAllBytes( executablePath, image );

		// And the game is whatever its resources say
		var title = Project.Config.Title ?? _exportConfig.ExecutableName;
		var data = new Dictionary<string, byte[]>
		{
			[Standalone.ManifestResourceName] = Encoding.UTF8.GetBytes( JsonSerializer.Serialize( StandaloneManifest ) ),
			[Standalone.ProjectConfigResourceName] = Encoding.UTF8.GetBytes( Project.Config.ToJson() ),
		};

		PeResources.Write( executablePath, data, new PeResources.VersionInfo( title, Project.Config.Org, $"Built {_exportConfig.BuildDate:u} with s&box" ), _exportConfig.TargetIcon );

		Logger.Info( $"Wrote {executablePath}" );
	}

	/// <summary>
	/// The executable name becomes a file name, so reject anything that can't be one before we've
	/// spent minutes copying assets.
	/// </summary>
	private static void ValidateExecutableName( string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) )
			throw new ArgumentException( "Export needs an executable name" );

		if ( name.IndexOfAny( Path.GetInvalidFileNameChars() ) >= 0 || name.EndsWith( '.' ) || name.Trim() != name )
			throw new ArgumentException( $"'{name}' isn't a valid executable name" );

		if ( name.EndsWith( ".exe", StringComparison.OrdinalIgnoreCase ) )
			throw new ArgumentException( $"Executable name '{name}' shouldn't include the .exe extension" );
	}

	/// <summary>
	/// The one thing an apphost knows is the path of the assembly it starts, in a NUL terminated
	/// buffer in its image, relative to itself. The SDK wrote the assembly's file name there; we
	/// want it under bin/managed instead.
	/// </summary>
	private static void SetAppHostAssemblyPath( byte[] image, string currentPath, string newPath )
	{
		var offset = image.AsSpan().IndexOf( Encoding.UTF8.GetBytes( currentPath + "\0" ) );
		if ( offset < 0 )
			throw new InvalidDataException( $"apphost doesn't start {currentPath} - is it really the standalone launcher?" );

		var path = Encoding.UTF8.GetBytes( newPath );
		if ( path.Length >= AppHostPathCapacity )
			throw new ArgumentException( $"'{newPath}' is too long for the apphost" );

		var buffer = image.AsSpan( offset, AppHostPathCapacity );
		buffer.Clear();
		path.CopyTo( buffer );
	}
}

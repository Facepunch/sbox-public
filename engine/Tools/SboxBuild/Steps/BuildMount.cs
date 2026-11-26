using Sandbox.ContentBuild;
using static Facepunch.Constants;

namespace Facepunch.Steps;

/// <summary>
/// Precompiles a mount addon .sbproj so the client can load it straight from a DLL.
/// Each output folder holds the compiled assembly, the project's <c>Mounts</c> folder, and its <c>Assets</c> folder with compiled assets.
/// </summary>
internal class BuildMount
{
	/// <summary>Folders copied from the project to the output, and the files of them that aren't (sources of compiled ones).</summary>
	static readonly (string folderName, string pattern)[] CopiedFolders = [("Assets", "*_c"), ("Mounts", "*_c")];

	readonly string[] projects;
	readonly string outputRoot;

	/// <param name="projects">.sbproj file(s)</param>
	/// <param name="outputRoot">Where each mount's folder goes. Defaults to game/mounts.</param>
	public BuildMount( string[] projects, string outputRoot )
	{
		this.projects = projects ?? [];
		this.outputRoot = outputRoot;
	}

	internal ExitCode Run()
	{
		try
		{
			var rootDir = Directory.GetCurrentDirectory();
			var gameDir = Path.Combine( rootDir, "game" );
			var outputDir = Path.GetFullPath( outputRoot ?? Path.Combine( gameDir, "mounts" ) );

			var menuBuildPath = Path.Combine( gameDir, "bin", "managed", OperatingSystem.IsWindows() ? "MenuBuild.exe" : "MenuBuild" );
			if ( !File.Exists( menuBuildPath ) )
			{
				Log.Error( $"MenuBuild not found at {menuBuildPath} - build managed first" );
				return ExitCode.Failure;
			}

			var projectFiles = new List<string>();

			foreach ( var project in projects )
			{
				var path = Path.GetFullPath( project );
				if ( Directory.Exists( path ) ) path = Path.Combine( path, ".sbproj" );

				if ( !File.Exists( path ) )
					throw new FileNotFoundException( $"Mount project not found: {project}" );

				projectFiles.Add( path );
			}

			if ( projectFiles.Count == 0 )
			{
				Log.Error( "No projects found. Specifiy .sbproj paths via the --project arg." );
				return ExitCode.Failure;
			}

			foreach ( var projectFile in projectFiles )
			{
				if ( !Build( menuBuildPath, gameDir, projectFile, outputDir ) )
					return ExitCode.Failure;
			}

			Log.Info( $"Built {projectFiles.Count} mount(s) into {outputDir}" );
			return ExitCode.Success;
		}
		catch ( Exception ex )
		{
			Log.Error( $"Mount build failed: {ex}" );
			return ExitCode.Failure;
		}
	}

	static bool Build( string menuBuildPath, string gameDir, string projectFile, string outputRoot )
	{
		var projectDir = Path.GetDirectoryName( projectFile );
		var name = Path.GetFileName( projectDir );
		var outputDir = Path.Combine( outputRoot, name );

		Log.Info( $"Building mount '{name}' from {projectFile}" );

		// start fresh
		if ( Directory.Exists( outputDir ) )
			Directory.Delete( outputDir, true );

		Directory.CreateDirectory( outputDir );

		if ( !Utility.RunProcess( menuBuildPath, $"--project \"{projectFile}\" --out \"{outputDir}\"", gameDir ) )
		{
			Log.Error( $"Failed to compile mount '{name}'" );
			return false;
		}

		var assetsDir = Path.Combine( projectDir, "Assets" );
		if ( Directory.Exists( assetsDir ) )
		{
			if ( !BuildShaders( gameDir, name, assetsDir ) ) return false;
			if ( !BuildContent( gameDir, $"content of mount '{name}'", $"mnt_{name}", assetsDir ) ) return false;
		}

		var mountsDir = FindFolder( projectDir, "Mounts" );
		if ( mountsDir is not null && !BuildThumbs( gameDir, name, mountsDir ) ) return false;

		foreach ( (var folder, var pattern) in CopiedFolders )
		{
			var source = FindFolder( projectDir, folder );
			if ( source is null ) continue;

			CopyDirectory( source, Path.Combine( outputDir, folder ), pattern );
		}

		Log.Info( $"Mount '{name}' -> {outputDir}" );
		return true;
	}

	/// <summary>
	/// Compiles the project's thumbnails (<c>Mounts/{ident}/thumbs/{path}.png</c>) to <c>Mounts/{ident}/thumbs/{hash}.vtex_c</c>,
	/// named by <see cref="ThumbHash"/> of the path; the pngs aren't shipped. The engine adds the shipped Mounts folder as a
	/// native search path, so a thumb loads as <c>{ident}/thumbs/{hash}.vtex</c>.
	/// </summary>
	static bool BuildThumbs( string gameDir, string name, string mountsDir )
	{
		// The texture sources only exist for this build, the pngs are what's kept in the project
		var sources = new List<string>();
		try
		{
			foreach ( var thumbsDir in Directory.EnumerateDirectories( mountsDir ).Select( x => Path.Combine( x, "thumbs" ) ).Where( Directory.Exists ) )
			{
				// Every thumb is compiled afresh, so clear out the last build's, which may be named differently
				foreach ( var old in Directory.EnumerateFiles( thumbsDir, "*.vtex_c", SearchOption.AllDirectories ) )
					File.Delete( old );

				foreach ( var image in Directory.EnumerateFiles( thumbsDir, "*.png", SearchOption.AllDirectories ) )
				{
					var thumbPath = Path.ChangeExtension( Path.GetRelativePath( thumbsDir, image ), null );
					var source = Path.Combine( thumbsDir, $"{ThumbHash( thumbPath )}.vtex" );
					File.WriteAllText( source, ThumbSource( Path.GetRelativePath( mountsDir, image ).Replace( '\\', '/' ) ) );
					sources.Add( source );
				}
			}

			if ( sources.Count == 0 ) return true;
			if ( !BuildContent( gameDir, $"thumbnails of mount '{name}'", $"mnt_{name}_thumbs", mountsDir ) ) return false;
		}
		finally
		{
			foreach ( var source in sources )
				File.Delete( source );
		}

		var missing = sources.Where( x => !File.Exists( $"{x}_c" ) ).ToArray();
		if ( missing.Length > 0 )
		{
			Log.Error( $"Thumbnails of mount '{name}' weren't compiled: {string.Join( ", ", missing )}" );
			return false;
		}

		return true;
	}

	/// <summary>
	/// A thumb's file name: the first 16 hex digits of the MD5 of its path within the thumbs folder, without extension,
	/// lowercased with forward slashes, as <c>string.Md5()</c> hashes it. ThumbLoader looks it up the same way.
	/// </summary>
	static string ThumbHash( string thumbPath )
	{
		var normalized = thumbPath.Replace( '\\', '/' ).ToLowerInvariant();
		return Convert.ToHexString( System.Security.Cryptography.MD5.HashData( System.Text.Encoding.UTF8.GetBytes( normalized ) ) )[..16].ToLowerInvariant();
	}

	static string ThumbSource( string imagePath ) => $$"""
		{
		  "$compiler": "texture",
		  "$source": "imagefile",
		  "data": {
		    "FilePath": "{{imagePath}}",
		    "Lossless": false
		  },
		  "compiled": null
		}
		""";

	/// <summary>
	/// Compiles the shaders under the mount's assets, mounted as the engine mounts them so they find each other.
	/// </summary>
	static bool BuildShaders( string gameDir, string name, string assetsDir )
	{
		if ( !Directory.EnumerateFiles( assetsDir, "*.shader", SearchOption.AllDirectories ).Any() ) return true;

		var shaderCompilerPath = Path.Combine( gameDir, "bin", "managed", OperatingSystem.IsWindows() ? "ShaderCompiler.exe" : "ShaderCompiler" );
		if ( !File.Exists( shaderCompilerPath ) )
		{
			Log.Error( $"ShaderCompiler not found at {shaderCompilerPath} - build managed first" );
			return false;
		}

		Log.Info( $"Compiling shaders of mount '{name}'" );
		if ( !Utility.RunProcess( shaderCompilerPath, $"-assets \"{assetsDir}\"", gameDir ) )
		{
			Log.Error( $"Failed to compile shaders of mount '{name}'" );
			return false;
		}

		return true;
	}

	/// <summary>
	/// Compiles a content root of the mount as an installed editor builds a project's: engine content is read, not built.
	/// Generated textures go into the root too, so they ship with it.
	/// </summary>
	static bool BuildContent( string gameDir, string what, string mod, string rootDir )
	{
		var worker = Path.Combine( gameDir, "bin", "managed", OperatingSystem.IsWindows() ? "contentworker.exe" : "contentworker" );
		if ( !File.Exists( worker ) )
		{
			Log.Error( $"contentworker not found at {worker} - build managed first" );
			return false;
		}

		Log.Info( $"Compiling {what}" );

		// Records and the built-in projects' compilers are only for this build, kept out of the shipped folder
		var temp = Directory.CreateTempSubdirectory( "sbox-mount" ).FullName;
		var compilers = Path.Combine( temp, "compilers" );
		Directory.CreateDirectory( compilers );

		ResourceCompiler compiler = null;
		try
		{
			var request = new BuildRequest
			{
				Roots = [new ContentRoot( mod, rootDir, Path.Combine( temp, "state" ) )],
				ReadOnlyMounts = [.. ContentRoots.Engine( gameDir ).Select( x => (x.Mod, x.Path) )],
				OutputRoots = new Dictionary<string, string> { ["transient"] = rootDir },
				OutputFallbacks = new Dictionary<string, string> { ["transient"] = Path.Combine( gameDir, "addons", "menu", "Transients" ) },
				MaxJobs = ResourceCompiler.DefaultMaxJobs,
			};

			if ( !Utility.RunProcess( worker, $"-compileprojects \"{compilers}\"", gameDir ) ) return false;

			compiler = new ResourceCompiler( worker, new AssetTypeTable( Path.Combine( gameDir, "bin", "assettypes.txt" ) ), request.Mounts, compilers );
			var report = ContentBuilder.Build( request, compiler );

			if ( !report.Success )
			{
				Log.Error( report.Error ?? report.Log.ToString() );
				Log.Error( $"Failed to compile {what}: {string.Join( ", ", report.Failed.Concat( report.Blocked ) )}" );
				return false;
			}

			Log.Info( $"Compiled {what}: {report.Jobs.Count} compiled, {report.Unsupported.Count} unsupported" );
			return true;
		}
		finally
		{
			compiler?.Finished();
			Directory.Delete( temp, true );
		}
	}

	/// <summary>Project folders are found regardless of case, like the engine does.</summary>
	static string FindFolder( string projectDir, string name )
	{
		foreach ( var dir in Directory.EnumerateDirectories( projectDir ) )
		{
			if ( string.Equals( Path.GetFileName( dir ), name, StringComparison.OrdinalIgnoreCase ) )
				return dir;
		}

		return null;
	}

	static void CopyDirectory( string source, string destination, string pattern = "*" )
	{
		Directory.CreateDirectory( destination );

		foreach ( var dir in Directory.EnumerateDirectories( source, "*", SearchOption.AllDirectories ) )
			Directory.CreateDirectory( Path.Combine( destination, Path.GetRelativePath( source, dir ) ) );

		foreach ( var file in Directory.EnumerateFiles( source, pattern, SearchOption.AllDirectories ) )
		{
			File.Copy( file, Path.Combine( destination, Path.GetRelativePath( source, file ) ), true );
		}
	}
}

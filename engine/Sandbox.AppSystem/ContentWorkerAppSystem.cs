using Sandbox.ContentBuild;
using Sandbox.Tasks;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;

namespace Sandbox;

/// <summary>
/// A content build worker: the resource compiler with the asset system and the editor's managed compilers, taking
/// jobs from standard input and running them on threads of their own.
/// </summary>
public sealed class ContentWorkerAppSystem : ToolAppSystem
{
	/// <summary>
	/// What compiles use of an app's startup (Bootstrap.PreInit), without Steam, the backend, downloads or error reports.
	/// </summary>
	protected override void PreInit()
	{
		ThreadPool.SetMinThreads( Environment.ProcessorCount, Environment.ProcessorCount );
		Graphics.Initialize();
		SyncContext.Init();

		// Compilers write numbers into outputs, on the threads jobs run on too
		var culture = CultureInfo.CreateSpecificCulture( "en-US" );
		CultureInfo.DefaultThreadCurrentCulture = culture;
		CultureInfo.DefaultThreadCurrentUICulture = culture;
		Thread.CurrentThread.CurrentCulture = culture;
		Thread.CurrentThread.CurrentUICulture = culture;

		Diagnostics.Logging.Enabled = true;
		Diagnostics.Logging.PrintToConsole = Application.IsHeadless;

		EngineFileSystem.InitializeDataFolder();

		// Initialize an empty mount host (Bootstrap.PreInit): a worker never mounts a game, and loading mount addons
		// would prepend game/mounts/*/Assets ahead of -searchpaths in the native filesystem.
		Mounting.Directory.LoadSources( loadMounts: false );
	}

	public override void Init()
	{
		base.Init();

		// Without -vulkan it has the empty render system, as a console app, and nothing reads a texture's pixels back. One
		// asked for a device (to copy compiled textures) has to have it.
		if ( Environment.GetCommandLineArgs().Contains( "-vulkan" ) && !Graphics.IsAvailable )
			throw new InvalidOperationException( "A content worker was asked for a render device (-vulkan), and none could be created: no GPU or Vulkan driver" );

		Editor.ContentCompilerHost.InitializeInterop();
		_appSystem.AddSystem( "assetsystem", "AssetSystem001" );
	}

	/// <summary>
	/// Loads the compiled tool and project assemblies in a folder, whose compilers and game resource types the build uses.
	/// </summary>
	public void LoadCompilers( string directory )
	{
		var files = directory is null ? [] : Directory.GetFiles( directory, "*.dll" ).Order( StringComparer.OrdinalIgnoreCase ).ToArray();
		Editor.ContentCompilerHost.InitializeCompilers( files.Select( Assembly.LoadFrom ).ToList(), Mounts( Environment.GetCommandLineArgs() ) );
	}

	/// <summary>
	/// The mounts of -searchpaths "mod|path;mod|path", in mount order.
	/// </summary>
	static IEnumerable<(string Mod, string Path)> Mounts( string[] args )
	{
		var i = Array.IndexOf( args, "-searchpaths" );
		if ( i < 0 ) return [];
		return args[i + 1].Split( ';', StringSplitOptions.RemoveEmptyEntries ).Select( x => x.Split( '|' ) ).Select( x => (x[0], x[1]) );
	}

	public int Serve() => Editor.ContentCompilerHost.Serve();

	public bool Compile( string path ) => Editor.ContentCompilerHost.Compile( path );

	/// <summary>
	/// Compiles the built-in projects and an optional project into a folder for workers to load.
	/// When a roots file is requested, activates every installed addon and writes its content roots for SboxBuild.
	/// </summary>
	public static bool CompileBuiltInProjects( string directory, string projectPath = null, string rootsFile = null )
	{
		var sw = System.Diagnostics.Stopwatch.StartNew();
		TimeSpan bootTime, initTime, compileTime, copyTime;
		using ( var app = new ToolAppSystem() )
		{
			bootTime = sw.Elapsed;
			sw.Restart();

			SyncContext.RunBlocking( Project.InitializeBuiltIn( syncPackageManager: false, activateAddons: rootsFile is not null ) );

			if ( projectPath is not null && Project.AddFromFile( projectPath ) is null ) return false;
			if ( rootsFile is not null ) Project.UpdateLibraryReferences();
			initTime = sw.Elapsed;
			sw.Restart();

			if ( !SyncContext.RunBlocking( Project.CompileAsync() ) ) return false;
			compileTime = sw.Elapsed;
			sw.Restart();

			Directory.CreateDirectory( directory );
			foreach ( var project in Project.All.Where( x => x.Active ) )
			{
				foreach ( var file in project.AssemblyFileSystem.FindFile( "", "*.dll", true ) )
					File.WriteAllBytes( Path.Combine( directory, Path.GetFileName( file ) ), project.AssemblyFileSystem.ReadAllBytes( file ).ToArray() );
			}

			if ( rootsFile is not null )
			{
				var gameDir = EngineFileSystem.Root.GetFullPath( "/" );
				var addonsDir = EngineFileSystem.Root.GetFullPath( "addons" );
				var engineRoots = ContentRoots.Engine( gameDir );
				var enginePaths = engineRoots.Select( x => x.Path ).ToHashSet( StringComparer.OrdinalIgnoreCase );
				var projects = Project.All
					.Where( x => string.Equals( x.RootDirectory?.Parent?.FullName, addonsDir, StringComparison.OrdinalIgnoreCase ) && x.HasAssetsPath() )
					.OrderBy( x => x.ConfigFilePath, StringComparer.OrdinalIgnoreCase )
					.Select( x => (x.Config.FullIdent, Assets: x.GetAssetsPath(), Directory: x.GetRootPath()) )
					.Where( x => !enginePaths.Contains( x.Assets ) );

				File.WriteAllText( rootsFile, JsonSerializer.Serialize( ContentRoots.WithProjects( engineRoots, projects ) ) );
			}

			copyTime = sw.Elapsed;
			sw.Restart();
		}

		Console.WriteLine( $"CompileBuiltInProjects: boot {bootTime.TotalSeconds:F1}s, init {initTime.TotalSeconds:F1}s, compile {compileTime.TotalSeconds:F1}s, copy {copyTime.TotalSeconds:F1}s, shutdown {sw.Elapsed.TotalSeconds:F1}s" );
		return true;
	}
}

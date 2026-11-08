using Sandbox.Tasks;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Sandbox;

/// <summary>
/// A content build worker: the resource compiler with the asset system and the editor's managed compilers, taking
/// one job at a time from standard input.
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

		// Compilers write numbers into outputs
		var culture = CultureInfo.CreateSpecificCulture( "en-US" );
		Thread.CurrentThread.CurrentCulture = culture;
		Thread.CurrentThread.CurrentUICulture = culture;

		Diagnostics.Logging.Enabled = true;
		Diagnostics.Logging.PrintToConsole = Application.IsHeadless;

		EngineFileSystem.InitializeDataFolder();

		// As every app has them (Bootstrap.PreInit); a worker never mounts a game, so a compile can't read one
		Mounting.Directory.LoadAssemblies();
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
	/// Compiles the projects the editor has built in, as the editor does, into a folder for workers to load. False if any
	/// of them didn't compile.
	/// </summary>
	public static bool CompileBuiltInProjects( string directory )
	{
		using var app = new ToolAppSystem();

		SyncContext.RunBlocking( Project.InitializeBuiltIn( false ) );
		if ( !SyncContext.RunBlocking( Project.CompileAsync() ) ) return false;

		Directory.CreateDirectory( directory );
		foreach ( var project in Project.All.Where( x => x.Active ) )
		{
			foreach ( var file in project.AssemblyFileSystem.FindFile( "", "*.dll", true ) )
				File.WriteAllBytes( Path.Combine( directory, Path.GetFileName( file ) ), project.AssemblyFileSystem.ReadAllBytes( file ).ToArray() );
		}

		return true;
	}
}

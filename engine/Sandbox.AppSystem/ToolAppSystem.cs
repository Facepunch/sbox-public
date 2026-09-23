using Sandbox.Engine;
using Sandbox.Tasks;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Sandbox;

/// <summary>
/// Used to create standalone tools that can still interop to the engine
/// </summary>
public class ToolAppSystem : AppSystem, IDisposable
{
	public static BaseFileSystem Content => EngineFileSystem.CoreContent;

	public void Dispose()
	{
		Shutdown();
	}

	public ToolAppSystem() : this( ResolveManagedToolGameRoot() )
	{
	}

	internal ToolAppSystem( string gameRoot )
	{
		InitEnginePaths( gameRoot );

		Init();
	}

	public override void Init()
	{
		TestSystemRequirements();

		base.Init();

		//	CreateGame();
		//	CreateMenu();

		var createInfo = new AppSystemCreateInfo()
		{
			WindowTitle = "s&box tool",
			Flags = AppSystemFlags.IsConsoleApp | AppSystemFlags.IsEditor
		};

		InitTool( createInfo );
		AddSearchPaths( System.Environment.GetCommandLineArgs() );
	}

	protected void InitTool( AppSystemCreateInfo createInfo )
	{
		var commandLine = System.Environment.CommandLine;
		commandLine = commandLine.Replace( ".dll", ".exe" ); // uck
		if ( Application.IsAutomation )
		{
			commandLine += " -noassert";
		}

		_appSystem = CMaterialSystem2AppSystemDict.Create( createInfo.ToMaterialSystem2AppSystemDictCreateInfo() );
		_appSystem.SetModGameSubdir( "core" );
		_appSystem.SetInToolsMode();
		_appSystem.SetSteamAppId( (uint)Application.AppId );

		//_appSystem.Init();

		if ( !NativeEngine.EngineGlobal.SourceEnginePreInit( commandLine, _appSystem ) )
		{
			throw new System.Exception( "SourceEnginePreInit failed" );
		}

		_appSystem.AddSystem( "resourcecompiler", "ResourceCompilerSystem001" );

		Bootstrap.InitApplication( _appSystem );
		Bootstrap.PreInit();

		//Bootstrap.Init();
	}

	static void AddSearchPaths( string[] args )
	{
		var i = Array.IndexOf( args, "-searchpaths" );
		if ( i < 0 ) return;

		var paths = args[i + 1];

		foreach ( var path in paths.Split( ";" ) )
		{
			var parts = path.Split( "|" );
			EngineFileSystem.AddContentPath( parts[1] );
		}

	}

	/// <summary>
	/// We want to set current dir to /game/ 
	/// and add the native dll paths to the path
	/// </summary>
	static string ResolveManagedToolGameRoot()
	{
		var exePath = Environment.GetCommandLineArgs()[0];
		exePath = System.IO.Path.GetDirectoryName( exePath );

		if ( exePath is null || !exePath.EndsWith( System.IO.Path.Combine( "bin", "managed" ), StringComparison.OrdinalIgnoreCase ) )
			throw new Exception( $"Unknown Location - expected to be running from bin/managed, got '{exePath}'" );

		return new DirectoryInfo( exePath ).Parent?.Parent?.FullName
			?? throw new Exception( $"Couldn't resolve game root from '{exePath}'" );
	}

	/// <summary>
	/// Set the current directory to the game root and configure native library lookup.
	/// </summary>
	static void InitEnginePaths( string gameRoot )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( gameRoot );

		gameRoot = System.IO.Path.TrimEndingDirectorySeparator( System.IO.Path.GetFullPath( gameRoot ) );
		var managedAssembly = System.IO.Path.Combine( gameRoot, "bin", "managed", "Sandbox.Engine.dll" );
		var nativeDllPath = System.IO.Path.Combine( gameRoot, "bin", GetNativePlatformDirectory() );

		if ( !File.Exists( managedAssembly ) )
			throw new DirectoryNotFoundException( $"Game root does not contain bin/managed/Sandbox.Engine.dll: '{gameRoot}'" );

		if ( !Directory.Exists( nativeDllPath ) )
			throw new DirectoryNotFoundException( $"Game root does not contain the native platform directory: '{nativeDllPath}'" );

		// Interop resolves native libraries relative to this directory.
		Environment.CurrentDirectory = gameRoot;
		NetCore.NativeDllPath = nativeDllPath;

		// The rest is Windows-only. Elsewhere the loader finds our own libraries from the
		// interop path and their dependencies through each library's RUNPATH.
		if ( !OperatingSystem.IsWindows() )
			return;

		// If we don't load sentry specifically from this directory, it'll try to load the
		// copy in the managed folder.
		NativeLibrary.TryLoad( System.IO.Path.Combine( nativeDllPath, "sentry.dll" ), out _ );

		var path = System.Environment.GetEnvironmentVariable( "PATH" ) ?? string.Empty;
		var firstPath = path.Split( System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries )
			.FirstOrDefault();
		var alreadyFirst = firstPath is not null && string.Equals(
			System.IO.Path.TrimEndingDirectorySeparator( firstPath ),
			nativeDllPath,
			StringComparison.OrdinalIgnoreCase );

		if ( !alreadyFirst )
		{
			System.Environment.SetEnvironmentVariable(
				"PATH",
				$"{nativeDllPath}{System.IO.Path.PathSeparator}{path}" );
		}
	}

	static string GetNativePlatformDirectory()
	{
		if ( OperatingSystem.IsWindows() ) return "win64";
		if ( OperatingSystem.IsLinux() ) return "linuxsteamrt64";
		if ( OperatingSystem.IsMacOS() ) return "osxarm64";

		throw new PlatformNotSupportedException();
	}
}

using Sandbox.Engine;
using Sandbox.Tasks;
using System;
using System.IO;
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

	public ToolAppSystem()
	{
		InitEnginePaths();

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
		if ( OperatingSystem.IsWindows() )
			commandLine = commandLine.Replace( ".dll", ".exe" ); // uck
		else
			commandLine = commandLine.Replace( ".dll", "" ); // double uck

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

		Bootstrap.PreInit( _appSystem );

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
	void InitEnginePaths()
	{
		var exePath = Environment.GetCommandLineArgs()[0];
		exePath = System.IO.Path.GetDirectoryName( exePath );

		// we're in the managed folder, we can set this shit up
		if ( exePath.EndsWith( System.IO.Path.Join( "bin", "managed" ), StringComparison.OrdinalIgnoreCase ) )
		{
			var dirInfo = new DirectoryInfo( exePath );

			var gameRoot = dirInfo.Parent.Parent;

			Environment.CurrentDirectory = gameRoot.FullName;
			var nativeDllPath = "";
			if ( OperatingSystem.IsWindows() )
			{

				nativeDllPath = $"{gameRoot.FullName}\\bin\\win64";
				//
				// If we don't load sentry specifically from this directly, it'll
				// try to load the one from the managed folder
				//
				NativeLibrary.TryLoad( $"{nativeDllPath}\\sentry.dll", out _ );
				//NativeLibrary.TryLoad( $"{nativeDllPath}\\tier0.dll", out _ );
				//NativeLibrary.TryLoad( $"{nativeDllPath}\\engine2.dll", out _ );
			}
			else if ( OperatingSystem.IsLinux() )
			{
				nativeDllPath = $"{gameRoot.FullName}/bin/linuxsteamrt64";
				NativeLibrary.TryLoad( $"{nativeDllPath}/libsentry.so", out _ );
			}
			else if ( OperatingSystem.IsMacOS() )
			{
				nativeDllPath = $"{gameRoot.FullName}/bin/osxarm64";
				NativeLibrary.TryLoad( $"{nativeDllPath}/libsentry.dylib", out _ );
			}
			else
				throw new Exception( "Unsupported system" );



			//
			// Put our native dll path first so that when looking up native dlls we'll
			// always use the ones from our folder first
			//
			if ( OperatingSystem.IsWindows() )
			{
				var path = System.Environment.GetEnvironmentVariable( "PATH" );
				path = $"{nativeDllPath};{path}";
				System.Environment.SetEnvironmentVariable( "PATH", path );
			}

			return;
		}

		throw new Exception( "Unknown Location" );
	}
}

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
		commandLine = commandLine.Replace( ".dll", ".exe" ); // uck

		_appSystem = null; // Set to null or a dummy implementation for Linux
		if ( !System.OperatingSystem.IsLinux() )
		{
			_appSystem = CMaterialSystem2AppSystemDict.Create( createInfo.ToMaterialSystem2AppSystemDictCreateInfo() );
		}
		
		        if ( _appSystem is not null )
		        {
		            _appSystem.Value.SetModGameSubdir( "core" );
		            _appSystem.Value.SetInToolsMode();
		            _appSystem.Value.SetSteamAppId( (uint)Application.AppId );
		
		            //_appSystem.Init();
		
		            if ( !NativeEngine.EngineGlobal.SourceEnginePreInit( commandLine, _appSystem.Value ) ) // Use .Value
		            {
		                throw new System.Exception( "SourceEnginePreInit failed" );
		            }
		
		            _appSystem.Value.AddSystem( "resourcecompiler", "ResourceCompilerSystem001" );
		
		            Bootstrap.PreInit( _appSystem.Value ); // Use .Value
		
		            //Bootstrap.Init();
		        }	}

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
		if ( exePath.EndsWith( Path.Combine( "bin", "managed" ), StringComparison.OrdinalIgnoreCase ) )
		{
			var dirInfo = new DirectoryInfo( exePath );

			var gameRoot = dirInfo.Parent.Parent;

			Environment.CurrentDirectory = gameRoot.FullName;
			string nativeDllPath = "";

			if ( OperatingSystem.IsLinux() )
			{
				nativeDllPath = Path.Combine( gameRoot.FullName, "bin", "linuxsteamrt64" );
			}
			else if ( OperatingSystem.IsWindows() )
			{
				nativeDllPath = Path.Combine( gameRoot.FullName, "bin", "win64" );
			}
			else
			{
				// Default to win64 for other OSes, or handle specifically if needed
				nativeDllPath = Path.Combine( gameRoot.FullName, "bin", "win64" );
			}

			//
			// If we don't load sentry specifically from this directly, it'll
			// try to load the one from the managed folder
			//
			if ( OperatingSystem.IsLinux() )
			{
				NativeLibrary.TryLoad( Path.Combine( nativeDllPath, "sentry.so" ), out _ );
				//NativeLibrary.TryLoad( Path.Combine( nativeDllPath, "tier0.so" ), out _ );
				//NativeLibrary.TryLoad( Path.Combine( nativeDllPath, "engine2.so" ), out _ );

				var path = System.Environment.GetEnvironmentVariable( "PATH" );
				path = $"{nativeDllPath}:{path}";
				System.Environment.SetEnvironmentVariable( "PATH", path );
			}
			else if ( OperatingSystem.IsWindows() )
			{
				NativeLibrary.TryLoad( Path.Combine( nativeDllPath, "sentry.dll" ), out _ );
				//NativeLibrary.TryLoad( Path.Combine( nativeDllPath, "tier0.dll"), out _ );
				//NativeLibrary.TryLoad( Path.Combine( nativeDllPath, "engine2.dll"), out _ );

				var path = System.Environment.GetEnvironmentVariable( "PATH" );
				path = $"{nativeDllPath};{path}";
				System.Environment.SetEnvironmentVariable( "PATH", path );
			}
			else
			{
				// Default to Windows behavior for other OSes, or handle specifically if needed
				NativeLibrary.TryLoad( Path.Combine( nativeDllPath, "sentry.dll" ), out _ );
				var path = System.Environment.GetEnvironmentVariable( "PATH" );
				path = $"{nativeDllPath};{path}";
				System.Environment.SetEnvironmentVariable( "PATH", path );
			}

			return;
		}

		throw new Exception( "Unknown Location" );
	}
}

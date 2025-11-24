using Sandbox.Diagnostics;
using Sandbox.Engine;
using Sandbox.Internal;
using System;
using System.Globalization;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;

namespace Sandbox;

public class AppSystem
{
	protected Logger log = new Logger( "AppSystem" );
	internal CMaterialSystem2AppSystemDict _appSystem { get; set; }

	[DllImport( "user32.dll", CharSet = CharSet.Unicode )]
	private static extern int MessageBox( IntPtr hWnd, string text, string caption, uint type );

	/// <summary>
	/// We should check all the system requirements here as early as possible.
	/// </summary>
	public void TestSystemRequirements()
	{
		if ( !OperatingSystem.IsWindows() )
			return;

		// AVX is on any sane CPU since 2011
		if ( !Avx.IsSupported )
		{
			MessageBox( IntPtr.Zero, "Your CPU needs to support AVX instructions to run this game.", "Unsupported CPU", 0x10 );
			Environment.Exit( 1 );
		}

		// check core count, ram, os?
		// rendersystemvulkan ends up checking gpu, driver, vram later on

		MissingDependancyDiagnosis.Run();
	}

	public virtual void Init()
	{
		GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
		NetCore.InitializeInterop( Environment.CurrentDirectory );

		if ( Utility.CommandLine.HasSwitch( "-quit" ) )
			Application.Exit(); // exit as soon as we can
	}

	void SetupEnvironment()
	{
		CultureInfo culture = (CultureInfo)CultureInfo.CurrentCulture.Clone();

		//
		// force GregorianCalendar, because that's how we're going to be parsing dates etc
		//
		if ( culture.DateTimeFormat.Calendar is not GregorianCalendar )
		{
			culture.DateTimeFormat.Calendar = new GregorianCalendar();
		}

		CultureInfo.DefaultThreadCurrentCulture = culture;
		CultureInfo.DefaultThreadCurrentUICulture = culture;
	}

	/// <summary>
	/// Create the Menu instance.
	/// </summary>
	protected void CreateMenu()
	{
		MenuDll.Create();
	}

	/// <summary>
	/// Create the Game (Sandbox.GameInstance)
	/// </summary>
	protected void CreateGame()
	{
		GameInstanceDll.Create();
	}

	/// <summary>
	/// Create the editor (Sandbox.Tools)
	/// </summary>
	protected void CreateEditor()
	{
		Editor.AssemblyInitialize.Initialize();
	}

	public void Run()
	{
		try
		{
			SetupEnvironment();

			Application.TryLoadVersionInfo( Environment.CurrentDirectory );

			//
			// Putting ErrorReporter.Initialize(); before Init here causes engine2.dll 
			// to be unable to load. I dont know wtf and I spent too much time looking into it.
			// It's finding the assemblies still, The last dll it loads is tier0.dll.
			//

			Init();

			NativeEngine.EngineGlobal.Plat_SetCurrentFrame( 0 );

			while ( RunFrame() )
			{
				BlockingLoopPumper.Run( () => RunFrame() );
			}

			Shutdown();
		}
		catch ( System.Exception e )
		{
			ErrorReporter.Initialize();
			ErrorReporter.ReportException( e );
			ErrorReporter.Flush();

			Console.WriteLine( $"Error: ({e.GetType()}) {e.Message}" );

			Environment.Exit( 1 );
		}
	}

	protected virtual bool RunFrame()
	{
		EngineLoop.RunFrame( _appSystem, out bool wantsToQuit );

		return !wantsToQuit;
	}

	public virtual void Shutdown()
	{
		// Shut the games down
		EngineLoop.Exiting();

		// Shut the engine down (close window etc)
		NativeEngine.EngineGlobal.SourceEngineShutdown( _appSystem, false );

		// Flush the api (close actvity, update stats etc)
		Api.Shutdown();

		if ( _appSystem.IsValid )
		{
			_appSystem.Destroy();
			_appSystem = default;
		}

		if ( steamApiDll != IntPtr.Zero )
		{
			NativeLibrary.Free( steamApiDll );
			steamApiDll = default;
		}
		// Unload native dlls:
		// At this point we should no longer need them.
		// If we still hold references to native resources, we want it to crash here rather than on application exit.
		Managed.SandboxEngine.NativeInterop.Free();

		// No-ops if editor isn't loaded
		Managed.SourceTools.NativeInterop.Free();
		Managed.SourceAssetSytem.NativeInterop.Free();
		Managed.SourceHammer.NativeInterop.Free();
		Managed.SourceModelDoc.NativeInterop.Free();
		Managed.SourceAnimgraph.NativeInterop.Free();
	}

	protected void InitGame( AppSystemCreateInfo createInfo )
	{
		var commandLine = System.Environment.CommandLine;
		commandLine = commandLine.Replace( ".dll", ".exe" ); // uck

		_appSystem = CMaterialSystem2AppSystemDict.Create( createInfo.ToMaterialSystem2AppSystemDictCreateInfo() );

		if ( createInfo.Flags.HasFlag( AppSystemFlags.IsEditor ) )
		{
			_appSystem.SetInToolsMode();
		}

		if ( createInfo.Flags.HasFlag( AppSystemFlags.IsUnitTest ) )
		{
			_appSystem.SetInTestMode();
		}

		if ( createInfo.Flags.HasFlag( AppSystemFlags.IsStandaloneGame ) )
		{
			_appSystem.SetInStandaloneApp();
		}

		if ( createInfo.Flags.HasFlag( AppSystemFlags.IsDedicatedServer ) )
		{
			_appSystem.SetDedicatedServer( true );
		}

		_appSystem.SetSteamAppId( (uint)Application.AppId );

		if ( !NativeEngine.EngineGlobal.SourceEnginePreInit( commandLine, _appSystem ) )
		{
			throw new System.Exception( "SourceEnginePreInit failed" );
		}

		Bootstrap.PreInit( _appSystem );

		if ( createInfo.Flags.HasFlag( AppSystemFlags.IsStandaloneGame ) )
		{
			Standalone.Init();
		}

		if ( !NativeEngine.EngineGlobal.SourceEngineInit( _appSystem ) )
		{
			throw new System.Exception( "SourceEngineInit returned false" );
		}

		Bootstrap.Init();
	}

	protected void SetWindowTitle( string title )
	{
		_appSystem.SetAppWindowTitle( title );
	}

	IntPtr steamApiDll = IntPtr.Zero;

	/// <summary>
	/// Explicitly load the Steam Api dll from our bin folder, so that it doesn't accidentally
	/// load one from c:\system32\ or something. This is a problem when people have installed
	/// pirate versions of Steam in the past and have the assembly hanging around still. By loading
	/// it here we're saying use this version, and it won't try to load another one.
	/// </summary>
	protected void LoadSteamDll()
	{
		if ( !OperatingSystem.IsWindows() )
			return;

		var dllName = $"{Environment.CurrentDirectory}\\bin\\win64\\steam_api64.dll";
		if ( !NativeLibrary.TryLoad( dllName, out steamApiDll ) )
		{
			throw new System.Exception( "Couldn't load bin/win64/steam_api64.dll" );
		}
	}
}

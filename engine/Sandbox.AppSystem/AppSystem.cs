using Sandbox.Diagnostics;
using Sandbox.Engine;
using Sandbox.Internal;
using Sandbox.Network;
using Sandbox.Rendering;
using System;
using System.Globalization;
using System.Linq;
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

			CrashShutdown();
		}
	}

	protected virtual bool RunFrame()
	{
		EngineLoop.RunFrame( _appSystem, out bool wantsToQuit );

		return !wantsToQuit;
	}

	/// <summary>
	/// Emergency shutdown for crash scenarios. Uses Plat_ExitProcess to immediately
	/// terminate without running finalizers, DLL destructors, or creating dumps.
	/// </summary>
	void CrashShutdown()
	{
		// Best effort to sve and flush things.
		// Might now work because we are in an unstable state.

		try { ConVarSystem.SaveAll(); } catch { }

		try { ErrorReporter.Flush(); } catch { }

		try { Api.Shutdown(); } catch { }

		try { NLog.LogManager.Shutdown(); } catch { }

		NativeEngine.EngineGlobal.Plat_ExitProcess( 1 );
	}

	public virtual void Shutdown()
	{
		// Tag crash reports during shutdown so they can be filtered in Sentry
		NativeErrorReporter.SetTag( "shutdown_crash", "true" );

		// Make sure game instance is closed
		IGameInstanceDll.Current?.CloseGame();

		// Send shutdown event, should allow us to track successful shutdown vs crash
		{
			var analytic = new Api.Events.EventRecord( "Exit" );
			analytic.SetValue( "uptime", RealTime.Now );
			// We could record a bunch of stats during the session and
			// submit them here. I'm thinking things like num games played
			// menus visited, time in menus, time in game, files downloaded.
			// Things to give us a whole session picture.
			analytic.Submit();
		}

		ConVarSystem.SaveAll();

		IToolsDll.Current?.Exiting();
		IMenuDll.Current?.Exiting();
		IGameInstanceDll.Current?.Exiting();

		// Flush API
		Api.Shutdown();

		SoundFile.Shutdown();
		SoundHandle.Shutdown();
		DedicatedServer.Shutdown();

		// Flush queued scene object/world deletes that normally run at
		// end-of-frame. During shutdown no frame is rendered, so native
		// never fires FreeHandle for these — leaving HandleIndex entries
		// that root SceneWorld/SceneModel trees.
		SceneWorld.FlushQueuedDeletes();

		Engine.InputRouter.Shutdown();
		Diagnostics.Logging.ClearListeners();

		// Flush mount utility preview cache — holds strong refs to textures
		Mounting.MountUtility.FlushCache();

		ConVarSystem.ClearNativeCommands();

		// Whatever package still exists needs to fuck off
		PackageManager.UnmountAll();

		// Release all cached stylesheets — their Styles hold Lazy<Texture> refs
		UI.StyleSheet.ResetStyleSheets();

		// Null all static native-resource and Panel references across every assembly loaded
		// in this AppDomain — catches engine, base addon, menu addon, game dlls, etc.
		// Only scan assemblies that actually reference the engine assembly; anything that
		// doesn't reference it can't possibly hold statics of Texture, Panel, etc.
		var engineAsmName = typeof( Texture ).Assembly.GetName().Name;
		foreach ( var asm in AppDomain.CurrentDomain.GetAssemblies() )
		{
			// The engine assembly itself always qualifies
			var isEngineAsm = asm.GetName().Name == engineAsmName;

			// Dynamic assemblies have no manifest references but may still hold Panel statics
			// (Razor-generated panels compile to dynamic assemblies)
			var isDynamic = asm.IsDynamic;

			// Static assemblies qualify only if they reference the engine
			var referencesEngine = !isDynamic && asm.GetReferencedAssemblies().Any( r => r.Name == engineAsmName );

			if ( !isEngineAsm && !isDynamic && !referencesEngine )
				continue;

			ReflectionUtility.NullStaticReferencesOfType( asm, typeof( Texture ) );
			ReflectionUtility.NullStaticReferencesOfType( asm, typeof( Model ) );
			ReflectionUtility.NullStaticReferencesOfType( asm, typeof( Material ) );
			ReflectionUtility.NullStaticReferencesOfType( asm, typeof( ComputeShader ) );
			ReflectionUtility.NullStaticReferencesOfType( asm, typeof( Rendering.CommandList ) );
			ReflectionUtility.NullStaticReferencesOfType( asm, typeof( UI.Panel ) );
		}

		Material.Shutdown();
		TextRendering.Shutdown();
		NativeResourceCache.ClearCache();

		// Renderpipeline may hold onto native resources, clear them out
		RenderPipeline.Shutdown();

		// Destroy all cached render targets immediately — must happen before
		// GlobalContext.Shutdown() so ResourceSystem is still alive for Unregister calls.
		RenderTarget.Shutdown();

		// Drain the RenderAttributes pool — cleanup code above and GC
		// finalizers may have returned items to the pool. Clear last so
		// nothing can re-fill it.
		RenderAttributes.Pool.Clear();

		// Release all resources held by both contexts.
		GlobalContext.Menu.Shutdown();
		GlobalContext.Game.Shutdown();

		// Clear font manager cache, dispose native font handles
		FontManager.Instance.Clear( true );

		// Drain any disposables queued for end-of-frame — no more frames
		// will run during shutdown so these would otherwise leak.
		EngineLoop.DrainFrameEndDisposables();
		MainThread.RunQueues();

		// Run GC and finalizers to clear any resources held by managed
		GC.Collect();
		GC.WaitForPendingFinalizers();

		// Run the queue one more time, since some finalizers queue tasks
		EngineLoop.DrainFrameEndDisposables();
		MainThread.RunQueues();

		GC.Collect();
		GC.WaitForPendingFinalizers();

		NativeResourceCache.HandleShutdownLeaks();

		// print each scene that is leaked
		foreach ( var leakedScene in Scene.All )
		{
			log.Warning( $"Leaked scene {leakedScene.Id} during shutdown." );
		}

		// Shut the engine down (close window etc)
		NativeEngine.EngineGlobal.SourceEngineShutdown( _appSystem, false );

		if ( _appSystem.IsValid )
		{
			_appSystem.Destroy();
			_appSystem = default;
		}

		// Shut down error reporting last before unloading native DLLs,
		// so crashpad stops monitoring. Any crash before this point
		// during shutdown will still be properly reported.
		NativeErrorReporter.Shutdown();

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

		EngineFileSystem.Shutdown();
		Application.Shutdown();
	}

	protected void InitGame( AppSystemCreateInfo createInfo, string commandLine = null )
	{
		commandLine ??= System.Environment.CommandLine;
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

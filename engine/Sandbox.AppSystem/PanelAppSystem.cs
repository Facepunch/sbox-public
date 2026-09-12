using Editor;
using Sandbox.Engine;
using Sandbox.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox;

/// <summary>
/// An app whose entire UI is panel windows - the launcher. Boots the least engine that can draw
/// panels: the render device and the systems the UI render path needs, with no engine main window,
/// no sound, no physics, no game loop - and no Steam, which is what makes it start fast. The app
/// is its windows: when the last one closes, it exits.
/// </summary>
public class PanelAppSystem : AppSystem
{
	Stopwatch bootTimer;
	long lastPhaseMs;

	void Phase( string name )
	{
		var now = bootTimer.ElapsedMilliseconds;
		log.Info( $"{name} took {now - lastPhaseMs}ms (at {now}ms)" );
		lastPhaseMs = now;
	}

	public override void Init()
	{
		bootTimer = Stopwatch.StartNew();

		// Everything before this point - the runtime coming up, Main - is startup time too,
		// and it's the part nobody instruments
		try
		{
			var beforeInit = DateTime.Now - Process.GetCurrentProcess().StartTime;
			log.Info( $"Process start to Init took {beforeInit.TotalMilliseconds:0}ms" );
		}
		catch ( Exception )
		{
			// Not worth failing boot for
		}

		base.Init();
		Phase( "Interop" );

		InitManagedMinimal();
		Phase( "Managed init" );

		// The type library, the fonts and the stylesheets are all pure managed work that
		// doesn't need the engine, and the engine's own boot below is native work that
		// doesn't need them - so they run on the pool while the main thread is in there,
		// and the two halves of startup take the time of the longer one instead of the sum
		var warmUp = WarmUp();

		var createInfo = new AppSystemCreateInfo
		{
			WindowTitle = "s&box",
			Flags = AppSystemFlags.IsEditor,
		};

		_appSystem = CMaterialSystem2AppSystemDict.Create( createInfo.ToMaterialSystem2AppSystemDictCreateInfo() );
		_appSystem.SetModGameSubdir( "core" );
		_appSystem.SetInToolsMode();
		_appSystem.SetSteamAppId( (uint)Application.AppId );

		// No Steam - it costs startup time and injects the Fossilize pipeline layer into the
		// device. No VR - nothing here renders in stereo. -panelapp tells the engine nobody here
		// is drawing a scene, so the fixed buffers and atlases that exist for one are sized down
		// to what a scene panel might want instead of what a map needs.
		var commandLine = Environment.CommandLine.Replace( ".dll", ".exe" ) + " -nosteam -novr -panelapp";

		// Tools mode creates the render device but no window - our windows are our own
		if ( !NativeEngine.EngineGlobal.SourceEnginePreInit( commandLine, _appSystem ) )
		{
			throw new Exception( "SourceEnginePreInit failed" );
		}

		Phase( "SourceEnginePreInit" );

		if ( !NativeEngine.EngineGlobal.SourceEnginePanelAppInit( _appSystem ) )
		{
			throw new Exception( "SourceEnginePanelAppInit failed" );
		}

		Phase( "Scene systems" );

		warmUp.GetAwaiter().GetResult();
		Phase( "Managed warm-up join" );

		WarmRenderLayers();
		Phase( "Warm render layers" );

		OnInitialized();
		Phase( "App init" );
	}

	/// <summary>
	/// The slice of <see cref="Bootstrap.PreInit"/> an app like this needs. No Steam, no api, no
	/// menu/game/tools bootstraps, no mounting - just filesystem, logging and threading.
	/// </summary>
	void InitManagedMinimal()
	{
		Application.Initialize( dedicated: false, headless: false, toolsMode: true, testMode: false, isRetail: NativeEngine.EngineGlobal.IsRetail() );

		DLLImportResolver.SetupResolvers();
		Sandbox.Tasks.SyncContext.Init();
		ThreadSafe.MarkMainThread();

		// Same as Bootstrap.PreInit - the engine formats and parses in one culture everywhere,
		// so an app that boots its own way rather than through PreInit has to say so too
		if ( CultureInfo.CurrentCulture.Name != "en-US" )
		{
			var culture = CultureInfo.CreateSpecificCulture( "en-US" );

			// Default* covers the thread pool as well, so work that ran off the main thread comes
			// back with numbers the rest of the engine can read
			CultureInfo.DefaultThreadCurrentCulture = culture;
			CultureInfo.DefaultThreadCurrentUICulture = culture;
			Thread.CurrentThread.CurrentCulture = culture;
			Thread.CurrentThread.CurrentUICulture = culture;
		}

		ThreadPool.SetMinThreads( Environment.ProcessorCount, Environment.ProcessorCount );

		TaskScheduler.UnobservedTaskException += ( _, args ) => log.Error( args.Exception, "Unobserved task exception" );
		AppDomain.CurrentDomain.UnhandledException += ( _, args ) => log.Error( args.ExceptionObject as Exception, "AppDomain unhandled exception" );

		Diagnostics.Logging.Enabled = true;
		Diagnostics.Logging.OnException = ErrorReporter.ReportException;

		// Nothing here hot reloads - no editing, no compiling - so don't pay to watch the disk
		BaseFileSystem.WatchingEnabled = false;

		EngineFileSystem.Initialize( Environment.CurrentDirectory );
		EngineFileSystem.InitializeConfigFolder();
		EngineFileSystem.InitializeDataFolder();

		// The UI reads files - stylesheets, images - through the context's mount. Core has
		// the engine styles, and this is an editor app so the editor's assets are part of it
		Sandbox.Engine.GlobalContext.Current.FileMount = new AggregateFileSystem();
		FileSystem.Mounted.CreateAndMount( EngineFileSystem.Root, "/core/" );
		FileSystem.Mounted.CreateAndMount( EngineFileSystem.Root, "/addons/editor/assets/" );

		// Engine controls find their [StyleSheet] attributes through the type library. It's
		// filled in by WarmUp, off the main thread
		Game.TypeLibrary = new Sandbox.Internal.TypeLibrary();

		Application.TryLoadVersionInfo( Environment.CurrentDirectory );

		ErrorReporter.Initialize();
	}

	/// <summary>
	/// The managed half of startup, run on a pool thread while the main thread boots the
	/// native engine. Nothing in here may touch the engine - it isn't up yet - and nothing
	/// on the main thread touches any of this until it's joined, before <see cref="OnInitialized"/>.
	/// </summary>
	Task WarmUp()
	{
		// Three independent jobs, each on its own thread - none of them needs the others, and
		// the type library alone is longer than the native boot it's hiding behind
		return Task.WhenAll(
			Task.Run( () =>
			{
				// Reflecting over the engine assembly is the single biggest managed cost of boot
				Game.TypeLibrary.AddIntrinsicTypes();
				Game.TypeLibrary.AddAssembly( typeof( Vector3 ).Assembly, false );
				Game.TypeLibrary.AddAssembly( typeof( Sandbox.UI.Panel ).Assembly, false );
			} ),
			Task.Run( () =>
			{
				// The UI reads fonts from core content, and nothing else in this app loads them
				FontManager.Instance.LoadAll( EngineFileSystem.CoreContent );
			} ),
			Task.Run( () =>
			{
				// Parsed sheets are cached by path, so the windows find theirs ready-made. One
				// thread for all of them - the cache they go into isn't built for two
				StyleSheet.FromFile( "/styles/base/rootpanel.scss", failSilently: true );

				foreach ( var sheet in StyleSheetsToWarm )
				{
					StyleSheet.FromFile( sheet, failSilently: true );
				}
			} ),
			Task.Run( OnWarmUp ) );
	}

	/// <summary>
	/// The app's own share of the warm-up: anything it's going to need in <see cref="OnInitialized"/>
	/// that doesn't need the engine - settings to read, static state to build. Runs on a pool
	/// thread while the engine boots, and is finished before <see cref="OnInitialized"/> runs.
	/// </summary>
	protected virtual void OnWarmUp()
	{
	}

	/// <summary>
	/// Stylesheets the app's windows are going to load, parsed ahead of time off the main
	/// thread so the windows don't pay for it. Paths as <see cref="StyleSheet.FromFile"/> takes them.
	/// </summary>
	protected virtual IEnumerable<string> StyleSheetsToWarm => [];

	/// <summary>
	/// The app is up - make your windows. Runs before the first frame.
	/// </summary>
	protected virtual void OnInitialized()
	{
	}

	/// <summary>
	/// Register an app's own assembly with the type library and mount the folder it was compiled
	/// from, so its panels find their stylesheets the way an addon's do. Everything in it is
	/// exposed - it's the app's own code, not something being sandboxed, so its types don't have
	/// to be attributed to be edited or serialized.
	/// </summary>
	protected static void RegisterCompiledPanelCode( System.Reflection.Assembly assembly, string path )
	{
		Game.TypeLibrary.AddAssembly( assembly, true );
		FileSystem.Mounted.CreateAndMount( path );
	}

	/// <summary>
	/// The render pipeline's layers hold shaders and materials in static fields, and creating
	/// those asserts the main thread - but in an app like this their first touch is on a render
	/// job. Run every layer's static constructor here, where it's allowed.
	/// </summary>
	static void WarmRenderLayers()
	{
		var baseType = typeof( Sandbox.Rendering.ProceduralRenderLayer );

		foreach ( var type in baseType.Assembly.GetTypes() )
		{
			if ( !baseType.IsAssignableFrom( type ) ) continue;

			System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor( type.TypeHandle );
		}
	}

	/// <summary>
	/// Completes when the backend api client is up. Boot doesn't wait for it - anything that
	/// wants the backend (package thumbnails, news) awaits this and fills in afterwards.
	/// </summary>
	public static Task ApiReady => apiReady.Task;
	static readonly TaskCompletionSource apiReady = new();

	bool loggedFirstFrame;

	/// <summary>
	/// How long an idle frame takes, in milliseconds. A window nobody is looking at doesn't
	/// need the display's frame rate - it just has to stay responsive.
	/// </summary>
	const int IdleFrameMs = 100;

	/// <summary>
	/// Whether anything is going on that deserves the display's full frame rate - the app has
	/// the keyboard, the cursor is over one of its windows, or a window has asked to keep
	/// drawing regardless.
	/// </summary>
	static bool IsBeingUsed()
	{
		foreach ( var window in PanelWindows.All )
		{
			if ( !window.IsOpen ) continue;
			if ( window.IsFocused || window.MouseInside || window.AlwaysFullFrameRate ) return true;
		}

		return false;
	}

	protected override bool RunFrame()
	{
		var frameStart = Stopwatch.GetTimestamp();

		// The clocks the UI runs on - EngineLoop drives these in a full app, here it's on us.
		// Without them every animation and transition sits frozen at time zero
		RealTime.Update( RealTime.NowDouble );
		Time.Update( RealTime.Now, RealTime.Delta );
		Sandbox.UI.PanelRealTime.Update();

		// Input lands in the panel windows from inside this - see PanelWindowGlue::HandleEvent
		NativeEngine.EngineGlobal.SourceEnginePanelAppFrame();

		// Await continuations queue for the main thread - without this pump they'd wait forever
		Sandbox.Tasks.SyncContext.MainThread?.ProcessQueue();

		var presented = PanelWindows.FrameAll();

		// The temporaries the render pipeline borrows go back in the pool here - without this
		// every frame allocates fresh render targets until the device runs out of memory
		PanelWindows.FrameEnd();

		if ( !loggedFirstFrame && presented )
		{
			loggedFirstFrame = true;
			Phase( "First frame" );

			// The backend comes up after the window is on screen, so it never costs startup
			// time. It's an http client - no Steam, no auth needed for public reads
			_ = Task.Run( () =>
			{
				try
				{
					Api.Init();
				}
				finally
				{
					apiReady.TrySetResult();
				}
			} );
		}

		// The window's present blocks for the display, which paces us. When nothing presented -
		// everything minimized - there's nothing to pace on, so don't spin
		if ( !presented )
		{
			Thread.Sleep( 30 );
		}
		else if ( !IsBeingUsed() )
		{
			// Sitting in the background. Presenting at the display's rate the whole time costs
			// real CPU for frames nobody asked for, so idle frames are paced out to something
			// that still feels awake when the cursor comes back.
			var elapsed = (int)Stopwatch.GetElapsedTime( frameStart ).TotalMilliseconds;
			if ( elapsed < IdleFrameMs ) Thread.Sleep( IdleFrameMs - elapsed );
		}

		return PanelWindows.All.Count > 0;
	}
}

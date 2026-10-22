using NativeEngine;
using Sandbox.Network;
using Sandbox.Utility;
using Sandbox.VR;
using Sentry;
using Steamworks;
using System.Diagnostics;
using System.Threading;

namespace Sandbox.Engine;

[SkipHotload]
internal static class Bootstrap
{
	/// <summary>
	/// The github SHA of the current build
	/// </summary>
	internal static string VersionSha { get; private set; }

	private static readonly Logger log = Logging.GetLogger();

	internal static Api.Events.EventRecord StartupTiming;

	static readonly Stopwatch StartupClock = new();
	static int? timeToWindow;

	internal static void StartStartupClock()
	{
		timeToWindow = null;
		StartupClock.Restart();
	}

	internal static void MarkStartupWindowVisible()
	{
		if ( StartupClock.IsRunning )
			timeToWindow = (int)StartupClock.ElapsedMilliseconds;
	}

	internal static void MarkMenuReady()
	{
		if ( StartupClock.IsRunning )
			StartupTiming?.SetValue( "TimeToMenu", (int)StartupClock.ElapsedMilliseconds );
	}

	/// <summary>
	/// Set up application flags, the main thread and filesystems so the startup window can read its settings.
	/// </summary>
	internal static void InitApplication( CMaterialSystem2AppSystemDict appDict, string rootFolder = null )
	{
		Application.Initialize( appDict.IsDedicatedServer(), appDict.IsConsoleApp(), appDict.IsInToolsMode(), appDict.IsInTestMode(), EngineGlobal.IsRetail() );
		InitFileSystem( rootFolder ?? EngineGlobal.GetGameRootFolder() );
		DLLImportResolver.SetupResolvers();
	}

	static Task preparedTypeLibraries;
	static readonly HashSet<GlobalContext> preparedContexts = new();

	/// <summary>
	/// Builds the menu and game type libraries on a worker, so the slow reflection work overlaps native startup.
	/// <see cref="Game.InitHost"/> picks the results up when the contexts bootstrap.
	/// </summary>
	internal static void PrepareTypeLibraries()
	{
		if ( !Application.IsInitialized || Application.IsEditor || Application.IsStandalone || Application.IsHeadless ) return;
		if ( IMenuDll.Current?.GetType().Assembly is not { } menu || IGameInstanceDll.Current?.GetType().Assembly is not { } game ) return;

		preparedTypeLibraries = Task.Run( () =>
		{
			var culture = Thread.CurrentThread.CurrentCulture;
			Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.CreateSpecificCulture( "en-US" );

			try
			{
				BuildTypeLibrary( GlobalContext.Menu, menu );
				BuildTypeLibrary( GlobalContext.Game, game );
			}
			finally
			{
				GlobalContext.Current = null;
				Thread.CurrentThread.CurrentCulture = culture;
			}
		} );
	}

	static void BuildTypeLibrary( GlobalContext context, System.Reflection.Assembly assembly )
	{
		GlobalContext.Current = context;
		context.LocalAssembly = assembly;
		Game.InitTypeLibrary();

		lock ( preparedContexts ) preparedContexts.Add( context );
	}

	/// <summary>
	/// True if the current context's type library was already built by <see cref="PrepareTypeLibraries"/>.
	/// Falls back to building it normally if preparation failed.
	/// </summary>
	internal static bool ConsumePreparedTypeLibrary()
	{
		if ( preparedTypeLibraries is null ) return false;

		try
		{
			preparedTypeLibraries.GetAwaiter().GetResult();
		}
		catch ( Exception e )
		{
			log.Warning( e, "Couldn't prepare type libraries early" );
		}

		lock ( preparedContexts ) return preparedContexts.Remove( GlobalContext.Current );
	}

	[Conditional( "DEBUG" )]
	internal static void AssertTypeLibraryReady()
	{
		global::System.Diagnostics.Debug.Assert( !ThreadSafe.IsMainThread || preparedTypeLibraries is null || preparedTypeLibraries.IsCompleted,
			"The main thread accessed shared startup state before the prepared type libraries finished building." );
	}

	static Task earlyResourceScan;

	/// <summary>
	/// Scanning the resource database walks every mounted file. Start it as soon as the native file system is
	/// ready, so it overlaps the rest of startup instead of blocking the menu from loading its resources.
	/// </summary>
	internal static void BeginResourceScan()
	{
		if ( preparedTypeLibraries is null ) return;

		earlyResourceScan = Task.Run( () => g_pResourceSystem.InvalidateDatabase() );
	}

	/// <summary>
	/// The scan started by <see cref="BeginResourceScan"/>, once, or null if the caller needs to scan itself.
	/// </summary>
	internal static Task TakeResourceScan()
	{
		var scan = earlyResourceScan;
		earlyResourceScan = null;
		return scan;
	}


	/// <summary>
	/// Bootstrap managed services after the startup window has been painted.
	/// </summary>
	internal static void PreInit( Action initializeWindow = null )
	{
		try
		{
			initializeWindow?.Invoke();
			InitServices();
			Graphics.Initialize();
			GameWindow.Current?.UpdateStartupProgress( 0.1f, "Preparing files" );


			StartupTiming = new Api.Events.EventRecord( $"StartupTiming.{(Application.IsEditor ? "Editor" : (Application.IsHeadless ? "Server" : "Game"))}" );
			if ( timeToWindow is { } windowTime )
				StartupTiming.SetValue( "TimeToWindow", windowTime );

			StartupTiming.StartTimer( "Time" );

			// This also calls SetSynchronizationContext
			SyncContext.Init();

			if ( Thread.CurrentThread.CurrentCulture.Name != "en-US" )
			{
				var culture = System.Globalization.CultureInfo.CreateSpecificCulture( "en-US" );
				Thread.CurrentThread.CurrentCulture = culture;
				Thread.CurrentThread.CurrentUICulture = culture;
			}

			Logging.Enabled = true;
			Logging.OnException = ErrorReporter.ReportException;
			Logging.PrintToConsole = Application.IsHeadless;

			if ( Application.IsEditor || CommandLine.HasSwitch( "-mcp" ) )
			{
				Editor.Mcp.LogBuffer.StartCapture();
			}

			{
				using var timerFs = StartupTiming?.ScopeTimer( "FilesystemInit" );

				EngineFileSystem.InitializeDataFolder();

				if ( !Application.IsStandalone )
				{
					EngineFileSystem.InitializeDownloadsFolder();

					string assetdownloadFolder = "/assets";
					EngineFileSystem.DownloadedFiles.CreateDirectory( assetdownloadFolder );

					AssetDownloadCache.Initialize( EngineFileSystem.DownloadedFiles.GetFullPath( assetdownloadFolder ) );
				}
			}

			Api.Init();
			GameWindow.Current?.UpdateStartupProgress( 0.2f, "Loading game code" );

			if ( Application.IsStandalone )
			{
				IGameInstanceDll.Current?.Bootstrap();
			}
			else
			{
				using ( var _ = StartupTiming?.ScopeTimer( "Menu PreInit Bootstrap" ) )
				{
					IMenuDll.Current?.Bootstrap();
				}
				using ( var _ = StartupTiming?.ScopeTimer( "Game PreInit Bootstrap" ) )
				{
					IGameInstanceDll.Current?.Bootstrap();
				}
				using ( var _ = StartupTiming?.ScopeTimer( "Tools PreInit Bootstrap" ) )
				{
					IToolsDll.Current?.Bootstrap();
				}

				Mounting.Directory.LoadAssemblies();
			}

			GameWindow.Current?.UpdateStartupProgress( 0.4f, "Starting engine systems" );
		}
		catch ( Exception ex )
		{
			// Window creation can fail before services and the exception logger are initialized.
			try
			{
				ErrorReporter.Initialize();
				ErrorReporter.ReportException( ex );
				ErrorReporter.Flush();
			}
			catch { }
			Log.Error( ex );
			EngineGlobal.Plat_MessageBox( "Bootstrap::PreInit Error", $"Failed to bootstrap engine: {ex.Message}\n\n{ex.StackTrace}" );
			try { NLog.LogManager.Shutdown(); } catch { }
			EngineGlobal.Plat_ExitProcess( 1 );
		}
	}

	/// <summary>
	/// Let's native exit the C# app so AppDomain.ProcessExit gets called
	/// </summary>
	internal static void EnvironmentExit( int nCode )
	{
		// When we exit the process from C++, make sure we flush the C# Sdk
		try { ErrorReporter.Flush(); } catch { }
		try { NLog.LogManager.Shutdown(); } catch { }

		// Calling Environment.Exit would be ideal but it calls C++ global destructors which fucks everything up
		// Source 2 depends on the process just being terminated abruptly and doing no cleanup... :)
		// Environment.Exit( nCode );
	}

	/// <summary>
	/// Called on exceptions from a task (delayed, because it'll only get called when the exception gets collected)
	/// TODO: Move this somewhere else
	/// </summary>
	private static void TaskScheduler_UnobservedTaskException( object sender, UnobservedTaskExceptionEventArgs e )
	{
		var exception = e.Exception.Flatten();

		log.Error( exception );

		foreach ( var ex in exception.InnerExceptions )
		{
			log.Error( ex );
		}
	}

	/// <summary>
	/// Called to initialize the engine.
	/// </summary>
	internal static void Init()
	{
		try
		{
			Material.Preload();
			using ( var _ = StartupTiming?.ScopeTimer( "Material Preload" ) )
				Material.Preload();
			GameWindow.Current?.UpdateStartupProgress( 0.55f, "Loading engine assemblies" );

			IToolsDll.Current?.Spin();

#pragma warning disable CS0612 // Type or member is obsolete

			// Look for a command line from Steam (this is for stuff like playing from https://sbox.game/)
			if ( NativeEngine.Steam.SteamApps().IsValid )
			{
				var steamCommandLine = NativeEngine.Steam.SteamApps().GetCommandLine();
				if ( !Application.IsHeadless && !string.IsNullOrEmpty( steamCommandLine ) )
				{
					CommandLine.CommandLineString = steamCommandLine;
					CommandLine.Parse();
				}
			}

#pragma warning restore CS0612 // Type or member is obsolete

			using ( var _ = StartupTiming?.ScopeTimer( "Static Constructors" ) )
			{
				ReflectionUtility.RunAllStaticConstructors( "Sandbox.System" );
				ReflectionUtility.RunAllStaticConstructors( "Sandbox.Engine" );
				Rendering.RenderPipeline.WarmUp();
			}
			GameWindow.Current?.UpdateStartupProgress( 0.6f, "Loading built-in projects" );

			//log.Trace( "Bootstrap::Init" );
			//log.Trace( $"Current Directory is {System.IO.Directory.GetCurrentDirectory()}" );
			//log.Trace( $"RootFolder is {EngineFileSystem.RootFolder}" );
			//log.Trace( $"Command Line is {CommandLine.Full}" );

			// Add built in projects for game&tools
			// Game uses menu project but shouldn't be anything else
			// Load everything else in ToolsDll
			using ( var _ = StartupTiming?.ScopeTimer( $"BuiltIn Projects Init" ) )
			{
				SyncContext.RunBlocking( Project.InitializeBuiltIn() );
			}

			GameWindow.Current?.UpdateStartupProgress( 0.7f, "Initializing services" );

			InitEngineConVars();
			ConsoleConfig.ExecuteAutoexec();

			// After registration, or the managed half of every quality profile is dropped on the
			// floor and shadows and post-processing sit at their code defaults.
			Settings.RenderSettings.Instance.ApplyQualityProfiles();

			if ( IToolsDll.Current is not null )
			{
				using var x = StartupTiming?.ScopeTimer( $"IToolsDll Bootstrap Init" );
				SyncContext.RunBlocking( IToolsDll.Current.Initialize() );
			}

			//
			// Init vr system
			//
			VRSystem.Init();

			Screen.UpdateFromEngine();

			// Not in RenderSettings' constructor: SystemInfo is only filled in at the tail of SourceEngineInit.
			if ( Graphics.IsAvailable && !Application.IsEditor )
			{
				Settings.RenderSettings.Instance.EnsureFirstRunPreset();
			}

			// The editor's asset system compiles shaders, nobody edits core shaders on a retail build
			if ( Graphics.IsAvailable && !Application.IsEditor && !Application.IsRetail )
			{
				Sandbox.Engine.Shaders.ShaderHotload.Watch( EngineFileSystem.CoreContent );
			}

			if ( !Application.IsHeadless && !Application.IsStandalone )
			{
				using var timeout = new CancellationTokenSource( 5000 );
				SyncContext.RunBlocking( Services.Inventory.WaitForSteamInventoryItems( timeout.Token ) );
			}

			if ( !Application.IsStandalone )
			{
				// Know what's subscribed before the menu or a game asks for a package
				Services.Ugc.InitializeSubscriptions();
			}

			GameWindow.Current?.UpdateStartupProgress( 0.8f, "Loading menu" );

			if ( IMenuDll.Current is not null )
			{
				using var x = StartupTiming?.ScopeTimer( $"MenuBootstrap" );
				SyncContext.RunBlocking( IMenuDll.Current.Initialize() );
				MarkMenuReady();
			}

			GameWindow.Current?.UpdateStartupProgress( 0.9f, "Loading game" );

			if ( IGameInstanceDll.Current is not null )
			{
				using var x = StartupTiming?.ScopeTimer( $"IGameMenuDll Bootstrap" );
				SyncContext.RunBlocking( IGameInstanceDll.Current.Initialize() );
			}

			GameWindow.Current?.UpdateStartupProgress( 0.95f, "Finalizing startup" );

			if ( SteamClient.IsValid && ErrorReporter.IsUsingSentry )
			{
				SentrySdk.ConfigureScope( scope =>
				{
					scope.User = new SentryUser
					{
						Username = SteamClient.Name,
						Id = SteamClient.SteamId.ToString()
					};
				} );
			}

			Internal.TypeLibrary.OnClassName = x => StringToken.FindOrCreate( x );

			if ( IToolsDll.Current is not null )
			{
				using var x = StartupTiming?.ScopeTimer( $"Load Project" );
				SyncContext.RunBlocking( IToolsDll.Current.LoadProject() );
			}

			if ( !Application.IsHeadless )
			{
				LoadingFinished();
			}

			// Steam starts us with a friend's "connect" rich presence when joining them from Steam
			if ( CommandLine.HasSwitch( "+connect" ) )
				Api.Activity.GameRequested( new( "invite" ) );

			// Run any commands
			foreach ( var sw in CommandLine.GetSwitches() )
			{
				var cmd = ConVarSystem.Find( sw.Key );
				if ( cmd is null || !cmd.IsConCommand ) continue;

				cmd.Run( sw.Value );
			}

			if ( Application.IsEditor )
			{
				Log.Info( "Bootstrap Init Done" );
			}

			//
			// Networking bootstrap
			//
			Networking.Bootstrap();

			// Local automation uses a separate same-user pipe for every process. Games opt in;
			// editors follow the project's AI preferences, including when launched with -mcp.
			if ( Application.IsEditor
				? Project.Current?.Config.AI?.EnableMcpCommunication != false
				: CommandLine.HasSwitch( "-mcp" ) )
			{
				Editor.Mcp.McpPipeServer.Start();
			}

			if ( Application.IsJoinLocal )
			{
				Api.Activity.GameRequested( new( "local" ) );
				NetworkConsoleCommands.ConnectToServer( "local" );
			}
		}
		catch ( Exception ex )
		{
			Log.Error( ex );

			var diagnostics = string.Join( "\n", Project.GetCompileDiagnostics()?.Where( x => x.Severity > Microsoft.CodeAnalysis.DiagnosticSeverity.Warning )
				.Select( x => $"{x.Severity} | {x.GetMessage()} - {x.Location?.SourceTree?.FilePath}:{x.Location?.GetLineSpan().StartLinePosition}" ) );

			EngineGlobal.Plat_MessageBox( "Bootstrap::Init Error", $"""
				Failed to bootstrap engine.
				
				{(string.IsNullOrEmpty( diagnostics ) ? ex : diagnostics)}

				This either means that we've messed something up, or you've edited a base addon - in that case, verify your game files.
				Take a look at your Log files if you're still having problems.
				""" );
			try { NLog.LogManager.Shutdown(); } catch { }
			EngineGlobal.Plat_ExitProcess( 1 );
		}
	}

	internal static void InitMinimal( string rootFolder )
	{
		InitFileSystem( rootFolder );
		InitServices();
	}

	static void InitFileSystem( string rootFolder )
	{
		Environment.CurrentDirectory = rootFolder;
		ThreadSafe.MarkMainThread();
		EngineFileSystem.Initialize( rootFolder );
		EngineFileSystem.InitializeConfigFolder();
	}

	static void InitServices()
	{
		if ( !Application.IsDedicatedServer )
			Sandbox.Utility.Steam.InitializeClient();

		ThreadPool.SetMinThreads( Environment.ProcessorCount, Environment.ProcessorCount );

		TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
		AppDomain.CurrentDomain.UnhandledException += ( _, args ) => Log.Error( args.ExceptionObject as Exception, "AppDomain unhandled exception" );

		//System.Net.ServicePointManager.ServerCertificateValidationCallback += ( sender, cert, chain, sslPolicyErrors ) => true;

		if ( !Application.IsStandalone )
		{
			ErrorReporter.Initialize();
		}
	}

	/// <summary>
	/// Should be called when startup has finished.
	/// If we have a client, this is when the menu is first entered.
	/// </summary>
	internal static void LoadingFinished()
	{
		if ( StartupTiming != null )
		{
			StartupTiming.FinishTimer( "Time" );
			StartupTiming.SetValue( "package.ident", Application.GameIdent );
			StartupTiming.Submit( true );
			try
			{
				StartupProfiler.Write( StartupTiming );
			}
			catch ( Exception ex )
			{
				Log.Warning( ex, "Couldn't write startup profile" );
			}
		}

		if ( Application.IsBenchmark )
			BenchmarkOrchestrator.InitFromCli();
	}

	static void InitEngineConVars()
	{
		ConVarSystem.AddAssembly( typeof( Bootstrap ).Assembly, "engine" );
	}
}

using NativeEngine;
using Sandbox.Engine.Settings;
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

	/// <summary>
	/// Called before anything else. This should set up any low level stuff that
	/// might be relied on if static functions are called.
	/// </summary>
	internal static void PreInit( CMaterialSystem2AppSystemDict appDict )
	{
		Application.Initialize( appDict.IsDedicatedServer(), appDict.IsConsoleApp(), appDict.IsInToolsMode(), appDict.IsInTestMode(), EngineGlobal.IsRetail() );

		try
		{
			InitMinimal( EngineGlobal.GetGameRootFolder() );

			StartupTiming = new Api.Events.EventRecord( $"StartupTiming.{(Application.IsEditor ? "Editor" : (Application.IsHeadless ? "Server" : "Game"))}" );
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

			{
				using var timerFs = StartupTiming?.ScopeTimer( "FilesystemInit" );

				EngineFileSystem.InitializeAddonsFolder();
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
		}
		catch ( Exception ex )
		{
			Log.Error( ex );
			ErrorReporter.Flush();
			EngineGlobal.Plat_MessageBox( "Bootstrap::PreInit Error", $"Failed to bootstrap engine: {ex.Message}\n\n{ex.StackTrace}" );
			Environment.Exit( 1 );
		}
	}

	/// <summary>
	/// Let's native exit the C# app so AppDomain.ProcessExit gets called
	/// </summary>
	internal static void EnvironmentExit( int nCode )
	{
		// When we exit the process from C++, make sure we flush the C# Sdk
		ErrorReporter.Flush();

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

			ReflectionUtility.RunAllStaticConstructors( "Sandbox.System" );
			ReflectionUtility.RunAllStaticConstructors( "Sandbox.Engine" );

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

			InitEngineConVars();

			if ( IToolsDll.Current is not null )
			{
				using var x = StartupTiming?.ScopeTimer( $"IToolsDll Bootstrap Init" );
				SyncContext.RunBlocking( IToolsDll.Current.Initialize() );
			}

			//
			// Init vr system
			//
			if ( VRSystem.WantsInit )
				VRSystem.Init();

			//
			// Init common engine shit
			//
			{
				Screen.UpdateFromEngine();
				Material.UI.InitStatic();
				Gizmo.GizmoDraw.InitStatic();
				Model.InitStatic();
				Texture.InitStatic();
				CubemapRendering.InitStatic();
				Graphics.InitStatic();
			}

			if ( !Application.IsHeadless && !Application.IsStandalone )
			{
				// we really want the items available before we continue
				// here we'll wait up to 5 seconds for them, but they're
				// generally available completely immediately.
				var timeout = new CancellationTokenSource( 5000 );
				SyncContext.RunBlocking( Services.Inventory.WaitForSteamInventoryItems( timeout.Token ) );
			}

			if ( IMenuDll.Current is not null )
			{
				using var x = StartupTiming?.ScopeTimer( $"MenuBootstrap" );
				SyncContext.RunBlocking( IMenuDll.Current.Initialize() );
			}

			if ( IGameInstanceDll.Current is not null )
			{
				using var x = StartupTiming?.ScopeTimer( $"IGameMenuDll Bootstrap" );
				SyncContext.RunBlocking( IGameInstanceDll.Current.Initialize() );
			}

			if ( SteamClient.IsValid )
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

			if ( Application.IsJoinLocal )
			{
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

			Environment.Exit( 1 );
		}
	}

	internal static void InitMinimal( string rootFolder )
	{
		Environment.CurrentDirectory = rootFolder;

		Sandbox.Utility.Steam.InitializeClient();
		ThreadSafe.MarkMainThread();

		ThreadPool.SetMinThreads( Environment.ProcessorCount, Environment.ProcessorCount );

		TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
		AppDomain.CurrentDomain.UnhandledException += ( _, args ) => Log.Error( args.ExceptionObject as Exception, "AppDomain unhandled exception" );

		//System.Net.ServicePointManager.ServerCertificateValidationCallback += ( sender, cert, chain, sslPolicyErrors ) => true;

		EngineFileSystem.Initialize( Environment.CurrentDirectory );
		EngineFileSystem.InitializeConfigFolder();

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
		}

		if ( Application.IsBenchmark )
		{
			if ( !Api.IsConnected )
			{
				Log.Warning( "Not connected to backend - quitting." );
				Environment.Exit( 10 );
			}

			RenderSettings.Instance.ApplySettingsForBenchmarks();

			// Load First Benchmark package
			if ( !TryLoadNextBenchmarkPackage() )
			{
				Console.WriteLine( "Quitting" );
				ConVarSystem.Run( "quit" );
			}
		}
	}

	private readonly record struct BenchmarkPackage( string PackageName, Dictionary<string, string> GameSettings = null );

	private static int _currentBenchmarkGameIndex = 0;

	private static List<BenchmarkPackage> _benchmarkGames = new()
	{
		new BenchmarkPackage( "facepunch.benchmark" ),
		new BenchmarkPackage( "facepunch.sbdm", new Dictionary<string, string> { { "sbdm.dev.benchmark", "1" } } ),
	};

	internal static bool TryLoadNextBenchmarkPackage()
	{
		if ( _currentBenchmarkGameIndex >= _benchmarkGames.Count ) return false;

		var benchmarkGame = _benchmarkGames[_currentBenchmarkGameIndex];
		LaunchArguments.GameSettings = benchmarkGame.GameSettings;
		_ = IGameInstanceDll.Current.LoadGamePackageAsync( benchmarkGame.PackageName, GameLoadingFlags.Host, default );
		_currentBenchmarkGameIndex++;

		return true;
	}

	static void InitEngineConVars()
	{
		ConVarSystem.AddAssembly( typeof( Bootstrap ).Assembly, "engine" );
	}
}

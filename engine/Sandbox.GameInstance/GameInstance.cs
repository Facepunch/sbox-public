using Sandbox.Engine;
using Sandbox.Internal;
using Sandbox.Menu;
using Sandbox.Modals;
using Sandbox.Tasks;
using Sentry;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Sandbox;

/// <summary>
/// Holds the state of a game menu
/// </summary>
internal class GameInstance : IGameInstance
{
	public string Ident { get; set; }

	/// <summary>
	/// Whether we're joining as a client to a host that is running in an editor session.
	/// </summary>
	protected bool IsDeveloperHost => flags.Contains( GameLoadingFlags.Developer );

	public bool WantsToQuit { get; private set; }

	bool _loadingFinished = false;

	public void OnLoadingFinished()
	{
		_loadingFinished = true;

		// A joining client isn't in the game until the host activates it
		if ( flags.Contains( GameLoadingFlags.Host ) )
			Api.Activity.LoadFinished();
	}

	public bool IsLoading => !_loadingFinished;

	/// <summary>
	/// Access to the filesystem for this game. This is either going to be inside the package or the project folder.
	/// </summary>
	public BaseFileSystem GameFileSystem => activePackage?.FileSystem;

	/// <summary>
	/// Returns true if this game is from a downloaded package, rather than something running locally
	/// </summary>
	public bool IsRemote => activePackage?.Package?.IsRemote ?? false;

	protected PackageManager.ActivePackage activePackage;
	protected Package _package;
	MapReference launchedMap;

	/// <summary>
	/// Assembly that will be used set during <see cref="LoadAsync"/>.
	/// </summary>
	protected Assembly _packageAssembly;

	public Package Package => _package;

	protected GameLoadingFlags flags;

	public GameInstance( string ident, GameLoadingFlags flags )
	{
		Ident = ident;
		this.flags = flags;
	}

	/// <summary>
	/// Delete this menu, remove all traces of it
	/// </summary>
	public void Close()
	{
		// Close all modals when closing the game menu
		if ( IModalSystem.Current is not null )
		{
			using ( IMenuDll.Current?.PushScope() )
			{
				IModalSystem.Current.CloseAll();
			}
		}

		// Show review modal - not from its menu, there's been nothing to review
		if ( Application.GamePackage is not null && !IsOnMenu )
		{
			using ( IMenuDll.Current?.PushScope() )
			{
				IMenuSystem.Current?.OnPackageClosed( Application.GamePackage );
			}
		}

		// Reset the cursor
		InputRouter.ShutdownUserCursors();
		Mouse.Visibility = MouseVisibility.Auto;

		WantsToQuit = true;
	}

	/// <summary>
	/// Delete/destroy this instance. Unlike Close, this is not called from
	/// the addon gamemenu.. so we don't call gamemenu.closed (which is used
	/// to trigger a navigate to main menu.
	/// </summary>
	internal void Shutdown()
	{
		SentrySdk.AddBreadcrumb( $"Shutdown Game {Ident}", "gameinstance.shutdown" );

		if ( _packageAssembly != null )
		{
			ExpirableSynchronizationContext.ForbidPersistentTaskMethods( _packageAssembly );
		}

		_packageAssembly = null;

		// Tear down the active scene while content is still mounted so native handles remain valid during cleanup
		Game.Shutdown();

		GlobalContext.Current.UISystem.Clear();

		// Destroy resource manifests before unmounting their package or network-backed files.
		Game.Resources.Clear();

		Sandbox.Engine.Shaders.GameShaders.Shutdown();

		launchedMap?.Release();

		GameInstanceDll.Current.WorkshopItems.Clear();

		if ( activePackage != null && !Application.IsStandalone )
		{
			Game.Language?.Shutdown();
			Game.Language = null;

			FileSystem.Mounted?.UnMount( activePackage.FileSystem );

			activePackage = null;
		}

		PackageManager.UnmountTagged( "gamemenu" );

		// Is this the right place for it? Map packages are marked with "game" so they never get unmounted
		PackageManager.UnmountTagged( "game" );

		GameInstanceDll.Current.Shutdown( this );

		// If we were running a benchmark, load the next package or finish
		if ( Application.IsBenchmark || BenchmarkOrchestrator.IsRunning )
		{
			if ( !BenchmarkOrchestrator.TryLoadNextPackage() )
			{
				if ( BenchmarkOrchestrator.IsRunning )
				{
					BenchmarkOrchestrator.IsRunning = false;
					BenchmarkOrchestrator.RestoreSettings();
					Game.Overlay.ShowBenchmarkResults( BenchmarkOrchestrator.LastBatchId, BenchmarkOrchestrator.Summaries );
				}
				else
				{
					BenchmarkOrchestrator.RestoreSettings();
					Console.WriteLine( "Quitting" );
					ConVarSystem.Run( "quit" );
				}
			}
		}

		ErrorReporter.ResetCounters();
	}

	public InputSettings InputSettings => ProjectSettings.Input;

	/// <summary>
	/// Attempt to download this package and mount it as a game menu
	/// </summary>
	public virtual async Task<bool> LoadAsync( PackageLoader.Enroller enroller, CancellationToken token )
	{
		Log.Trace( $"LoadAsync: {Ident} (dev:{IsDeveloperHost})" );
		SentrySdk.AddBreadcrumb( $"Loading Game {Ident}", "gameinstance.load" );

		LoadingScreen.Title = "Fetching Package Info";
		Api.Activity.LoadStage( "fetch" );
		_package = await Package.FetchAsync( Ident, false );

		// A newer load may have started while we were fetching. Bail before we
		// touch the shared loading screen state that now belongs to it.
		if ( token.IsCancellationRequested )
			return false;

		if ( !IsDeveloperHost )
		{
			if ( Package is null )
			{
				Log.Warning( $"Package {Ident} wasn't found!" );
				return false;
			}
		}

		Application.GameIdent = Package?.FullIdent ?? Ident;
		Application.GamePackage = _package;
		Application.ExceptionCount = default;

		EngineFileSystem.ProjectSettings = new AggregateFileSystem();
		Game.Language = new LanguageContainer();

		//
		// When joining a server, we don't mind if the package is missing or bullshit
		// because they might have some assemblies that run the game.
		//
		if ( Package is null && IsDeveloperHost )
		{
			LoadProjectSettings();
			SetupFileWatch();
			return true;
		}

		if ( Package.TypeName != "game" && !Application.IsEditor )
		{
			throw new Exception( $"Package {Ident} is not a game" );
		}

		// Starts the backend fetch. The game doesn't wait for it: unlocks asked for before it lands are queued.
		_ = _package.GetAchievements();

		Log.Trace( $"Install Async {Package.Title}" );
		LoadingScreen.Title = $"Installing {Package.Title}";
		Api.Activity.LoadStage( "install" );
		LoadingScreen.Media = Package.LoadingScreen.MediaUrl;
		LoadingScreen.Package = Package;

		var identWithVersion = Package.FullIdent;

		// Make sure we install the correct revision - if we're joining a server
		// we want to download their revision, not the latest one available.
		// Package.FullIdent does not include the version, and in almost all
		// cases except this one, we don't want it to either.
		if ( Package.Revision is not null )
			identWithVersion = $"{identWithVersion}#{Package.Revision.VersionId}";

		using var loadingScreen = new MenuLoadingScreen();

		// Prefetch the game before installing its references. Steam installs Workshop games as a single item.
		if ( _package.IsRemote && !_package.InstallsFromWorkshop )
		{
			await _package.Prefetch( token, new PackageLoadOptions { Loading = loadingScreen } );
			token.ThrowIfCancellationRequested();
		}

		var downloadOptions = new PackageLoadOptions()
		{
			PackageIdent = identWithVersion,
			Package = _package,
			ContextTag = "gamemenu",
			CancellationToken = token,
			Loading = loadingScreen
		};

		activePackage = await PackageManager.InstallAsync( downloadOptions );
		LoadingScreen.CompleteDownloads();

		if ( activePackage is null )
		{
			Log.Warning( $"Package {identWithVersion} was null" );
			return false;
		}

		if ( token.IsCancellationRequested )
			return false;

		Log.Trace( $"Loading package {Package.Title}" );
		LoadingScreen.Title = $"Loading {Package.Title}";
		Api.Activity.LoadStage( "assemblies" );
		await Task.Yield(); // make frame
		token.ThrowIfCancellationRequested();

		try
		{
			// Developer sessions enroll assemblies received from the host.
			enroller.LoadPackage( Package.FullIdent, !IsDeveloperHost );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Exception when loading {Package.FullIdent}: {e.Message}" );
			return false;
		}

		var map = LaunchArguments.Map;
		if ( !string.IsNullOrWhiteSpace( map ) )
		{
			// A map picked by identifier runs as the Workshop item it's published as.
			launchedMap = await MapReference.ResolveAsync( map );
			LaunchArguments.Map = launchedMap.Text;
			Application.Map = launchedMap;
			Api.Activity.LoadStage( "map" );

			if ( !await launchedMap.LaunchAsync( flags.Contains( GameLoadingFlags.Remote ), GameFileSystem, token ) )
			{
				if ( token.IsCancellationRequested ) return false;

				// The menu stops offering a map that can't be loaded.
				using ( IMenuDll.Current?.PushScope() )
				{
					IMenuDll.Current?.RunEvent( "game.mapfailed", Package.FullIdent, map );
				}

				throw new Exception( $"Couldn't load the map '{map}'." );
			}
		}

		if ( LaunchArguments.GameSettings is not null )
		{
			foreach ( var cvar in LaunchArguments.GameSettings )
			{
				ConVarSystem.SetValue( cvar.Key, cvar.Value, LaunchArguments.CanSetProtected( cvar.Key, cvar.Value ) );
			}
		}

		LoadingScreen.Title = $"Loading Resources";
		Api.Activity.LoadStage( "resources" );
		await Task.Yield(); // make frame
		token.ThrowIfCancellationRequested();

		Log.Trace( $"All Loaded" );

		FileSystem.Mounted.Mount( activePackage.FileSystem );

		// Assets of the projects in addons/local are the host's choice, like its Workshop addons - a client joining a
		// server only gets what the server has, so its own local addons stay out
		if ( flags.Contains( GameLoadingFlags.Host ) && !Application.IsStandalone )
			FileSystem.Mounted.Mount( LocalAddons.Content );

		EngineFileSystem.ProjectSettings.Mount( activePackage.ProjectSettings );
		Game.Language.FileSystem.Mount( activePackage.Localization );
		Game.Language.Refresh();

		LoadProjectSettings();

		// Clients wait for the host's scripts before deserializing resources that may use their types.
		// Developer sessions also receive their resources through the network tables.
		if ( !IsDeveloperHost && !Networking.IsClient )
		{
			Log.Trace( $"Loading GameResources" );

			await ResourceLoader.LoadAllGameResourceAsync( FileSystem.Mounted, token );
		}

		// Scan the game and map first so each addon runs once and can override their content.
		// The host chooses the addons; joining clients install the same Workshop IDs.
		if ( flags.Contains( GameLoadingFlags.Host ) && Package.TypeName == "game" && Package.Info.AllowsAddons )
		{
			LoadingScreen.Title = "Loading Workshop Addons";
			Workshop.Item[] addons = [];
			try
			{
				addons = await Workshop.GetEnabledAddonsAsync( Application.GameIdent, token );
			}
			catch ( Exception e ) when ( e is not OperationCanceledException )
			{
				Log.Warning( e, $"Couldn't query Workshop addons: {e.Message}" );
			}

			foreach ( var addon in addons )
			{
				await GameInstanceDll.Current.WorkshopItems.LoadAsync( addon.Id, Workshop.ItemType.Addon, token );
			}
		}

		// Dedicated servers install packages that GameResources reference as they load, wait
		// for them so their resources are registered before the game starts using them
		if ( Application.IsDedicatedServer )
		{
			Log.Trace( $"Waiting for referenced packages" );
			await Game.Resources.WaitForReferencedPackagesAsync( token );
		}

		LoadingScreen.Title = $"Loading Fonts";
		Api.Activity.LoadStage( "fonts" );
		await Task.Yield(); // make frame
		token.ThrowIfCancellationRequested();

		Log.Trace( $"Loading Fonts" );
		FontManager.Instance.LoadAll( FileSystem.Mounted );

		SetupFileWatch();

		try
		{
			GameInstanceDll.Current.UpdateProjectConfig( Package );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Exception when loading {Package.FullIdent}: {e.Message}" );
			return false;
		}

		_packageAssembly = null;

		if ( Package.TryParseIdentParts( Package.FullIdent, out var ident ) )
		{
			var loaded = enroller.FindAssembly( Package, $"package.{ident.org}.{ident.package}" );
			if ( loaded is not null )
			{
				_packageAssembly = loaded.Assembly;
			}
		}

		Services.Achievements.DelayAchievementUnlocks( 5 ); // don't trigger achievements for 5 seconds

		//await LoadPackageRevisionAsync( Package.Revision );
		return true;
	}

	protected void LoadProjectSettings()
	{
		ProjectSettings.ClearCache();

		Input.ReadConfig( ProjectSettings.Input );
		Audio.Mixer.LoadFromSettings( ProjectSettings.Mixer, GlobalContext.Current.TypeLibrary );
		LoadCursors();
	}

	/// <summary>
	/// Load or reload the <see cref="CursorSettings"/> from the active project settings.
	/// </summary>
	internal static void LoadCursors()
	{
		if ( Application.IsHeadless ) return;

		InputRouter.ShutdownUserCursors();

		if ( ProjectSettings.Cursor?.Cursors is null )
			return;

		foreach ( var kv in ProjectSettings.Cursor.Cursors )
		{
			var cursor = kv.Value;
			InputRouter.CreateUserCursor( EngineFileSystem.Mounted, kv.Key, cursor.Image, (int)cursor.Hotspot.x, (int)cursor.Hotspot.y );
		}
	}

	protected void SetupFileWatch()
	{
		var watcher = FileSystem.Mounted.Watch();
		watcher.OnChanges += x =>
		{
			foreach ( var file in x.Changes )
			{
				Texture.Hotload( FileSystem.Mounted, file );
			}
		};

		// The editor's asset system compiles shaders
		if ( !Application.IsEditor && !Application.IsHeadless )
		{
			Sandbox.Engine.Shaders.ShaderHotload.Watch( FileSystem.Mounted );
		}

		var settingsWatcher = EngineFileSystem.ProjectSettings.Watch();
		settingsWatcher.OnChanges += x =>
		{
			LoadProjectSettings();
		};
	}

	public void ResetBinds()
	{
		var collection = InputBinds.FindCollection( Application.GameIdent );
		collection.ResetToDefaults();

		SaveBinds();
	}

	public void SaveBinds()
	{
		var collection = InputBinds.FindCollection( Application.GameIdent );
		collection.SaveToDisk();
	}

	public string GetBind( string actionName, out bool isDefault, out bool isCommon )
	{
		var commonBind = InputBinds.FindCollection( "common" ).GetBind( actionName, false );
		var commonValue = InputBinds.FindCollection( "common" ).Get( actionName, 0 );

		var collection = InputBinds.FindCollection( Application.GameIdent );

		isCommon = false;

		var bind = collection.GetBind( actionName );
		var value = collection.Get( actionName, 0 );


		if ( commonBind != null && (string.IsNullOrEmpty( value ) || value == commonValue) )
		{
			isCommon = true;
			value = commonValue;
		}
		else if ( string.IsNullOrWhiteSpace( value ) )
		{
			value = bind.Default;
		}

		isDefault = bind.Default == value;

		return value;
	}

	public void SetBind( string actionName, string buttonName )
	{
		var collection = InputBinds.FindCollection( Application.GameIdent );
		collection.Set( actionName, 0, buttonName );
	}

	/// <summary>
	/// For binding reasons, get a list of buttons that are currently pressed
	/// </summary>
	public void TrapButtons( Action<string[]> callback )
	{
		Game.InputContext.StartTrapping( callback );
	}

	public Scene Scene => Game.ActiveScene;

	/// <summary>
	/// The game's own menu scene - null for a game without one
	/// </summary>
	string MenuScene => Package?.GetMeta<string>( "MenuScene", null ) is { Length: > 0 } scene ? scene : null;

	bool IsMenuScene( GameResource scene ) => scene is SceneFile && MenuScene is { } menuScene
		&& string.Equals( Resource.FixPath( menuScene ), Resource.FixPath( scene.ResourcePath ), StringComparison.OrdinalIgnoreCase );

	/// <summary>
	/// The scene it's on is its menu scene. The game's own scene, whoever's asking - the s&amp;box menu has one of its own.
	/// </summary>
	public bool IsOnMenu => !Application.IsDedicatedServer && IsMenuScene( GlobalContext.Game.ActiveScene?.Source );

	public bool ReturnToMenu()
	{
		ThreadSafe.AssertIsMainThread();

		// Joined someone else's game - the code loaded is theirs. Or it has no menu to go back to.
		if ( flags.Contains( GameLoadingFlags.Remote ) || Application.IsDedicatedServer || MenuScene is null )
			return false;

		using var scope = GlobalContext.GameScope();

		// Out of the session - handed to someone else if there's anyone to take it
		if ( Networking.IsActive )
			Networking.Disconnect();

		return Game.ChangeScene( MenuScene );
	}

	public bool OnSceneChange( SceneFile scene )
	{
		// A game joined is its server's to move between scenes
		if ( Application.IsDedicatedServer || IsDeveloperHost ) return false;
		if ( flags.Contains( GameLoadingFlags.Remote ) ) return false;
		if ( Networking.IsActive && !Networking.IsHost ) return false;

		if ( IsMenuScene( scene ) )
		{
			// Back on its menu - nobody comes along
			if ( Networking.IsActive )
				Networking.Disconnect();

			return false;
		}

		return IsOnMenu;
	}

	string GetStartupScene()
	{
		var gameScene = Package.GetMeta<string>( "StartupScene", "start.scene" );
		if ( launchedMap?.Asset is { } asset )
		{
			if ( asset.EndsWith( ".scene", StringComparison.OrdinalIgnoreCase ) ) return asset;
			return Package.GetMeta<string>( "MapStartupScene", null ) ?? gameScene;
		}

		var dedicatedScene = Package.GetMeta<string>( "DedicatedServerStartupScene", null );
		return Application.IsDedicatedServer && !string.IsNullOrWhiteSpace( dedicatedScene ) ? dedicatedScene : gameScene;
	}

	/// <summary>
	/// Open the game's menu, selected map or configured startup scene and run its initialization events.
	/// </summary>
	public bool OpenStartupScene()
	{
		Game.ActiveScene?.Destroy();
		Game.ActiveScene = null;
		Game.ActiveScene = new Scene();

		if ( IsDeveloperHost || (Application.IsEditor && !Game.IsPlaying) ) return true;

		var menu = flags.Contains( GameLoadingFlags.Menu ) && !Application.IsDedicatedServer ? MenuScene : null;
		var startupScene = menu ?? GetStartupScene();
		if ( string.IsNullOrWhiteSpace( startupScene ) )
		{
			Log.Warning( "Startup Scene was not defined - can't start!" );
			return false;
		}

		// Startup callbacks can load additional scenes into this one.
		var options = new SceneLoadOptions { IsAdditive = true };
		if ( !options.SetScene( startupScene ) ) return false;

		// Additive loading leaves Source unset. The menu needs it for Game.IsOnMenu during startup.
		if ( menu is not null ) Game.ActiveScene.Source = options.GetSceneFile();

		Game.ActiveScene.RunEvent<ISceneStartup>( x => x.OnHostPreInitialize( options.GetSceneFile() ) );
		if ( !Game.ActiveScene.Load( options ) ) return false;

		Game.ActiveScene.AddSystemScene();
		Game.ActiveScene.RunEvent<ISceneStartup>( x => x.OnHostInitialize() );
		if ( !Application.IsDedicatedServer ) Game.ActiveScene.RunEvent<ISceneStartup>( x => x.OnClientInitialize() );

		return true;
	}
}

class MenuLoadingScreen : ILoadingInterface
{
	public void Dispose()
	{
		LoadingScreen.Subtitle = "";
		LoadingScreen.Progress = null;
	}

	public void LoadingProgress( LoadingProgress progress )
	{
		LoadingScreen.Progress = progress;
		LoadingScreen.Title = $"{progress.Title}";
		LoadingScreen.Subtitle = progress.Mbps > 0
			? $"{progress.Percent:n0}% • {progress.Mbps:n0}mbps • {progress.CalculateETA().ToRemainingTimeString()}"
			: "";
	}
}

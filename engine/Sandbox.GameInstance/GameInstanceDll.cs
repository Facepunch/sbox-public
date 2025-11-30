using Microsoft.CodeAnalysis.CSharp;
using Sandbox.ActionGraphs;
using Sandbox.Audio;
using Sandbox.Diagnostics;
using Sandbox.Internal;
using Sandbox.UI;
using Sandbox.Utility;
using Sandbox.VR;
using System;
using System.IO;

namespace Sandbox;

internal partial class GameInstanceDll : Engine.IGameInstanceDll
{
	public static GameInstanceDll Current { get; private set; }

	public static GameInstance gameInstance;

	public static PackageLoader PackageLoader { get; set; }
	PackageLoader.Enroller AssemblyEnroller { get; set; }

	private bool _isAssemblyLoadingPaused;

	public void Bootstrap()
	{
		Current = this;

		GlobalContext.Current.Reset();
		GlobalContext.Current.LocalAssembly = GetType().Assembly;

		Game.InitHost();

		SetupInputContext();

		JsonUpgrader.UpdateUpgraders( TypeLibrary );

		Sandbox.Generator.Processor.DefaultPackageAssetResolver = ResolvePackageAsset;

		PackageManager.OnPackageInstalledToContext += OnPackageInstalled;

		//
		// Files accessible by game instance
		//
		GlobalContext.Current.FileMount = new AggregateFileSystem();
		{
			if ( Application.IsEditor )
			{
				// If we're in the editor, also mount the cloud folder, which is where we 
				// download resources and assets from sbox.game to.
				FileSystem.Mounted.Mount( EngineFileSystem.LibraryContent );
			}

			if ( Application.IsStandalone )
			{
				// In standalone, we don't ship code - only assets
				FileSystem.Mounted.CreateAndMount( EngineFileSystem.Addons, $"/base/assets" );
				FileSystem.Mounted.CreateAndMount( EngineFileSystem.Root, "/core/" );
			}
			else
			{
				FileSystem.Mounted.CreateAndMount( EngineFileSystem.Addons, "/base/assets/" );
				FileSystem.Mounted.CreateAndMount( EngineFileSystem.Addons, "/base/code/" );
				FileSystem.Mounted.CreateAndMount( EngineFileSystem.Root, "/core/" );
				FileSystem.Mounted.CreateAndMount( EngineFileSystem.Addons, "/citizen/assets/" );
			}
		}

		Assert.IsNull( PackageLoader );
		PackageLoader = new PackageLoader( "GameMenu", typeof( GameInstanceDll ).Assembly );
		PackageLoader.HotloadWatch( Game.GameAssembly ); // Sandbox.Game is per instance
		PackageLoader.OnAfterHotload = OnAfterHotload;

		{
			ConVarSystem.AddAssembly( GetType().Assembly, "game" );
			ConVarSystem.AddAssembly( Game.GameAssembly, "game" );
		}
	}

	public Task Initialize()
	{
		ResetEnvironment();
		Networking.StartThread();

		return Task.CompletedTask;
	}

	public void Exiting()
	{
		Networking.Disconnect();
		Networking.StopThread();

		Event.Run( "app.exit" );
		Game.Cookies?.Save();
	}

	static int Counter;

	void OnAfterHotload()
	{
		GlobalContext.Current.OnHotload();
		Game.ActiveScene?.OnHotload();
		Event.Run( "hotloaded" );
	}

	/// <summary>
	/// Called from the code generator. The game package should already contain this package's content
	/// so we just need to work out where it's meant to point to
	/// </summary>
	private string ResolvePackageAsset( string packageName )
	{
		Package package = Package.FetchAsync( packageName, false ).Result;
		return package?.PrimaryAsset;
	}

	/// <summary>
	/// This should reset our environment to a clean state.
	/// </summary>
	public void ResetEnvironment()
	{
		Log.Trace( "Game Menu - ResetEnvironment" );

		// Use a new package loader for every game if we're not in editor
		// The editor is only going to load 1 game and ToolsDll has a reference to it
		if ( !PackageLoader.ToolsMode )
		{
			PackageLoader?.Dispose();
			PackageLoader = null;

			PackageLoader = new PackageLoader( "GameMenu", typeof( GameInstanceDll ).Assembly );
			PackageLoader.HotloadWatch( Game.GameAssembly ); // Sandbox.Game is per instance
			PackageLoader.OnAfterHotload = OnAfterHotload;
		}

		if ( DidMountNetworkedFiles )
		{
			EngineFileSystem.Mounted.UnMount( NetworkedSmallFiles.Files );
			EngineFileSystem.ProjectSettings.UnMount( NetworkedConfigFiles.Files );
			DidMountNetworkedFiles = false;
		}

		FontManager.Instance.Reset();
		FontManager.Instance.LoadAll( FileSystem.Mounted );

		AssemblyEnroller?.Dispose();
		AssemblyEnroller = null;

		CodeArchiveTable.Reset();
		NetworkedSmallFiles.Reset();
		NetworkedConfigFiles.Reset();
		NetworkedLargeFiles.Reset();
		ReplicatedConvars.Reset();
		ServerPackages.Clear();

		FileWatchers.ForEach( w => w.Dispose() );
		FileWatchers.Clear();

		Screen.UpdateFromEngine();

		Game.InitTypeLibrary();

		UserPermission.Load();

		Input.ReadConfig( null );
		StyleSheet.InitStyleSheets();
		Networking.Reset();
		Connection.Reset();
		GlobalContext.Current.Reset();
		NativeResourceCache.Clear();
		Speech.Recognition.Reset();
		Json.Initialize();
		VRSystem.Reset();

		if ( !Application.IsEditor )
		{
			Mixer.ResetToDefault();
		}

		Sound.Clear();
		Application.ClearGame();

		ReflectionQueryCache.ClearTypeCache();

		EngineFileSystem.ProjectSettings?.Dispose();
		EngineFileSystem.ProjectSettings = null;

		ProjectSettings.ClearCache();
		ErrorReporter.ResetCounters();

		AssemblyEnroller = PackageLoader.CreateEnroller( $"gamedll{Counter++}" );

		AssemblyEnroller.OnAssemblyAdded += ( a ) =>
		{
			Assert.NotNull( a.Assembly );

			if ( a.IsEditorAssembly )
				return;

			Game.TypeLibrary.AddAssembly( a.Assembly, true );
			Game.NodeLibrary.AddAssembly( a.Assembly );
			ConVarSystem.AddAssembly( a.Assembly, "game" );
			Cloud.UpdateTypes( a.Assembly );
			Json.Initialize();
			JsonUpgrader.UpdateUpgraders( TypeLibrary );

			if ( !a.IsEditorAssembly && a.CodeArchiveBytes is not null )
			{
				AddArchiveToCodeArchiveTable( a );
			}

			ReplicatedConvars.OnAssembliesLoaded();
		};

		AssemblyEnroller.OnAssemblyRemoved += ( a ) =>
		{
			if ( a.IsEditorAssembly )
				return;

			Assert.NotNull( a.Assembly );

			Game.NodeLibrary.RemoveAssembly( a.Assembly );
			Game.TypeLibrary.RemoveAssembly( a.Assembly );
			ConVarSystem.RemoveAssembly( a.Assembly );
			JsonUpgrader.UpdateUpgraders( TypeLibrary );

			CodeArchiveTable.Remove( a.Name );
		};

		AssemblyEnroller.OnAssemblyFastHotload += ( a ) =>
		{
			if ( !a.IsEditorAssembly && a.CodeArchiveBytes is not null )
			{
				AddArchiveToCodeArchiveTable( a );
			}
		};

		// Clear resource library so resources don't leak between games,
		// then let IMenuDll reload whatever resources it needs

		Game.Resources.Clear();
		IMenuDll.Current?.Reset();
	}

	/// <summary>
	/// This method grabs the currently loaded assembly's code archive, strips serverside code and spits it back out into the 
	/// CodeArchiveTable.
	/// </summary>
	/// <param name="a"></param>
	private void AddArchiveToCodeArchiveTable( LoadedAssembly a )
	{
		if ( a?.CodeArchiveBytes is null )
			return;

		//
		// If we're not a dedicated server OR this isn't a game assembly,
		// just use the original archive bytes
		//
		if ( !Application.IsDedicatedServer || !a.IsGame )
		{
			CodeArchiveTable.Set( a.Name, a.CodeArchiveBytes );
			return;
		}

		//
		// Find matching compiler
		//
		var compiler = Project.All
			.Select( x => x.Compiler )
			.FirstOrDefault( x => x is not null && x.AssemblyName.Equals( a.Name ) );

		if ( compiler?.Output?.Archive is null )
		{
			CodeArchiveTable.Set( a.Name, a.CodeArchiveBytes );
			return;
		}

		var config = compiler.GetConfiguration();

		//
		// Strip the SERVER define from the archive's DefineConstants
		//
		var parts = config.DefineConstants.Split( ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ).ToList();
		parts.RemoveAll( x => x.Equals( "SERVER", StringComparison.OrdinalIgnoreCase ) );

		var newConfig = config with { DefineConstants = string.Join( ";", parts ) };

		var strippedArchive = new CodeArchive
		{
			CompilerName = compiler.Name,
			Configuration = newConfig
		};

		foreach ( var tree in compiler.Output.Archive.SyntaxTrees )
		{
			var text = tree.GetText().ToString();
			var newTree = CSharpSyntaxTree.ParseText( text, path: tree.FilePath, encoding: System.Text.Encoding.UTF8, options: newConfig.GetParseOptions() );

			//
			// Make sure we strip any disabled text trivia from the tree
			//
			newTree = Compiler.StripDisabledTextTrivia( newTree );
			strippedArchive.SyntaxTrees.Add( newTree );
		}

		//
		// Need to maintain all the extra stuff in the original archive
		//
		foreach ( var f in compiler.Output.Archive.AdditionalFiles )
		{
			strippedArchive.AdditionalFiles.Add( f );
		}

		foreach ( var kv in compiler.Output.Archive.FileMap )
		{
			strippedArchive.FileMap[kv.Key] = kv.Value;
		}

		foreach ( var r in compiler.Output.Archive.References )
		{
			strippedArchive.References.Add( r );
		}

		var bytes = strippedArchive.Serialize();
		CodeArchiveTable.Set( a.Name, bytes );
	}

	/// <summary>
	/// Don't attempt to load new assemblies during <see cref="Tick"/> inside this scope.
	/// </summary>
	private DisposeAction PauseLoadingAssemblies()
	{
		if ( _isAssemblyLoadingPaused )
		{
			// Don't unpause after this scope if we're already paused

			return default;
		}

		_isAssemblyLoadingPaused = true;

		return new DisposeAction( () =>
		{
			_isAssemblyLoadingPaused = false;
		} );
	}

	public void FinishLoadingAssemblies()
	{
		PackageLoader.Tick();

		// we send table updates right after packageloader has run
		// so that any further messages will be read with the same
		// assemblies
		Networking.System?.SendTableUpdates();
	}

	public void CloseGame()
	{
		if ( gameInstance is null ) return;

		ConVarSystem.SaveAll();

		// Scope disconnect so we can shutdown game before disconnect and stop game objects from sending network destroy,
		// orphaned action should take care of it.
		using ( Networking.DisconnectScope() )
		{
			gameInstance.Shutdown();
			gameInstance = null;
			IGameInstance.Current = null;
		}

		Application.ClearGame();

		LoadingScreen.IsVisible = false;
		LoadingScreen.Media = null;

		Sound.StopAll( 0.2f );

		ResetEnvironment();
		Mounting.MountUtility.TickPreviewRenders();
	}

	internal Input.Context _perFrameInput = Input.Context.Create( "ClientPerFrame" );

	public void Tick()
	{
		var scene = Game.ActiveScene;

		using var sceneScope = scene?.Push();

		if ( scene is not null )
		{
			// Update the time now that we're in the scene scope
			scene.UpdateTime( RealTime.Delta );

			// If we're a client then advance server time
			scene.SyncServerTime();

			// push this time scope now that the scene has updated time
			Time.Update( scene.TimeNow, scene.TimeDelta );
		}

		if ( gameInstance?.WantsToQuit ?? false )
		{
			CloseGame();
		}
		else
		{
			//
			// Update input
			//
			{
				_perFrameInput.Flip();
				_perFrameInput.Push();
				Input.Process();
			}

			//
			// Recieve incoming network messages, send heartbeat and other outgoing messages
			//
			Networking.PreFrameTick();

			//
			// We may have got new assemblies in the network update,
			// so finish loading them now before running updates
			//
			if ( !_isAssemblyLoadingPaused )
			{
				FinishLoadingAssemblies();
			}

			//
			// Run the actual game scene tick
			//
			using ( Sandbox.Diagnostics.Performance.Scope( "GameFrame" ) )
			{
				RunGameFrame( scene );
			}

			Networking.PostFrameTick();
		}

		if ( !Application.IsDedicatedServer )
		{
			ActionGraphDebugger.Tick();
			RichPresenceSystem.Tick();
			Services.Achievements.Tick();
		}

		// Advance per frame scene metrics
		TickSceneStats( scene );

		Analytics.Tick();

		// Run any pending queue'd mainthread tasks here
		// so they're in the same scene scope
		MainThread.RunQueues();
	}

	private void RunGameFrame( Scene activeScene )
	{
		if ( !Game.IsPlaying ) return;
		if ( activeScene is null ) return;
		if ( Networking.IsConnecting ) return;

		LoadingScreen.IsVisible = activeScene.IsLoading;

		activeScene.GameTick( 0 ); // we already advanced time 

		// Run any pending queue'd mainthread tasks here
		// so they're in the same scene scope
		MainThread.RunQueues();
	}

	public void SimulateUI()
	{
		bool mouseIsAllowed = true;
		if ( IMenuDll.Current is not null )
		{
			mouseIsAllowed = !IMenuDll.Current.HasOverlayMouseInput();
		}

		using ( Game.ActiveScene?.Push() )
		{
			Game.Language?.Tick();
			GlobalContext.Current.UISystem.Simulate( mouseIsAllowed );

			Game.ActiveScene?.ProcessDeletes();
		}
	}

	public void ClosePopups( object panelClickedOn )
	{
		BasePopup.CloseAll( panelClickedOn as Panel );
	}

	public void Disconnect()
	{
		Game.Close();
	}

	/// <summary>
	/// Loads the game asynchronously
	/// </summary>
	public async Task LoadGamePackageAsync( string ident, GameLoadingFlags flags, CancellationToken ct )
	{
		try
		{
			ThreadSafe.AssertIsMainThread();
			await LoadGamePackageAsyncInternal( ident, flags, ct );
		}
		catch ( System.Exception e )
		{
			LoadingScreen.IsVisible = false;
			LoadingScreen.Media = null;

			using ( IMenuDll.Current?.PushScope() )
			{
				IMenuSystem.Current?.Popup( "error", "Loading Error", $"There was an error when loading this game. {e.Message}" );
			}

			Log.Warning( e, e.Message );
		}
	}

	public async Task LoadGamePackageAsyncInternal( string ident, GameLoadingFlags flags, CancellationToken ct )
	{
		//
		// We might not need to reload if this is the same package.
		// Bit of extra dancing here because we want #local to be treated as released
		//
		if ( gameInstance is not null && !flags.Contains( GameLoadingFlags.Reload ) )
		{
			Package.TryParseIdent( ident, out var iparts );
			Package.TryParseIdent( gameInstance?.Ident, out var oparts );

			// No need to recreate - this is the same package (and the same version)
			if ( iparts.package == oparts.package && iparts.org == oparts.org && iparts.version == oparts.version )
				return;
		}

		gameInstance?.Shutdown();

		//
		// If this isn't part of a remote connection, leave any active network session
		//
		if ( !flags.Contains( GameLoadingFlags.Remote ) )
		{
			Networking.Disconnect();
		}

		Application.ClearGame();

		if ( !Application.IsDedicatedServer && !Application.IsStandalone )
		{
			// Get the stats ready
			var s = Sandbox.Services.Stats.GetGlobalStats( ident );
			if ( !s.IsRefreshing )
			{
				_ = s.Refresh();
			}

			// get the local stats too
			var playerStats = Sandbox.Services.Stats.GetLocalPlayerStats( ident );
			if ( !playerStats.IsRefreshing )
			{
				_ = playerStats.Refresh();
			}

			await Task.Delay( 10 );
			LoadingScreen.Title ??= "Loading..";
		}

		GameInstance newInstance = default;

		try
		{
			if ( Application.IsStandalone )
			{
				newInstance = new StandaloneGameInstance( ident, flags );
			}
			else
			{
				newInstance = new GameInstance( ident, flags );
			}

			ResetEnvironment();

			NativeErrorReporter.Breadcrumb( true, "game", $"Loading game package {ident}" );
			NativeErrorReporter.SetTag( "game", ident );
			NativeErrorReporter.SetTag( "map", LaunchArguments.Map );

			if ( !Application.IsDedicatedServer && !Application.IsStandalone )
			{
				await Task.Delay( 10 );
			}

			if ( !await newInstance.LoadAsync( AssemblyEnroller, ct ) )
			{
				ResetEnvironment();
				newInstance.Close();
				newInstance.Shutdown();
				newInstance = default;

				throw new System.Exception( "Loading failed." );
			}

			if ( ct.IsCancellationRequested )
				return;

			await Task.Delay( 10 );
			GC.Collect( GC.MaxGeneration, GCCollectionMode.Optimized, false, false );
			await Task.Delay( 10 );

			if ( Package.TryParseIdent( ident, out var parsed ) )
			{
				EngineFileSystem.Data.CreateDirectory( parsed.org );

				var package = parsed.local ? $"{parsed.package}#local" : parsed.package;

				GlobalContext.Current.FileOrg = EngineFileSystem.Data.CreateSubSystem( parsed.org );
				GlobalContext.Current.FileOrg.CreateDirectory( package );

				GlobalContext.Current.FileData = FileSystem.OrganizationData.CreateSubSystem( package );
			}
			else
			{
				EngineFileSystem.Data.CreateDirectory( ".local" );

				GlobalContext.Current.FileOrg = EngineFileSystem.Data.CreateSubSystem( ".local" );
				GlobalContext.Current.FileOrg.CreateDirectory( ident );

				GlobalContext.Current.FileData = FileSystem.OrganizationData.CreateSubSystem( ident );
			}

			Game.Cookies = new CookieContainer( "cookies", false, GlobalContext.Current.FileData );

			IGameInstance.Current = newInstance;
			gameInstance = newInstance;
			newInstance = default;

			//
			// Boot up
			//
			if ( flags.Contains( GameLoadingFlags.Host ) )
			{
				if ( !gameInstance.OpenStartupScene() )
				{
					ResetEnvironment();
					LoadingScreen.IsVisible = false;
					LoadingScreen.Media = null;
					return;
				}
			}

			if ( Application.IsEditor )
			{
				Game.CheatsEnabled = true; // auto-enable cheats in the editor
			}

			//
			// If we're the game, start it straight away.
			// In editor we're going to start in editor mode
			//
			if ( !Application.IsEditor )
			{
				Game.IsPlaying = true;
			}
		}
		finally
		{
			// Loading failed
			if ( newInstance is not null )
			{
				newInstance.Close();
				newInstance.Shutdown();
				newInstance = default;
			}
		}
	}

	private void OnPackageInstalled( PackageManager.ActivePackage package, string context )
	{
		Log.Trace( $"OnPackageInstalled: {package.Package.FullIdent} {context}" );

		// only load if a game context (tools can install packages)
		if ( context != "game" ) return;

		// Load all the GameResources and fonts in the package
		if ( package.FileSystem is not null )
		{
			ResourceLoader.LoadAllGameResource( package.FileSystem );
			FontManager.Instance.LoadAll( package.FileSystem );
		}
	}

	/// <summary>
	/// Called when the game menu is closed
	/// </summary>
	/// <param name="instance"></param>
	public void OnGameInstanceClosed( IGameInstance instance )
	{
		NativeErrorReporter.Breadcrumb( true, "game", "Closed game instance" );
		NativeErrorReporter.SetTag( "game", null );
		NativeErrorReporter.SetTag( "map", null );

		if ( gameInstance == instance ) gameInstance = null;
		if ( IGameInstance.Current == instance ) IGameInstance.Current = null;
	}

	public void OnRender( SwapChainHandle_t swapChain )
	{
		Game.Render( swapChain );
	}

	/// <summary>
	/// The play button was pressed in the editor
	/// </summary>
	public void EditorPlay()
	{
		if ( gameInstance is null )
		{
			Log.Warning( "Tried to editor play but we don't have a game instance" );
			return;
		}

		Game.IsPlaying = true;

		if ( !gameInstance.OpenStartupScene() )
		{
			Log.Warning( "There was a problem opening the StartupScene" );
			return;
		}
	}

	public TypeLibrary TypeLibrary => Sandbox.Internal.GlobalGameNamespace.TypeLibrary;

	/// <summary>
	/// Pushes the game scope. This will push the active scene and the right time.
	/// </summary>
	public IDisposable PushScope()
	{
		return Game.ActiveScene?.Push();
	}

	/// <summary>
	/// Called per frame to add scene's stats to our analytics
	/// </summary>
	void TickSceneStats( Scene scene )
	{
		var sceneValid = scene.IsValid();
		Api.Performance.CollectStat( "GameObjectCount", sceneValid ? scene.Directory.GameObjectCount : 0 );
		Api.Performance.CollectStat( "ComponentCount", sceneValid ? scene.Directory.ComponentCount : 0 );
		Api.Performance.CollectStat( "RootGameObjects", sceneValid ? scene.Children.Count : 0 );
		Api.Performance.CollectStat( "CameraCount", sceneValid ? scene.GetAllComponents<CameraComponent>().Count() : 0 );
		Api.Performance.CollectStat( "ColliderCount", sceneValid ? scene.PhysicsWorld.Bodies.Count() : 0 );
		Api.Performance.CollectStat( "DynamicBodyCount", sceneValid ? scene.PhysicsWorld.Bodies.Where( x => x.BodyType == PhysicsBodyType.Dynamic ).Count() : 0 );
		Api.Performance.CollectStat( "KeyframeBodyCount", sceneValid ? scene.PhysicsWorld.Bodies.Where( x => x.BodyType == PhysicsBodyType.Keyframed ).Count() : 0 );
		Api.Performance.CollectStat( "StaticBodyCount", sceneValid ? scene.PhysicsWorld.Bodies.Where( x => x.BodyType == PhysicsBodyType.Static ).Count() : 0 );
		Api.Performance.CollectStat( "Particles", sceneValid ? scene.GetAllComponents<ParticleEffect>().Sum( x => x.Particles.Count ) : 0 );

		Api.Performance.CollectStat( "GameObjectsDestroyed", SceneMetrics.GameObjectsDestroyed );
		Api.Performance.CollectStat( "ParticlesCreated", SceneMetrics.ParticlesCreated );
		Api.Performance.CollectStat( "ParticlesDestroyed", SceneMetrics.ParticlesDestroyed );
		Api.Performance.CollectStat( "GameObjectsCreated", SceneMetrics.GameObjectsCreated );
		Api.Performance.CollectStat( "GameObjectsDestroyed", SceneMetrics.GameObjectsDestroyed );
		Api.Performance.CollectStat( "ComponentsCreated", SceneMetrics.ComponentsCreated );
		Api.Performance.CollectStat( "ComponentsDestroyed", SceneMetrics.ComponentsDestroyed );
		Api.Performance.CollectStat( "RayTrace", SceneMetrics.RayTrace );
		Api.Performance.CollectStat( "RayTraceAll", SceneMetrics.RayTraceAll );

		SceneMetrics.Flip();
	}

	void IGameInstanceDll.ResetSceneListenerMetrics()
	{
		Game.ActiveScene.ResetListenerMetrics();
	}

	object IGameInstanceDll.GetSceneListenerMetrics()
	{
		return Game.ActiveScene.GetListenerMetrics();
	}

	[ConCmd( "game", ConVarFlags.Protected, Help = "Play a game" )]
	public static async Task StartGame( string gameIdent, string mapIdent = null )
	{
		// We don't want to open games in the editor
		if ( Application.IsEditor )
			return;

		// We can load and run projects if we're a Dedicated Server.
		if ( Application.IsDedicatedServer && gameIdent.ToLower().Contains( ".sbproj" ) )
		{
			await Project.InitializeBuiltIn( false );

			var project = Project.AddFromFile( gameIdent );

			NativeEngine.FullFileSystem.AddProjectPath( gameIdent, project.GetAssetsPath().ToLowerInvariant() );

			var libraries = Path.Combine( project.RootDirectory.FullName, "Libraries" );

			// We should iterate all available libraries and add their projects
			foreach ( var folder in Directory.EnumerateDirectories( libraries ) )
			{
				var configs = Directory.EnumerateFiles( folder, "*.sbproj" ).ToArray();
				if ( configs.Length != 1 ) continue;
				Project.AddFromFile( configs[0] );
			}

			// We need to reload the project since we may have had a bunch of libraries added
			project.Load();

			await Project.CompileAsync();

			if ( !project.Active )
			{
				Log.Error( $"Unable to load {gameIdent}" );
				return;
			}

			gameIdent = project.Package.FullIdent;
		}

		Log.Info( $"Loading game '{gameIdent}'" );

		LaunchArguments.Map = mapIdent;

		if ( LaunchArguments.Map is not null )
		{
			Log.Info( $" with map: '{LaunchArguments.Map}'" );
		}

		await IGameInstanceDll.Current.LoadGamePackageAsync( gameIdent, GameLoadingFlags.Host, default );
		Log.Info( $"Load Complete" );
	}

	public static void Create()
	{
		IGameInstanceDll.Current = new GameInstanceDll();

		// PreJIT the methods in these dlls to avoid doing it during the game
		{
			var e = new Api.Events.EventRecord( "PreJIT.Game" );

			using ( e.ScopeTimer( "Sandbox.GameInstance" ) )
			{
				Sandbox.ReflectionUtility.PreJIT( typeof( GameInstanceDll ).Assembly );
			}

			using ( e.ScopeTimer( "Sandbox.System" ) )
			{
				Sandbox.ReflectionUtility.PreJIT( typeof( Vector3 ).Assembly );
			}

			using ( e.ScopeTimer( "Sandbox.Engine" ) )
			{
				Sandbox.ReflectionUtility.PreJIT( typeof( Bootstrap ).Assembly );
			}

			e.Submit();
		}
	}

	/// <summary>
	/// Try to get the replicated var value from the host
	/// </summary>
	public bool TryGetReplicatedVarValue( string name, out string value )
	{
		value = default;

		if ( !Networking.IsActive ) return false;
		if ( Networking.IsHost ) return false;

		return ReplicatedConvars.TryGetValue( name, out value );
	}

	public InputContext InputContext { get; private set; }

	internal void SetupInputContext()
	{
		var uiSystem = new UISystem();

		var input = new InputContext();
		input.Name = GetType().Name;
		input.TargetUISystem = uiSystem;
		input.OnGameMouseWheel += Sandbox.Input.AddMouseWheel;
		input.OnMouseMotion += Sandbox.Input.AddMouseMovement;
		input.OnGameButton += Input.OnButton;

		InputContext = input;

		GlobalContext.Current.UISystem = uiSystem;
		GlobalContext.Current.InputContext = input;
	}

	/// <summary>
	/// Load the assemblies from this package into the current game instance
	/// </summary>
	public Task LoadPackageAssembliesAsync( Package package )
	{
		AssemblyEnroller.LoadPackage( package.FullIdent, true );
		return Task.CompletedTask;
	}
}

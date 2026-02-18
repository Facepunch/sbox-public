using Sandbox.ActionGraphs;
using Sandbox.Engine;
using Sandbox.Services;
using Sandbox.Tasks;
using Sandbox.UI;
using Sandbox.Utility;
using Steamworks;
using System;
using IMenuSystem = Sandbox.Internal.IMenuSystem;
using IModalSystem = Sandbox.Modals.IModalSystem;

namespace Sandbox;

internal sealed class MenuDll : IMenuDll
{
	/// <summary>
	/// Called by AppSystem to create the MenuDll instance.
	/// </summary>
	public static void Create()
	{
		IMenuDll.Current = new MenuDll();
	}

	private PackageLoader Loader { get; set; }
	private PackageLoader.Enroller Enroller { get; set; }
	private Task AccountUpdateTask { get; set; }
	private bool SceneDestroyedForGame { get; set; }

	public Scene Scene => MenuScene.Scene;

	/// <summary>
	/// Runs menu async tasks, so they execute in the same context as the menu.
	/// </summary>
	public static ExpirableSynchronizationContext AsyncContext { get; } = new ExpirableSynchronizationContext( false );

	public void Bootstrap()
	{
		using var scope = PushScope();

		GlobalContext.Current.Reset();
		GlobalContext.Current.LocalAssembly = GetType().Assembly;

		Game.InitHost();

		SetupInputContext();

		//
		// Init Steam
		//
		if ( !Application.IsEditor )
		{
			SteamClient.Init( (int)Application.AppId );
			AccountUpdateTask = AccountInformation.Update();
		}

		//
		// Files accessible by menu addon
		//
		GlobalContext.Current.FileMount = new AggregateFileSystem();
		{
			if ( Application.IsStandalone )
			{
				// No menu or citizen addon in standalone
				FileSystem.Mounted.CreateAndMount( EngineFileSystem.Addons, $"/base/code" );
				FileSystem.Mounted.CreateAndMount( EngineFileSystem.Addons, $"/base/assets" );
			}
			else
			{
				FileSystem.Mounted.CreateAndMount( EngineFileSystem.Addons, "/base/code/" );
				FileSystem.Mounted.CreateAndMount( EngineFileSystem.Addons, "/base/assets/" );
				FileSystem.Mounted.CreateAndMount( EngineFileSystem.Addons, "/menu/code/" );
				FileSystem.Mounted.CreateAndMount( EngineFileSystem.Addons, "/menu/assets/" );
				FileSystem.Mounted.CreateAndMount( EngineFileSystem.Addons, "/citizen/assets/" );
			}

			FileSystem.Mounted.CreateAndMount( EngineFileSystem.Root, "/core/" );
			FileSystem.Mounted.CreateAndMount( EngineFileSystem.Root, "/thirdpartylegalnotices/" );
		}

		//
		// Localization from menu 
		//
		{
			var localizationFolder = new AggregateFileSystem();
			localizationFolder.CreateAndMount( EngineFileSystem.Addons, "/menu/localization/" );

			Game.Language = new LanguageContainer( localizationFolder );
		}



		//
		// Give the GAM an early init, because we might want to load an assembly before
		// it's actually initialized properly
		//
		Loader = new PackageLoader( "Menu", GetType().Assembly, disableAccessControl: true );
		Loader.HotloadWatch( Game.GameAssembly ); // Sandbox.Game is per instance
		Loader.OnAfterHotload = () =>
		{
			GlobalContext.Current.OnHotload();
			Game.ActiveScene?.OnHotload();
			Event.Run( "hotloaded" );
		};

		Enroller = Loader.CreateEnroller( "menu" );

		Enroller.OnAssemblyAdded += ( a ) =>
		{
			Game.TypeLibrary.AddAssembly( a.Assembly, true );
			Game.NodeLibrary.AddAssembly( a.Assembly );
			ConVarSystem.AddAssembly( a.Assembly, "menu", "menu" );
			Event.RegisterAssembly( a.Assembly );
			Cloud.UpdateTypes( a.Assembly );

			// While this technically doesn't belong here, it means that there will be upgraders available
			// in the menu, before the game has even started. It should get over-ridden by the game's typelibrary.
			JsonUpgrader.UpdateUpgraders( Game.TypeLibrary );
		};

		Enroller.OnAssemblyRemoved += ( a ) =>
		{
			ConVarSystem.RemoveAssembly( a.Assembly );
			Event.UnregisterAssembly( a.Assembly );
			Game.NodeLibrary.RemoveAssembly( a.Assembly );
			Game.TypeLibrary.RemoveAssembly( a.Assembly );
		};

		ConVarSystem.AddAssembly( Game.GameAssembly, "menu", "menu" );

		Json.Initialize();
	}

	public async Task Initialize()
	{
		using var _ = PushScope();

		//
		// LoopEvent.Init
		//
		{
			StyleSheet.InitStyleSheets();
			GlobalContext.Current.Reset();
		}

		Game.Cookies = new CookieContainer( "menu" );
		SteamCallbacks.InitSteamCallbacks();

		// start listening to backend messages
		Sandbox.Services.Messaging.OnMessage += OnMessageFromBackend;

		// We shouldn't actually have anything to compile on retail
		await Project.CompileAsync();

		Enroller.LoadPackage( "local.menu#local" );

		if ( IMenuSystem.Current != null )
		{
			Log.Info( "Aready inited?" );
			return;
		}

		{
			using var tx = Sandbox.Engine.Bootstrap.StartupTiming?.ScopeTimer( "Menu - Fonts" );
			FontManager.Instance.LoadAll( FileSystem.Mounted );
		}

		// We can wait right up until we start the menu scene to want valid account info
		if ( AccountUpdateTask != null )
		{
			//
			// TODO - handle not logged in, api down etc
			//

			using var tx = Sandbox.Engine.Bootstrap.StartupTiming?.ScopeTimer( "Menu - Account Update Task" );
			await AccountUpdateTask;
		}

		// If the avatar was found on the backend, replace the cookie one
		if ( !string.IsNullOrWhiteSpace( AccountInformation.AvatarJson ) )
		{
			Avatar.AvatarJson = AccountInformation.AvatarJson;
		}

		if ( !Application.IsEditor )
		{
			using ( Sandbox.Engine.Bootstrap.StartupTiming?.ScopeTimer( "Menu - Resources" ) )
			{
				LoadResources();
			}

			SetupMenuScene();
		}

		IMenuSystem.Current = TypeLibrary.Create<IMenuSystem>( "MenuSystem", true );

		if ( IMenuSystem.Current == null )
		{
			NativeEngine.EngineGlobal.Plat_MessageBox( "Menu Load Error", "Couldn't create MenuSystem!" );
			throw new System.Exception( "Menu couldn't load. Can't continue." );
		}

		// Allow tasks in menu assembly to persist when game sessions end
		ExpirableSynchronizationContext.AllowPersistentTaskMethods( IMenuSystem.Current.GetType().Assembly );

		IMenuSystem.Current.Init();

		SetupFileWatch();
	}

	public void Exiting()
	{
		using ( PushScope() )
		{
			// Shutdown menu system
			IMenuSystem.Current?.Shutdown();
			IMenuSystem.Current = null;

			// Unregister messaging
			Sandbox.Services.Messaging.OnMessage -= OnMessageFromBackend;

			// Save and dispose cookies
			Game.Cookies?.Save();
			Game.Cookies = null;

			// Cleanup scene
			MenuScene.Scene?.Destroy();
			MenuScene.Scene = null;

			// Dispose package loader and enroller
			Enroller?.Dispose();
			Enroller = null;

			Loader?.Dispose();
			Loader = null;

			// Shutdown Steamworks interfaces
			if ( !Application.IsEditor )
			{
				Steamworks.SteamClient.Cleanup();
			}

			// Expire async context to prevent lingering tasks
			AsyncContext.Expire( null );

			// Clear global context
			GlobalContext.Current.Reset();

			IMenuDll.Current = null;
		}
	}

	void LoadResources()
	{
		ResourceLoader.LoadAllGameResource( FileSystem.Mounted );
	}

	public void OnGameEntered()
	{
		if ( Application.IsEditor )
			return;

		// Already destroyed (e.g. switching between games without returning to menu)
		if ( SceneDestroyedForGame )
			return;

		using var scope = PushScope();

		// Destroy the entire menu scene to free all resources
		// (models, textures, lights, cameras, particles, etc.).
		// The overlay UI (loading screen, pause menu, popups) lives in the
		// menu's UISystem as RootPanels, not in the scene, so it survives.
		if ( MenuScene.Scene is not null )
		{
			MenuScene.Scene.Destroy();
			MenuScene.Scene = null;
		}

		SceneDestroyedForGame = true;
	}

	public void OnGameExited()
	{
		if ( Application.IsEditor )
			return;

		if ( !SceneDestroyedForGame )
			return;

		using var scope = PushScope();

		SceneDestroyedForGame = false;

		// Recreate the menu scene from scratch
		SetupMenuScene();
	}

	private void SetupMenuScene()
	{
		var package = PackageManager.Find( "local.menu" ).Package;
		MenuScene.Startup( package.GetMeta<string>( "StartupScene" ) );
	}

	/// <summary>
	/// A message has come in from the web pubsub protbuf stuff
	/// </summary>
	private void OnMessageFromBackend( Messaging.Message message )
	{
		using var scope = PushScope();

		if ( message.Data is Protobuf.AchievementMsg.AchievementUnlocked msg )
		{
			var data = new IAchievementListener.UnlockDescription();
			data.Title = msg.Title;
			data.Description = msg.Description;
			data.Icon = msg.Icon;
			data.ScoreAdded = msg.ScoreAdded;
			data.TotalPlayerScore = msg.PlayerScore;
			data.TotalPackageScore = msg.PackageScore;

			Event.EventSystem.RunInterface<IAchievementListener>( x => x.OnAchievementUnlocked( data ) );
		}
	}

	public IDisposable PushScope()
	{
		var contextLocal = GlobalContext.MenuScope();
		var scene = MenuScene.Scene?.Push();

		return DisposeAction.Create( () =>
		{
			contextLocal?.Dispose();
			scene?.Dispose();
		} );
	}

	public void Tick()
	{
		using var _ = PushScope();

		try
		{
			IMenuSystem.Current?.Tick();
		}
		catch ( System.Exception e )
		{
			Log.Error( e, "Error in MenuSystem tick" );
		}

		try
		{
			Loader.Tick();
		}
		catch ( System.Exception e )
		{
			Log.Error( e, "Error in PackageLoader tick" );
		}

		try
		{
			MenuScene.Tick();
		}
		catch ( System.Exception e )
		{
			Log.Error( e, "Error in MenuScene tick" );
		}

		MenuUtility.Tick?.Invoke();

		// Run any pending queue'd mainthread tasks here
		// so they're in the same scene scope
		MainThread.RunQueues();
		AsyncContext.ProcessQueue();
	}

	void IMenuDll.LateTick()
	{
		using var _ = PushScope();

		if ( Input.EscapePressed && IGameInstance.Current is not null )
		{
			Input.EscapePressed = false;
			IModalSystem.Current?.PauseMenu();
		}
	}

	public void Reset()
	{
		if ( Application.IsEditor )
			return;

		using var _ = PushScope();
		LoadResources();
	}


	public void SimulateUI()
	{
		using var _ = PushScope();

		using ( MenuScene.Scene?.Push() )
		{
			Game.Language.Tick();
			GlobalContext.Current.UISystem.Simulate( true );
		}
	}

	public void ClosePopups( object panelClickedOn )
	{
		BasePopup.CloseAll( panelClickedOn as Panel );
	}

	[MenuConCmd( "menu_reload", ConVarFlags.Protected )]
	public static void Recreate()
	{
		IMenuSystem.Current?.Shutdown();
		IMenuSystem.Current?.Init();
	}

	[MenuConCmd( "lobbies", ConVarFlags.Protected )]
	public static async void ListLobbies()
	{
		Log.Info( "Querying.." );
		var list = await SteamMatchmaking.LobbyList.FilterDistanceWorldwide().WithMaxResults( 1000 ).RequestAsync( default );

		foreach ( var item in list )
		{
			Log.Info( $"{item}: {string.Join( ";", item.Data.Select( x => $"{x.Key}={x.Value}" ) )}" );
		}

		Log.Info( "End." );
	}

	bool IMenuDll.HasOverlayMouseInput()
	{
		using var _ = PushScope();

		if ( GlobalContext.Current.UISystem.Input.Hovered is null )
			return false;

		for ( int i = GlobalContext.Current.UISystem.RootPanels.Count() - 1; i >= 0; i-- )
		{
			if ( GlobalContext.Current.UISystem.RootPanels[i].RenderedManually || GlobalContext.Current.UISystem.RootPanels[i].IsWorldPanel )
				continue;

			if ( GlobalContext.Current.UISystem.RootPanels[i] == GlobalContext.Current.UISystem.Input.Hovered.FindRootPanel() )
				return true;
		}

		return false;
	}

	public void OnRender( SwapChainHandle_t swapChain )
	{
		using var _ = PushScope();

		MenuScene.Render( swapChain );
	}

	void SetupFileWatch()
	{
		using var _ = PushScope();

		var watcher = FileSystem.Mounted.Watch();
		watcher.OnChanges += x =>
		{
			foreach ( var file in x.Changes )
			{
				Texture.Hotload( FileSystem.Mounted, file );
			}
		};
	}

	public void RunEvent( string name )
	{
		Event.Run( name );
	}

	public void RunEvent( string name, object argument )
	{
		Event.Run( name, argument );
	}

	public void RunEvent( string name, object arg0, object arg1 )
	{
		Event.Run( name, arg0, arg1 );
	}

	public void RunEvent<T>( Action<T> action )
	{
		Event.EventSystem.RunInterface<T>( action );
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
}


public interface IAchievementListener
{
	public struct UnlockDescription
	{
		public string Title { get; internal set; }
		public string Description { get; internal set; }
		public string Icon { get; internal set; }
		public int ScoreAdded { get; internal set; }
		public int TotalPackageScore { get; internal set; }
		public int TotalPlayerScore { get; internal set; }
	}

	void OnAchievementUnlocked( UnlockDescription data );
}

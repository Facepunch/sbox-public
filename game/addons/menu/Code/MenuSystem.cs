global using Sandbox.Menu;
global using Sandbox.UI;
global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Threading.Tasks;
global using static Sandbox.Internal.GlobalGameNamespace;
using Menu;
using Sandbox;
using Sandbox.Audio;
using Sandbox.Internal;
using Sandbox.UI.Dev;

[Library]
public partial class MenuSystem : IMenuSystem
{
	public static MenuSystem Instance;

	DevLayer Dev;

	public Action<Package> OnPackageSelected { get; set; }

	[MenuConVar( "show_version_overlay" )]
	public static bool ShowOverlay { get; set; } = true;

	public void Init()
	{
		Instance = this;

		// Creation order is important
		// Panel created first will be on top

		Dev = new DevLayer();

		MenuUtility.SetModalSystem( new ModalSystem() );
		MenuOverlay.Init();

		var startupGameIdent = MenuUtility.StartupGameIdent;
		if ( !string.IsNullOrEmpty( startupGameIdent ) )
		{
			Game.Overlay.ShowGameModal( startupGameIdent );
		}
	}

	public void Shutdown()
	{
		MenuOverlay.Shutdown();

		Dev?.Delete();
		Dev = null;

		// Null so GC can have it's way
		Instance = null;
	}

	Package oldGamePackage;

	public void Tick()
	{
		if ( Application.IsEditor ) return;

		if ( oldGamePackage != MenuUtility.GamePackage )
		{
			oldGamePackage = MenuUtility.GamePackage;

			if ( MenuUtility.GamePackage is not null )
			{
				var panel = new GameStarting();
				panel.Parent = MenuOverlay.Instance.PopupCanvasTopLeft;
			}
		}

		UpdateMusic();
	}


	public void Popup( string type, string title, string subtitle )
	{
		MenuOverlay.Message( type, title, subtitle );
	}

	/// <summary>
	/// Show a question
	/// </summary>
	public void Question( string message, string icon, Action yes, Action no )
	{
		MenuOverlay.Question( message, icon, yes, no );
	}

	public string Url
	{
		get => MainMenuPanel.Instance.Navigator.CurrentUrl;
		set => MainMenuPanel.Instance.Navigator.Navigate( value );
	}

	public bool ForceCursorVisible => DeveloperMode.Open;


	class MenuMusic
	{
		public bool Enabled;
		public float Volume;
		public float TargetVolume = 0.5f;
		string file;
		MusicPlayer player;

		public MenuMusic( string filename )
		{
			file = filename;
		}

		public void Update()
		{
			float targetVolume = Enabled ? 1 : 0;
			if ( targetVolume == Volume )
				return;

			Volume = Volume.Approach( targetVolume, RealTime.SmoothDelta * 2.0f ); // 0.5s fade
			if ( Volume <= 0.001f )
			{
				player?.Dispose();
				player = null;
				return;
			}

			if ( player is null )
			{
				try
				{
					player = MusicPlayer.Play( FileSystem.Mounted, file );
				}
				catch ( ArgumentException )
				{
					// music not found, fuck it
					return;
				}

				player.Repeat = true;
			}

			player.Volume = Volume * TargetVolume;
			player.Position = new Vector3( 0, 0, 0 );
			player.ListenLocal = true;
			player.TargetMixer = Mixer.FindMixerByName( "music" );
		}
	}

	MenuMusic menu = new MenuMusic( "music/menu-bg.wav" );
	MenuMusic loading = new MenuMusic( "music/menu-loading.wav" );
	MenuMusic avatar = new MenuMusic( "music/furniture_shop_loop.ogg" );

	void UpdateMusic()
	{
		bool isAvatarMenu = Game.ActiveScene.Get<AvatarEditManager>() != null;
		bool isLoadingScreen = LoadingScreen.IsVisible;

		menu.Enabled = Game.IsMainMenuVisible && !isLoadingScreen && !isAvatarMenu;
		menu.Update();

		loading.Enabled = LoadingScreen.IsVisible && (IGameInstance.Current is null || IGameInstance.Current.IsLoading);
		loading.Update();

		avatar.Enabled = isAvatarMenu;
		avatar.TargetVolume = 0.1f;
		avatar.Update();
	}

	void IMenuSystem.OnPackageClosed( Package package )
	{
		var panel = new GameClosedToast() { Package = package };
		panel.Parent = MenuOverlay.Instance.PopupCanvasBottomRight;
	}

	[MenuConCmd( "menu_packageclosed" )]
	public static async Task PackageClosedTest( string ident )
	{
		var package = await Package.FetchAsync( ident, false );
		((IMenuSystem)MenuSystem.Instance).OnPackageClosed( package );
	}
}

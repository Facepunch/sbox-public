global using Sandbox.Menu;
global using Sandbox.UI;
global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Threading.Tasks;
using MenuProject;
using MenuProject.Overlay.Overlays;
using Sandbox;
using Sandbox.Audio;
using Sandbox.Internal;
using Sandbox.UI.Construct;
using Sandbox.UI.Dev;

[Library]
public partial class MenuSystem : IMenuSystem
{
	public static MenuSystem Instance;

	DevLayer Dev;

	public Action<Package> OnPackageSelected { get; set; }

	public void Init()
	{
		Instance = this;

		// Creation order is important
		// Panel created first will be on top

		Dev = new DevLayer();

		MenuUtility.SetModalSystem( new ModalSystem() );
		MenuOverlay.Init();

		// What's hidden, so a hidden game's page can offer to show it again
		_ = MenuUtility.Hidden.Refresh();

		var startupGameIdent = MenuUtility.StartupGameIdent;
		if ( !string.IsNullOrEmpty( startupGameIdent ) )
		{
			Discovery.Clicked( new DiscoveryContext { Surface = "web", Referrer = MenuUtility.StartupWebReferrer }, startupGameIdent );
			Game.Overlay.ShowGameModal( startupGameIdent );
		}
	}

	public void Shutdown()
	{
		MenuProject.Multiplayer.GlobalChatService.Close();
		gameClosingPanel?.Delete();
		gameClosingPanel = null;

		MenuOverlay.Shutdown();

		Dev?.Delete();
		Dev = null;

		// Null so GC can have it's way
		Instance = null;
	}

	Package oldGamePackage;

	GameClosing gameClosingPanel;
	GameClosedToast gameClosedToast;

	public void Tick()
	{
		MenuProject.Multiplayer.GlobalChatService.Tick();
		if ( Application.IsEditor ) return;

		if ( oldGamePackage != MenuUtility.GamePackage )
		{
			oldGamePackage = MenuUtility.GamePackage;

			// Anything left over from the previous game goes away when a new one starts
			if ( MenuUtility.GamePackage is not null )
			{
				gameClosedToast?.Delete( true );
				gameClosedToast = null;
			}
		}

		TickEscapeToClose();
		UpdateMusic();
	}

	void TickEscapeToClose()
	{
		if ( Game.InGame )
		{
			var startDelay = 0.2f;
			var holdDelay = 1.5f;

			if ( MenuUtility.EscapeTime > startDelay )
			{
				var et = MenuUtility.EscapeTime - startDelay;

				if ( !gameClosingPanel.IsValid() )
				{
					gameClosingPanel = new GameClosing();
					gameClosingPanel.Parent = MenuOverlay.Instance.TopCenter;
				}

				gameClosingPanel.Progress = Math.Clamp( et / holdDelay, 0f, 1f );
				gameClosingPanel.StateHasChanged();

				if ( gameClosingPanel.Progress >= 1 )
				{
					gameClosingPanel?.Delete();
					gameClosingPanel = null;
					Game.Close();
				}
			}
			else
			{
				gameClosingPanel?.Delete();
				gameClosingPanel = null;
			}
		}
		else
		{
			gameClosingPanel?.Delete();
			gameClosingPanel = null;
		}
	}

	public void Popup( string type, string title, string subtitle )
	{
		var content = new Panel( null, "popup has-message" );
		content.AddClass( type );
		content.Add.Label( title, "message" );
		content.Add.Label( subtitle, "subtitle" );
		MenuOverlay.Queue( content );
	}

	/// <summary>
	/// Show a question
	/// </summary>
	public void Question( string message, string icon, Action yes, Action no )
	{
		MenuOverlay.Question( message, icon, yes, no );
	}

	/// <summary>
	/// A friend's asked us into their party - it pops up where questions do, with who's asking and
	/// who's in their party. See <see cref="PartyInviteToast"/>.
	/// </summary>
	public void OnPartyInvite( Friend from, Action accept, Action decline )
	{
		PartyInviteToast.Show( from, accept, decline );
	}

	public string Url
	{
		get => MainMenu.Instance.Navigator.CurrentUrl;
		set => MainMenu.Instance.Navigator.Navigate( value );
	}

	public bool ForceCursorVisible => DeveloperMode.Open || ChatOverlay.IsOpen;


	SoundFile menuTrack;
	SoundFile loadingTrack;
	SoundFile avatarTrack;

	/// <summary>
	/// Volume multiplier for menu, loading screen and avatar editor music.
	/// </summary>
	[MenuConVar( "music_volume_menu", Help = "Menu music volume", Saved = true, Min = 0, Max = 1 )]
	public static float MenuMusicVolume { get; set; } = 1.0f;

	/// <summary>
	/// Music is one shared channel, so only ever touch it when it's silent or playing one of our tracks.
	/// A game's music carries on through loading screens and after it starts.
	/// </summary>
	void UpdateMusic()
	{
		menuTrack ??= SoundFile.Load( "music/menu.mp3" );
		loadingTrack ??= SoundFile.Load( "music/menu-loading.wav" );
		avatarTrack ??= SoundFile.Load( "music/furniture_shop_loop.ogg" );

		var current = Game.Music.Track;
		bool isOurs = current is null || current == menuTrack || current == loadingTrack || current == avatarTrack;
		if ( !isOurs )
			return;

		bool isLoading = LoadingScreen.IsVisible && (IGameInstance.Current is null || IGameInstance.Current.IsLoading);
		bool isInGame = IGameInstance.Current is not null;
		bool isAvatarMenu = Game.ActiveScene?.Get<AvatarEditManager>() != null;

		if ( isLoading )
		{
			Game.Music.Play( loadingTrack, fade: 0.5f, volume: 0.5f * MenuMusicVolume );
		}
		else if ( isInGame )
		{
			Game.Music.Stop( 0.5f );
		}
		else if ( isAvatarMenu )
		{
			Game.Music.Play( avatarTrack, fade: 0.5f, volume: 0.1f * MenuMusicVolume );
		}
		else
		{
			Game.Music.Play( menuTrack, fade: 0.5f, volume: 0.1f * MenuMusicVolume );
		}
	}

	async void IMenuSystem.OnPackageClosed( Package package )
	{
		gameClosedToast?.Delete( true );
		gameClosedToast = null;

		// A moment after, once you're back in the menu - not on top of the way out
		await GameTask.DelayRealtime( 2000 );

		// Off into another game in the meantime - it's not the time
		if ( MenuUtility.GamePackage is not null ) return;

		gameClosedToast?.Delete( true );
		gameClosedToast = new GameClosedToast() { Package = package };
		MenuOverlay.Instance.BottomCenter.Queue( gameClosedToast, duration: 0, clickToDismiss: false );
	}

	/// <summary>Go to a menu url from the console, for driving the menu from a test or tool.</summary>
	[MenuConCmd( "menu_goto" )]
	public static void GoTo( string url )
	{
		MainMenu.Instance?.Navigator?.Navigate( url );
	}

	/// <summary>
	/// Open party setup without having to invite someone first.
	/// </summary>
	[MenuConCmd( "party_create" )]
	public static async Task CreateParty()
	{
		if ( PartyRoom.Current is not null )
		{
			Log.Info( "Already in a party" );
			return;
		}

		await PartyDeck.EnsurePartyExists();
	}

	[MenuConCmd( "menu_packageclosed" )]
	public static async Task PackageClosedTest( string ident )
	{
		var package = await Package.FetchAsync( ident, false );
		((IMenuSystem)MenuSystem.Instance).OnPackageClosed( package );
	}

	public Action<string, long> OnPackageUsageChanged { get; set; }

	public void PackageUsageChanged( string packageIdent, long userCount )
	{
		OnPackageUsageChanged?.InvokeWithWarning( packageIdent, userCount );
	}

	public void PackageFavouritesChanged( string packageIdent, long value )
	{
		// ignore for now
	}
}

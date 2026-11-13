global using Sandbox.Menu;
global using Sandbox.UI;
global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Threading.Tasks;
using MenuProject;
using MenuProject.Modals;
using MenuProject.Overlay.Overlays;
using MenuProject.Platform;
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

		// Whatever the last run was on goes, before anything that asks is made
		PlatformMenu.Reset();

		// Creation order is important
		// Panel created first will be on top

		Dev = new DevLayer();

		MenuUtility.SetModalSystem( new ModalSystem() );
		MenuOverlay.Init();

		// Made after the overlay, so its modals, toasts and the loading screen draw over it. Straight onto a game's
		// menu - the one given on the command line, or the one picked last time. Not in the editor, where it's only
		// up over the game being worked on - paused, or for what it opens (PlatformRoot.Visible).
		PlatformRoot.Instance = new PlatformRoot();

		if ( Application.IsEditor ) return;

		PlatformMenu.Start();

		_ = ShowStreamerModePromptIfNeeded();
		_ = FetchResolutionsWhenIdle();
	}

	static async Task FetchResolutionsWhenIdle()
	{
		// Fetch resolutions early so opening the settings doesn't stutter later, but not while the menu is first appearing.
		await GameTask.DelayRealtime( 3000 );
		MenuProject.Settings.SettingsCatalog.FetchResolutions();
	}

	static async Task ShowStreamerModePromptIfNeeded()
	{
		await GameTask.DelayRealtime( 1500 );

		if ( Preferences.StreamerMode ) return;

		// Only inform the user about Streamer Mode once, then remember it across startups.
		if ( Game.Cookies.Get<bool>( StreamerModePromptModal.CookieKey, false ) ) return;

		// Enumerating every process can take tens of ms, so keep it off the main thread.
		if ( !await RecordingSoftware.IsRunningAsync() ) return;

		Game.Cookies.Set( StreamerModePromptModal.CookieKey, true );
		ModalSystem.Instance?.Open( new StreamerModePromptModal() );
	}

	public void Shutdown()
	{
		MenuProject.Multiplayer.GlobalChatService.Close();
		gameClosingPanel?.Delete();
		gameClosingPanel = null;

		MenuOverlay.Shutdown();

		PlatformRoot.Instance?.Delete();
		PlatformRoot.Instance = null;

		Dev?.Delete();
		Dev = null;

		// Null so GC can have it's way
		Instance = null;
	}

	GameClosing gameClosingPanel;

	public void Tick()
	{
		MenuProject.Multiplayer.GlobalChatService.Tick();
		if ( Application.IsEditor ) return;

		TickEscapeToClose();
		UpdateMusic();
	}

	void TickEscapeToClose()
	{
		// Held, a game's left for Sandbox's front - on a game's menu, there's nowhere to go
		if ( Game.InGame && !Game.IsOnMenu )
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

					PlatformMenu.LeaveToDefault();
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

	public bool ForceCursorVisible => DeveloperMode.Open || ChatOverlay.IsOpen;


	SoundFile menuTrack;
	SoundFile loadingTrack;

	/// <summary>
	/// Volume multiplier for menu and loading screen music.
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

		var current = Game.Music.Track;
		bool isOurs = current is null || current == menuTrack || current == loadingTrack;
		if ( !isOurs )
			return;

		bool isLoading = LoadingScreen.IsVisible && (IGameInstance.Current is null || IGameInstance.Current.IsLoading);
		bool isInGame = IGameInstance.Current is not null;

		if ( isLoading )
		{
			Game.Music.Play( loadingTrack, fade: 0.5f, volume: 0.5f * MenuMusicVolume );
		}
		else if ( isInGame )
		{
			Game.Music.Stop( 0.5f );
		}
		else
		{
			Game.Music.Play( menuTrack, fade: 0.5f, volume: 0.1f * MenuMusicVolume );
		}
	}

	void IMenuSystem.OnPackageClosed( Package package )
	{
		// Back onto its game's menu - unless it's switching, which is going somewhere else
		if ( Application.IsEditor || PlatformMenu.IsSwitching ) return;

		PlatformMenu.ReturnAfterClose( package );
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

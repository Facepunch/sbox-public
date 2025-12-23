using NativeEngine;
using Sandbox.Engine;
using Sandbox.Network;
using Sandbox.VR;

namespace Sandbox;

/// <summary>
/// Provides global access to core game state, utilities, and operations for S&amp;box.
/// <para>
/// The <see cref="Game"/> class exposes static properties and methods to query and control the running game,
/// such as checking if the game is running, getting your steamid, taking screenshots, and managing game sessions.
/// </para>
/// </summary>
/// <example>
/// <code>
/// // Check if the game is running in the editor
/// if (Game.IsEditor)
/// {
///     // Perform editor-specific logic
/// }
/// </code>
/// </example>
/// <example>
/// <code>
/// // Take a screenshot
/// Game.TakeScreenshot();
/// </code>
/// </example>
/// <seealso cref="Application"/>
public static partial class Game
{
	/// <summary>
	/// The input context for this context (menu, gamemenu, client)
	/// </summary>
	internal static InputContext InputContext
	{
		get => GlobalContext.Current.InputContext;
		set => GlobalContext.Current.InputContext = value;
	}

	/// <summary>
	/// Steam AppId of S&amp;box.
	/// </summary>
	public static ulong AppId => Application.AppId;

	/// <summary>
	/// Return true if we're in a game (ie, not in the main menu)
	/// </summary>
	public static bool InGame => IGameInstance.Current is not null;

	/// <summary>
	/// Returns true if the game is running with the editor enabled
	/// </summary>
	public static bool IsEditor => Application.IsEditor;

	/// <summary>
	/// Returns the current game's ident - ie facepunch.sandbox
	/// </summary>
	public static string Ident => Application.GameIdent;

	/// <summary>
	/// Returns true if the main menu is visible. Note that this will work serverside too but will only
	/// return the state of the host.
	/// </summary>
	public static bool IsMainMenuVisible => !InGame;

	/// <summary>
	/// True if we're currently recording a video (using the video command, or F6)
	/// </summary>
	public static bool IsRecordingVideo => ScreenRecorder.IsRecording();

	/// <summary>
	/// Set to true when the game is closing
	/// </summary>
	public static bool IsClosing { get; internal set; } = false;

	/// <summary>
	/// Return true if we're running in VR
	/// </summary>
	public static bool IsRunningInVR => VRSystem.IsActive;

	/// <summary>
	/// Return true if we're running on a handheld device (the deck). Will always be false serverside.
	/// </summary>
	public static bool IsRunningOnHandheld { get; internal set; }

	/// <summary>
	/// A shared random that is automatically seeded on tick
	/// </summary>
	public static System.Random Random => SandboxSystem.Random;

	/// <summary>
	/// Set the seed for Game.Random
	/// </summary>
	public static void SetRandomSeed( int seed )
	{
		SandboxSystem.SetRandomSeed( seed );
	}

	/// <summary>
	/// Your SteamId
	/// </summary>
	public static SteamId SteamId => Utility.Steam.SteamId;

	/// <summary>
	/// Create a limited web surface
	/// </summary>
	public static WebSurface CreateWebSurface()
	{
		return new WebSurface( !IsMenu );
	}

	/// <summary>
	/// Disconnect from the current game session
	/// </summary>
	public static void Disconnect()
	{
		ConVarSystem.Run( "disconnect end game" );
	}

	/// <summary>
	/// Trace against the physics in the current scene
	/// </summary>
	public static PhysicsTraceBuilder PhysicsTrace => Game.ActiveScene.PhysicsWorld.Trace;

	/// <summary>
	/// Trace against the physics and hitboxes in the current scene
	/// </summary>
	public static SceneTrace SceneTrace => Game.ActiveScene.Trace;

	/// <summary>
	/// Close the current game.
	/// </summary>
	public static void Close()
	{
		// Editor only: just exit playmode
		if ( IToolsDll.Current is not null )
		{
			IToolsDll.Current.ExitPlaymode();
			return;
		}

		// Might want to queue this up. do it the next frame?
		// Be aware that this could be called from the GameDll or the MenuDll
		// So anything here needs to be safe to call from either

		if ( IGameInstance.Current is not null )
		{
			IGameInstance.Current.Close();
			LaunchArguments.Reset();
		}

		// Standalone mode and Dedicated Server only: exit whole app
		if ( Application.IsStandalone || Application.IsDedicatedServer )
		{
			Application.Exit();
		}
	}

	/// <summary>
	/// Load a game. You can configure the new game with LaunchArguments before calling this.
	/// </summary>
	public static void Load( string gameIdent, bool keepClients )
	{
		Log.Info( $"Game Load: {gameIdent}" );

		_ = LoadAsync( gameIdent, keepClients );
	}

	static async Task LoadAsync( string gameIdent, bool keepClients )
	{
		if ( IToolsDll.Current is not null )
		{
			IToolsDll.Current.ExitPlaymode();
			Log.Warning( $"Called Game.Load( {gameIdent} ) but we're in the editor, so just stopping." );
			return;
		}

		if ( Networking.IsActive && Networking.IsHost )
		{
			if ( keepClients )
			{
				// Send a reconnect message but with no delay - this must be sent right now!
				var msg = new ReconnectMsg { Game = gameIdent, Map = LaunchArguments.Map };
				SceneNetworkSystem.Instance.Broadcast( msg, default, NetFlags.Reliable | NetFlags.SendImmediate );
			}

			// Allow for a 1 second grace period for clients to receive the message
			await Task.Delay( 1000 );

			Networking.Disconnect();
		}

		// close old game
		if ( IGameInstance.Current is not null )
		{
			Log.Info( $"Closing {IGameInstance.Current.Package?.Title}" );
			IGameInstanceDll.Current.CloseGame();
		}

		Application.ClearGame();

		LoadingScreen.IsVisible = true;

		// Load new game
		Log.Info( $"Loading {gameIdent}.." );
		await IGameInstanceDll.Current.LoadGamePackageAsync( gameIdent, GameLoadingFlags.Host, default );
		Log.Info( $"Loading {gameIdent} complete!" );
	}

	/// <summary>
	/// Capture a screenshot. Saves it in Steam.
	/// </summary>
	[ConCmd( "screenshot", Help = "Take a screenshot and save it to your Steam screenshots" )]
	public static void TakeScreenshot()
	{
		ScreenshotService.RequestCapture();
	}

	/// <summary>
	/// Capture a high resolution screenshot using the active scene camera.
	/// </summary>
	[ConCmd( "screenshot_highres", Help = "Take a high resolution screenshot you specify the width and height" )]
	public static void TakeHighResScreenshot( int width, int height )
	{
		ScreenshotService.TakeHighResScreenshot( Application.GetActiveScene(), width, height );
	}

	/// <summary>
	/// This has to be in Game.dll so the codegen will get generated for it
	/// </summary>
	[ConVar( "sv_cheats", ConVarFlags.Replicated | ConVarFlags.Protected, Help = "Enable cheats on the server" )]
	public static bool CheatsEnabled { get; set; }
}

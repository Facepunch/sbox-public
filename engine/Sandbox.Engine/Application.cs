using Editor;
using Sandbox.Engine;
using Sandbox.Engine.Settings;
using Sandbox.Utility;
using Sandbox.VR;
using System.Globalization;

namespace Sandbox;

public static class Application
{
	/// <summary>
	/// Prevent double initialization
	/// </summary>
	internal static bool IsInitialized { get; private set; }

	/// <summary>
	/// Steam AppId of S&amp;box.
	/// </summary>
	public static ulong AppId { get; internal set; } = 590830;

	/// <summary>
	/// True if we're running the engine as part of a unit test
	/// </summary>
	public static bool IsUnitTest { get; internal set; }

	/// <summary>
	/// True if running without a graphics window, such as in a terminal.
	/// </summary>
	public static bool IsHeadless { get; private set; }

	/// <summary>
	/// True if running in a terminal like console, instead of a game window or editor.
	/// </summary>
	public static bool IsConsoleApp => IsHeadless;

	/// <summary>
	/// True if this is a dedicated server
	/// </summary>
	public static bool IsDedicatedServer { get; private set; }

	/// <summary>
	/// True if running a benchmark
	/// </summary>
	internal static bool IsBenchmark { get; private set; }

	/// <summary>
	/// True if running with the tools or editor attached
	/// </summary>
	public static bool IsEditor { get; private set; }

	/// <summary>
	/// True if running with -joinlocal. This is an instance that launches and joins
	/// an in process editor session.
	/// </summary>
	internal static bool IsJoinLocal { get; private set; }

	/// <summary>
	/// The engine's version string
	/// </summary>
	public static string Version { get; internal set; }

	/// <summary>
	/// True if this is compiled and published on steam
	/// </summary>
	internal static bool IsRetail { get; private set; }

	/// <summary>
	/// The date of this version, as a UTC datetime.
	/// </summary>
	public static DateTime VersionDate { get; internal set; }

	/// <summary>
	/// Number of exceptions we've had. Resets on game exit.
	/// </summary>
	internal static int ExceptionCount { get; set; }

	/// <summary>
	/// True if the game is running in standalone mode
	/// </summary>
	public static bool IsStandalone { get; internal set; }

	/// <summary>
	/// The language code for the current language
	/// </summary>
	[ConVar( "language", ConVarFlags.Saved | ConVarFlags.Protected, Name = "Language" )]
	public static string LanguageCode { get; internal set; } = "en";

	/// <summary>
	/// True if the game is running in VR mode
	/// </summary>
	public static bool IsVR => VRSystem.IsActive; // garry: I think this is right? But feels like this should be set at startup and never change?

	internal static void Initialize( bool dedicated, bool headless, bool toolsMode, bool testMode, bool isRetail )
	{
		if ( IsInitialized )
			throw new InvalidOperationException( "Already Initialized" );

		IsInitialized = true;

		IsDedicatedServer = dedicated;
		IsRetail = isRetail;
		IsUnitTest = testMode;
		IsHeadless = headless;
		IsEditor = toolsMode;
		IsJoinLocal = CommandLine.HasSwitch( "-joinlocal" );
		IsBenchmark = Environment.GetEnvironmentVariable( "SBOX_MODE" ) == "BENCHMARK";
	}

	internal static void Shutdown()
	{
		IsInitialized = false;
	}

	internal static void TryLoadVersionInfo( string gameFolder )
	{
		Version = "0000000";
		VersionDate = DateTime.Now;

		var versionPath = System.IO.Path.Combine( gameFolder, ".version" );

		if ( System.IO.File.Exists( versionPath ) )
		{
			var text = System.IO.File.ReadAllText( versionPath );
			var split = text.Split( "\n" );

			Version = split[0].Trim();
			VersionDate = DateTime.ParseExact( split[4], "dd/MM/yyyy HH:mm:ss", null, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal );
		}
	}

	/// <summary>
	/// The currently loaded game package. May be null if no game loaded. 
	/// Controlled by GameMenuDll.
	/// </summary>
	internal static Package GamePackage { get; set; }

	/// <summary>
	/// The currently loaded map package
	/// </summary>
	internal static Package MapPackage { get; set; }


	/// <summary>
	/// The currently loaded game package's ident - if applicable.
	/// </summary>
	internal static string GameIdent { get; set; }

#if DEBUG
	public static bool IsDebug => true;
#endif

#if !DEBUG
	public static bool IsDebug => false;
#endif

	/// <summary>
	/// Returns true if the microphone is currently listening
	/// </summary>
	public static bool IsMicrophoneListening => VoiceManager.IsListening;

	/// <summary>
	/// Returns true if the microphone is currently listening and actually hearing/capturing sounds
	/// </summary>
	public static bool IsMicrophoneRecording => VoiceManager.IsRecording;

	/// <summary>
	/// Is the game window in focus?
	/// </summary>
	public static bool IsFocused => NativeEngine.EngineGlobal.IsWindowFocused();

	internal static bool WantsExit { get; set; }

	/// <summary>
	/// Exits the application if we're running in standalone mode or we are a Dedicated Server.
	/// </summary>
	internal static void Exit()
	{
		WantsExit = true;
	}

	internal static void ClearGame()
	{
		GameIdent = default;
		GamePackage = default;
		ExceptionCount = default;
		MapPackage = default;
	}

	public static bool CheatsEnabled
	{
		get => ConVarSystem.GetValue( "sv_cheats", "false", true ).ToBool();
	}

	/// <summary>
	/// Allows access to the RenderSettings singleton, which contains settings related to rendering in the game.
	/// You're only able to access this when in standalone mode. When accessing in the editor, or in sbox it will return null.
	/// </summary>
	public static RenderSettings RenderSettings
	{
		get
		{
			if ( !IsStandalone ) return null;
			return RenderSettings.Instance;
		}
	}

	/// <summary>
	/// Gets the active scene. This could be in the menu system, or in the game. This is provided
	/// for internal engine, and should never be accessible to the user code.
	/// </summary>
	internal static Scene GetActiveScene()
	{
		if ( IGameInstance.Current?.Scene is Scene gameScene && gameScene.IsValid() )
		{
			return gameScene;
		}

		if ( IMenuDll.Current?.Scene is Scene menuScene && menuScene.IsValid() )
		{
			return menuScene;
		}

		return null;
	}

	/// <summary>
	/// Get the current editor if any. Will return null if we're not in the editor, or there is no active editor session.
	/// </summary>
	public static EditorSystem Editor => IToolsDll.Current?.ActiveEditor;

}

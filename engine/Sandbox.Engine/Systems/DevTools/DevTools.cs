using Sandbox.Engine;
using Sandbox.UI;

namespace Sandbox.Diagnostics;

/// <summary>
/// The dev tools (<c>devtools</c>, F11): one place for the engine's performance numbers, which used to be a
/// separate <c>overlay_*</c> convar each, stacked up the side of the screen. The overview tab says what's limiting
/// the frame and what's worth looking at, for anyone. The other tabs are the detail behind it, for developers - see
/// <see cref="ShowAdvanced"/>.
/// <para>
/// The dev tools dock along the bottom of the game window, over the game (<see cref="Host.GameDock"/>), and pop out into
/// a window of their own (a <see cref="SurfaceWindow"/>) that doesn't cover what it's measuring or take the game's mouse
/// and keyboard. The editor shows them in one of its docks instead.
/// </para>
/// </summary>
internal static partial class DevTools
{
	[ConVar( "devtools_scale", ConVarFlags.Protected | ConVarFlags.Saved, Min = 0.5f, Max = 3f, Help = "Scale of the dev tools, on top of the desktop scale" )]
	internal static float WindowScale { get; set; } = 1f;

	[ConVar( "devtools_popout", ConVarFlags.Protected | ConVarFlags.Saved, Help = "Show the dev tools in a window of their own rather than docked along the bottom of the game window" )]
	internal static bool PoppedOut { get; set; }

	/// <summary>
	/// What the dev tools are drawn in. Each gets different buttons in the strip.
	/// </summary>
	internal enum Host
	{
		/// <summary>
		/// A window of their own. The strip is its title bar, with the window buttons and one to dock back into the game.
		/// </summary>
		Window,

		/// <summary>
		/// Docked along the bottom of the game window, over the game, with a button to pop out into a window.
		/// </summary>
		GameDock,

		/// <summary>
		/// An editor dock, which has its own title and buttons.
		/// </summary>
		EditorDock,
	}

	[ConVar( "devtools_advanced", ConVarFlags.Protected | ConVarFlags.Saved, Help = "Show every dev tools tab, not just the overview. Always on in the editor and with sv_cheats" )]
	internal static bool AdvancedEnabled { get; set; }

	/// <summary>
	/// The size the popped out window opens at: wide and short, to sit under the game.
	/// </summary>
	static readonly Vector2 WindowSize = new( 1560, 340 );

	/// <summary>
	/// Whether the developer tabs are available. Players get the overview; the rest needs the editor, cheats, or
	/// opting in with <c>devtools_advanced</c>.
	/// </summary>
	internal static bool ShowAdvanced => Application.IsEditor || Application.CheatsEnabled || AdvancedEnabled;

	/// <summary>
	/// Whether the tabs that would work as a wallhack are available - the audio tab shows where sounds are. Their
	/// overlays were cheat convars; this keeps them that way.
	/// </summary>
	internal static bool ShowCheatTabs => Application.IsEditor || Application.CheatsEnabled;

	/// <summary>
	/// Is the dev tools on screen?
	/// </summary>
	internal static bool IsOpen { get; private set; }

	/// <summary>
	/// The tab being shown. Opens on the console; falls back to the overview when that tab isn't available.
	/// </summary>
	internal static string ActiveTab { get; set; } = ConsoleTabId;

	internal const string OverviewTabId = "overview";
	internal const string ConsoleTabId = "console";

	/// <summary>
	/// What the dev tools costs to run, so it can be told apart from what it's measuring.
	/// </summary>
	internal static PerformanceStats.Timings Timing { get; } = PerformanceStats.Timings.Get( "Dev Tools", "#9aa5b1" );

	[ConCmd( "devtools", ConVarFlags.Protected, Help = "Toggle the dev tools. Pass a tab name (overview, cpu, gpu, memory, rendering, network, ui, audio) to open it on that tab" )]
	internal static void ToggleCommand( string tab = "" )
	{
		if ( Application.IsHeadless )
			return;

		if ( !string.IsNullOrWhiteSpace( tab ) )
		{
			ActiveTab = tab.Trim().ToLowerInvariant();
			Open();
			return;
		}

		if ( IsOpen ) Close();
		else Open();
	}

	internal static void Open()
	{
		// The editor shows them in a dock instead of a window
		if ( ShowDock is not null )
		{
			ShowDock( true );
			return;
		}

		IsOpen = true;
		Analyse( force: true );
	}

	internal static void Close()
	{
		if ( ShowDock is not null )
		{
			ShowDock( false );
			return;
		}

		IsOpen = false;
		CloseWindow();
		CloseGameDock();
	}

	/// <summary>
	/// Move the dev tools out of the game window into a window of their own, or back.
	/// </summary>
	internal static void SetPoppedOut( bool poppedOut )
	{
		if ( PoppedOut == poppedOut ) return;
		PoppedOut = poppedOut;
		_windowPosition = null;
	}

	/// <summary>
	/// Set by the editor, which shows dev tools in one of its docks rather than a window of their own: opening and
	/// closing them shows and hides that dock.
	/// </summary>
	internal static Action<bool> ShowDock { get; set; }

	/// <summary>
	/// Whether dev tools are on screen in an editor dock. While they are, they count as open and there's no window.
	/// </summary>
	internal static bool Docked { get; private set; }

	/// <summary>
	/// The editor dock appeared or went away - closed, or hidden behind another tab.
	/// </summary>
	internal static void SetDocked( bool docked )
	{
		if ( Docked == docked ) return;
		Docked = docked;
		IsOpen = docked;

		if ( docked )
		{
			CloseWindow();
			CloseGameDock();
			Analyse( force: true );
		}
	}

	/// <summary>
	/// Fill a root with dev tools: the window's own, the game dock's, or an editor dock's.
	/// </summary>
	internal static void BuildRoot( RootPanel root, Host host )
	{
		root.AddClass( "devtools-root" );
		root.StyleSheet.Add( DevToolsStyles.Sheet );

		var window = new DevToolsWindow( host );
		if ( host == Host.GameDock ) root.AddChild( new DevToolsGameDock( window ) );
		else root.AddChild( window );
	}

	/// <summary>
	/// Is this tab open and on screen? Data that's only collected while someone is looking at it - GPU timelines,
	/// allocation tracking, network message sizes, sound markers - checks this.
	/// </summary>
	internal static bool IsShowing( string tab )
	{
		if ( !IsOpen || !string.Equals( ActiveTab, tab, StringComparison.OrdinalIgnoreCase ) ) return false;
		if ( tab == OverviewTabId ) return true;
		if ( tab == "audio" ) return ShowCheatTabs;
		return ShowAdvanced;
	}

	static readonly Dictionary<string, double> _collectUntil = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Run a tab's collectors for a while without the tab on screen, so the MCP tools (<see cref="Editor.Mcp.PerfTools"/>)
	/// can read what the tab would show. The overview's collectors are the GPU's.
	/// </summary>
	internal static void CollectFor( string tab, float seconds )
	{
		var until = RealTime.Now + seconds;
		if ( until > _collectUntil.GetValueOrDefault( tab ) ) _collectUntil[tab] = until;
	}

	/// <summary>
	/// Should the data behind this tab be collected? True while it's showing, or while a tool asked for it with
	/// <see cref="CollectFor"/>. Collection only - drawing in the world stays with <see cref="IsShowing"/>.
	/// </summary>
	internal static bool IsCollecting( string tab ) =>
		IsShowing( tab ) || RealTime.Now < _collectUntil.GetValueOrDefault( tab );

	/// <summary>
	/// The dev tools reads GPU pass timings for the overview's top costs and the GPU tab, so profiling runs while it's open.
	/// </summary>
	internal static bool WantsGpuTimings => IsOpen || IsCollecting( "gpu" );

	/// <summary>
	/// The frame picked out for a closer look, by its <see cref="FrameHistory.Number"/>. Picking one pauses the dev tools, so
	/// the frame stays put.
	/// </summary>
	internal static long? SelectedFrame { get; private set; }

	/// <summary>
	/// How many frames back the selected frame is in <see cref="ShownFrames"/>, or -1 with none selected.
	/// </summary>
	internal static int SelectedAge => SelectedFrame is { } n ? ShownFrames.AgeOf( n ) : -1;

	/// <summary>
	/// Pick the frame <paramref name="age"/> frames back in what's showing. Pauses first, so ages stop moving.
	/// </summary>
	internal static void SelectFrame( int age )
	{
		if ( age < 0 || age >= ShownFrames.Count ) return;
		var number = ShownFrames.Number( age );
		Paused = true;
		SelectedFrame = number;
	}

	internal static void ClearSelection() => SelectedFrame = null;

	static SurfaceWindow _surfaceWindow;
	static Vector2? _windowPosition;

	/// <summary>
	/// How often the window redraws. Its numbers move four times a second; it doesn't need to keep up with the game,
	/// and every frame it skips is a layout and a present the game doesn't pay for.
	/// </summary>
	const float WindowFrameRate = 30;

	/// <summary>
	/// Keep the window or the game dock in step with <see cref="IsOpen"/> and the analysis up to date. Called once a frame
	/// from the engine loop; the window lays itself out and draws from the engine's panel window pass, the game dock from
	/// the game window's overlay pass.
	/// </summary>
	internal static void Simulate()
	{
		if ( Application.IsHeadless || !IsOpen )
			return;

		using var _ = Timing.Scope();

		Analyse();

		// The editor's dock draws them itself
		if ( Docked ) return;

		if ( !PoppedOut && GameWindow.Current is not null )
		{
			CloseWindow();
			SimulateGameDock();
			return;
		}

		CloseGameDock();
		EnsureWindow();
		_surfaceWindow.Surface.Scale = WindowScale;
	}

	static void EnsureWindow()
	{
		if ( _surfaceWindow is not null )
			return;

		// Borderless: the dev tools' strip is the title bar, the way the launcher draws its own
		var window = new SurfaceWindow( "Dev Tools", WindowSize, _windowPosition, borderless: true )
		{
			MaxFramesPerSecond = WindowFrameRate,
			RoundedCorners = true,
			FrameTiming = Timing,
			BackgroundColor = new Color( 0.086f, 0.086f, 0.086f ),
		};

		BuildRoot( window.Root, Host.Window );

		// Closing the window closes the dev tools
		window.OnCloseRequested = () =>
		{
			ForgetWindow();
			IsOpen = false;
			return true;
		};

		_surfaceWindow = window;
		if ( _windowPosition is null ) window.SnapToDisplay();
	}

	static void CloseWindow()
	{
		var window = _surfaceWindow;
		if ( window is null ) return;

		ForgetWindow();
		window.Dispose();
	}

	/// <summary>
	/// Remember where the window was, so it opens there again this session.
	/// </summary>
	static void ForgetWindow()
	{
		if ( _surfaceWindow is null ) return;

		if ( _surfaceWindow.IsShown ) _windowPosition = _surfaceWindow.Position;
		_surfaceWindow = null;
	}
}

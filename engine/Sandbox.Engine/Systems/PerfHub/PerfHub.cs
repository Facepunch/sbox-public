using Sandbox.UI;

namespace Sandbox.Diagnostics;

/// <summary>
/// The performance hub (<c>perf_hub</c>, F11): one place for the engine's performance numbers, which used to be a
/// separate <c>overlay_*</c> convar each, stacked up the side of the screen. The overview tab says what's limiting
/// the frame and what's worth looking at, for anyone. The other tabs are the detail behind it, for developers - see
/// <see cref="ShowAdvanced"/>.
/// <para>
/// The hub is a window of its own (a <see cref="SurfaceWindow"/>), never drawn over the game, so it doesn't cover
/// what it's measuring or take the game's mouse and keyboard.
/// </para>
/// </summary>
internal static partial class PerfHub
{
	[ConVar( "perf_hub_scale", ConVarFlags.Protected | ConVarFlags.Saved, Min = 0.5f, Max = 3f, Help = "Scale of the performance hub, on top of the desktop scale" )]
	internal static float HubScale { get; set; } = 1f;

	[ConVar( "perf_hub_advanced", ConVarFlags.Protected | ConVarFlags.Saved, Help = "Show every performance hub tab, not just the overview. Always on in the editor and with sv_cheats" )]
	internal static bool AdvancedEnabled { get; set; }

	/// <summary>
	/// Whether the developer tabs are available. Players get the overview; the rest needs the editor, cheats, or
	/// opting in with <c>perf_hub_advanced</c>.
	/// </summary>
	internal static bool ShowAdvanced => Application.IsEditor || Application.CheatsEnabled || AdvancedEnabled;

	/// <summary>
	/// Whether the tabs that would work as a wallhack are available - the audio tab shows where sounds are. Their
	/// overlays were cheat convars; this keeps them that way.
	/// </summary>
	internal static bool ShowCheatTabs => Application.IsEditor || Application.CheatsEnabled;

	/// <summary>
	/// Is the hub on screen?
	/// </summary>
	internal static bool IsOpen { get; private set; }

	/// <summary>
	/// The tab being shown. Falls back to the overview when that tab isn't available.
	/// </summary>
	internal static string ActiveTab { get; set; } = OverviewTabId;

	internal const string OverviewTabId = "overview";

	/// <summary>
	/// What the hub costs to run, so it can be told apart from what it's measuring.
	/// </summary>
	internal static PerformanceStats.Timings Timing { get; } = PerformanceStats.Timings.Get( "Perf Hub", "#9aa5b1" );

	[ConCmd( "perf_hub", ConVarFlags.Protected, Help = "Toggle the performance hub. Pass a tab name (overview, cpu, gpu, memory, rendering, network, ui, audio) to open it on that tab" )]
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
		IsOpen = true;
		Analyse( force: true );
	}

	internal static void Close()
	{
		IsOpen = false;
		CloseWindow();
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
	/// The hub reads GPU pass timings for the overview's top costs and the GPU tab, so profiling runs while it's open.
	/// </summary>
	internal static bool WantsGpuTimings => IsOpen || IsCollecting( "gpu" );

	static SurfaceWindow _surfaceWindow;
	static Vector2? _windowPosition;

	/// <summary>
	/// How often the window redraws. Its numbers move four times a second; it doesn't need to keep up with the game,
	/// and every frame it skips is a layout and a present the game doesn't pay for.
	/// </summary>
	const float WindowFrameRate = 30;

	/// <summary>
	/// Keep the window in step with <see cref="IsOpen"/> and the analysis up to date. Called once a frame from the
	/// engine loop; the window lays itself out and draws from the engine's panel window pass.
	/// </summary>
	internal static void Simulate()
	{
		if ( Application.IsHeadless || !IsOpen )
			return;

		using var _ = Timing.Scope();

		Analyse();
		EnsureWindow();
		_surfaceWindow.Surface.Scale = HubScale;
	}

	static void EnsureWindow()
	{
		if ( _surfaceWindow is not null )
			return;

		// Borderless: the hub's strip is the title bar, the way the launcher draws its own
		var window = new SurfaceWindow( "Performance", new Vector2( 720, 980 ), _windowPosition, borderless: true )
		{
			MaxFramesPerSecond = WindowFrameRate,
			RoundedCorners = true,
			FrameTiming = Timing,
			BackgroundColor = new Color( 0.086f, 0.086f, 0.086f ),
		};

		window.Root.AddClass( "perfhub-root" );
		window.Root.StyleSheet.Add( HubStyles.Sheet );
		window.Root.AddChild( new HubWindow() );

		// Closing the window closes the hub
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

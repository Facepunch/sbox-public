namespace Sandbox;

/// <summary>
/// Configuration constants and ConVars for the server-side visibility / fog-of-war system.
/// All tuning knobs live here so they can be adjusted without touching logic code.
/// </summary>
public static class VisibilityConfig
{
	// ───────────────────────────────────────────────────────────────
	//  Spatial Grid
	// ───────────────────────────────────────────────────────────────

	/// <summary>
	/// Size of each spatial grid cell in world units.
	/// Smaller = more precise bucketing but more cells.  512–1024 is a good default for
	/// typical s&amp;box maps.
	/// </summary>
	[ConVar( "sv_vis_grid_cell_size", ConVarFlags.Protected,
		Help = "Spatial grid cell size in world units." )]
	public static float GridCellSize { get; set; } = 512f;

	/// <summary>
	/// Maximum distance (world units) at which we even bother doing LoS checks.
	/// Beyond this range the entity is always culled regardless of line-of-sight.
	/// </summary>
	[ConVar( "sv_vis_max_range", ConVarFlags.Protected,
		Help = "Maximum visibility range in world units." )]
	public static float MaxVisibilityRange { get; set; } = 8192f;

	/// <summary>
	/// Squared version of <see cref="MaxVisibilityRange"/>, cached to avoid sqrt in hot paths.
	/// Recomputed lazily.
	/// </summary>
	public static float MaxVisibilityRangeSq => MaxVisibilityRange * MaxVisibilityRange;

	// ───────────────────────────────────────────────────────────────
	//  Line-of-Sight
	// ───────────────────────────────────────────────────────────────

	/// <summary>
	/// How many BBox corners to test per entity.  The full set is 8 (all corners of the
	/// axis-aligned bounding box) plus the center, totalling 9 test points.
	/// If ANY point is visible the entity is considered visible.
	/// </summary>
	public const int BBoxTestPoints = 9; // 8 corners + center

	/// <summary>
	/// Tag applied to surfaces that should NOT block visibility (glass, fences, etc.).
	/// Traces will use <c>WithoutTags</c> to skip these.
	/// </summary>
	public const string TranslucentTag = "translucent";

	/// <summary>
	/// Additional tags that should never block LoS traces (triggers, water surfaces, etc.).
	/// </summary>
	public static readonly string[] NonBlockingTags = { TranslucentTag, "trigger", "water" };

	// ───────────────────────────────────────────────────────────────
	//  Tick Interleaving
	// ───────────────────────────────────────────────────────────────

	/// <summary>
	/// Maximum number of full LoS checks (per observer) to perform in a single server tick.
	/// Remaining targets are deferred to the next tick.  This prevents CPU spikes when many
	/// players are in the same area.
	/// </summary>
	[ConVar( "sv_vis_max_checks_per_tick", ConVarFlags.Protected,
		Help = "Max LoS checks per observer per tick." )]
	public static int MaxLoSChecksPerTick { get; set; } = 16;

	/// <summary>
	/// How many server ticks a cached visibility result is considered valid before
	/// a full re-check is required.  Higher values save CPU but increase the window
	/// in which a stale result could be wrong.
	/// </summary>
	[ConVar( "sv_vis_cache_ticks", ConVarFlags.Protected,
		Help = "Number of ticks a cached visibility result stays valid." )]
	public static int CacheValidTicks { get; set; } = 3;

	// ───────────────────────────────────────────────────────────────
	//  Predictive Pre-fetching
	// ───────────────────────────────────────────────────────────────

	/// <summary>
	/// How far ahead (in seconds) to predict an entity's future position when deciding
	/// whether to start transmitting early.  Scaled by the observer's ping.
	/// </summary>
	[ConVar( "sv_vis_predict_time", ConVarFlags.Protected,
		Help = "Prediction lookahead time in seconds." )]
	public static float PredictionLookaheadTime { get; set; } = 0.15f;

	/// <summary>
	/// Minimum ping (ms) before we start adding extra prediction buffer.
	/// Below this threshold the base <see cref="PredictionLookaheadTime"/> is used as-is.
	/// </summary>
	public const float PingPredictionThresholdMs = 60f;

	/// <summary>
	/// Extra prediction time added per millisecond of ping above the threshold.
	/// e.g. at 160 ms ping → extra = (160-60) * 0.001 = 0.1 s added.
	/// </summary>
	public const float PingPredictionScalePerMs = 0.001f;

	// ───────────────────────────────────────────────────────────────
	//  Sound-based Visibility
	// ───────────────────────────────────────────────────────────────

	/// <summary>
	/// Maximum distance at which a "noisy" entity is always transmitted regardless of LoS.
	/// This prevents desync when a player hears footsteps / gunfire but the entity is
	/// behind a wall.
	/// </summary>
	[ConVar( "sv_vis_sound_range", ConVarFlags.Protected,
		Help = "Range in world units for sound-based forced visibility." )]
	public static float SoundVisibilityRange { get; set; } = 2048f;

	/// <summary>
	/// How long (seconds) after the last noise event an entity remains force-visible.
	/// </summary>
	[ConVar( "sv_vis_sound_linger", ConVarFlags.Protected,
		Help = "Seconds an entity stays visible after making noise." )]
	public static float SoundLingerTime { get; set; } = 1.0f;

	// ───────────────────────────────────────────────────────────────
	//  Culling Hysteresis
	// ───────────────────────────────────────────────────────────────

	/// <summary>
	/// Seconds an entity must remain invisible before it is actually culled from the
	/// network stream.  This prevents rapid show/hide flickering at visibility boundaries.
	/// The engine already has a 2 s <c>CullDelay</c> in <see cref="NetworkObject"/>; this
	/// value is an additional layer on top of that.
	/// </summary>
	[ConVar( "sv_vis_cull_grace", ConVarFlags.Protected,
		Help = "Grace period (seconds) before culling after losing visibility." )]
	public static float CullGracePeriod { get; set; } = 0.5f;

	// ───────────────────────────────────────────────────────────────
	//  Debug
	// ───────────────────────────────────────────────────────────────

	/// <summary>
	/// Enable the server-side debug overlay that draws green/red LoS lines.
	/// </summary>
	[ConVar( "sv_vis_debug", ConVarFlags.Protected,
		Help = "Enable visibility debug overlay." )]
	public static bool DebugEnabled { get; set; } = false;

	/// <summary>
	/// When debug is enabled, only draw lines for this specific Steam ID (0 = all players).
	/// </summary>
	[ConVar( "sv_vis_debug_steamid", ConVarFlags.Protected,
		Help = "Filter debug overlay to a specific Steam ID (0 = all)." )]
	public static ulong DebugFilterSteamId { get; set; } = 0;
}

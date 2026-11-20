namespace Sandbox.Rendering;

/// <summary>
/// A native light's type (<c>CSceneLightObject::GetLightType</c>).
/// </summary>
internal enum ShadowLightType
{
	Point = 1,
	Directional = 2,
	Spot = 3,
}

/// <summary>
/// The shadow settings players and games set as convars. Borealis reads them through <c>SboxShadowSettings</c> and draws
/// its own shadows (<c>Borealis.Shadows.ShadowMapper</c>).
/// </summary>
internal static class ShadowMapper
{
	[ConVar( "r.shadows.max", Min = -1, Max = 256, Help = "Maximum number of shadow-casting local lights. Lights are sorted by screen size, least important are culled first." )]
	public static int MaxShadows { get; set; } = 256;

	[ConVar( "r.shadows.maxresolution", Min = 128, Max = 4096, Help = "Max texture size (square) for a projected light shadow map, higher is better but uses more vram, these are scaled automatically too." )]
	public static int MaxResolution { get; set; } = 1024; // Low=512, Medium/High=1024, Ultra=2048

	[ConVar( "r.shadows.quality", Min = 0, Max = 4, Help = "What filtering to use, higher is more GPU intensive. 0 = Off, 1 = Low, 2 = Med, 3 = High, 4 = Experimental Penumbra Shadows" )]
	public static int ShadowFilter { get; set; } = 3;

	[ConVar( "r.shadows.csm.maxcascades", Min = 1, Max = 4, Help = "Maximum number of cascades for directional light shadows." )]
	public static int MaxCascades { get; set; } = 3;

	[ConVar( "r.shadows.csm.maxresolution", Min = 512, Max = 8192, Help = "Maximum resolution for each cascade shadow map." )]
	public static int MaxCascadeResolution { get; set; } = 2048;

	[ConVar( "r.shadows.csm.distance", Min = 500, Max = 50000, Help = "Maximum distance from the camera that directional light shadows are rendered." )]
	public static float CascadeDistance { get; set; } = 15000;

	[ConVar( "r.shadows.csm.enabled", Help = "Enable directional light (CSM) shadows." )]
	public static bool CSMEnabled { get; set; } = true;

	[ConVar( "r.shadows.local.enabled", Help = "Enable local light (spot/point) shadows." )]
	public static bool LocalShadowsEnabled { get; set; } = true;

	[ConVar( "r.shadows.updates", Min = 1, Max = 256, Help = "How many local light shadow maps may be re-rendered per frame. The most stale lights (weighted by screen size) go first; new, moved and resized lights always update." )]
	public static int MaxUpdatesPerFrame { get; set; } = 8;

	[ConVar( "r.shadows.depthbias", Min = -256, Max = 0, Help = "Rasterizer constant depth bias applied during shadow map rendering. More negative = stronger bias." )]
	public static int ShadowDepthBias { get; set; } = -1;

	[ConVar( "r.shadows.slopescale", Min = -16.0f, Max = 0.0f, Help = "Rasterizer slope-scaled depth bias applied during shadow map rendering. More negative = stronger bias on angled surfaces." )]
	public static float ShadowSlopeScale { get; set; } = -1.5f;

	[ConVar( "r.shadows.size_cull_threshold", Min = 0.0f, Max = 1.0f, Help = "Threshold of screen size percentage below which projected shadows get culled" )]
	public static float SizeCullThreshold { get; set; } = 0.25f;
}

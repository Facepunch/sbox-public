namespace Sandbox.Rendering;

/// <summary>
/// The managed scene renderer's own debug views, which only draw while it renders the camera (<c>r_managed_scene 1</c>):
/// LOD colours, cull reasons and the shadow map viewer. Its host reads these, and gives back what each frame found for
/// <see cref="DebugOverlay.ManagedViews"/> to label. See docs/managed/basalt.md, <i>Debug views</i>.
/// </summary>
internal static class ManagedDebugViews
{
	[ConVar( "r_managed_debug_lod", ConVarFlags.Cheat, Help = "Tint every mesh the managed scene renderer draws by the LOD it drew at" )]
	public static bool LodColors { get; set; }

	[ConVar( "r_managed_debug_cull", ConVarFlags.Cheat, Help = "Draw what the managed scene renderer culled on the CPU anyway, tinted by why, and show occlusion culling's boxes, with counts" )]
	public static bool CullReasons { get; set; }

	[ConVar( "r_managed_debug_shadows", ConVarFlags.Cheat, Help = "Show the managed scene renderer's shadow maps - the sun's cascades, local lights' maps and static caches - with their resolution, last render and cache state" )]
	public static bool ShadowMaps { get; set; }

	/// <summary>
	/// The views on.
	/// </summary>
	public static Basalt.DebugViews Views => (LodColors ? Basalt.DebugViews.LodColors : 0)
		| (CullReasons ? Basalt.DebugViews.CullReasons : 0)
		| (ShadowMaps ? Basalt.DebugViews.ShadowMaps : 0);

	/// <summary>
	/// What the last frame drawn into a window found, or null. Its renderer fills it again each frame.
	/// </summary>
	public static Basalt.DebugViewReport Latest { get; set; }
}

namespace Sandbox.Rendering;

/// <summary>
/// Borealis's switches and its frame counters. A process drawing on Borealis.Gpu (<c>-borealisgpu</c>) renders every scene
/// through Borealis (<see cref="SceneRenderer"/>); any other renders through native's scene system.
/// See docs/managed/borealis.md, <i>The GameObject bridge</i>, and docs/managed/render-paths.md.
/// </summary>
internal static partial class ManagedSceneRendering
{
	/// <summary>
	/// Whether Borealis renders this process's scenes: it draws on a Borealis.Gpu device (<c>-borealisgpu</c>).
	/// </summary>
	public static bool Enabled => RenderBackend.Default is BorealisGpuRenderBackend;

	[ConVar( "r_managed_occlusion", Help = "Skip opaque meshes hidden by the depth prepass in the managed scene renderer" )]
	public static bool Occlusion { get; set; }
}

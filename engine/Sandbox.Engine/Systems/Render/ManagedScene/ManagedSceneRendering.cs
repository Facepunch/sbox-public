namespace Sandbox.Rendering;

/// <summary>
/// Borealis's switches and its frame counters. A process drawing on Borealis.Gpu (<c>-borealisgpu</c>) renders every scene
/// through Borealis (<see cref="SceneRenderer"/>); any other renders none.
/// See docs/reference/borealis.md, <i>The GameObject bridge</i>, and docs/reference/borealis-render-paths.md.
/// </summary>
internal static partial class ManagedSceneRendering
{
	/// <summary>
	/// Whether Borealis renders this process's scenes: it draws on a Borealis.Gpu device (<c>-borealisgpu</c>).
	/// </summary>
	public static bool Enabled => RenderBackend.Default is BorealisGpuRenderBackend;

	[ConVar( "r_managed_occlusion", Help = "Skip opaque meshes hidden by the depth prepass in the managed scene renderer" )]
	public static bool Occlusion { get; set; }

	/// <summary>
	/// How occlusion culling handles aggregate fragments. Must match Borealis's <c>FragmentOcclusion</c>.
	/// </summary>
	internal enum AggregateOcclusion
	{
		Off,

		/// <summary>Hidden fragments are drawn with no instances.</summary>
		Empty,

		/// <summary>Hidden fragments are removed from the draw. Same as <see cref="Empty"/> without drawIndirectCount.</summary>
		Compact,
	}

	[ConVar( "r_occlusion_aggregates", Min = 0, Max = 2, Help = "Occlusion cull aggregate fragments (0: off, 1: draw hidden ones empty, 2: compact, needs drawIndirectCount)" )]
	internal static int AggregateMode { get; set; } = (int)AggregateOcclusion.Compact;

	internal static AggregateOcclusion OcclusionAggregates => (AggregateOcclusion)AggregateMode;
}

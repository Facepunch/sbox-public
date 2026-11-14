namespace Borealis;

/// <summary>
/// How aggregate fragments are occlusion culled (<see cref="RenderHost.FragmentOcclusion"/>).
/// </summary>
public enum FragmentOcclusion
{
	/// <summary>Fragments aren't culled.</summary>
	Off,

	/// <summary>Hidden fragments draw empty.</summary>
	Empty,

	/// <summary>Hidden fragments are compacted out of the draw, which needs drawIndirectCount.</summary>
	Compact,
}

/// <summary>
/// What occlusion culling's debug view draws (<see cref="RenderHost.OcclusionDebugView"/>).
/// </summary>
public enum OcclusionDebugView
{
	/// <summary>Nothing.</summary>
	Off,

	/// <summary>Boxes of what was hidden.</summary>
	Hidden,

	/// <summary>Boxes of everything tested.</summary>
	All,

	/// <summary>Boxes of what was hidden, over the depth they were tested against.</summary>
	Depth,
}

/// <summary>
/// How the early occlusion cull runs (<see cref="RenderHost.OcclusionEarlyCull"/>).
/// </summary>
public enum OcclusionEarlyCull
{
	/// <summary>No early cull: the late cull tests everything.</summary>
	Off,

	/// <summary>Cull against last frame's depth first.</summary>
	On,

	/// <summary>Reject everything early, so the late prepass draws all that's visible.</summary>
	RejectAll,
}

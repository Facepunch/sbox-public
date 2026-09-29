namespace Sandbox.SceneRenderer;

/// <summary>
/// What a layer makes for later layers in the same frame (<see cref="RenderLayer.Writes"/>) or reads of what earlier ones
/// made (<see cref="RenderLayer.Reads"/>). A layer that makes something is only kept while a later layer that's kept reads
/// it (<see cref="FrameRecorder"/>), as Unity's render graph culls passes nothing reads; one that makes nothing draws into
/// the frame, and is kept whenever it's needed. Internal to the renderer: camera stages read what native's hook layers
/// could, since what their command lists read can't be known.
/// </summary>
[Flags]
internal enum FrameResources : ushort
{
	None = 0,

	/// <summary>
	/// The depth with min and max in every mip (<see cref="DepthChainLayer"/>, <c>DepthChainDownsample</c>).
	/// </summary>
	DepthChain = 1 << 0,

	/// <summary>
	/// The quarter-resolution depth bloom objects test against (<see cref="QuarterDepthDownsampleLayer"/>).
	/// </summary>
	QuarterDepth = 1 << 1,

	/// <summary>
	/// Bloom objects drawn into the bloom target, not blurred yet (<see cref="BloomLayer"/>).
	/// </summary>
	BloomObjects = 1 << 2,

	/// <summary>
	/// The blurred bloom target the bloom effect reads (<see cref="BloomBlurLayer"/>, <c>QuarterResEffectsBloomInputTexture</c>).
	/// </summary>
	BloomInput = 1 << 3,

	/// <summary>
	/// Glass's depth for the frame copy's mask (<see cref="RefractionStencilLayer"/>).
	/// </summary>
	RefractionStencil = 1 << 4,
}

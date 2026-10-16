namespace Sandbox.SceneRenderer;

/// <summary>
/// What a layer makes for later layers in the same frame (<see cref="RenderLayer.Writes"/>) or reads of what earlier ones
/// made (<see cref="RenderLayer.Reads"/>). A layer that makes something is only kept while a later layer that's kept reads
/// it (<see cref="FrameRecorder"/>), unless it has side effects. Cross-queue conflicts determine semaphore waits.
/// Internal to the renderer: camera stages declare known inputs and untracked access, since their command lists cannot
/// be inspected for complete resource dependencies.
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
	/// Bloom objects drawn into the bloom target the bloom effect reads (<see cref="BloomLayer"/>,
	/// <c>QuarterResEffectsBloomInputTexture</c>).
	/// </summary>
	BloomObjects = 1 << 2,

	/// <summary>
	/// Glass's depth for the frame copy's mask (<see cref="RefractionStencilLayer"/>).
	/// </summary>
	RefractionStencil = 1 << 4,

	/// <summary>The main view's depth attachment.</summary>
	Depth = 1 << 5,

	/// <summary>The depth prepass's normals and roughness.</summary>
	Normals = 1 << 6,

	/// <summary>The ambient occlusion effect's output and history.</summary>
	AmbientOcclusion = 1 << 7,

	/// <summary>The screen-space contact shadow mask.</summary>
	ContactShadows = 1 << 8,

	/// <summary>The reflection effect's output and history.</summary>
	Reflections = 1 << 9,

	/// <summary>Shadow maps, including their cached contents.</summary>
	ShadowMaps = 1 << 10,

	/// <summary>Arbitrary shader, camera-hook or custom-object accesses. Outstanding compute must finish first.</summary>
	Untracked = 1 << 15,
}

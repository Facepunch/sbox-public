namespace Sandbox.Rendering;

[Flags]
internal enum LayerFlags : UInt64
{
	NeedsFullSort = 0x0000000000000001,
	DoNotSort = 0x0000000000000002,
	NeverRemove = 0x0000000000000004,
	DebugDraw = 0x0000000000000008,
	DebugBreakOnRendering = 0x0000000000000010,
	DebugBreakOnSubmission = 0x0000000000000020,
	DoesntModifyColorBuffers = 0x0000000000000040,
	DiscardColorBuffers = 0x0000000000000100,
	DoesntModifyDepthStencilBuffer = 0x0000000000000200,
	DiscardDepthStencilBuffer = 0x0000000000000400,
	PreserveColorBuffers = 0x0000000000000800,
	PreserveDepthBuffer = 0x0000000000001000,

	PrimaryTargetOutput = 0x0000000000002000,
	MatchTargetViewportSiz = 0x0000000000004000,

	LightBinnerSetupLayer = 0x0000000000010000,
	ReadOnlyDepthStencilNoResolve = 0x0000000000020000,
	NeedsPerViewLightingConstants = 0x0000000000040000,
	IsDepthRenderingPass = 0x0000000000080000,

	ReadOnlyDepthStencil = 0x0000000000100000,
	PreserveStencilBuffer = 0x0000000000200000,

	ClearWholeTargetViewportSize = 0x0000000000400000,

	NoPerLayerViewConstants = 0x0000000000800000,
	RequirePrimaryRenderContext = 0x0000000001000000,

	ForceDepthFastPath = 0x0000000002000000,

	IgnoreLayerMatchID = 0x0000000004000000,

	CountArtistTriangles = 0x0000000008000000,
	ShowHiPolyDrawCalls = 0x0000000010000000,
	AsyncComputeContext = 0x0000000020000000,
	ByRegionDependency = 0x0000000040000000,
	StartRenderPass = 0x0000000080000000,

	RemoveIfOtherLayerNoRenderObjects = 0x0000000100000000,

	FadeWithoutAlphaBlending = 0x0000000400000000,

	OnlyKickDependentViews = 0x0000000800000000,

	/// <summary>
	/// This layer does not need to store the results of its color target rendering 
	/// </summary>
	DiscardColorBuffersStore = 0x0000001000000000,

	/// <summary>
	/// This layer does not need to store the results of its depth/stencil rendering
	/// </summary>
	DiscardDepthStencilBufferStore = 0x0000002000000000,

	/// <summary>
	/// Similar to FullSort, but instead of using depth (or custom sort key), use default sorting
	/// </summary>
	SortAcrossPartitions = 0x0000004000000000,

	/// <summary>
	/// Any layer marked IsDepthRenderingPass is subject to ShouldOverrideDepthMaterial.
	/// Setting this disables ShouldOverrideDepthMaterial for depth rendering.
	/// </summary>
	NoOverrideDepthMaterial = 0x0000008000000000,

	/// <summary>
	/// Use pyramid geometry for light rendering instead of cone geo. Don't restrict to geometry
	/// </summary>
	UseVolumePyramidSpotlightGeo = 0x0000010000000000,

	/// <summary>
	/// Only draw what passed the view's GPU occlusion cull.
	/// </summary>
	OcclusionCulled = 0x0000020000000000,

	/// <summary>
	/// Depth prepass that only draws what passed the occlusion cull against last frame's depth.
	/// </summary>
	OcclusionCulledEarly = 0x0000040000000000,

	/// <summary>
	/// Depth prepass for what the early occlusion cull rejected but is visible now.
	/// </summary>
	OcclusionCulledLate = 0x0000080000000000,
};

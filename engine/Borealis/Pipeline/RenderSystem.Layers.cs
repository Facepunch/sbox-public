namespace Borealis;

/// <summary>
/// Layer and camera-stage order, matching <c>CRenderingPipelineStandard::AddLayersToView</c>.
/// </summary>
public sealed partial class RenderSystem
{
	RenderLayer[] layers;

	/// <summary>
	/// This system's layers, in frame order: the standard pipeline's, or a 3D skybox's.
	/// </summary>
	internal RenderLayer[] Layers => layers ??= isSkybox ? SkyboxLayers() : StandardLayers();

	ContactShadowLayer contactShadows;
	ScreenSpaceReflectionsLayer reflections;

	RenderLayer[] StandardLayers()
	{
		var bloom = new BloomLayer();
		contactShadows = new ContactShadowLayer();
		reflections = new ScreenSpaceReflectionsLayer();
		var overlayPrepass = new OverlayDepthPrepassLayer();
		return
		[
			new OcclusionCullLayer( early: true ),

			// Viewmodels claim depth and stencil before the world.
			overlayPrepass,
			new DepthPrepassLayer( overlayPrepass ),

			new OcclusionCullLayer( early: false ),
			new LateDepthPrepassLayer( overlayPrepass ),

			// Pass inputs and outputs determine queue waits; this list is also the serial execution order.
			new DepthChainLayer(),
			new IdsNormalsLayer(),
			new EarlyStageLayer( RenderStage.AfterDepthPrepass, StageQueue.AsyncCompute ),
			contactShadows,
			new EarlyStageLayer( RenderStage.AfterDepthPrepass, StageQueue.Graphics ),
			reflections,
			new ShadowLayer(),
			new ShadowsReadyLayer(),

			// Quarter-resolution bloom with scene-depth occlusion.
			new QuarterDepthDownsampleLayer( bloom ),
			bloom,

			// Glass depth for the frame-copy mask.
			new RefractionStencilLayer(),

			// Contact shadows and shadow maps precede camera commands.
			new StageLayer( RenderStage.AfterDepthPrepass, color: false ),

			new OpaqueLayer(),

			// Tools-only materials.
			new ToolsUtilLayer( translucent: false ),
			new StaticOverlayLayer(),
			new ScreenSpaceReflectionsHistoryLayer(),
			new StageLayer( RenderStage.AfterOpaque ),

			new Skybox3DLayer(),
			new SkyLayer(),
			new StageLayer( RenderStage.AfterSkybox ),

			// Decals sample scene depth.
			new DecalLayer(),

			new TranslucentLayer( "Translucent Forward", fade: true ),
			new ToolsUtilLayer( translucent: true ),

			// Depth effects need opaque viewmodel depth; translucent viewmodels draw afterward.
			new GameOverlayLayer(),
			new StageLayer( RenderStage.AfterTransparent ),
			new StageLayer( RenderStage.AfterViewmodel ),
			new GameOverlayTranslucentLayer(),

			// HDR effects, output copy, then screen overlays and UI.
			new StageLayer( RenderStage.EarlyUI ),
			new StageLayer( RenderStage.BeforePostProcess ),
			new StageLayer( RenderStage.Tonemapping ),
			new OutputLayer(),
			new StageLayer( RenderStage.AfterPostProcess, final: true ),
			new ToolOverlayLayer(),
			new ToolScreenOverlayLayer(),
			new ScreenOverlayLayer( withDepth: true ),
			new ScreenOverlayLayer( withDepth: false ),
			new StageLayer( RenderStage.UI, final: true ),
			new StageLayer( RenderStage.AfterUI, final: true ),

			// Overlays above screen UI.
			new AfterUILayer(),

			// Borealis's own debug views, over everything.
			new DebugViewLayer(),

			// Resolve final MSAA output, then mirror it into a flipped view's texture.
			new OutputResolveLayer(),
			new OutputFlipLayer(),

			// Prepare fog for the next frame.
			new VolumetricFogLayer(),

			new OcclusionHistoryLayer(),
		];
	}

	/// <summary>
	/// Skybox pipeline: prepass, forward, unfaded translucency, then 2D sky (<c>Skybox3DPipeline</c>).
	/// </summary>
	static RenderLayer[] SkyboxLayers() =>
	[
		new DepthPrepassLayer( null ),
		new OpaqueLayer(),
		new TranslucentLayer( "3DSkybox Translucent Forward", fade: false ),
		new SkyLayer(),
	];
}

using Sandbox.Rendering;

namespace Sandbox.SceneRenderer;

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

	// Unique pool keys for contact-shadow masks.
	static int systemCount;
	ContactShadowLayer contactShadows;

	RenderLayer[] StandardLayers()
	{
		var bloom = new BloomLayer();
		contactShadows = new ContactShadowLayer( System.Threading.Interlocked.Increment( ref systemCount ) );
		var overlayPrepass = new OverlayDepthPrepassLayer();
		return
		[
			// Viewmodels claim depth and stencil before the world.
			overlayPrepass,
			new DepthPrepassLayer( overlayPrepass ),

			// Compute over the prepass's depth, on the async compute queue where there is one, while the shadow maps draw
			// (FrameRecorder). Native draws its shadow views first; nothing between here and the stage below reads them.
			new DepthChainLayer(),
			contactShadows,
			new AsyncStageLayer( Stage.AfterDepthPrepass ),
			new ShadowLayer(),
			new ShadowsReadyLayer(),

			// Quarter-resolution bloom with scene-depth occlusion.
			new QuarterDepthDownsampleLayer( bloom ),
			bloom,
			new BloomBlurLayer( bloom ),

			// Glass depth for the frame-copy mask.
			new RefractionStencilLayer(),

			// Contact shadows and shadow maps precede camera commands.
			new StageLayer( Stage.AfterDepthPrepass, color: false ),

			new OpaqueLayer(),

			// Tools-only materials.
			new ToolsUtilLayer( translucent: false ),
			new StaticOverlayLayer(),
			new StageLayer( Stage.AfterOpaque ),

			new Skybox3DLayer(),
			new SkyLayer(),
			new StageLayer( Stage.AfterSkybox ),

			// Decals sample scene depth.
			new DecalLayer(),

			new TranslucentLayer( "Translucent Forward", fade: true ),
			new ToolsUtilLayer( translucent: true ),

			// Depth effects need opaque viewmodel depth; translucent viewmodels draw afterward.
			new GameOverlayLayer(),
			new StageLayer( Stage.AfterTransparent ),
			new StageLayer( Stage.AfterViewmodel ),
			new GameOverlayTranslucentLayer(),

			// HDR effects, output copy, then screen overlays and UI.
			new StageLayer( Stage.EarlyUI ),
			new StageLayer( Stage.BeforePostProcess ),
			new StageLayer( Stage.Tonemapping ),
			new OutputLayer(),
			new StageLayer( Stage.AfterPostProcess, final: true ),
			new ScreenOverlayLayer( withDepth: true ),
			new ScreenOverlayLayer( withDepth: false ),
			new StageLayer( Stage.UI, final: true ),
			new StageLayer( Stage.AfterUI, final: true ),

			// Overlays above screen UI.
			new AfterUILayer(),

			// Resolve final MSAA output.
			new OutputResolveLayer(),

			// Prepare fog for the next frame.
			new VolumetricFogLayer(),
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

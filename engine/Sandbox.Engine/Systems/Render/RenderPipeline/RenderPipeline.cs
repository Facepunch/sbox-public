using NativeEngine;

namespace Sandbox.Rendering;

/// <summary>
/// Start moving the c++ render pipeline here
/// It won't be the prettiest to start, but we can start simplifying afterwards
/// </summary>
internal partial class RenderPipeline
{
	DepthNormalPrepassLayer DepthNormalLargePrepass { get; } = new( true );
	DepthNormalPrepassLayer DepthNormalSmallPrepass { get; } = new( false );
	LightbinnerLayer LightbinnerLayer { get; } = new();
	DepthDownsampleLayer DepthDownsampleLayer { get; } = new();
	TiledCullingLayer TiledCullingLayer { get; } = new();
	BloomLayer BloomLayer { get; } = new();
	BloomDownsampleLayer BloomDownsampleLayer { get; } = new();
	RefractionStencilLayer RefractionStencilLayer { get; } = new();
	QuarterDepthDownsampleLayer QuarterDepthDownsampleLayer { get; } = new();
	MediaRecorderLayer RecordMovieFrameLayer { get; } = new();
	MediaRecorderOverlayLayer PostRecordMovieFrameLayer { get; } = new();

	internal void AddLayersToView( ISceneView view, RenderViewport viewport, SceneViewRenderTargetHandle rtColor, SceneViewRenderTargetHandle rtDepth, RenderMultisampleType nMSAA, CRenderAttributes pipelineAttrs, RenderViewport screenSize )
	{
		var msaa = nMSAA.FromEngine();
		var pipelineAttributes = new RenderAttributes( pipelineAttrs );

		// renderingpipeline_standard.cpp:1786
		// Already run: clear layer

		{
			LightbinnerLayer.Setup( pipelineAttributes );
			LightbinnerLayer.AddToView( view, viewport );
		}


		// Depth Prepass with a small GBuffer ( Normals, Roughness )
		{
			var gbufferColor = RenderTarget.GetTemporary(
				(int)screenSize.Rect.Width,
				(int)screenSize.Rect.Height,
				colorFormat: ImageFormat.RGBA16161616F,
				depthFormat: ImageFormat.None,
				msaa: msaa );

			//
			// Two layer depth prepass, initial layer renders fewer larger objects, second layer renders everything else
			// matt: I don't think this makes sense anymore, Valve used to do it to opt out of smaller objects entirely.
			//       However doing 1 big pass seems to double draw calls, it's possible it's not rendering everything?
			//
			DepthNormalLargePrepass.Setup( view, gbufferColor, rtDepth );
			var largePrepass = DepthNormalLargePrepass.AddToView( view, viewport );
			largePrepass.SetBoundingVolumeSizeCullThresholdInPercent( 60 );

			DepthNormalSmallPrepass.Setup( view, gbufferColor, rtDepth );
			var smallPrepass = DepthNormalSmallPrepass.AddToView( view, viewport );
			smallPrepass.SetBoundingVolumeSizeCullThresholdInPercent( -60 );

			// Pass that DepthNormals are enabled to the rest of the pipeline
			view.GetRenderAttributesPtr().SetIntValue( "NormalsTextureIndex", gbufferColor.ColorTarget.Index );
		}

		// Compute Async: Depth downscale, tiled culling
		{
			DepthDownsampleLayer.Setup( viewport, rtDepth, msaaInput: msaa != MultisampleAmount.MultisampleNone, view );
			DepthDownsampleLayer.AddToView( view, viewport );

			TiledCullingLayer.Setup( view );
			TiledCullingLayer.AddToView( view, viewport );
		}

		// Bloom layer, Effects that only show up on bloom like a ghost effect
		{
			RenderViewport quarterViewport = viewport / 4;

			var bloomRt = RenderTarget.GetTemporary(
				(int)quarterViewport.Rect.Width,
				(int)quarterViewport.Rect.Height,
				colorFormat: ImageFormat.RGBA1010102,
				depthFormat: ImageFormat.D32,
				numMips: (int)Math.Log2( Math.Max( quarterViewport.Rect.Width, quarterViewport.Rect.Height ) ) );

			QuarterDepthDownsampleLayer.Setup( view, quarterViewport, rtDepth, msaa != MultisampleAmount.MultisampleNone, bloomRt );
			QuarterDepthDownsampleLayer.AddToView( view, quarterViewport );

			BloomLayer.Setup( view, bloomRt );
			BloomLayer.AddToView( view, quarterViewport );

			view.GetRenderAttributesPtr().SetTextureValue( "QuarterResEffectsBloomInputTexture", bloomRt.ColorTarget.native, -1 );

			BloomDownsampleLayer.RT = bloomRt;
			BloomDownsampleLayer.AddToView( view, quarterViewport );
		}

		// Refraction stencil layer, used for filtering out depth on Framebuffer copies
		{
			RenderViewport quarterViewport = viewport / 4;
			RefractionStencilLayer.Setup( view, quarterViewport );
			RefractionStencilLayer.AddToView( view, quarterViewport );
		}

		// Opaque pass
		// Transparent pass
		// Etc.
	}

	internal void PipelineEnd( ISceneView view, RenderViewport viewport, SceneViewRenderTargetHandle rtColor, SceneViewRenderTargetHandle rtDepth, RenderMultisampleType nMSAA, CRenderAttributes pipelineAttrs, RenderViewport screenSize )
	{
		var cameraId = view.m_ManagedCameraId;
		if ( cameraId == 0 )
			return;

		var viewCamera = IManagedCamera.FindById( cameraId );
		if ( viewCamera is null )
			return;

		var mainCamera = IManagedCamera.GetMainCamera();

		if ( viewCamera == mainCamera )
		{
			RecordMovieFrameLayer.AddToView( view, viewport );
			PostRecordMovieFrameLayer.AddToView( view, viewport );
		}
	}
}

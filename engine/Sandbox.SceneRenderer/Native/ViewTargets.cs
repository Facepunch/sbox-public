using NativeEngine;

namespace Sandbox.SceneRenderer.Native;

/// <summary>
/// The scene's colour and depth targets for a standard view. The camera hands over its output; the scene renders into an HDR
/// intermediate when the output isn't one (copied over before overlays and UI), into a depth target of its own when it's
/// multisampled differently, and at a lower resolution when upscaling, into targets the upscaler then fills the output from.
/// </summary>
internal static class ViewTargets
{
	// EUpscalerMode
	const int Fsr1 = 2;

	/// <summary>
	/// Make the targets and set them on <paramref name="setup"/>: <see cref="CRenderPipelineSetup.m_hColor"/> and
	/// <see cref="CRenderPipelineSetup.m_hDepth"/>, the upscaler's, and whether colour gets copied to the output.
	/// </summary>
	public static void Create( CRenderPipelineSetup setup )
	{
		var view = setup.m_pView;
		var width = setup.m_nWidth;
		var height = setup.m_nHeight;
		var msaa = setup.m_nMSAA;
		var upscaling = setup.m_nUpscalerMode != 0;
		var hdrFormat = RenderPipelineTools.GetHdrFormat();

		var color = setup.m_hFinalColor;
		var depth = setup.m_hFinalDepth;
		var resolveToFinal = false;

		// An output that isn't HDR (the swap chain) gets an HDR intermediate, copied to it after post processing
		var outputIsFloat = RenderPipelineTools.IsFloatFormat( RenderPipelineTools.GetTargetFormat( view, setup.m_hFinalColor ) );
		if ( hdrFormat != RenderPipelineTools.GetBackBufferFormat() && !outputIsFloat )
		{
			if ( !upscaling ) color = RenderPipelineTools.FindOrCreateScratchTarget( view, "intermediate_color_stereo_rt", width, height, hdrFormat, msaa, false );
			resolveToFinal = true;
		}

		// We can't use a depth buffer sampled differently: make a matching one
		if ( !upscaling && setup.m_nOutputMSAA != msaa )
		{
			depth = RenderPipelineTools.FindOrCreateScratchTarget( view, "intermediate_depth_stereo_rt", width, height, setup.m_nDepthFormat, msaa, false );
		}

		if ( upscaling )
		{
			const RenderMultisampleType none = RenderMultisampleType.RENDER_MULTISAMPLE_NONE;

			// A non-MSAA full resolution output, written as a UAV by FSR1's RCAS pass; FSR1 has a scratch of its own between
			// its two passes. FidelityFX registers depth as pure D32, not D32S8 or D24S8.
			setup.m_hUpscaleColor = RenderPipelineTools.FindOrCreateScratchTarget( view, "fsr3_output_color", width, height, hdrFormat, none, true );
			if ( setup.m_nUpscalerMode == Fsr1 )
			{
				setup.m_hFsr1IntermediateColor = RenderPipelineTools.FindOrCreateScratchTarget( view, "fsr1_easu_intermediate", width, height, hdrFormat, none, true );
			}

			setup.m_hUpscaleDepth = RenderPipelineTools.FindOrCreateScratchTarget( view, "fsr3_output_depth", width, height, ImageFormat.D32, none, false );

			// The scene at the render resolution
			var renderWidth = setup.m_nUpscaleRenderWidth;
			var renderHeight = setup.m_nUpscaleRenderHeight;
			color = RenderPipelineTools.FindOrCreateScratchTarget( view, "fsr3_render_color", renderWidth, renderHeight, hdrFormat, none, false );
			depth = RenderPipelineTools.FindOrCreateScratchTarget( view, "fsr3_render_depth", renderWidth, renderHeight, ImageFormat.D32, none, false );
			resolveToFinal = true;
		}

		setup.m_hColor = color;
		setup.m_hDepth = depth;
		setup.m_bResolveToFinal = resolveToFinal;
	}
}

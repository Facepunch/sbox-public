using NativeEngine;
using Sandbox.Rendering;

namespace Sandbox.SceneRenderer.Native;

/// <summary>
/// The Hi-Z depth chain (<c>DepthChainDownsample</c>): the depth resolved and min/max downsampled through every mip, for
/// depth-reading effects.
/// </summary>
internal sealed class DepthDownsampleLayer : NativeProceduralLayer
{
	RenderViewport viewport;
	Texture destDepth;
	bool msaaInput;
	internal bool SignalPostProcess { get; set; }

	public DepthDownsampleLayer() : base( "Hi-Z Depth Downsample" )
	{
	}

	public void Setup( ISceneView view, RenderViewport viewport, SceneViewRenderTargetHandle sourceDepth, bool msaaInput, Texture target = null )
	{
		this.viewport = viewport;
		this.msaaInput = msaaInput;
		SetInput( "SourceDepth", sourceDepth );

		destDepth = target ?? SharedPasses.DepthChainTarget( (int)viewport.Rect.Width, (int)viewport.Rect.Height ).DepthTarget;

		view.GetRenderAttributesPtr().SetTextureValue( "DepthChainDownsample", destDepth.native, -1 );
		view.GetRenderAttributesPtr().SetTextureValue( "DepthChainDownsamplePrevFrame", destDepth.native, -1 );
	}

	protected override void OnRender()
	{
		// Our SourceDepth comes from the scene render target system
		SharedPasses.BuildDepthChain( Graphics.Context, null, destDepth, msaaInput, (int)viewport.Rect.Width, (int)viewport.Rect.Height );

		if ( SignalPostProcess )
		{
			// Finish shared-input transitions on graphics before releasing compute.
			Graphics.Context.TextureBarrierTransition( destDepth.native, -1,
				RenderBarrierPipelineStageFlags_t.ComputeShaderBit, RenderBarrierPipelineStageFlags_t.ComputeShaderBit,
				RenderImageLayout_t.RENDER_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,
				RenderBarrierAccessFlags_t.ShaderWriteBit, RenderBarrierAccessFlags_t.ShaderReadBit );
			Graphics.Context.TextureBarrierTransition( Graphics.SceneLayer.GetTextureValue( "NormalsGBuffer" ), -1,
				RenderBarrierPipelineStageFlags_t.ColorAttachmentOutputBit, RenderBarrierPipelineStageFlags_t.ComputeShaderBit,
				RenderImageLayout_t.RENDER_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,
				RenderBarrierAccessFlags_t.ColorAttachmentWriteBit, RenderBarrierAccessFlags_t.ShaderReadBit );
			Graphics.SceneLayer.SetSemaphore( Graphics.Context.SemaphoreSignalAtEnd() );
		}
	}
}

/// <summary>
/// The depth at a quarter of the resolution, into the bloom layer's target, so bloom objects are hidden behind the scene.
/// </summary>
internal sealed class BloomDepthLayer : NativeProceduralLayer
{
	bool msaaInput;

	public BloomDepthLayer() : base( "Quarter Depth Downsample" )
	{
		Flags |= LayerFlags.DoesntModifyColorBuffers;
		ClearFlags = ClearFlags.Depth | ClearFlags.Stencil;
		LayerType = SceneLayerType.Opaque;
	}

	public void Setup( ISceneView view, SceneViewRenderTargetHandle sourceDepth, bool msaaInput, RenderTarget target )
	{
		SetInput( "SourceDepth", sourceDepth );
		this.msaaInput = msaaInput;

		ColorAttachment = target.ToColorHandle( view );
		DepthAttachment = target.ToDepthHandle( view );
	}

	protected override void OnRender()
	{
		SharedPasses.DownsampleQuarterDepth( Graphics.Attributes, msaaInput );
	}
}

/// <summary>Reconstruct the scene depth at output resolution for post-processing and overlays.</summary>
internal sealed class UpscaleDepthLayer : NativeProceduralLayer
{
	bool msaaInput;
	Vector2 outputToSourceScale;

	public UpscaleDepthLayer() : base( "Upscale Depth" )
	{
		Flags |= LayerFlags.DoesntModifyColorBuffers;
		LayerType = SceneLayerType.Opaque;
	}

	public void Setup( SceneViewRenderTargetHandle sourceDepth, SceneViewRenderTargetHandle outputDepth, bool msaaInput, Vector2 outputToSourceScale )
	{
		SetInput( "SourceDepth", sourceDepth );
		DepthAttachment = outputDepth;
		this.msaaInput = msaaInput;
		this.outputToSourceScale = outputToSourceScale;
	}

	protected override void OnRender()
	{
		SharedPasses.UpscaleDepth( Graphics.Attributes, msaaInput, outputToSourceScale );
	}
}

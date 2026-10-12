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
	RenderTarget destDepth;
	bool msaaInput;

	public DepthDownsampleLayer() : base( "Hi-Z Depth Downsample" )
	{
	}

	public void Setup( ISceneView view, RenderViewport viewport, SceneViewRenderTargetHandle sourceDepth, bool msaaInput )
	{
		this.viewport = viewport;
		this.msaaInput = msaaInput;
		SetInput( "SourceDepth", sourceDepth );

		destDepth = SharedPasses.DepthChainTarget( (int)viewport.Rect.Width, (int)viewport.Rect.Height );

		view.GetRenderAttributesPtr().SetTextureValue( "DepthChainDownsample", destDepth.DepthTarget.native, -1 );
		view.GetRenderAttributesPtr().SetTextureValue( "DepthChainDownsamplePrevFrame", destDepth.DepthTarget.native, -1 );
	}

	protected override void OnRender()
	{
		// Our SourceDepth comes from the scene render target system
		SharedPasses.BuildDepthChain( Graphics.Context, null, destDepth.DepthTarget, msaaInput, (int)viewport.Rect.Width, (int)viewport.Rect.Height );
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

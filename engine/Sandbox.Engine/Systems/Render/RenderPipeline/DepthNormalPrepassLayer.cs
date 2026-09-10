using NativeEngine;

namespace Sandbox.Rendering;

internal class DepthNormalPrepassLayer : RenderLayer
{
	public enum PrepassMode
	{
		Overlay,
		Small,
		Large,
	};

	// ponytail: 0 = don't size-cull (Overlay). Fine as a sentinel since real thresholds are ±60.
	public int CullThresholdPercent { get; }

	public PrepassMode Mode { get; }

	public DepthNormalPrepassLayer( PrepassMode mode )
	{
		Name = $"Depth Normal Prepass ({mode})";
		Mode = mode;

		CullThresholdPercent = mode switch
		{
			PrepassMode.Large => 60,
			PrepassMode.Small => -60,
			_ => 0,
		};
		LayerType = SceneLayerType.DepthPrepass;
		Flags |= LayerFlags.NeverRemove;
		ShaderMode = "Depth";

		if ( mode == (PrepassMode)0 )
		{
			Flags |= LayerFlags.DiscardColorBuffers;
			ClearFlags = ClearFlags.Color;
		}

		if ( mode == PrepassMode.Large )
		{
			// With fewer, larger objects we favor GPU perf over CPU perf by using fullsort
			Flags |= LayerFlags.NeedsFullSort;
		}

		ObjectFlagsRequired = SceneObjectFlags.IsOpaque;
		ObjectFlagsExcluded = SceneObjectFlags.IsLight | SceneObjectFlags.ExcludeGameLayer | SceneObjectFlags.NoZPrepass;

		// Overlays (viewmodels) prepass separately: they stomp world depth and claim their pixels
		// with a stencil bit so depth-sampling effects see real overlay depth even inside walls.
		if ( mode == PrepassMode.Overlay )
		{
			ObjectFlagsRequired |= SceneObjectFlags.GameOverlayLayer;
		}
		else
		{
			ObjectFlagsExcluded |= SceneObjectFlags.GameOverlayLayer;
		}

		// Discard pixels conservatively, our depth prepass shader doesn't do the fancy a2c smoothing math. ( But shadows do, so don't remove me! )
		Attributes.SetCombo( "D_ALPHA_TEST_CONSERVATIVE", 1 );


	}

	public void Setup( ISceneView view, RenderTarget gbufferColor, SceneViewRenderTargetHandle rtDepth )
	{
		ColorAttachment = gbufferColor.ToColorHandle( view );
		DepthAttachment = rtDepth;
	}
}

using NativeEngine;
using Sandbox.Rendering;

namespace Sandbox.SceneRenderer.Native;

/// <summary>
/// The layers of a 3D skybox's view: the view native embeds in the main one, drawing the skybox world from the camera's
/// scaled position (<c>C3DSkyboxRenderer</c>, <c>CRenderingPipeline3DSkybox</c>). One per view at a time, like
/// <see cref="StandardPipeline"/>.
/// </summary>
internal sealed class Skybox3DPipeline
{
	static readonly StringToken ForwardMode = "Forward";
	static readonly StringToken DepthMode = "Depth";

	readonly ClusteredCullingLayer clusteredCulling = new();

	public void AddLayersToView( CRenderPipelineSetup setup )
	{
		var view = setup.m_pView;
		var viewport = setup.m_viewport;
		var color = setup.m_hColor;
		var depth = setup.m_hDepth;

		ViewSetup.SetSkybox3D( setup );
		view.GetRenderAttributesPtr().SetBoolValue( "IsSkybox", true );

		NativeLayers.AddLightBinner( view, viewport, setup.m_pAttributes );
		clusteredCulling.Setup( view, viewport );
		clusteredCulling.AddToView( view, viewport );

		const LayerFlags preserve = LayerFlags.PreserveColorBuffers | LayerFlags.PreserveDepthBuffer | LayerFlags.PreserveStencilBuffer;

		if ( setup.m_bDepthPrepass )
		{
			var prepass = NativeLayers.Add( view, "3DSkybox Depth Prepass", viewport, DepthMode, NativeRenderPipeline.NoTarget, depth, RenderColorSpace.RENDER_SRGB,
				preserve | LayerFlags.NeverRemove | LayerFlags.IsDepthRenderingPass | LayerFlags.NeedsPerViewLightingConstants );
			prepass.AddObjectFlagsRequiredMask( SceneObjectFlags.IsOpaque | SceneObjectFlags.StaticObject );
			prepass.AddObjectFlagsExcludedMask( SceneObjectFlags.IsLight | SceneObjectFlags.NoZPrepass );
			prepass.SetBoundingVolumeSizeCullThresholdInPercent( setup.m_flDepthPrepassCullThreshold );
		}

		var forward = NativeLayers.Add( view, "3DSkyboxForward", viewport, ForwardMode, color, depth, RenderColorSpace.RENDER_SRGB,
			preserve | LayerFlags.NeedsPerViewLightingConstants | LayerFlags.PrimaryTargetOutput | LayerFlags.CountArtistTriangles );
		forward.AddObjectFlagsRequiredMask( SceneObjectFlags.IsOpaque );
		forward.AddObjectFlagsExcludedMask( SceneObjectFlags.IsLight );
		forward.GetRenderAttributesPtr().SetComboValue( "D_SKYBOX", 1 );

		var translucent = NativeLayers.Add( view, "3DSkybox Translucent Forward", viewport, ForwardMode, color, depth, RenderColorSpace.RENDER_SRGB,
			LayerFlags.NeedsFullSort | LayerFlags.NeedsPerViewLightingConstants | LayerFlags.CountArtistTriangles );
		translucent.AddObjectFlagsRequiredMask( SceneObjectFlags.IsTranslucent );
		var attributes = translucent.GetRenderAttributesPtr();
		attributes.SetComboValue( "D_SKYBOX", 1 );
		attributes.SetIntValue( "EnableAlphaTint", 1 );

		// The procedural sky behind the 3D skybox's geometry
		if ( setup.m_bRenderSky )
		{
			NativeLayers.Add2DSky( setup, viewport, color, depth );
		}
	}
}

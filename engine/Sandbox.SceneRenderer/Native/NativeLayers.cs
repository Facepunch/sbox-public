using NativeEngine;
using Sandbox.Rendering;

namespace Sandbox.SceneRenderer.Native;

/// <summary>
/// Layers both native pipelines (<see cref="StandardPipeline"/>, <see cref="Skybox3DPipeline"/>) add, and the helpers
/// they build layers with. Each is one native <c>ISceneLayer</c>, configured straight after it's added.
/// </summary>
internal static class NativeLayers
{
	/// <summary>
	/// Every scene object: a layer that excludes these draws none (<c>SCENEOBJECTFLAG_DRAW_ALL_SCENE_OBJECTS</c>).
	/// </summary>
	public const SceneObjectFlags AllObjects = (SceneObjectFlags)ulong.MaxValue;

	static readonly StringToken ToolsUtilMode = "ToolsUtil";
	static readonly StringToken SkyBoxMatch = "SkyBox";

	/// <summary>
	/// A layer that draws the scene objects it matches, into <paramref name="color"/> and <paramref name="depth"/>.
	/// </summary>
	public static ISceneLayer Add( ISceneView view, string name, RenderViewport viewport, StringToken shaderMode,
		SceneViewRenderTargetHandle color, SceneViewRenderTargetHandle depth, RenderColorSpace colorSpace, LayerFlags flags )
	{
		var layer = view.AddRenderLayer( name, viewport, shaderMode, default );
		layer.m_nLayerFlags |= flags;
		layer.SetOutput( color, depth, colorSpace );
		return layer;
	}

	/// <summary>
	/// The light binner: packs the view's lights, envmaps, light probe volumes and decals. The pipeline attributes can
	/// leave some out, for <c>mat_fullbright</c> and the like.
	/// </summary>
	public static void AddLightBinner( ISceneView view, RenderViewport viewport, CRenderAttributes pipelineAttributes )
	{
		var excluded = SceneObjectFlags.None;
		if ( !pipelineAttributes.GetBoolValue( "directLighting", true ) ) excluded |= SceneObjectFlags.IsDirectLight;
		if ( !pipelineAttributes.GetBoolValue( "indirectLighting", true ) ) excluded |= SceneObjectFlags.IsIndirectLight;
		if ( !pipelineAttributes.GetBoolValue( "renderSun", true ) ) excluded |= SceneObjectFlags.IsSunLight;
		if ( !pipelineAttributes.GetBoolValue( "environmentMaps", true ) ) excluded |= SceneObjectFlags.IsEnvMap;
		if ( !pipelineAttributes.GetBoolValue( "lightProbeVolumes", true ) ) excluded |= SceneObjectFlags.IsLightVolume;

		var layer = view.AddRenderLayer( "Lightbinner", viewport, default, default );
		layer.m_nLayerFlags = LayerFlags.NeverRemove | LayerFlags.LightBinnerSetupLayer;
		layer.SetOutput( NativeRenderPipeline.NoTarget, NativeRenderPipeline.NoTarget );
		layer.AddObjectFlagsRequiredMask( SceneObjectFlags.IsLight );
		layer.AddObjectFlagsExcludedMask( excluded );
	}

	/// <summary>
	/// The 2D sky, behind everything at the far end of the depth range, if the view renders the <c>skybox</c> tag
	/// (<c>Add2DSkyboxLayers</c>).
	/// </summary>
	public static void Add2DSky( CRenderPipelineSetup setup, RenderViewport viewport, SceneViewRenderTargetHandle color, SceneViewRenderTargetHandle depth )
	{
		if ( !setup.RendersTag( "skybox" ) )
			return;

		viewport.MinZ = viewport.MaxZ = 0.0f;

		var layer = Add( setup.m_pView, "Sky", viewport, new StringToken( setup.GetShadingMode() ), color, depth, setup.m_colorSpace,
			LayerFlags.PreserveColorBuffers | LayerFlags.PreserveDepthBuffer | LayerFlags.PreserveStencilBuffer |
			LayerFlags.PrimaryTargetOutput | LayerFlags.NeedsFullSort | LayerFlags.NeedsPerViewLightingConstants );
		layer.SetObjectMatchID( SkyBoxMatch );
	}

	/// <summary>
	/// The tools layers after opaque or translucent geometry: objects in <c>ToolsUtil</c> mode, then the ones matching the
	/// tool layers (<c>AddOpqaueToolLayers</c>, <c>AddTranslucentToolLayers</c>).
	/// </summary>
	public static void AddToolLayers( CRenderPipelineSetup setup, RenderViewport viewport, SceneViewRenderTargetHandle color, SceneViewRenderTargetHandle depth, bool translucent )
	{
		var view = setup.m_pView;

		// FIXME: Not sure if this is what we want, but particle systems and all translucent objects are trying to render in the
		// translucent one, only to be culled at the last minute because they don't support "ToolsUtil" render mode.
		var util = Add( view, translucent ? "ToolsUtil Translucent" : "ToolsUtil Opaque", viewport, ToolsUtilMode, color, depth, setup.m_colorSpace, default );
		util.AddObjectFlagsRequiredMask( translucent ? SceneObjectFlags.IsTranslucent : SceneObjectFlags.IsOpaque );
		util.AddObjectFlagsExcludedMask( SceneObjectFlags.WantsFrameBufferCopyTexture );

		// These use a layer match id and the default shader mode
		if ( translucent )
		{
			AddToolLayer( view, "Tools_Translucent", viewport, color, depth, LayerFlags.NeedsFullSort );
			return;
		}

		AddToolLayer( view, "Tools_Opaque", viewport, color, depth, default );
		AddToolLayer( view, "Tools_PreTranslucent", viewport, color, depth, LayerFlags.NeedsFullSort );
	}

	static void AddToolLayer( ISceneView view, string name, RenderViewport viewport, SceneViewRenderTargetHandle color, SceneViewRenderTargetHandle depth, LayerFlags flags )
	{
		var layer = Add( view, name, viewport, default, color, depth, RenderColorSpace.RENDER_SRGB, flags );
		layer.SetObjectMatchID( name );
	}

	/// <summary>
	/// A forward layer of opaque objects, static or dynamic, with the layer its fading objects go to after it
	/// (<c>CRenderingPipelineStandard::AddOpaqueLayers</c>). Static objects are the world, dynamic everything else.
	/// </summary>
	public static void AddOpaque( CRenderPipelineSetup setup, string name, string fadeName, bool dynamicObjects )
	{
		var view = setup.m_pView;
		var viewport = setup.m_viewport;
		var shadingMode = new StringToken( setup.GetShadingMode() );

		ISceneLayer opaque = default;
		if ( setup.m_bRenderOpaque )
		{
			// Opaque geometry that was z-prepassed
			opaque = Add( view, name, viewport, shadingMode, setup.m_hColor, setup.m_hDepth, setup.m_colorSpace,
				LayerFlags.NeverRemove | LayerFlags.NeedsPerViewLightingConstants | LayerFlags.CountArtistTriangles );
			opaque.LayerEnum = SceneLayerType.Opaque;
			opaque.DisableReflectionsAndRefractionsForObjectsInThisLayer( true );
			opaque.AddObjectFlagsRequiredMask( SceneObjectFlags.IsOpaque );
			opaque.AddObjectFlagsExcludedMask( SceneObjectFlags.IsLight | SceneObjectFlags.ExcludeGameLayer );

			if ( dynamicObjects ) opaque.AddObjectFlagsExcludedMask( SceneObjectFlags.StaticObject );
			else opaque.AddObjectFlagsRequiredMask( SceneObjectFlags.StaticObject );

			if ( setup.m_bWireframe )
			{
				var attributes = opaque.GetRenderAttributesPtr();
				attributes.SetVector4DValue( "wireframeColor", new Vector4( 1.0f, 1.0f, 1.0f, 1.0f ) );
				attributes.SetBoolValue( "g_bWireframeMode", true );
				opaque.SetWireframeOverride();
			}
		}

		// Objects fading out end up here, drawn dithered rather than blended
		var fade = Add( view, fadeName, viewport, shadingMode, setup.m_hColor, setup.m_hDepth, setup.m_colorSpace,
			LayerFlags.NeedsPerViewLightingConstants | LayerFlags.CountArtistTriangles | LayerFlags.FadeWithoutAlphaBlending );
		fade.LayerEnum = SceneLayerType.Opaque;
		fade.DisableReflectionsAndRefractionsForObjectsInThisLayer( true );
		fade.AddObjectFlagsExcludedMask( AllObjects );
		fade.GetRenderAttributesPtr().SetComboValue( "D_OPAQUE_FADE", 1 );

		if ( opaque.IsValid ) opaque.SetFadeTargetLayer( fade );
	}
}

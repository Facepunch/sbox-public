using NativeEngine;
using Sandbox.Rendering;

namespace Sandbox.SceneRenderer.Native;

/// <summary>
/// The standard pipeline's layers for one view, in the order they render. Native sets up the view's render targets and
/// attributes first (<c>CRenderingPipelineStandard::AddLayersToView</c>), and the steps that stay native - procedural
/// renderers, the 3D skybox's view, the temporal upscalers and auto exposure - this asks <see cref="CRenderPipelineSetup"/> for.
/// One per view at a time: its procedural layers keep the view's state until it renders.
/// </summary>
internal sealed class StandardPipeline
{
	/// <summary>
	/// The slice of the depth range game overlays draw in, at the near end (reverse-Z): <c>DEPTH_RANGE_GAME_OVERLAYS</c>.
	/// </summary>
	const float GameOverlayDepthRange = 0.01f;

	/// <summary>
	/// Prepass size culls, in percent of the screen: the first prepass draws the larger objects (full sort, favouring the GPU),
	/// the second everything smaller. matt: I don't think this makes sense anymore, Valve used to do it to opt out of smaller
	/// objects entirely. However doing 1 big pass seems to double draw calls, it's possible it's not rendering everything?
	/// </summary>
	const float LargePrepassCullPercent = 60.0f;

	static readonly StringToken ForwardMode = "Forward";
	static readonly StringToken DepthMode = "Depth";
	static readonly StringToken StaticOverlayMatch = "StaticOverlayLayer";
	static readonly StringToken OverlayWithDepthMatch = "OverlayWithDepth";
	static readonly StringToken OverlayWithoutDepthMatch = "OverlayWithoutDepth";

	readonly ClusteredCullingLayer clusteredCulling = new();
	readonly DepthDownsampleLayer depthDownsample = new();
	readonly MotionVectorLayer motionVectors = new();
	readonly ReactiveMaskLayer reactiveMask = new();
	readonly BloomDepthLayer bloomDepth = new();
	readonly MotionVectorDebugLayer motionVectorDebug = new();
	readonly ReactiveMaskDebugLayer reactiveMaskDebug = new();

	public void AddLayersToView( CRenderPipelineSetup setup )
	{
		ViewTargets.Create( setup );

		var view = setup.m_pView;
		var pipelineAttributes = setup.m_pAttributes;
		var viewAttributes = view.GetRenderAttributesPtr();
		var viewport = setup.m_viewport;
		var color = setup.m_hColor;
		var depth = setup.m_hDepth;
		var colorSpace = setup.m_colorSpace;
		var shadingMode = new StringToken( setup.GetShadingMode() );
		var msaa = setup.m_nMSAA;
		var multisampled = msaa != RenderMultisampleType.RENDER_MULTISAMPLE_NONE;
		var dynamicObjects = setup.m_bRenderDynamicObjects;
		var hooks = setup.m_bManagedHooks;

		ViewSetup.SetStandard( setup );
		ViewSetup.AddLightmapWarnings( setup );

		//
		// Clear colour, depth and stencil
		//
		{
			var layer = NativeLayers.Add( view, "Clear Color Depth Stencil", viewport, default, color, depth, colorSpace,
				LayerFlags.NeverRemove | LayerFlags.DiscardColorBuffers | LayerFlags.DiscardDepthStencilBuffer | LayerFlags.ClearWholeTargetViewportSize );
			layer.m_nClearFlags = setup.m_nClearFlags;
			layer.SetClearColor( setup.m_vClearColor, 0 );
			layer.AddObjectFlagsExcludedMask( NativeLayers.AllObjects );
		}

		var quadOverdraw = QuadOverdraw.IsOn( view );
		if ( quadOverdraw ) QuadOverdraw.AddClear( view, viewport, depth );

		var logicalViewport = view.GetMainViewport().Rect;
		viewAttributes.SetVector4DValue( "PainterScreenViewport", new Vector4( logicalViewport.Left, logicalViewport.Top, logicalViewport.Width, logicalViewport.Height ) );

		//
		// Lights, envmaps and decals: binned, then culled into clusters
		//
		NativeLayers.AddLightBinner( view, viewport, pipelineAttributes );
		clusteredCulling.Setup( view, viewport );
		clusteredCulling.AddToView( view, viewport );

		viewAttributes.SetIntValue( "ShadowFilterQuality", ShadowMapper.ShadowFilter );

		AddDepthNormalPrepasses( setup, viewport, depth, multisampled );

		// Compute: the depth chain
		depthDownsample.Setup( view, viewport, depth, multisampled );
		depthDownsample.AddToView( view, viewport );

		// Motion vectors, for the temporal upscalers (FSR3, DLSS)
		if ( Upscaler.IsTemporal( setup.m_nUpscalerMode ) )
		{
			motionVectors.Setup( view, viewport );
			motionVectors.AddToView( view, viewport );

			// Reactive mask: re-render viewmodel and overlay objects in depth-only mode, then convert to an R16F mask that
			// tells the upscaler to prefer current-frame data for these pixels (they have no valid motion vectors)
			var reactiveDepth = reactiveMask.Setup( view, viewport );
			AddReactiveDepth( view, viewport, "Reactive Depth (Viewmodel)", SceneObjectFlags.ViewModelLayer, reactiveDepth, clearDepth: true );
			AddReactiveDepth( view, viewport, "Reactive Depth (Overlay)", SceneObjectFlags.GameOverlayLayer, reactiveDepth, clearDepth: false );
			reactiveMask.AddToView( view, viewport );
		}

		// Bloom objects: effects that only show up in bloom, like a ghost effect
		{
			var quarterViewport = viewport / 4;
			var bloom = SharedPasses.BloomTarget( (int)quarterViewport.Rect.Width, (int)quarterViewport.Rect.Height );

			bloomDepth.Setup( view, depth, multisampled, bloom );
			bloomDepth.AddToView( view, quarterViewport );

			var layer = NativeLayers.Add( view, "Bloom Layer", quarterViewport, ForwardMode, bloom.ToColorHandle( view ), bloom.ToDepthHandle( view ), RenderColorSpace.RENDER_SRGB,
				LayerFlags.NeverRemove | LayerFlags.NeedsPerViewLightingConstants );
			layer.LayerEnum = SceneLayerType.Opaque;
			layer.m_nClearFlags = (int)ClearFlags.Color;
			layer.SetClearColor( default, 0 );
			layer.AddObjectFlagsRequiredMask( SceneObjectFlags.EffectsBloomLayer );
			layer.AddObjectFlagsExcludedMask( SceneObjectFlags.IsLight );

			viewAttributes.SetTextureValue( "QuarterResEffectsBloomInputTexture", bloom.ColorTarget.native, -1 );
		}

		// Refraction stencil: glass depth at a quarter of the resolution, filtering depth out of frame buffer copies
		{
			var quarterViewport = viewport / 4;
			var stencil = SharedPasses.RefractionStencilTarget( (int)quarterViewport.Rect.Width, (int)quarterViewport.Rect.Height );

			var layer = NativeLayers.Add( view, "Refraction Stencil Layer", quarterViewport, DepthMode, NativeRenderPipeline.NoTarget, stencil.ToDepthHandle( view ), RenderColorSpace.RENDER_SRGB,
				LayerFlags.NeverRemove | LayerFlags.IsDepthRenderingPass | LayerFlags.ForceDepthFastPath );
			layer.LayerEnum = SceneLayerType.Opaque;
			layer.m_nClearFlags = (int)(ClearFlags.Depth | ClearFlags.Stencil);
			layer.SetClearColor( default, 0 );
			layer.AddObjectFlagsRequiredMask( SceneObjectFlags.WantsFrameBufferCopyTexture | SceneObjectFlags.IsTranslucent );
			var attributes = layer.GetRenderAttributesPtr();
			attributes.SetComboValue( "D_REFRACTION_TEST", 1 );
			attributes.SetComboValue( "D_RENDER_BACKFACES", 1 );

			viewAttributes.SetTextureValue( "RefractionDepthTexture", stencil.DepthTarget.native, -1 );
		}

		setup.AddVolumetricFog();

		if ( hooks ) HookLayers.Add( view, "Managed: AfterDepthPrepass", Stage.AfterDepthPrepass, viewport, color, depth );

		//
		// Opaque: the world, its overlays, then everything else
		//
		NativeLayers.AddOpaque( setup, "Static Opaque Forward", "Static Opaque Forward Fade", dynamicObjects: false );

		if ( setup.m_bToolsView )
		{
			NativeLayers.AddToolLayers( setup, viewport, color, depth, translucent: false );
		}

		if ( setup.m_bDrawOverlays )
		{
			var layer = NativeLayers.Add( view, "Static World Overlays", viewport, shadingMode, color, depth, colorSpace,
				LayerFlags.NeedsFullSort | LayerFlags.NeedsPerViewLightingConstants | LayerFlags.CountArtistTriangles );
			setup.SortByRenderOrder( layer );
			layer.SetObjectMatchID( StaticOverlayMatch );
			layer.SetFadeTargetLayer( layer ); // Don't cull overlays with alpha < 1.0f

			if ( !dynamicObjects ) layer.AddObjectFlagsRequiredMask( SceneObjectFlags.StaticObject );
		}

		if ( dynamicObjects )
		{
			NativeLayers.AddOpaque( setup, "Dynamic Opaque Forward", "Dynamic Opaque Forward Fade", dynamicObjects: true );
		}

		if ( hooks ) HookLayers.Add( view, "Managed: AfterOpaque", Stage.AfterOpaque, viewport, color, depth );

		//
		// Sky: the 3D skybox's view, which draws the 2D sky behind itself, or the 2D sky
		//
		var added3DSkybox = setup.Add3DSkybox();
		if ( setup.m_bRenderSky && !added3DSkybox )
		{
			NativeLayers.Add2DSky( setup, setup.m_skyViewport, color, depth );
		}

		if ( hooks ) HookLayers.Add( view, "Managed: AfterSkybox", Stage.AfterSkybox, viewport, color, depth );

		//
		// Decals, static and dynamic
		//
		if ( setup.m_bRenderDecals )
		{
			var layer = NativeLayers.Add( view, "Decals (Static and Dynamic)", viewport, shadingMode, color, NativeRenderPipeline.NoTarget, colorSpace,
				LayerFlags.NeedsPerViewLightingConstants | LayerFlags.NeedsFullSort | LayerFlags.PrimaryTargetOutput | LayerFlags.DoesntModifyDepthStencilBuffer );
			layer.AddObjectFlagsRequiredMask( SceneObjectFlags.IsDecal );
			layer.SetFadeTargetLayer( layer );
			layer.GetRenderAttributesPtr().SetComboValue( "D_MSAA_DEPTH_BUFFER", (byte)(multisampled ? 1 : 0) );
			layer.SetAttr( "SceneDepth", depth, multisampled ? SceneLayerMSAAMode_t.On : SceneLayerMSAAMode_t.Off, NativeRenderPipeline.RenderTargetInput );
		}

		// Must never be removed, so that during submission we look at the layer dependencies list, if there are any
		var label = view.AddLabelLayer( "BeforeTranslucent" );
		label.m_nLayerFlags |= LayerFlags.NeverRemove;

		//
		// Translucent objects and particles
		//
		if ( setup.m_bRenderTranslucent )
		{
			var layer = NativeLayers.Add( view, "Translucent Forward", viewport, shadingMode, color, depth, colorSpace,
				LayerFlags.NeedsFullSort | LayerFlags.NeedsPerViewLightingConstants | LayerFlags.CountArtistTriangles | LayerFlags.DoesntModifyDepthStencilBuffer );
			layer.LayerEnum = SceneLayerType.Translucent;
			layer.AddObjectFlagsRequiredMask( SceneObjectFlags.IsTranslucent );
			layer.AddObjectFlagsExcludedMask( SceneObjectFlags.ExcludeGameLayer );
			layer.SetFadeTargetLayer( layer );

			// Faded objects redirected here might need to set blend state based on this attribute
			var attributes = layer.GetRenderAttributesPtr();
			attributes.SetIntValue( "EnableAlphaTint", 1 );
			attributes.SetComboValue( "D_OPAQUE_FADE", 1 );

			if ( !dynamicObjects ) layer.AddObjectFlagsRequiredMask( SceneObjectFlags.StaticObject );

			if ( setup.m_hRefractColor != NativeRenderPipeline.NoTarget )
			{
				layer.SetAttr( "vrRefractColor", setup.m_hRefractColor, SceneLayerMSAAMode_t.Off, NativeRenderPipeline.RenderTargetInput );
			}
		}

		if ( setup.m_bToolsView )
		{
			NativeLayers.AddToolLayers( setup, viewport, color, depth, translucent: true );
		}

		//
		// Game overlays (viewmodels), in front of everything else in their own slice of the depth range
		//
		{
			var overlayViewport = viewport;
			overlayViewport.MinZ = 1.0f - GameOverlayDepthRange;
			overlayViewport.MaxZ = 1.0f;

			// Opaque, before the managed hooks so DoF can clip against the remapped viewmodel depth
			var opaque = NativeLayers.Add( view, "GameOverlay", overlayViewport, shadingMode, color, depth, colorSpace,
				LayerFlags.PreserveColorBuffers | LayerFlags.NeedsPerViewLightingConstants | LayerFlags.CountArtistTriangles );
			opaque.LayerEnum = SceneLayerType.Opaque;
			opaque.AddObjectFlagsRequiredMask( SceneObjectFlags.IsOpaque | SceneObjectFlags.GameOverlayLayer );
			opaque.AddObjectFlagsExcludedMask( SceneObjectFlags.ExcludeGameLayer );

			if ( hooks ) HookLayers.Add( view, "Managed: AfterTransparent", Stage.AfterTransparent, viewport, color, depth );
			if ( hooks ) HookLayers.Add( view, "Managed: AfterViewModel", Stage.AfterViewmodel, viewport, color, depth );

			// Translucent, after the managed hooks: these don't write depth so depth-based effects there (e.g. depth of field)
			// would composite blurred background over them
			var translucent = NativeLayers.Add( view, "GameOverlay Translucent", overlayViewport, shadingMode, color, depth, colorSpace,
				LayerFlags.NeedsFullSort | LayerFlags.PreserveColorBuffers | LayerFlags.NeedsPerViewLightingConstants | LayerFlags.CountArtistTriangles );
			translucent.LayerEnum = SceneLayerType.Translucent;
			translucent.AddObjectFlagsRequiredMask( SceneObjectFlags.IsTranslucent | SceneObjectFlags.GameOverlayLayer );
			translucent.AddObjectFlagsExcludedMask( SceneObjectFlags.ExcludeGameLayer );
		}

		if ( viewAttributes.GetIntValue( "ToolsVisMode", 0 ) == (int)SceneCameraDebugMode.ShaderIDColor )
		{
			ViewSetup.AddShaderIDLegend( setup, color );
		}

		//
		// Upscale to full resolution before post processing: colour, depth and the viewport are the full resolution ones after.
		// Auto exposure still measures the scene as it rendered.
		//
		var exposureColor = color;
		var exposureViewport = viewport;
		var finalColor = setup.m_hFinalColor;
		var resolveToFinal = setup.m_bResolveToFinal;
		var presentColor = NativeRenderPipeline.NoTarget;

		var upscaling = setup.m_nUpscalerMode != 0;
		if ( upscaling )
		{
			Upscaler.Add( setup, color, setup.m_skyViewport );
			color = setup.m_hUpscaleColor;
			depth = setup.m_hUpscaleDepth;
			viewport = setup.m_upscaleViewport;

			// Everything after uses the non-MSAA full resolution output as its final colour; it's copied to the real one last
			presentColor = finalColor;
			finalColor = color;
			resolveToFinal = false;
		}

		// Screen-space UI that wants post-processing applied to it (ScreenPanel.RenderTiming.BeforePostProcess). Costs a seed
		// and composite pass on the HDR buffer, so the camera says whether it has any.
		if ( hooks && pipelineAttributes.GetBoolValue( "enableEarlyUI", false ) )
		{
			HookLayers.AddUI( view, "Managed: EarlyUI", Stage.EarlyUI, viewport, color );
		}

		if ( hooks ) HookLayers.Add( view, "Managed: BeforePostProcessing", Stage.BeforePostProcess, viewport, color, depth );

		setup.AddAutoExposure( exposureColor, exposureViewport );

		if ( hooks ) HookLayers.Add( view, "Managed: Tonemapping", Stage.Tonemapping, viewport, color, depth );

		// The rest is SDR for now: if we rendered in HDR, copy to the final output
		if ( resolveToFinal )
		{
			var layer = view.AddBlitTextureToTargetLayer( "ResolveHDRToFinalSDR", color, viewport, viewport, finalColor, RenderColorSpace.RENDER_SRGB );
			layer.m_nClearFlags = (int)(ClearFlags.Color | ClearFlags.Depth | ClearFlags.Stencil);
		}

		if ( hooks ) HookLayers.Add( view, "Managed: AfterPostProcessing", Stage.AfterPostProcess, viewport, finalColor, depth );

		//
		// Overlays after post processing (gizmos, debug text), with and without depth
		//
		AddScreenOverlay( view, "OverlayWithDepth", OverlayWithDepthMatch, viewport, shadingMode, finalColor, depth, colorSpace );
		AddScreenOverlay( view, "OverlayWithoutDepth", OverlayWithoutDepthMatch, viewport, shadingMode, finalColor, NativeRenderPipeline.NoTarget, colorSpace );

		if ( quadOverdraw ) QuadOverdraw.AddVisualize( view, viewport, finalColor, colorSpace );

		// Screen-space UI (ScreenPanel.RenderTiming.AfterPostProcess)
		if ( hooks && pipelineAttributes.GetBoolValue( "enableUI", true ) )
		{
			HookLayers.AddUI( view, "Managed: UI", Stage.UI, viewport, finalColor );
			HookLayers.Add( view, "Managed: AfterUI", Stage.AfterUI, viewport, finalColor, depth );
		}

		// Objects drawn over the UI
		{
			var layer = NativeLayers.Add( view, "UI Overlay", viewport, shadingMode, finalColor, NativeRenderPipeline.NoTarget, colorSpace,
				LayerFlags.NeedsFullSort | LayerFlags.PreserveColorBuffers | LayerFlags.NeedsPerViewLightingConstants | LayerFlags.CountArtistTriangles );
			layer.LayerEnum = SceneLayerType.Translucent;
			layer.AddObjectFlagsRequiredMask( SceneObjectFlags.UIOverlayLayer );
			layer.m_nClearFlags = (int)ClearFlags.Stencil;
			layer.SetFadeTargetLayer( layer );
			var attributes = layer.GetRenderAttributesPtr();
			attributes.SetIntValue( "EnableAlphaTint", 1 );
			attributes.SetComboValue( "D_OPAQUE_FADE", 1 );
		}

		// Upscaled: copy the full resolution output into the real one
		if ( upscaling && presentColor != NativeRenderPipeline.NoTarget )
		{
			view.AddBlitTextureToTargetLayer( "Upscaler Present", finalColor, viewport, viewport, presentColor, RenderColorSpace.RENDER_SRGB );
			finalColor = presentColor;
		}

		//
		// Debug visualizations over the final colour
		//
		var toolsVisMode = (SceneCameraDebugMode)pipelineAttributes.GetIntValue( "ToolsVisMode", 0 );
		if ( toolsVisMode == SceneCameraDebugMode.MotionVectors )
		{
			motionVectorDebug.ColorAttachment = finalColor;
			motionVectorDebug.AddToView( view, viewport );
		}
		else if ( toolsVisMode == SceneCameraDebugMode.ReactiveMask )
		{
			reactiveMaskDebug.ColorAttachment = finalColor;
			reactiveMaskDebug.AddToView( view, viewport );
		}
	}

	/// <summary>
	/// The depth prepass, with a small G-buffer (normals, roughness) for screen-space effects. Overlays (viewmodels) go first:
	/// they stomp world depth and claim their pixels with a stencil bit, so depth-sampling effects see real overlay depth even
	/// inside walls. The other two test that bit, so their (potentially nearer) depth can't overwrite those pixels.
	/// </summary>
	static void AddDepthNormalPrepasses( CRenderPipelineSetup setup, RenderViewport viewport, SceneViewRenderTargetHandle depth, bool multisampled )
	{
		var view = setup.m_pView;
		var screen = setup.m_screenViewport.Rect;

		var normals = RenderTarget.GetTemporary(
			(int)screen.Width,
			(int)screen.Height,
			colorFormat: ImageFormat.RGBA16161616F,
			depthFormat: ImageFormat.None,
			msaa: setup.m_nMSAA.FromEngine() );
		var normalsHandle = normals.ToColorHandle( view );

		var noCulling = view.GetRenderAttributesPtr().GetBoolValue( "NoPrepassCulling", false );

		AddDepthNormalPrepass( view, viewport, "Depth Normal Prepass (Overlay)", normalsHandle, depth, overlay: true, cullPercent: 0, noCulling );
		AddDepthNormalPrepass( view, viewport, "Depth Normal Prepass (Large)", normalsHandle, depth, overlay: false, LargePrepassCullPercent, noCulling );
		AddDepthNormalPrepass( view, viewport, "Depth Normal Prepass (Small)", normalsHandle, depth, overlay: false, -LargePrepassCullPercent, noCulling );

		// Pass that DepthNormals are enabled to the rest of the pipeline
		view.GetRenderAttributesPtr().SetIntValue( "NormalsTextureIndex", normals.ColorTarget.Index );
		view.GetRenderAttributesPtr().SetTextureValue( "NormalsGBuffer", normals.ColorTarget.native, -1 );
	}

	static void AddDepthNormalPrepass( ISceneView view, RenderViewport viewport, string name, SceneViewRenderTargetHandle normals, SceneViewRenderTargetHandle depth,
		bool overlay, float cullPercent, bool noCulling )
	{
		var flags = LayerFlags.NeverRemove;
		if ( overlay ) flags |= LayerFlags.DiscardColorBuffers;

		// With fewer, larger objects we favor GPU perf over CPU perf by using fullsort
		if ( cullPercent > 0 ) flags |= LayerFlags.NeedsFullSort;

		var layer = NativeLayers.Add( view, name, viewport, DepthMode, normals, depth, RenderColorSpace.RENDER_SRGB, flags );
		layer.LayerEnum = SceneLayerType.DepthPrepass;
		layer.AddObjectFlagsRequiredMask( SceneObjectFlags.IsOpaque );
		layer.AddObjectFlagsExcludedMask( SceneObjectFlags.IsLight | SceneObjectFlags.ExcludeGameLayer | SceneObjectFlags.NoZPrepass );

		if ( overlay )
		{
			layer.m_nClearFlags = (int)ClearFlags.Color;
			layer.SetClearColor( default, 0 );
			layer.AddObjectFlagsRequiredMask( SceneObjectFlags.GameOverlayLayer );
		}
		else
		{
			layer.AddObjectFlagsExcludedMask( SceneObjectFlags.GameOverlayLayer );
		}

		// Discard pixels conservatively, our depth prepass shader doesn't do the fancy a2c smoothing math. ( But shadows do, so don't remove me! )
		layer.GetRenderAttributesPtr().SetComboValue( "D_ALPHA_TEST_CONSERVATIVE", 1 );

		layer.SetOverlayStencil( overlay );

		if ( overlay ) return;

		layer.SetBoundingVolumeSizeCullThresholdInPercent( cullPercent );
		layer.SetLayerNoCull( noCulling );
	}

	/// <summary>
	/// Objects in a viewmodel or overlay layer, depth only, into the reactive mask's depth target.
	/// </summary>
	static void AddReactiveDepth( ISceneView view, RenderViewport viewport, string name, SceneObjectFlags required, SceneViewRenderTargetHandle depth, bool clearDepth )
	{
		var layer = NativeLayers.Add( view, name, viewport, DepthMode, NativeRenderPipeline.NoTarget, depth, RenderColorSpace.RENDER_SRGB,
			LayerFlags.NeverRemove | LayerFlags.IsDepthRenderingPass );
		layer.LayerEnum = SceneLayerType.DepthPrepass;
		layer.AddObjectFlagsRequiredMask( required );
		layer.AddObjectFlagsExcludedMask( SceneObjectFlags.IsLight );

		if ( clearDepth )
		{
			layer.m_nClearFlags = (int)ClearFlags.Depth;
			layer.SetClearColor( default, 0 );
		}
	}

	static void AddScreenOverlay( ISceneView view, string name, StringToken match, RenderViewport viewport, StringToken shadingMode,
		SceneViewRenderTargetHandle color, SceneViewRenderTargetHandle depth, RenderColorSpace colorSpace )
	{
		var layer = NativeLayers.Add( view, name, viewport, shadingMode, color, depth, colorSpace, LayerFlags.NeedsPerViewLightingConstants | LayerFlags.NeedsFullSort );
		layer.LayerEnum = SceneLayerType.Opaque;
		layer.SetObjectMatchID( match );
		layer.SetTonemapOverrideScaleValue( 1.0f );
		layer.SetFadeTargetLayer( layer );
	}
}

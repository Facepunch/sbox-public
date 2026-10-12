using NativeEngine;
using Sandbox.Rendering;
using System.Runtime.InteropServices;

namespace Sandbox.SceneRenderer.Native;

/// <summary>
/// Upscaling to full resolution before post processing. Native makes the targets and picks the mode; the temporal upscalers
/// (FSR3, DLSS) stay native (<see cref="CRenderPipelineSetup.AddTemporalUpscale"/>), the rest is a stretch blit or FSR1's two
/// compute passes (EASU, then RCAS).
/// </summary>
internal static class Upscaler
{
	// EUpscalerMode
	const int Stretch = 1, Fsr1 = 2, Fsr3 = 3, Dlss = 4;

	// FSR1 works in 16x16 tiles, 64 threads each
	const int Fsr1TileSize = 16;

	[UnmanagedFunctionPointer( CallingConvention.StdCall )]
	delegate void OnRenderCallback( ManagedRenderSetup_t setup );

	static readonly Material EasuMaterial = Material.Load( "materials/dev/fsr1_easu.vmat" );
	static readonly Material RcasMaterial = Material.Load( "materials/dev/fsr1_rcas.vmat" );
	static readonly DelegateFunctionPointer Easu = DelegateFunctionPointer.Get<OnRenderCallback>( setup => Dispatch( setup, EasuMaterial ) );
	static readonly DelegateFunctionPointer Rcas = DelegateFunctionPointer.Get<OnRenderCallback>( setup => Dispatch( setup, RcasMaterial ) );

	static readonly StringToken FsrInput = "FsrInput";
	static readonly StringToken FsrOutput = "FsrOutput";
	static readonly StringToken FsrInputViewportSize = "FsrInputViewportSize";
	static readonly StringToken FsrInputSize = "FsrInputSize";
	static readonly StringToken FsrOutputSize = "FsrOutputSize";
	static readonly StringToken FsrRcasAttenuation = "FsrRcasAttenuation";

	/// <summary>
	/// The temporal upscalers, which take motion vectors and a reactive mask.
	/// </summary>
	public static bool IsTemporal( int mode ) => mode is Fsr3 or Dlss;

	/// <summary>
	/// Upscale <paramref name="color"/>, rendered into <paramref name="lowResViewport"/>, into the setup's full resolution
	/// colour.
	/// </summary>
	public static void Add( CRenderPipelineSetup setup, SceneViewRenderTargetHandle color, RenderViewport lowResViewport )
	{
		var view = setup.m_pView;
		var fullResViewport = setup.m_upscaleViewport;
		var output = setup.m_hUpscaleColor;

		// Discard the output colour and depth, to satisfy scratch target write-before-read rules
		var discard = NativeLayers.Add( view, "Upscaler Output Discard", fullResViewport, default, output, setup.m_hUpscaleDepth, setup.m_colorSpace,
			LayerFlags.NeverRemove | LayerFlags.DiscardColorBuffers | LayerFlags.DiscardDepthStencilBuffer );
		discard.m_nClearFlags = (int)(ClearFlags.Depth | ClearFlags.Stencil);
		discard.AddObjectFlagsExcludedMask( NativeLayers.AllObjects );

		switch ( setup.m_nUpscalerMode )
		{
			case Fsr1:
				AddFsr1( setup, lowResViewport, fullResViewport, color, output );
				break;

			case Fsr3:
			case Dlss:
				setup.AddTemporalUpscale();
				break;

			case Stretch:
			default:
				var blit = view.AddBlitTextureToTargetLayer( "Stretch Upscale", color, lowResViewport, fullResViewport, output, setup.m_colorSpace );

				// A full-target blit discards, but the layer above already did: a second discard on a write that isn't the
				// first is illegal (scratch target validation)
				blit.m_nLayerFlags &= ~LayerFlags.DiscardColorBuffers;
				break;
		}
	}

	/// <summary>
	/// FSR1: EASU upscales into an intermediate, RCAS sharpens that into the output.
	/// </summary>
	static void AddFsr1( CRenderPipelineSetup setup, RenderViewport lowResViewport, RenderViewport fullResViewport,
		SceneViewRenderTargetHandle input, SceneViewRenderTargetHandle output )
	{
		if ( EasuMaterial is null || RcasMaterial is null ) return;

		var view = setup.m_pView;
		var intermediate = setup.m_hFsr1IntermediateColor;
		var lowResSize = new Vector3( lowResViewport.Rect.Width, lowResViewport.Rect.Height, 0 );
		var fullResSize = new Vector3( fullResViewport.Rect.Width, fullResViewport.Rect.Height, 0 );

		var discard = NativeLayers.Add( view, "FSR1 EASU Output Discard", fullResViewport, default, intermediate, NativeRenderPipeline.NoTarget, RenderColorSpace.RENDER_SRGB,
			LayerFlags.NeverRemove | LayerFlags.DiscardColorBuffers );
		discard.AddObjectFlagsExcludedMask( NativeLayers.AllObjects );

		var easu = AddPass( view, "FSR1 EASU", Easu, fullResViewport, input, intermediate );
		var attributes = easu.GetRenderAttributesPtr();
		attributes.SetVectorValue( FsrInputViewportSize, lowResSize );
		attributes.SetVectorValue( FsrInputSize, lowResSize );
		attributes.SetVectorValue( FsrOutputSize, fullResSize );

		var rcas = AddPass( view, "FSR1 RCAS", Rcas, fullResViewport, intermediate, output );
		attributes = rcas.GetRenderAttributesPtr();
		attributes.SetVectorValue( FsrInputViewportSize, fullResSize );
		attributes.SetVectorValue( FsrInputSize, fullResSize );
		attributes.SetVectorValue( FsrOutputSize, fullResSize );
		attributes.SetFloatValue( FsrRcasAttenuation, (1.0f - Math.Clamp( setup.m_flFsr1Sharpness, 0.0f, 1.0f )) * 2.0f );
	}

	static ISceneLayer AddPass( ISceneView view, string name, DelegateFunctionPointer callback, RenderViewport viewport,
		SceneViewRenderTargetHandle input, SceneViewRenderTargetHandle output )
	{
		var layer = view.AddManagedProceduralLayer( name, viewport, callback, default, true );
		layer.SetAttr( FsrInput, input, SceneLayerMSAAMode_t.Off, NativeRenderPipeline.RenderTargetInput );
		layer.SetAttr( FsrOutput, output, SceneLayerMSAAMode_t.Off, NativeRenderPipeline.RenderTargetOutput );
		layer.SetOutput( NativeRenderPipeline.NoTarget, NativeRenderPipeline.NoTarget );
		layer.m_nLayerFlags |= LayerFlags.NeverRemove | LayerFlags.DoesntModifyColorBuffers | LayerFlags.DoesntModifyDepthStencilBuffer;
		return layer;
	}

	static void Dispatch( ManagedRenderSetup_t setup, Material material )
	{
		var viewport = setup.sceneLayer.m_viewport.Rect;
		var groupsX = ((int)viewport.Width + Fsr1TileSize - 1) / Fsr1TileSize;
		var groupsY = ((int)viewport.Height + Fsr1TileSize - 1) / Fsr1TileSize;
		if ( groupsX <= 0 || groupsY <= 0 ) return;

		var mode = material.native.GetMode();
		if ( mode.IsNull ) return;

		RenderTools.Compute( setup.renderContext, setup.sceneLayer.GetRenderAttributesPtr(), mode, groupsX * 64, groupsY, 1 );
	}
}

/// <summary>
/// The shading complexity view's quad overdraw (<see cref="SceneCameraDebugMode.QuadOverdraw"/>): its counters cleared before
/// the scene, and drawn over the final colour after it. Native makes the counters (<c>SetupShadingMode</c>).
/// </summary>
internal static class QuadOverdraw
{
	[UnmanagedFunctionPointer( CallingConvention.StdCall )]
	delegate void OnRenderCallback( ManagedRenderSetup_t setup );

	static readonly StringToken Combo = "D_QUAD_OVERDRAW";
	static readonly StringToken Counters = "QuadOverdrawUAV";
	static readonly StringToken Locks = "QuadOverdrawMutexUAV";

	static readonly Material VisualizeMaterial = Material.Load( "materials/dev/visualize_quad_overdraw.vmat" );
	static readonly DelegateFunctionPointer Clear = DelegateFunctionPointer.Get<OnRenderCallback>( ClearCounters );
	static readonly DelegateFunctionPointer Visualize = DelegateFunctionPointer.Get<OnRenderCallback>( Draw );

	public static bool IsOn( ISceneView view ) => view.GetRenderAttributesPtr().GetComboValue( Combo, 0 ) != 0;

	public static void AddClear( ISceneView view, RenderViewport viewport, SceneViewRenderTargetHandle depth )
	{
		var layer = view.AddManagedProceduralLayer( "ClearQuadCoverage", viewport, Clear, default, true );
		layer.m_nLayerFlags |= LayerFlags.NeverRemove;
		layer.SetOutput( NativeRenderPipeline.NoTarget, depth, RenderColorSpace.RENDER_LINEAR );
	}

	public static void AddVisualize( ISceneView view, RenderViewport viewport, SceneViewRenderTargetHandle color, RenderColorSpace colorSpace )
	{
		var layer = view.AddManagedProceduralLayer( "VisualizeQuads", viewport, Visualize, default, true );
		layer.SetOutput( color, NativeRenderPipeline.NoTarget, colorSpace );
		layer.m_nLayerFlags |= LayerFlags.NeverRemove;
	}

	static void ClearCounters( ManagedRenderSetup_t setup )
	{
		var attributes = setup.sceneView.GetRenderAttributesPtr();
		setup.renderContext.ClearTextureUint( attributes.GetTextureValue( Counters, default ), 0 );
		setup.renderContext.ClearTextureUint( attributes.GetTextureValue( Locks, default ), 0xffffffff );
	}

	static void Draw( ManagedRenderSetup_t setup )
	{
		using var _ = new Graphics.Scope( in setup );
		Graphics.Blit( VisualizeMaterial );
	}
}

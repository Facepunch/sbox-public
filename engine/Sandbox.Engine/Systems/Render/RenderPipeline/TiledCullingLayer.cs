using System;
using NativeEngine;

namespace Sandbox.Rendering;

/// <summary>
/// Managed tiled light culling layer. Owns the tiled light buffer and dispatches the compute shader.
/// </summary>
internal class TiledCullingLayer : ProceduralRenderLayer
{
	private const int NumTilesX = 128;
	private const int NumTilesY = 128;

	private const int MaxLightsPerTile = 128;
	private const int MaxEnvMapsPerTile = 32;
	private const int MaxDecalsPerTile = 64;

	private const int MipLevel = 5; // 32x32 tiles
	private const int CullingLightJobCount = 3; // Matches shader enum CULLING_LIGHT_JOB_COUNT

	[ConVar( "r_tiled_rendering_conservative_culling", Help = "Use conservative near-z culling for tiled rendering.", Min = 0, Max = 1 )]
	public static bool ConservativeCulling { get; set; }

	private readonly ComputeShader _computeShader = new( "shaders/tiled_light_culling_cs.shader" );
	private GpuBuffer _tiledLightBuffer;

	public TiledCullingLayer()
	{
		Name = "Tiled Culling";
		Flags |= LayerFlags.NeverRemove;
		Flags |= LayerFlags.DoesntModifyColorBuffers;
		Flags |= LayerFlags.DoesntModifyDepthStencilBuffer;
		Flags |= LayerFlags.NeedsPerViewLightingConstants;

		EnsureResources();
	}

	public void Setup( ISceneView view )
	{
		BindAttributes( view );
		view.GetRenderAttributesPtr().SetIntValue( "UseTiledRendering", 1 );
	}

	internal override void OnRender()
	{
		var view = Graphics.SceneView;

		BindAttributes( view );
		Dispatch( new RenderViewport( Graphics.Viewport ) );
	}

	private void EnsureResources()
	{
		var tileCount = NumTilesX * NumTilesY;
		var elementCount = tileCount * (3 + MaxLightsPerTile + MaxEnvMapsPerTile + MaxDecalsPerTile);
		_tiledLightBuffer ??= new GpuBuffer( elementCount, sizeof( uint ), GpuBuffer.UsageFlags.Structured, "TiledRenderLights" );
	}

	private void BindAttributes( ISceneView view )
	{
		var attrs = view.GetRenderAttributesPtr();
		attrs.SetBufferValue( "TiledLightBuffer", _tiledLightBuffer.native );
		attrs.SetBufferValue( "g_TiledLightBuffer", _tiledLightBuffer.native ); // Legacy name
	}

	private void Dispatch( RenderViewport viewport )
	{
		var width = Math.Max( (int)viewport.Rect.Width >> MipLevel, 1 );
		var height = Math.Max( (int)viewport.Rect.Height >> MipLevel, 1 );

		var numTilesWidth = Math.Clamp( width, 1, NumTilesX );
		var numTilesHeight = Math.Clamp( height, 1, NumTilesY );

		Graphics.ResourceBarrierTransition( _tiledLightBuffer, ResourceState.UnorderedAccess );

		var attributes = RenderAttributes.Pool.Get();
		attributes.SetCombo( "D_CONSERVATIVE_CULLING", ConservativeCulling );
		attributes.Set( "TiledLightBuffer", _tiledLightBuffer );
		attributes.Set( "g_TiledLightBuffer", _tiledLightBuffer );

		_computeShader.DispatchWithAttributes( attributes, numTilesWidth, numTilesHeight, CullingLightJobCount );

		RenderAttributes.Pool.Return( attributes );

		Graphics.ResourceBarrierTransition( _tiledLightBuffer, ResourceState.UnorderedAccess, ResourceState.GenericRead );
	}
}

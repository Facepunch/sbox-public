using NativeEngine;
using System.Collections.Concurrent;

namespace Sandbox.Rendering;

/// <summary>
/// Layer configuration for the 3D skybox rendering pipeline.
/// </summary>
internal class Skybox3DPipeline
{
	[ConVar( "r_3d_skybox" )] static bool Enabled { get; set; } = true;
	[ConVar( "r_3d_skybox_depth_prepass" )] static bool DepthPrepass { get; set; } = true;
	[ConVar( "r_3d_skybox_depth_prepass_cull_threshold" )] static float DepthPrepassCullThreshold { get; set; } = 30.0f;

	static ConcurrentQueue<Skybox3DPipeline> Pool = new();

	LightbinnerLayer LightbinnerLayer { get; } = new();
	ClusteredCullingLayer ClusteredCullingLayer { get; } = new();

	Skybox3DDepthPrepassLayer DepthPrepassLayer { get; } = new();
	Skybox3DForwardLayer ForwardLayer { get; } = new();
	Skybox3DTranslucentLayer TranslucentLayer { get; } = new();

	/// <summary>
	/// Static entry point called from C++ when setting up the 3D skybox view.
	/// </summary>
	internal static void InternalAddLayersToView( ISceneView view, RenderViewport viewport, SceneViewRenderTargetHandle rtColor, SceneViewRenderTargetHandle rtDepth, CRenderAttributes pipelineAttrs )
	{
		if ( !Enabled )
			return;

		// Grab a pooled pipeline instance
		if ( !Pool.TryDequeue( out var pipeline ) )
			pipeline = new();

		var pipelineAttributes = new RenderAttributes( pipelineAttrs );
		pipeline.AddLayersToView( view, viewport, rtColor, rtDepth, pipelineAttributes );

		// Return to pool
		Pool.Enqueue( pipeline );
	}

	/// <summary>
	/// Adds 3D skybox layers to the view.
	/// </summary>
	public void AddLayersToView( ISceneView view, RenderViewport viewport, SceneViewRenderTargetHandle rtColor, SceneViewRenderTargetHandle rtDepth, RenderAttributes pipelineAttributes )
	{
		view.GetRenderAttributesPtr().SetBoolValue( "IsSkybox", true );

		{
			LightbinnerLayer.Setup( pipelineAttributes );
			LightbinnerLayer.AddToView( view, viewport );

			ClusteredCullingLayer.Setup( view, viewport );
			ClusteredCullingLayer.AddToView( view, viewport );
		}

		if ( DepthPrepass )
		{
			DepthPrepassLayer.Setup( rtDepth, DepthPrepassCullThreshold );
			DepthPrepassLayer.AddToView( view, viewport );
		}

		{
			ForwardLayer.Setup( rtColor, rtDepth );
			ForwardLayer.AddToView( view, viewport );
		}

		{
			TranslucentLayer.Setup( rtColor, rtDepth );
			TranslucentLayer.AddToView( view, viewport );
		}
	}
}

/// <summary>
/// Lightbinner layer for the 3D skybox.
/// </summary>
internal class Skybox3DLightbinnerLayer : RenderLayer
{
	public Skybox3DLightbinnerLayer()
	{
		Name = "3DSkyboxLightBinnerStandard";
		Flags = LayerFlags.PreserveColorBuffers
			  | LayerFlags.PreserveDepthBuffer
			  | LayerFlags.PreserveStencilBuffer
			  | LayerFlags.LightBinnerSetupLayer
			  | LayerFlags.NeverRemove;

		ObjectFlagsRequired = SceneObjectFlags.IsLight;
	}
}

/// <summary>
/// Depth prepass layer for the 3D skybox.
/// </summary>
internal class Skybox3DDepthPrepassLayer : RenderLayer
{
	float CullThreshold { get; set; } = 30.0f;

	public Skybox3DDepthPrepassLayer()
	{
		Name = "3DSkybox Depth Prepass";
		ShaderMode = "Depth";
		Flags = LayerFlags.PreserveColorBuffers
			  | LayerFlags.PreserveDepthBuffer
			  | LayerFlags.PreserveStencilBuffer
			  | LayerFlags.NeverRemove
			  | LayerFlags.IsDepthRenderingPass
			  | LayerFlags.NeedsPerViewLightingConstants;

		ObjectFlagsRequired = SceneObjectFlags.IsOpaque | SceneObjectFlags.StaticObject;
		ObjectFlagsExcluded = SceneObjectFlags.IsLight | SceneObjectFlags.NoZPrepass;
	}

	public void Setup( SceneViewRenderTargetHandle rtDepth, float cullThreshold = 30.0f )
	{
		ColorAttachment = -1; // SCENE_VIEW_RENDER_TARGET_INVALID
		DepthAttachment = rtDepth;
		CullThreshold = cullThreshold;
	}

	public new ISceneLayer AddToView( ISceneView view, RenderViewport viewport )
	{
		var layer = base.AddToView( view, viewport );
		layer.SetBoundingVolumeSizeCullThresholdInPercent( CullThreshold );
		return layer;
	}
}

/// <summary>
/// Forward opaque layer for the 3D skybox.
/// </summary>
internal class Skybox3DForwardLayer : RenderLayer
{
	bool RenderDynamicObjects { get; set; } = true;

	public Skybox3DForwardLayer()
	{
		Name = "3DSkyboxForward";
		ShaderMode = "Forward";
		Flags = LayerFlags.PreserveColorBuffers
			  | LayerFlags.PreserveDepthBuffer
			  | LayerFlags.PreserveStencilBuffer
			  | LayerFlags.NeedsPerViewLightingConstants
			  | LayerFlags.PrimaryTargetOutput
			  | LayerFlags.CountArtistTriangles;

		ObjectFlagsRequired = SceneObjectFlags.IsOpaque;
		ObjectFlagsExcluded = SceneObjectFlags.IsLight;

		// Set D_SKYBOX combo
		Attributes.SetCombo( "D_SKYBOX", 1 );
	}

	public void Setup( SceneViewRenderTargetHandle rtColor, SceneViewRenderTargetHandle rtDepth )
	{
		ColorAttachment = rtColor;
		DepthAttachment = rtDepth;
		ObjectFlagsRequired = SceneObjectFlags.IsOpaque;
	}
}

/// <summary>
/// Translucent forward layer for the 3D skybox.
/// </summary>
internal class Skybox3DTranslucentLayer : RenderLayer
{
	public Skybox3DTranslucentLayer()
	{
		Name = "3DSkybox Translucent Forward";
		ShaderMode = "Forward";
		Flags = LayerFlags.NeedsFullSort
			  | LayerFlags.NeedsPerViewLightingConstants
			  | LayerFlags.CountArtistTriangles;

		ObjectFlagsRequired = SceneObjectFlags.IsTranslucent;

		// Set D_SKYBOX combo
		Attributes.SetCombo( "D_SKYBOX", 1 );
		Attributes.Set( "EnableAlphaTint", 1 );
	}

	public void Setup( SceneViewRenderTargetHandle rtColor, SceneViewRenderTargetHandle rtDepth )
	{
		ColorAttachment = rtColor;
		DepthAttachment = rtDepth;

		ObjectFlagsRequired = SceneObjectFlags.IsTranslucent;
	}
}

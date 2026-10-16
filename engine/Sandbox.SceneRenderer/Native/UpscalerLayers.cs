using NativeEngine;
using Sandbox.Rendering;

namespace Sandbox.SceneRenderer.Native;

/// <summary>
/// The camera's motion vectors (<c>MotionVectors</c>), for the temporal upscalers (FSR3, DLSS).
/// </summary>
internal sealed class MotionVectorLayer : NativeProceduralLayer
{
	static readonly Material MotionVectorMaterial = Material.FromShader( "shaders/motion_vectors.shader" );

	public MotionVectorLayer() : base( "Static Motion Vectors" )
	{
		ClearFlags = ClearFlags.Color;
	}

	public void Setup( ISceneView view, RenderViewport viewport )
	{
		var target = RenderTarget.GetTemporary(
			(int)viewport.Rect.Width,
			(int)viewport.Rect.Height,
			colorFormat: ImageFormat.RGBA16161616F,
			depthFormat: ImageFormat.None,
			targetName: "MotionVectors" );

		ColorAttachment = target.ToColorHandle( view );

		view.GetRenderAttributesPtr().SetTextureValue( "MotionVectors", target.ColorTarget.native, -1 );
	}

	protected override void OnRender()
	{
		Graphics.Blit( MotionVectorMaterial );
	}
}

/// <summary>
/// Converts the reactive depth target into an R16F reactive mask (<c>ReactiveMask</c>) for the temporal upscalers.
/// Any pixel with depth > 0 (written by a reactive object, the viewmodel and overlays) outputs 1.0: those have no valid
/// motion vectors, so the upscaler prefers this frame's colour there.
/// </summary>
internal sealed class ReactiveMaskLayer : NativeProceduralLayer
{
	static readonly Material MaskMaterial = Material.FromShader( "shaders/reactive_mask_generate.shader" );

	public ReactiveMaskLayer() : base( "Reactive Mask" )
	{
		Flags |= LayerFlags.DoesntModifyDepthStencilBuffer;
		ClearFlags = ClearFlags.Color;
	}

	/// <summary>
	/// Make the reactive depth and mask targets. Returns the depth target the reactive objects render into.
	/// </summary>
	public SceneViewRenderTargetHandle Setup( ISceneView view, RenderViewport viewport )
	{
		var depth = RenderTarget.GetTemporary(
			(int)viewport.Rect.Width,
			(int)viewport.Rect.Height,
			colorFormat: ImageFormat.None,
			depthFormat: ImageFormat.D32,
			targetName: "ReactiveDepth" );

		var mask = RenderTarget.GetTemporary(
			(int)viewport.Rect.Width,
			(int)viewport.Rect.Height,
			colorFormat: ImageFormat.R16F,
			depthFormat: ImageFormat.None,
			targetName: "ReactiveMask" );

		var depthHandle = depth.ToDepthHandle( view );

		ColorAttachment = mask.ToColorHandle( view );
		SetInput( "ReactiveDepth", depthHandle );

		view.GetRenderAttributesPtr().SetTextureValue( "ReactiveMask", mask.ColorTarget.native, -1 );

		return depthHandle;
	}

	protected override void OnRender()
	{
		Graphics.Blit( MaskMaterial );
	}
}

/// <summary>
/// Debug visualization (<see cref="SceneCameraDebugMode.MotionVectors"/>): the motion vectors over the final colour.
/// </summary>
internal sealed class MotionVectorDebugLayer : NativeProceduralLayer
{
	static readonly Material DebugMaterial = Material.FromShader( "shaders/motion_vectors_debug.shader" );

	public MotionVectorDebugLayer() : base( "Motion Vectors Debug" )
	{
	}

	protected override void OnRender()
	{
		Graphics.Blit( DebugMaterial );
	}
}

/// <summary>
/// Debug visualization (<see cref="SceneCameraDebugMode.ReactiveMask"/>): the reactive mask as a red overlay on the final colour.
/// </summary>
internal sealed class ReactiveMaskDebugLayer : NativeProceduralLayer
{
	static readonly Material DebugMaterial = Material.FromShader( "shaders/reactive_mask_debug.shader" );

	public ReactiveMaskDebugLayer() : base( "Reactive Mask Debug" )
	{
		Flags |= LayerFlags.DoesntModifyDepthStencilBuffer;
	}

	protected override void OnRender()
	{
		Graphics.Blit( DebugMaterial );
	}
}

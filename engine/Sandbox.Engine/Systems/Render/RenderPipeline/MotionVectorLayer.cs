using NativeEngine;

namespace Sandbox.Rendering;

internal class MotionVectorLayer : ProceduralRenderLayer
{
	private static Material MotionVectorMaterial = Material.FromShader( "shaders/motion_vectors.shader" );

	RenderViewport Viewport;
	RenderTarget MotionVectorRT;

	public MotionVectorLayer()
	{
		Name = "Static Motion Vectors";
		Flags |= LayerFlags.NeverRemove;
	}

	public void Setup( RenderViewport viewport, ISceneView view )
	{
		Viewport = viewport;

		MotionVectorRT = RenderTarget.GetTemporary(
			(int)Viewport.Rect.Width,
			(int)Viewport.Rect.Height,
			colorFormat: ImageFormat.RGBA16161616F,
			depthFormat: ImageFormat.None,
			targetName: "MotionVectors" );

		ColorAttachment = MotionVectorRT.ToColorHandle( view );
		ClearFlags = ClearFlags.Color;

		view.GetRenderAttributesPtr().SetTextureValue( "MotionVectors", MotionVectorRT.ColorTarget.native, -1 );
	}

	internal override void OnRender()
	{
		Graphics.Blit( MotionVectorMaterial );
	}
}

internal class MotionVectorDebugLayer : ProceduralRenderLayer
{
	private static Material DebugMaterial = Material.FromShader( "shaders/motion_vectors_debug.shader" );

	public MotionVectorDebugLayer()
	{
		Name = "Motion Vectors Debug";
		Flags |= LayerFlags.NeverRemove;
	}

	internal override void OnRender()
	{
		Graphics.Blit( DebugMaterial );
	}
}

using NativeEngine;
using Sandbox.Rendering;

namespace Sandbox.SceneRenderer.Native;

/// <summary>
/// Screen-space reflections (<see cref="ScreenSpaceReflectionsPass.Trace"/>), traced on graphics right after the Hi-Z chain
/// from the depth-normals G-buffer, before anything in the view samples them.
/// </summary>
internal sealed class ScreenSpaceReflectionsLayer : NativeProceduralLayer
{
	internal ScreenSpaceReflectionsPass Pass { get; set; }
	internal RenderMultisampleType Msaa { get; set; }

	public ScreenSpaceReflectionsLayer() : base( "Screen-space reflections" )
	{
	}

	protected override void Render( ManagedRenderSetup_t setup )
	{
		// The trace has no attachments, but still samples the camera's multisampled normals.
		setup.msaaLevel = Msaa;
		base.Render( setup );
	}

	protected override void OnRender() => Pass?.Trace();
}

/// <summary>
/// Keeps the lit scene after opaque as the next frame's screen-space reflection history
/// (<see cref="ScreenSpaceReflectionsPass.CaptureHistory"/>).
/// </summary>
internal sealed class ScreenSpaceReflectionsHistoryLayer : NativeProceduralLayer
{
	internal ScreenSpaceReflectionsPass Pass { get; set; }

	public ScreenSpaceReflectionsHistoryLayer() : base( "Screen-space reflections history" )
	{
		Flags |= LayerFlags.DoesntModifyColorBuffers;
	}

	public void Setup( SceneViewRenderTargetHandle color, SceneViewRenderTargetHandle depth )
	{
		ColorAttachment = color;
		DepthAttachment = depth;
	}

	protected override void OnRender() => Pass?.CaptureHistory();
}

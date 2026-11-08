using Sandbox.Rendering;

namespace Sandbox.SceneRenderer;

/// <summary>
/// Screen-space reflections (<see cref="ScreenSpaceReflectionsPass.Trace"/>), traced right after the depth chain from the
/// depth-normals G-buffer, as native's <see cref="Native.ScreenSpaceReflectionsLayer"/> does.
/// </summary>
internal sealed class ScreenSpaceReflectionsLayer : RenderLayer
{
	public ScreenSpaceReflectionsLayer() : base( "Screen-space reflections" ) { }

	public override bool IsNeeded( RenderFrame frame ) => frame.Stages?.ScreenSpaceReflections is not null;

	public override FrameResources Reads( RenderFrame frame ) => FrameResources.DepthChain | FrameResources.Normals;
	public override FrameResources Writes => FrameResources.Reflections;

	public override void Record( RenderFrame frame, RenderContext rc, ref RenderStats stats )
	{
		frame.RunScreenSpaceReflections( rc, history: false );
	}
}

/// <summary>
/// Keeps the lit scene after opaque as the next frame's screen-space reflection history
/// (<see cref="ScreenSpaceReflectionsPass.CaptureHistory"/>).
/// </summary>
internal sealed class ScreenSpaceReflectionsHistoryLayer : RenderLayer
{
	public ScreenSpaceReflectionsHistoryLayer() : base( "Screen-space reflections history" ) { }

	public override bool IsNeeded( RenderFrame frame ) => frame.Stages?.ScreenSpaceReflections is not null;

	public override void Record( RenderFrame frame, RenderContext rc, ref RenderStats stats )
	{
		frame.RunScreenSpaceReflections( rc, history: true );
	}
}

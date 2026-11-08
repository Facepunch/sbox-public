namespace Basalt;

/// <summary>
/// Screen-space reflections (the host's, through <see cref="IRenderStages.RenderScreenSpaceReflections"/>), traced right
/// after the depth chain from the depth-normals G-buffer, as native's <c>ScreenSpaceReflectionsLayer</c> does.
/// </summary>
internal sealed class ScreenSpaceReflectionsLayer : RenderLayer
{
	public ScreenSpaceReflectionsLayer() : base( "Screen-space reflections" ) { }

	public override bool IsNeeded( RenderFrame frame ) => frame.Stages?.HasScreenSpaceReflections == true;

	public override FrameResources Reads( RenderFrame frame ) => FrameResources.DepthChain | FrameResources.Normals;
	public override FrameResources Writes => FrameResources.Reflections;

	public override void Record( RenderFrame frame, GpuContext rc, ref RenderStats stats )
	{
		frame.RunScreenSpaceReflections( rc, history: false );
	}
}

/// <summary>
/// Keeps the lit scene after opaque as the next frame's screen-space reflection history
/// (<see cref="IRenderStages.RenderScreenSpaceReflections"/>).
/// </summary>
internal sealed class ScreenSpaceReflectionsHistoryLayer : RenderLayer
{
	public ScreenSpaceReflectionsHistoryLayer() : base( "Screen-space reflections history" ) { }

	public override bool IsNeeded( RenderFrame frame ) => frame.Stages?.HasScreenSpaceReflections == true;

	public override void Record( RenderFrame frame, GpuContext rc, ref RenderStats stats )
	{
		frame.RunScreenSpaceReflections( rc, history: true );
	}
}

namespace Borealis;

/// <summary>
/// Screen-space reflections (the host's, through <see cref="IRenderStages.RenderScreenSpaceReflections"/>), traced right
/// after the depth chain, as native's <c>ScreenSpaceReflectionsLayer</c> does - while <see cref="Enabled"/>, which is off.
/// </summary>
internal sealed class ScreenSpaceReflectionsLayer : RenderLayer
{
	/// <summary>
	/// Whether Borealis traces screen-space reflections. Off: they trace from material normals and roughness at each ray's origin,
	/// which only native's depth-normals G-buffer had, and Borealis draws none. Turn it back on once the forward pass or the
	/// material shader format supplies them.
	/// </summary>
	internal static bool Enabled { get; set; }

	public ScreenSpaceReflectionsLayer() : base( "Screen-space reflections" ) { }

	public override bool IsNeeded( RenderFrame frame ) => Enabled && frame.Stages?.HasScreenSpaceReflections == true;

	public override FrameResources Reads( RenderFrame frame ) => FrameResources.DepthChain;
	public override FrameResources Writes => FrameResources.Reflections;

	public override void Record( RenderFrame frame, GpuContext rc, ref RenderStats stats )
	{
		frame.RunScreenSpaceReflections( rc, history: false );
	}
}

/// <summary>
/// Keeps the lit scene after opaque as the next frame's screen-space reflection history
/// (<see cref="IRenderStages.RenderScreenSpaceReflections"/>), while <see cref="ScreenSpaceReflectionsLayer.Enabled"/>.
/// </summary>
internal sealed class ScreenSpaceReflectionsHistoryLayer : RenderLayer
{
	public ScreenSpaceReflectionsHistoryLayer() : base( "Screen-space reflections history" ) { }

	public override bool IsNeeded( RenderFrame frame ) => ScreenSpaceReflectionsLayer.Enabled && frame.Stages?.HasScreenSpaceReflections == true;

	public override void Record( RenderFrame frame, GpuContext rc, ref RenderStats stats )
	{
		frame.RunScreenSpaceReflections( rc, history: true );
	}
}

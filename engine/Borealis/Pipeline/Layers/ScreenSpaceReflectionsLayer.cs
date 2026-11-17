namespace Borealis;

/// <summary>
/// Screen-space reflections (the host's, through <see cref="IRenderStages.RenderScreenSpaceReflections"/>), traced right
/// after the depth chain, as native's <c>ScreenSpaceReflectionsLayer</c> does, while <see cref="Enabled"/>. They trace from
/// the normal and roughness at each ray's origin, which the opaque pass writes beside colour while they're on
/// (<see cref="NormalsFor"/>): the trace, before the opaque pass, reads last frame's, where this frame's surface was then.
/// </summary>
internal sealed class ScreenSpaceReflectionsLayer : RenderLayer
{
	/// <summary>
	/// Whether Borealis traces screen-space reflections, on a device whose opaque pass can write normals and roughness
	/// (<see cref="GpuDevice.WritesForwardNormals"/>), for a camera that has them. The host keeps it as the graphics settings
	/// leave them (<c>r_ssr_downsample_ratio</c>).
	/// </summary>
	internal static bool Enabled { get; set; } = true;

	/// <summary>
	/// The format of <see cref="NormalsFor"/>: the octahedral normal in red and green, roughness in blue, alpha 1 where a
	/// surface wrote it (sbox/surface_passes.slangh).
	/// </summary>
	internal const TextureFormat NormalsFormat = TextureFormat.RGBA1010102;

	GpuTexture normals;

	public ScreenSpaceReflectionsLayer() : base( "Screen-space reflections" ) { }

	/// <summary>
	/// The target the frame's opaque pass writes normals and roughness into while it traces reflections, the size and sample
	/// count of its colour, kept from frame to frame; null, and released, while it doesn't. A new one starts cleared, so its
	/// first frame traces nothing.
	/// </summary>
	internal GpuTexture NormalsFor( RenderFrame frame )
	{
		var device = RenderHost.Current.Device;
		if ( !Enabled || frame.IsSkybox || frame.Stages?.HasScreenSpaceReflections != true || device is not { WritesForwardNormals: true } )
		{
			Release();
			return null;
		}

		var color = frame.Output.Target.Color;
		if ( normals is { } kept && kept.Width == color.Width && kept.Height == color.Height && kept.Samples == color.Samples ) return kept;

		Release();
		normals = device.CreateTexture( TextureDesc.Target( color.Width, color.Height, NormalsFormat, color.Samples ) with { SampledMultisampled = true }, "Borealis forward normals" );
		device.Clear( normals, Color.Transparent );
		return normals;
	}

	void Release()
	{
		normals?.Dispose();
		normals = null;
	}

	public override bool IsNeeded( RenderFrame frame ) => frame.Normals is not null;

	public override FrameResources Reads( RenderFrame frame ) => FrameResources.DepthChain;
	public override FrameResources Writes => FrameResources.Reflections;

	public override void Record( RenderFrame frame, GpuContext rc, ref RenderStats stats )
	{
		using var zone = Zones.Reflections.Start();
		frame.RunScreenSpaceReflections( rc, history: false );
	}

	public override void Dispose() => Release();
}

/// <summary>
/// Keeps the lit scene after opaque as the next frame's screen-space reflection history
/// (<see cref="IRenderStages.RenderScreenSpaceReflections"/>), while the frame traces them.
/// </summary>
internal sealed class ScreenSpaceReflectionsHistoryLayer : RenderLayer
{
	public ScreenSpaceReflectionsHistoryLayer() : base( "Screen-space reflections history" ) { }

	public override bool IsNeeded( RenderFrame frame ) => frame.Normals is not null;

	public override void Record( RenderFrame frame, GpuContext rc, ref RenderStats stats )
	{
		using var zone = Zones.Reflections.Start();
		frame.RunScreenSpaceReflections( rc, history: true );
	}
}

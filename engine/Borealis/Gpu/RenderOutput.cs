namespace Borealis.Gpu;

/// <summary>
/// Frame target and optional final copy to a texture.
/// </summary>
internal readonly struct RenderOutput
{
	/// <summary>
	/// What the view draws into.
	/// </summary>
	public ViewTarget Target { get; init; }

	/// <summary>
	/// Whether the frame is shown in a window (the game window's scene): its plan and debug views' report go to the host
	/// (<see cref="RenderHost.PublishFramePlan"/>, <see cref="RenderHost.PublishDebugViews"/>). Texture and child views'
	/// frames aren't.
	/// </summary>
	public bool Window { get; init; }

	/// <summary>
	/// Optional texture destination for the HDR copy.
	/// </summary>
	public GpuTexture Texture { get; init; }

	/// <summary>
	/// MSAA scratch for final stages, preserving compatible depth before resolving into <see cref="Texture"/>
	/// (<c>CCameraRenderer::RenderToLayer</c>).
	/// </summary>
	public GpuTexture Scratch { get; init; }

	/// <summary>
	/// Where the finished frame is copied mirrored, once everything has drawn (<see cref="RenderView.FlipX"/>,
	/// <see cref="RenderView.FlipY"/>), as native blits a flipped view into its texture (<c>CCameraRenderer::RenderToTexture</c>).
	/// The frame draws into <see cref="Texture"/> or <see cref="Target"/> as usual, which are the render system's own then.
	/// </summary>
	public GpuTexture FlipInto { get; init; }

	/// <summary>
	/// Which ways <see cref="FlipInto"/> is mirrored.
	/// </summary>
	public bool FlipX { get; init; }
	public bool FlipY { get; init; }

	/// <summary>
	/// Full target size in pixels.
	/// </summary>
	public Vector2 Size => Target.Size;

	/// <summary>
	/// Samples per pixel. The shaders read it (<c>g_nMSAASampleCount</c>).
	/// </summary>
	public int Samples => Target.Samples;

	/// <summary>
	/// A frame into a texture: drawn into an HDR target, then copied in.
	/// </summary>
	public static RenderOutput ForTexture( ViewTarget hdr, GpuTexture texture, GpuTexture scratch = null ) => new() { Target = hdr, Texture = texture, Scratch = scratch };

	public static RenderOutput ForTarget( ViewTarget target ) => new() { Target = target };
}

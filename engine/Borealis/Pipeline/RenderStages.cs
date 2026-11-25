namespace Borealis;

/// <summary>
/// Camera commands, effects and UI at native hook points (<c>HookLayers</c>).
/// Stages through tonemapping use HDR; later stages use the final output. Targets are bound on entry.
/// </summary>
internal interface IRenderStages
{
	void Render( RenderStage stage, in StageTarget target );

	/// <summary>
	/// Whether an independent effect can run ahead of <paramref name="stage"/> on <paramref name="queue"/>.
	/// </summary>
	bool HasEarlyPostProcess( RenderStage stage, StageQueue queue ) => false;

	/// <summary>
	/// Run it in <paramref name="target"/>'s context, with no frame targets bound. The normal stage then skips it.
	/// </summary>
	void RenderEarlyPostProcess( RenderStage stage, StageQueue queue, in StageTarget target ) { }

	/// <summary>
	/// Whether the camera's command lists draw at <paramref name="stage"/>. At <see cref="RenderStage.AfterDepthPrepass"/>
	/// they draw into the frame's colour and depth, so the frame clears colour before that stage instead of in the
	/// opaque pass (GAMMA's mirror glass).
	/// </summary>
	bool DrawsAt( RenderStage stage ) => false;

	/// <summary>
	/// Whether the camera renders screen-space reflections.
	/// </summary>
	bool HasScreenSpaceReflections => false;

	/// <summary>
	/// Trace them in <paramref name="target"/>'s context with no frame targets bound, or keep the lit scene as their
	/// <paramref name="history"/> with colour bound.
	/// </summary>
	void RenderScreenSpaceReflections( bool history, in StageTarget target ) { }
}

/// <summary>
/// Targets, context and attributes available to a stage.
/// </summary>
internal readonly struct StageTarget
{
	public GpuContext Context { get; init; }

	/// <summary>
	/// The frame's attributes - view constants, lighting, the bindless set.
	/// </summary>
	public ShaderParameters Attributes { get; init; }

	public GpuTexture Color { get; init; }
	public GpuTexture Depth { get; init; }

	/// <summary>
	/// The opaque pass's second colour target while screen-space reflections are on: each opaque surface's normal and
	/// roughness (<see cref="ScreenSpaceReflectionsLayer.NormalsFor"/>). Bound beside <see cref="Color"/> in the opaque
	/// pass; given to the reflections' trace, which reads last frame's.
	/// </summary>
	public GpuTexture Normals { get; init; }

	/// <summary>
	/// The array slice or cube face of <see cref="Depth"/> bound, for a shadow view with no colour.
	/// </summary>
	public int DepthSlice { get; init; }

	/// <summary>
	/// HDR colour for auto exposure. Equals <see cref="Color"/> until the output copy.
	/// </summary>
	public GpuTexture HdrColor { get; init; }

	/// <summary>
	/// Whether colour is written through an sRGB view: an 8-bit output is, a float one isn't.
	/// </summary>
	public bool SrgbWrite { get; init; }

	/// <summary>
	/// Whether this target is the final output.
	/// </summary>
	public bool IsOutput { get; init; }

	/// <summary>
	/// The targets' size in pixels, which <see cref="Viewport"/> is within.
	/// </summary>
	public Vector2 Size { get; init; }

	public Rect Viewport { get; init; }
	public float MinZ { get; init; }
	public float MaxZ { get; init; }

	public TextureFormat ColorFormat { get; init; }
	public int Samples { get; init; }
}

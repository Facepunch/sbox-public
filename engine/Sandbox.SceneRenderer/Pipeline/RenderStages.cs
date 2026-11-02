using NativeEngine;
using Sandbox.Rendering;

namespace Sandbox.SceneRenderer;

/// <summary>
/// Camera commands, effects and UI at native hook points (<see cref="Native.HookLayers"/>).
/// Stages through tonemapping use HDR; later stages use the final output. Targets are bound on entry.
/// </summary>
internal interface IRenderStages
{
	void Render( Stage stage, in StageTarget target );

	/// <summary>
	/// Whether an independent effect can run ahead of <paramref name="stage"/> on <paramref name="queue"/>.
	/// </summary>
	bool HasEarlyPostProcess( Stage stage, PostProcessQueue queue ) => false;

	/// <summary>
	/// Run it in <paramref name="target"/>'s context, with no frame targets bound. The normal stage then skips it.
	/// </summary>
	void RenderEarlyPostProcess( Stage stage, PostProcessQueue queue, in StageTarget target ) { }

	/// <summary>
	/// The camera's screen-space reflections, or null when it doesn't render them.
	/// </summary>
	ScreenSpaceReflectionsPass ScreenSpaceReflections => null;

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
	public IRenderContext Context { get; init; }

	/// <summary>
	/// The frame's attributes - view constants, lighting, the bindless set.
	/// </summary>
	public RenderAttributes Attributes { get; init; }

	public ITexture Color { get; init; }
	public ITexture Depth { get; init; }

	/// <summary>
	/// The array slice or cube face of <see cref="Depth"/> bound, for a shadow view with no colour.
	/// </summary>
	public int DepthSlice { get; init; }

	/// <summary>
	/// HDR colour for auto exposure. Equals <see cref="Color"/> until the output copy.
	/// </summary>
	public ITexture HdrColor { get; init; }

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

	public ImageFormat ColorFormat { get; init; }
	public int Samples { get; init; }
}

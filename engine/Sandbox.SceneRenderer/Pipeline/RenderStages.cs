using NativeEngine;
using Sandbox.Rendering;

namespace Sandbox.SceneRenderer;

/// <summary>
/// Camera commands, effects and UI at native hook points (<c>CManagedRenderPipeline::OnHook</c>).
/// Stages through tonemapping use HDR; later stages use the final output. Targets are bound on entry.
/// </summary>
internal interface IRenderStages
{
	void Render( Stage stage, in StageTarget target );

	/// <summary>
	/// Whether something at <paramref name="stage"/> can run on the async compute queue, ahead of it.
	/// </summary>
	bool HasAsyncCompute( Stage stage ) => false;

	/// <summary>
	/// Run it, into <paramref name="target"/>'s context on the compute queue, with no targets bound. The stage then skips it.
	/// </summary>
	void RenderAsyncCompute( Stage stage, in StageTarget target ) { }
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

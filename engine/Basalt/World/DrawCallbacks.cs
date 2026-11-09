namespace Basalt;

/// <summary>
/// Work an object runs around its own draws in colour layers (<c>SCENEOBJECTFLAG_CALLBACK_BEFORE</c> and
/// <c>_AFTER</c>): before the first of a contiguous stretch of its draws, and after the last, as native's batch lists call
/// them (<c>CRenderBatchList::DrawCurrentPrimitives</c>, <c>FlushAfterRenderCallback</c>). Depth and shadow layers skip
/// them. An object with callbacks draws in runs of its own, recorded on the main thread.
/// </summary>
public abstract class DrawCallbacks
{
	/// <summary>
	/// Run <see cref="Render"/> before the object's draws.
	/// </summary>
	public bool Before { get; set; }

	/// <summary>
	/// Run <see cref="Render"/> after the object's draws.
	/// </summary>
	public bool After { get; set; }

	/// <summary>
	/// Run the work before (<paramref name="after"/> false) or after the object's draws, with the layer's targets bound.
	/// Targets are restored afterward.
	/// </summary>
	internal abstract void Render( GpuContext context, ViewPass view, MeshLayer layer, bool after );
}

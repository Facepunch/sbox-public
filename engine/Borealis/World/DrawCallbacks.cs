namespace Borealis;

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

	/// <summary>
	/// Count the samples its stretches of draws pass in colour layers (occlusion queries, <see cref="GpuContext.BeginCount"/>)
	/// and give a frame's total to <see cref="Counted"/> a few frames later, one frame's counts at a time.
	/// </summary>
	internal virtual bool Counts => false;

	/// <summary>
	/// The samples the object's draws passed in every view of one frame: none when all of it was hidden.
	/// </summary>
	internal virtual void Counted( ulong samples ) { }

	// The frame's counts still out, for the device that takes them
	internal int CountFrame, CountPending;
	internal ulong CountSum;
}

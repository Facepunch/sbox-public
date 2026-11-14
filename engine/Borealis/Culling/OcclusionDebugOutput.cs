namespace Borealis.Culling;

/// <summary>
/// Where occlusion culling's debug view goes: a grid the cull draws its boxes into, resolved over the depth chain into an
/// image for the host to show, with the cull's counts. The host makes one (<see cref="RenderHost.CreateOcclusionDebug"/>);
/// without one there's no debug view.
/// </summary>
internal abstract class OcclusionDebugOutput : IDisposable
{
	/// <summary>
	/// Size the grid for this frame's viewport. Called on the main thread, before the view renders.
	/// </summary>
	public abstract void Resize( Rect viewport );

	/// <summary>
	/// Bind the cull shader's debug inputs. Boxes are drawn from <paramref name="worldToProjection"/>, the camera's view, so
	/// they follow it even while the cull is frozen.
	/// </summary>
	public abstract void Bind( ShaderParameters parameters, Matrix worldToProjection );

	/// <summary>
	/// Zero the grid and counts before the frame's first cull.
	/// </summary>
	public abstract void Clear( Gpu.GpuContext context );

	/// <summary>
	/// Resolve the grid over <paramref name="chain"/> after the late cull, and read the counts back.
	/// </summary>
	public abstract void Resolve( Gpu.GpuContext context, GpuTexture chain, bool frozen );

	public abstract void Dispose();
}

using NativeEngine;
using System.Runtime.InteropServices;

namespace Sandbox.Rendering;

/// <summary>
/// A camera's <c>r_occlusion_debug</c> output: the grid the cull draws its boxes into, the image the overlay shows, and the
/// cull's counts, read back a few frames late.
/// </summary>
internal sealed class OcclusionDebugTarget : IDisposable
{
	/// <summary>
	/// Counter slots. Must match occlusion_cull_cs.slang's CELL_ and COUNT_ values.
	/// </summary>
	internal enum Count
	{
		/// <summary>Final phase instances.</summary>
		Final,
		NeverCulled,
		Visible,
		Untested,
		PartlyHidden,
		Hidden,

		/// <summary>Drawn by the late prepass: hidden last frame, visible now.</summary>
		Late,

		/// <summary>Early phase instances, and those the early prepass drew.</summary>
		Early,
		EarlyDrawn,
	}

	// Must match DEBUG_HEADER in occlusion_cull_cs.slang
	internal const int Header = 16;

	// What r_occlusion_debug 1 draws boxes for
	const int Culled = 1 << (int)Count.PartlyHidden | 1 << (int)Count.Hidden | 1 << (int)Count.Late;

	GpuBuffer<uint> grid;
	readonly uint[] counts = new uint[Header];

	public OcclusionDebugTarget()
	{
	}

	public Vector2Int Size { get; private set; }

	public Texture Image { get; private set; }
	public Rect Viewport { get; private set; }
	public bool Native { get; private set; }
	public bool Frozen { get; private set; }

	/// <summary>
	/// Sizes the grid for this frame's viewport. Call on the main thread, before the view renders.
	/// </summary>
	public void Resize( Rect viewport )
	{
		Viewport = viewport;
		var size = new Vector2Int( Math.Max( 1, (int)viewport.Width ), Math.Max( 1, (int)viewport.Height ) );
		if ( grid is not null && size == Size ) return;

		Dispose();
		Size = size;
		grid = new GpuBuffer<uint>( Header + size.x * size.y, GpuBuffer.UsageFlags.Structured, "Occlusion debug grid" );
		Image = Texture.CreateRenderTarget().WithFormat( ImageFormat.RGBA8888 ).WithSize( size.x, size.y ).WithUAVBinding().Create( "Occlusion debug" );
	}

	/// <summary>
	/// Binds the cull shader's debug inputs, or turns them off without a target. Boxes are drawn from
	/// <paramref name="worldToProjection"/>, the camera's view, so they follow it even while the cull is frozen.
	/// </summary>
	public static void Bind( RenderAttributes attributes, OcclusionDebugTarget target, Matrix worldToProjection )
	{
		attributes.SetCombo( "D_OCCLUSION_DEBUG", target is not null );
		if ( target is null ) return;

		attributes.Set( "OcclusionDebug", target.grid );
		attributes.Set( "OcclusionDebugSize", target.Size );
		attributes.Set( "OcclusionDebugShown", OcclusionDebug.View == OcclusionDebugView.All ? ~0 : Culled );
		attributes.Set( "OcclusionDebugWorldToProjection", worldToProjection );
	}

	public uint this[Count count]
	{
		get
		{
			lock ( counts ) return counts[(int)count];
		}
	}

	/// <summary>
	/// The device retains resources used by in-flight frames.
	/// </summary>
	public void Dispose()
	{
		grid?.Dispose();
		Image?.Dispose();
		grid = null;
		Image = null;
	}
}

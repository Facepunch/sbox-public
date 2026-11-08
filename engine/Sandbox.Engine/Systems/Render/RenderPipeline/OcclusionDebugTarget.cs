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
	/// Counter slots. Must match occlusion_cull_cs.shader's CELL_ and COUNT_ values.
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

	// Must match DEBUG_HEADER in occlusion_cull_cs.shader
	internal const int Header = 16;

	// What r_occlusion_debug 1 draws boxes for
	const int Culled = 1 << (int)Count.PartlyHidden | 1 << (int)Count.Hidden | 1 << (int)Count.Late;

	const RenderBarrierPipelineStageFlags_t ComputeStage = RenderBarrierPipelineStageFlags_t.ComputeShaderBit;
	const RenderBarrierPipelineStageFlags_t TransferStage = RenderBarrierPipelineStageFlags_t.TransferBit;
	const RenderBarrierPipelineStageFlags_t PixelStage = RenderBarrierPipelineStageFlags_t.FragmentShaderBit;
	const RenderBarrierAccessFlags_t ReadAccess = RenderBarrierAccessFlags_t.ShaderReadBit;
	const RenderBarrierAccessFlags_t WriteAccess = RenderBarrierAccessFlags_t.ShaderWriteBit;
	const RenderBarrierAccessFlags_t CopyAccess = RenderBarrierAccessFlags_t.TransferReadBit;

	static Material shader;

	GpuBuffer<uint> grid;
	readonly uint[] counts = new uint[Header];
	readonly AsyncGPUReadback.BufferReadDelegate onRead;

	public OcclusionDebugTarget()
	{
		onRead = OnRead;
		shader ??= Material.FromShader( "shaders/occlusion_debug_cs.shader" );
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

	/// <summary>
	/// Zeroes the grid and counters before this frame's first cull.
	/// </summary>
	public void Clear( IRenderContext context )
	{
		// Last frame's counters were copied out for the readback
		context.BufferBarrierTransition( grid.native, ComputeStage | TransferStage, ComputeStage, ReadAccess | WriteAccess | CopyAccess, WriteAccess );
		Dispatch( context, null );
		context.BufferBarrierTransition( grid.native, ComputeStage, ComputeStage, WriteAccess, ReadAccess | WriteAccess );
	}

	/// <summary>
	/// Resolves the grid into <see cref="Image"/> after the late cull, over <paramref name="chain"/> in Depth mode, and offers
	/// it to the overlay.
	/// </summary>
	public void Resolve( IRenderContext context, Texture chain, bool native, bool frozen )
	{
		context.BufferBarrierTransition( grid.native, ComputeStage, ComputeStage, WriteAccess, ReadAccess );
		context.TextureBarrierTransition( Image.native, -1, PixelStage, ComputeStage, RenderImageLayout_t.RENDER_IMAGE_LAYOUT_GENERAL, ReadAccess, WriteAccess );
		Dispatch( context, chain );
		context.TextureBarrierTransition( Image.native, -1, ComputeStage, PixelStage, RenderImageLayout_t.RENDER_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, WriteAccess, ReadAccess );

		Native = native;
		Frozen = frozen;
		OcclusionDebug.Publish( this );
	}

	/// <summary>
	/// Reads the counters back. Like any readback, this submits everything recorded before it.
	/// </summary>
	public void ReadCounts( IRenderContext context )
	{
		context.BufferBarrierTransition( grid.native, ComputeStage, TransferStage, WriteAccess, CopyAccess );
		context.ReadBufferAsync( grid, onRead, 0, Header * sizeof( uint ) );
	}

	public uint this[Count count]
	{
		get
		{
			lock ( counts ) return counts[(int)count];
		}
	}

	// Without a chain, clears
	void Dispatch( IRenderContext context, Texture chain )
	{
		var attributes = RenderAttributes.Pool.Get();
		attributes.SetCombo( "D_RESOLVE", chain is not null );
		attributes.Set( "OcclusionDebug", grid );
		attributes.Set( "OcclusionDebugSize", Size );

		if ( chain is not null )
		{
			attributes.Set( "OcclusionDebugOutput", Image );
			attributes.Set( "OcclusionDebugChain", chain );
			attributes.Set( "OcclusionDebugMip", OcclusionDebug.View == OcclusionDebugView.Depth ? OcclusionDebug.Mip : -1 );
		}

		RenderTools.Compute( context, attributes.Get(), shader.native.GetMode(), Size.x, Size.y, 1 );
		RenderAttributes.Pool.Return( attributes );
	}

	void OnRead( Span<byte> bytes )
	{
		var values = MemoryMarshal.Cast<byte, uint>( bytes );
		lock ( counts ) values[..Header].CopyTo( counts );
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

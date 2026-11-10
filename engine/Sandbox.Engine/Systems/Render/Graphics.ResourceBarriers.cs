using NativeEngine;
using Sandbox.Rendering;

namespace Sandbox;

public static partial class Graphics
{
	/// <summary>
	/// Executes a barrier transition for the given GPU Texture Resource.
	/// Transitions the texture resource to a new pipeline stage and access state.
	/// </summary>
	/// <param name="texture">The texture to transition.</param>
	/// <param name="state">The new resource state for the texture.</param>
	/// <param name="mip">The mip level to transition (-1 for all mips).</param>
	public static void ResourceBarrierTransition( Texture texture, ResourceState state, int mip = -1 )
	{
		ArgumentNullException.ThrowIfNull( texture );

		Backend.ResourceBarrierTransition( texture, state, mip );
	}

	/// <summary>
	/// Executes a barrier transition for the given GPU Buffer Resource.
	/// Transitions the buffer resource to a new pipeline stage and access state.
	/// </summary>
	/// <typeparam name="T">The unmanaged type of the buffer elements.</typeparam>
	/// <param name="buffer">The GPU buffer to transition.</param>
	/// <param name="state">The new resource state for the buffer.</param>
	public static void ResourceBarrierTransition<T>( GpuBuffer<T> buffer, ResourceState state ) where T : unmanaged
	{
		ArgumentNullException.ThrowIfNull( buffer );

		Backend.ResourceBarrierTransition( buffer, state );
	}

	/// <summary>
	/// Executes a barrier transition for the given GPU Buffer Resource.
	/// Transitions the buffer resource to a new pipeline stage and access state.
	/// </summary>
	/// <param name="buffer">The GPU buffer to transition.</param>
	/// <param name="state">The new resource state for the buffer.</param>
	public static void ResourceBarrierTransition( GpuBuffer buffer, ResourceState state )
	{
		ArgumentNullException.ThrowIfNull( buffer );

		Backend.ResourceBarrierTransition( buffer, state );
	}

	/// <summary>
	/// Executes a barrier transition for the given GPU Buffer Resource.
	/// Transitions the buffer resource from a known source state to a specified destination state.
	/// </summary>
	/// <typeparam name="T">The unmanaged type of the buffer elements.</typeparam>
	/// <param name="buffer">The GPU buffer to transition.</param>
	/// <param name="before">The current resource state of the buffer.</param>
	/// <param name="after">The desired resource state of the buffer after the transition.</param>
	public static void ResourceBarrierTransition<T>( GpuBuffer<T> buffer, ResourceState before, ResourceState after ) where T : unmanaged
	{
		ArgumentNullException.ThrowIfNull( buffer );

		Backend.ResourceBarrierTransition( buffer, before, after );
	}

	/// <summary>
	/// Executes a barrier transition for the given GPU Buffer Resource.
	/// Transitions the buffer resource from a known source state to a specified destination state.
	/// </summary>
	/// <param name="buffer">The GPU buffer to transition.</param>
	/// <param name="before">The current resource state of the buffer.</param>
	/// <param name="after">The desired resource state of the buffer after the transition.</param>
	public static void ResourceBarrierTransition( GpuBuffer buffer, ResourceState before, ResourceState after )
	{
		ArgumentNullException.ThrowIfNull( buffer );

		Backend.ResourceBarrierTransition( buffer, before, after );
	}

	/// <summary>
	/// Issues a UAV barrier for the given texture, ensuring writes from prior shader invocations
	/// are visible to subsequent ones without changing the resource layout.
	/// </summary>
	/// <param name="texture">The texture to barrier.</param>
	public static void UavBarrier( Texture texture )
	{
		ArgumentNullException.ThrowIfNull( texture );

		Backend.UavBarrier( texture );
	}

	/// <summary>
	/// A UAV barrier between compute dispatches only, so it's valid on an async compute queue too
	/// </summary>
	internal static void ComputeUavBarrier( IRenderContext context, Texture texture )
	{
		var stage = RenderBarrierPipelineStageFlags_t.ComputeShaderBit;
		var access = RenderBarrierAccessFlags_t.ShaderReadBit | RenderBarrierAccessFlags_t.ShaderWriteBit;

		context.TextureBarrierTransition( texture.native, -1, stage, stage, RenderImageLayout_t.RENDER_IMAGE_LAYOUT_GENERAL, access, access );
	}

	/// <summary>
	/// Issues a UAV barrier for the given GPU buffer, ensuring writes from prior shader invocations
	/// are visible to subsequent ones.
	/// </summary>
	/// <param name="buffer">The buffer to barrier.</param>
	public static void UavBarrier( GpuBuffer buffer )
	{
		ArgumentNullException.ThrowIfNull( buffer );

		Backend.UavBarrier( buffer );
	}
}

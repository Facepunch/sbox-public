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

		RenderBarrierPipelineStageFlags_t srcStage, dstStage;
		RenderBarrierAccessFlags_t barrierAccessFlags;
		RenderImageLayout_t imageLayout;

		// It doesn't seem to care if we don't actually know this already
		// But we could know it from native if we care
		srcStage = RenderBarrierPipelineStageFlags_t.BottomOfPipeBit;

		ResourceStateToVulkanFlags( state, out dstStage, out barrierAccessFlags, out imageLayout );

		Context.TextureBarrierTransition( texture.native, mip, srcStage, dstStage, imageLayout, 0, barrierAccessFlags );
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

		RenderBarrierPipelineStageFlags_t srcStage, dstStage;
		RenderBarrierAccessFlags_t barrierAccessFlags;
		RenderImageLayout_t imageLayout;

		// It doesn't seem to care if we don't actually know this already
		// But we could know it from native if we care
		srcStage = RenderBarrierPipelineStageFlags_t.BottomOfPipeBit;

		ResourceStateToVulkanFlags( state, out dstStage, out barrierAccessFlags, out imageLayout, buffer.Usage );

		Context.BufferBarrierTransition( buffer.native, srcStage, dstStage, 0, barrierAccessFlags );
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

		RenderBarrierPipelineStageFlags_t srcStage, dstStage;
		RenderBarrierAccessFlags_t barrierAccessFlags;
		RenderImageLayout_t imageLayout;

		// It doesn't seem to care if we don't actually know this already
		// But we could know it from native if we care
		srcStage = RenderBarrierPipelineStageFlags_t.BottomOfPipeBit;

		ResourceStateToVulkanFlags( state, out dstStage, out barrierAccessFlags, out imageLayout, buffer.Usage );

		Context.BufferBarrierTransition( buffer.native, srcStage, dstStage, 0, barrierAccessFlags );
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

		ResourceStateToVulkanFlags( before, out var srcStage, out var srcFlags, out _, buffer.Usage );
		ResourceStateToVulkanFlags( after, out var dstStage, out var dstFlags, out _, buffer.Usage );

		Context.BufferBarrierTransition( buffer.native, srcStage, dstStage, srcFlags, dstFlags );
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

		ResourceStateToVulkanFlags( before, out var srcStage, out var srcFlags, out _, buffer.Usage );
		ResourceStateToVulkanFlags( after, out var dstStage, out var dstFlags, out _, buffer.Usage );

		Context.BufferBarrierTransition( buffer.native, srcStage, dstStage, srcFlags, dstFlags );
	}

	/// <summary>
	/// Issues a UAV barrier for the given texture, ensuring writes from prior shader invocations
	/// are visible to subsequent ones without changing the resource layout.
	/// </summary>
	/// <param name="texture">The texture to barrier.</param>
	public static void UavBarrier( Texture texture )
	{
		ArgumentNullException.ThrowIfNull( texture );

		var stage = RenderBarrierPipelineStageFlags_t.FragmentShaderBit | RenderBarrierPipelineStageFlags_t.ComputeShaderBit;
		var access = RenderBarrierAccessFlags_t.ShaderReadBit | RenderBarrierAccessFlags_t.ShaderWriteBit;

		Context.TextureBarrierTransition( texture.native, -1, stage, stage, RenderImageLayout_t.RENDER_IMAGE_LAYOUT_GENERAL, access, access );
	}

	/// <summary>
	/// Issues a UAV barrier for the given GPU buffer, ensuring writes from prior shader invocations
	/// are visible to subsequent ones.
	/// </summary>
	/// <param name="buffer">The buffer to barrier.</param>
	public static void UavBarrier( GpuBuffer buffer )
	{
		ArgumentNullException.ThrowIfNull( buffer );

		var stage = RenderBarrierPipelineStageFlags_t.FragmentShaderBit | RenderBarrierPipelineStageFlags_t.ComputeShaderBit;
		var access = RenderBarrierAccessFlags_t.ShaderReadBit | RenderBarrierAccessFlags_t.ShaderWriteBit;

		Context.BufferBarrierTransition( buffer.native, stage, stage, access, access );
	}

	/// <summary>
	/// Figure out what flags Vulkan needs for the given ResourceState
	/// </summary>
	private static void ResourceStateToVulkanFlags( ResourceState resourceState, out RenderBarrierPipelineStageFlags_t dstStageFlags, out RenderBarrierAccessFlags_t accessFlags, out RenderImageLayout_t imageLayout, GpuBuffer.UsageFlags bufferUsageFlags = 0 )
	{
		// By default set the barrier to max, we want to use this from anywhere
		dstStageFlags = RenderBarrierPipelineStageFlags_t.TopOfPipeBit;
		accessFlags = 0;
		imageLayout = RenderImageLayout_t.RENDER_IMAGE_LAYOUT_GENERAL;

		// We can refine usage from the given state, the only thing we gain from getting this right is performance
		// In terms of everything working the above case covers everything
		// TODO: These states are FLAGS, we should be combining them
		switch ( resourceState )
		{
			case ResourceState.GenericRead:
				break;
			case ResourceState.VertexOrIndexBuffer:
				dstStageFlags = RenderBarrierPipelineStageFlags_t.VertexInputBit;

				if ( bufferUsageFlags.HasFlag( GpuBuffer.UsageFlags.Index ) )
					accessFlags = RenderBarrierAccessFlags_t.IndexReadBit;

				if ( bufferUsageFlags.HasFlag( GpuBuffer.UsageFlags.Vertex ) )
					accessFlags = RenderBarrierAccessFlags_t.VertexAttributeReadBit;

				break;
			case ResourceState.RenderTarget:
				imageLayout = RenderImageLayout_t.RENDER_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
				accessFlags = RenderBarrierAccessFlags_t.ColorAttachmentReadBit | RenderBarrierAccessFlags_t.ColorAttachmentWriteBit;
				dstStageFlags = RenderBarrierPipelineStageFlags_t.ColorAttachmentOutputBit;
				break;
			case ResourceState.UnorderedAccess:
				accessFlags = RenderBarrierAccessFlags_t.ShaderReadBit | RenderBarrierAccessFlags_t.ShaderWriteBit;
				dstStageFlags = RenderBarrierPipelineStageFlags_t.FragmentShaderBit | RenderBarrierPipelineStageFlags_t.ComputeShaderBit;
				break;
			case ResourceState.DepthRead:
				imageLayout = RenderImageLayout_t.RENDER_IMAGE_LAYOUT_DEPTH_STENCIL_READ_ONLY_OPTIMAL;
				accessFlags = RenderBarrierAccessFlags_t.DepthStencilAttachmentReadBit;
				dstStageFlags = RenderBarrierPipelineStageFlags_t.FragmentShaderBit;
				break;
			case ResourceState.DepthWrite:
				imageLayout = RenderImageLayout_t.RENDER_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL;
				accessFlags = RenderBarrierAccessFlags_t.DepthStencilAttachmentReadBit | RenderBarrierAccessFlags_t.DepthStencilAttachmentWriteBit;
				dstStageFlags = RenderBarrierPipelineStageFlags_t.FragmentShaderBit;
				break;
			case ResourceState.NonPixelShaderResource:
				imageLayout = RenderImageLayout_t.RENDER_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
				accessFlags = RenderBarrierAccessFlags_t.ShaderReadBit;
				dstStageFlags = RenderBarrierPipelineStageFlags_t.PreRasterizationShadersBit | RenderBarrierPipelineStageFlags_t.ComputeShaderBit;
				break;
			case ResourceState.PixelShaderResource:
				imageLayout = RenderImageLayout_t.RENDER_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
				accessFlags = RenderBarrierAccessFlags_t.ShaderReadBit;
				dstStageFlags = RenderBarrierPipelineStageFlags_t.FragmentShaderBit;
				break;
			case ResourceState.CopyDestination:
				imageLayout = RenderImageLayout_t.RENDER_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
				accessFlags = RenderBarrierAccessFlags_t.TransferWriteBit;
				dstStageFlags = RenderBarrierPipelineStageFlags_t.TransferBit;
				break;
			case ResourceState.CopySource:
				imageLayout = RenderImageLayout_t.RENDER_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL;
				accessFlags = RenderBarrierAccessFlags_t.TransferReadBit;
				dstStageFlags = RenderBarrierPipelineStageFlags_t.TransferBit;
				break;
			case ResourceState.IndirectArgument:
				accessFlags = RenderBarrierAccessFlags_t.IndirectCommandReadBit;
				dstStageFlags = RenderBarrierPipelineStageFlags_t.DrawIndirectBit;
				break;
		}
	}
}

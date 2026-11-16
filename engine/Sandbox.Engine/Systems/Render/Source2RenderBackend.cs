using NativeEngine;
using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// <see cref="Graphics"/> on Source 2's render device: each call records into the block's native render context
/// (<see cref="Graphics.Context"/>) through <c>RenderTools</c>, as <see cref="Graphics"/> always has. It holds no state
/// of its own, so one instance serves every block on every thread.
/// </summary>
internal sealed class Source2RenderBackend : RenderBackend
{
	public static Source2RenderBackend Instance { get; } = new();

	const int maxDynamicVertexBuffer = 512 * 1024;

	static IRenderContext Context => Graphics.Context;

	//
	// Blocks and resources
	//

	public override int TextureIndex( Texture texture ) => g_pRenderDevice.GetTextureViewIndex( texture.native, 0, RenderTextureDimension.RENDER_TEXTURE_DIMENSION_2D );

	public override int SamplerIndex( SamplerState state ) => SamplerState.GetSource2BindlessIndex( state );

	public override IntPtr UploadContext => Graphics.Context;

	//
	// Targets and viewport
	//

	public override RenderViewport GetViewport() => Context.GetViewport();

	public override void SetViewport( in RenderViewport viewport ) => Context.SetViewport( viewport );

	public override void SetViewport( Rect rect ) => Context.SetViewport( rect );

	public override void BindRenderTarget( RenderTarget target ) => Context.BindRenderTargets( target.ColorTarget.native );

	public override void Clear( Color color, bool clearColor, bool clearDepth, bool clearStencil ) => Context.Clear( color, clearColor, clearDepth, clearStencil );

	public override void ClearTexture( Texture texture, Color color ) => Context.ClearTexture( texture.native, color );

	//
	// Draws
	//

	public override unsafe void DrawVertices( Type vertexType, int vertexSize, IntPtr vertices, int vertCount, IntPtr indices, int indexCount, Material material, RenderAttributes attributes, Graphics.PrimitiveType primitiveType )
	{
		// Get the layout for this vertex. This will create if not found.
		var layout = VertexLayout.Get( vertexType );
		if ( !layout.IsValid ) return;

		if ( layout.m_Size > vertexSize )
			throw new ArgumentException( $"{vertexType} vertex layout is larger than the vertex type" );

		// Set the material etc
		if ( !RenderTools.SetRenderState( Context, attributes.Get(), Graphics.ModeFor( material ), layout, Graphics.Stats ) )
			return;

		var totalSize = (long)vertexSize * vertCount;
		if ( indices != IntPtr.Zero || totalSize < maxDynamicVertexBuffer )
		{
			RenderTools.Draw( Context, (RenderPrimitiveType)primitiveType, layout, vertices, vertCount, indices, indexCount, Graphics.Stats );
			return;
		}

		int verticesPerChunk = maxDynamicVertexBuffer / vertexSize;

		// if we're drawing triangles, and have no indices, round to chunks of 3 so we don't
		// get geometry split between chunks.. because that would be a disaster
		if ( primitiveType == Graphics.PrimitiveType.Triangles && indices == IntPtr.Zero )
		{
			verticesPerChunk = verticesPerChunk.SnapToGrid( 3 );
		}

		//
		// todo - for lines chunk size needs to be multiple of 2, for triangles multiple of 3
		//

		for ( int i = 0; i < vertCount; i += verticesPerChunk )
		{
			var chunkCount = Math.Min( verticesPerChunk, vertCount - i );
			RenderTools.Draw( Context, (RenderPrimitiveType)primitiveType, layout, vertices + i * vertexSize, chunkCount, default, default, Graphics.Stats );
		}
	}

	public override bool SetRenderState( Material material, RenderAttributes attributes, Type vertexType )
	{
		NativeEngine.VertexLayout layout = default;
		if ( vertexType is not null )
		{
			layout = VertexLayout.Get( vertexType );
			if ( layout.IsNull )
				throw new ArgumentException( "T vertex layout is invalid.", "T" );
		}

		return RenderTools.SetRenderState( Context, attributes.Get(), Graphics.ModeFor( material ), layout, Graphics.Stats );
	}

	public override void Draw( GpuBuffer vertexBuffer, Graphics.PrimitiveType primitiveType, int startVertex, int vertexCount )
	{
		Context.BindVertexBuffer( 0, vertexBuffer.native, 0 );
		Context.Draw( (RenderPrimitiveType)primitiveType, startVertex, vertexCount );
	}

	public override void DrawIndexed( GpuBuffer vertexBuffer, GpuBuffer indexBuffer, Graphics.PrimitiveType primitiveType, int startIndex, int indexCount )
	{
		Context.BindVertexBuffer( 0, vertexBuffer.native, 0 );
		Context.BindIndexBuffer( indexBuffer.native, indexBuffer.ElementSize, 0 );
		Context.DrawIndexed( (RenderPrimitiveType)primitiveType, startIndex, indexCount, 0, 0 );
	}

	public override void DrawIndexedInstanced( GpuBuffer indexBuffer, Graphics.PrimitiveType primitiveType, int instanceCount )
	{
		Context.BindIndexBuffer( indexBuffer.native, indexBuffer.ElementSize, 0 );
		Context.DrawIndexedInstanced( (RenderPrimitiveType)primitiveType, 0, indexBuffer.ElementCount, instanceCount, 0, 0 );
	}

	public override void DrawInstancedIndirect( GpuBuffer vertexBuffer, Graphics.PrimitiveType primitiveType, GpuBuffer indirectBuffer, uint bufferOffset, uint drawCount, uint stride )
	{
		if ( vertexBuffer is not null ) Context.BindVertexBuffer( 0, vertexBuffer.native, 0 );
		Context.DrawInstancedIndirect( (RenderPrimitiveType)primitiveType, indirectBuffer.native, bufferOffset, drawCount, stride );
	}

	public override void DrawIndexedInstancedIndirect( GpuBuffer vertexBuffer, GpuBuffer indexBuffer, Graphics.PrimitiveType primitiveType, GpuBuffer indirectBuffer, uint bufferOffset, uint drawCount, uint stride )
	{
		if ( vertexBuffer is not null ) Context.BindVertexBuffer( 0, vertexBuffer.native, 0 );
		Context.BindIndexBuffer( indexBuffer.native, indexBuffer.ElementSize, 0 );
		Context.DrawIndexedInstancedIndirect( (RenderPrimitiveType)primitiveType, indirectBuffer.native, bufferOffset, drawCount, stride );
	}

	//
	// Models
	//

	public override unsafe void DrawModel( Model model, ReadOnlySpan<Transform> transforms, int? lodLevel, RenderAttributes attributes )
	{
		fixed ( Transform* pTransforms = transforms )
		{
			if ( lodLevel is int lod )
				RenderTools.DrawModel( Context, Graphics.SceneLayer, Graphics.DrawShaderMode, model.native, (IntPtr)pTransforms, transforms.Length, attributes.Get(), lod );
			else
				RenderTools.DrawModel( Context, Graphics.SceneLayer, Graphics.DrawShaderMode, model.native, (IntPtr)pTransforms, transforms.Length, attributes.Get() );
		}
	}

	public override void DrawModel( Model model, int count, RenderAttributes attributes )
	{
		RenderTools.DrawModel( Context, Graphics.SceneLayer, Graphics.DrawShaderMode, model.native, IntPtr.Zero, count, attributes.Get() );
	}

	public override void DrawModelIndirect( Model model, GpuBuffer arguments, int argumentsOffset, RenderAttributes attributes )
	{
		RenderTools.DrawModel( Context, Graphics.SceneLayer, Graphics.DrawShaderMode, model.native, arguments.native, argumentsOffset, attributes.Get() );
	}

	public override void DrawModelIndirect( Model model, GpuBuffer transforms, GpuBuffer arguments, int argumentsOffset, int lodLevel, RenderAttributes attributes )
	{
		RenderTools.DrawModel( Context, Graphics.SceneLayer, Graphics.DrawShaderMode, model.native, transforms.native, arguments.native, argumentsOffset, attributes.Get(), lodLevel );
	}

	//
	// Copies, grabs and mips
	//

	public override void CopyTexture( Texture source, Texture destination, int sourceMip, int sourceSlice, int destinationMip, int destinationSlice )
	{
		RenderTools.CopyTexture( Context, source.native, destination.native, default, 0, 0, (uint)sourceMip, (uint)sourceSlice, (uint)destinationMip, (uint)destinationSlice );
	}

	public override void ResolveColor( Texture destination, Rect viewport ) => RenderTools.ResolveFrameBuffer( Context, destination.native, viewport );

	public override void ResolveDepth( Texture destination, Rect viewport ) => RenderTools.ResolveDepthBuffer( Context, destination.native, viewport );

	public override void GenerateMipMaps( Texture texture, Graphics.DownsampleMethod method, int initialMip )
	{
		if ( method is not (Graphics.DownsampleMethod.GaussianBlur or Graphics.DownsampleMethod.GaussianBorder or Graphics.DownsampleMethod.GGX) )
		{
			Graphics.DownsampleSpd( Context, texture, method, initialMip );
			return;
		}

		// Set the attributes
		for ( int i = initialMip; i < texture.Mips - 1; i++ )
		{
			// We want the width and height of the next mip level
			int width = texture.Width >> (i + 1);
			int height = texture.Height >> (i + 1);

			// Don't attempt to generate mips smaller than 1x1
			if ( width < 1 || height < 1 )
				break;

			var attributes = RenderAttributes.Pool.Get();
			attributes.SetCombo( "D_DOWNSAMPLE_METHOD", (int)method );
			attributes.Set( $"MipLevel0", texture, i );
			attributes.Set( $"MipLevel1", texture, i + 1 );
			attributes.Set( "TextureSize", new Vector2( width, height ) );
			attributes.Set( "InvTextureSize", new Vector2( 1.0f / width, 1.0f / height ) );

			// And send to the GPU
			// GGX threads write 2x2 texels each
			if ( method == Graphics.DownsampleMethod.GGX )
				Graphics.MipMapGenerator.DispatchWithAttributes( attributes, (width + 1) / 2, (height + 1) / 2, 1 );
			else
				Graphics.MipMapGenerator.DispatchWithAttributes( attributes, width, height, 1 );

			RenderAttributes.Pool.Return( attributes );

			// Add a UAV barrier for the specific mip level that was just written to
			// This ensures the compute shader dispatch completes before the next iteration
			// that will read from this mip level
			// matt: ... why not use the high level api
			Context.TextureBarrierTransition(
				texture.native,
				i + 1, // Specific mip level we just wrote to
				RenderBarrierPipelineStageFlags_t.ComputeShaderBit,
				RenderBarrierPipelineStageFlags_t.ComputeShaderBit,
				RenderImageLayout_t.RENDER_IMAGE_LAYOUT_GENERAL,
				RenderBarrierAccessFlags_t.ShaderWriteBit | RenderBarrierAccessFlags_t.ShaderReadBit,
				RenderBarrierAccessFlags_t.ShaderWriteBit | RenderBarrierAccessFlags_t.ShaderReadBit );
		}
	}

	//
	// Compute
	//

	public override void Dispatch( Material computeMaterial, RenderAttributes attributes, int threadsX, int threadsY, int threadsZ )
	{
		RenderTools.Compute( Context, attributes.Get(), computeMaterial.native.GetMode(), threadsX, threadsY, threadsZ );
	}

	public override void DispatchIndirect( Material computeMaterial, RenderAttributes attributes, GpuBuffer indirectBuffer, uint bufferOffset )
	{
		RenderTools.ComputeIndirect( Context, attributes.Get(), computeMaterial.native.GetMode(), indirectBuffer.native, bufferOffset );
	}

	//
	// Barriers
	//

	public override void ResourceBarrierTransition( Texture texture, ResourceState state, int mip )
	{
		// It doesn't seem to care if we don't actually know this already
		// But we could know it from native if we care
		var srcStage = RenderBarrierPipelineStageFlags_t.BottomOfPipeBit;

		ResourceStateToVulkanFlags( state, out var dstStage, out var barrierAccessFlags, out var imageLayout );

		Context.TextureBarrierTransition( texture.native, mip, srcStage, dstStage, imageLayout, 0, barrierAccessFlags );
	}

	public override void ResourceBarrierTransition( GpuBuffer buffer, ResourceState state )
	{
		// It doesn't seem to care if we don't actually know this already
		// But we could know it from native if we care
		var srcStage = RenderBarrierPipelineStageFlags_t.BottomOfPipeBit;

		ResourceStateToVulkanFlags( state, out var dstStage, out var barrierAccessFlags, out _, buffer.Usage );

		Context.BufferBarrierTransition( buffer.native, srcStage, dstStage, 0, barrierAccessFlags );
	}

	public override void ResourceBarrierTransition( GpuBuffer buffer, ResourceState before, ResourceState after )
	{
		ResourceStateToVulkanFlags( before, out var srcStage, out var srcFlags, out _, buffer.Usage );
		ResourceStateToVulkanFlags( after, out var dstStage, out var dstFlags, out _, buffer.Usage );

		Context.BufferBarrierTransition( buffer.native, srcStage, dstStage, srcFlags, dstFlags );
	}

	public override void UavBarrier( Texture texture )
	{
		var stage = RenderBarrierPipelineStageFlags_t.FragmentShaderBit | RenderBarrierPipelineStageFlags_t.ComputeShaderBit;
		var access = RenderBarrierAccessFlags_t.ShaderReadBit | RenderBarrierAccessFlags_t.ShaderWriteBit;

		Context.TextureBarrierTransition( texture.native, -1, stage, stage, RenderImageLayout_t.RENDER_IMAGE_LAYOUT_GENERAL, access, access );
	}

	public override void UavBarrier( GpuBuffer buffer )
	{
		var stage = RenderBarrierPipelineStageFlags_t.FragmentShaderBit | RenderBarrierPipelineStageFlags_t.ComputeShaderBit;
		var access = RenderBarrierAccessFlags_t.ShaderReadBit | RenderBarrierAccessFlags_t.ShaderWriteBit;

		Context.BufferBarrierTransition( buffer.native, stage, stage, access, access );
	}

	/// <summary>
	/// Figure out what flags Vulkan needs for the given ResourceState
	/// </summary>
	static void ResourceStateToVulkanFlags( ResourceState resourceState, out RenderBarrierPipelineStageFlags_t dstStageFlags, out RenderBarrierAccessFlags_t accessFlags, out RenderImageLayout_t imageLayout, GpuBuffer.UsageFlags bufferUsageFlags = 0 )
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

		// A compute queue has no graphics stages: what a pixel shader will read, compute reads there, and the graphics queue
		// waits for it in every stage
		if ( Graphics.OnComputeQueue )
		{
			dstStageFlags &= ComputeQueueStages;
			if ( dstStageFlags == 0 ) dstStageFlags = RenderBarrierPipelineStageFlags_t.ComputeShaderBit;
		}
	}

	const RenderBarrierPipelineStageFlags_t ComputeQueueStages = RenderBarrierPipelineStageFlags_t.DrawIndirectBit | RenderBarrierPipelineStageFlags_t.ComputeShaderBit
		| RenderBarrierPipelineStageFlags_t.TransferBit | RenderBarrierPipelineStageFlags_t.TopOfPipeBit | RenderBarrierPipelineStageFlags_t.BottomOfPipeBit;

	//
	// Buffers
	//

	public override void SetBufferData( GpuBuffer buffer, IntPtr data, int size, int offset )
	{
		RenderTools.SetGPUBufferData( Context, buffer.native, data, (uint)size, (uint)offset );
	}

	public override void ReadBufferAsync( GpuBuffer buffer, AsyncGPUReadback.BufferReadDelegate callback, int offset, int size )
	{
		Context.ReadBufferAsync( buffer, callback, offset, size );
	}

	public override void CopyBuffer( GpuBuffer source, GpuBuffer destination, uint sourceOffset, uint destinationOffset, uint size )
	{
		RenderTools.CopyGPUBuffer( Context, source.native, destination.native, sourceOffset, destinationOffset, size );
	}

	public override void FillBuffer( GpuBuffer buffer, uint value ) => RenderTools.FillGPUBuffer( Context, buffer.native, value );

	public override void CopyStructureCount( GpuBuffer source, GpuBuffer destination, uint destinationOffset )
	{
		RenderTools.CopyGPUBufferHiddenStructureCount( Context, source.native, destination.native, destinationOffset );
	}

	public override void SetStructureCount( GpuBuffer buffer, uint value ) => RenderTools.SetGPUBufferHiddenStructureCount( Context, buffer.native, value );
}

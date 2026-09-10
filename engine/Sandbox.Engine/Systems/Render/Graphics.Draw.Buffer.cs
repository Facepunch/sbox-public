using NativeEngine;
using System.Runtime.InteropServices;

namespace Sandbox;

partial class Graphics
{
	private static void ValidateMaterial( Material material )
	{
		if ( material is null || material.native.IsNull )
			throw new ArgumentException( $"{nameof( material )} is invalid.", nameof( material ) );
	}

	private static void ValidateVertexBuffer( GpuBuffer vertexBuffer )
	{
		if ( !vertexBuffer.IsValid() )
			throw new ArgumentException( $"{nameof( vertexBuffer )} is invalid.", nameof( vertexBuffer ) );

		if ( !vertexBuffer.Usage.Contains( GpuBuffer.UsageFlags.Vertex ) )
			throw new ArgumentException( $"{nameof( vertexBuffer )} does not have the required usage flag '{GpuBuffer.UsageFlags.Vertex}'.", nameof( vertexBuffer ) );
	}

	private static void ValidateIndexBuffer( GpuBuffer indexBuffer )
	{
		if ( !indexBuffer.IsValid() )
			throw new ArgumentException( $"{nameof( indexBuffer )} is invalid.", nameof( indexBuffer ) );

		if ( !indexBuffer.Usage.Contains( GpuBuffer.UsageFlags.Index ) )
			throw new ArgumentException( $"{nameof( indexBuffer )} does not have the required usage flag '{GpuBuffer.UsageFlags.Index}'.", nameof( indexBuffer ) );

		if ( indexBuffer.ElementSize != sizeof( uint ) && indexBuffer.ElementSize != sizeof( ushort ) )
			throw new ArgumentOutOfRangeException( nameof( indexBuffer ), $"{nameof( indexBuffer )} must be size of {sizeof( uint )} or {sizeof( ushort )}." );
	}

	private static void ValidateIndirectBuffer( GpuBuffer indirectBuffer )
	{
		if ( !indirectBuffer.IsValid() )
			throw new ArgumentException( $"{nameof( indirectBuffer )} is invalid.", nameof( indirectBuffer ) );

		if ( !indirectBuffer.Usage.Contains( GpuBuffer.UsageFlags.IndirectDrawArguments ) )
			throw new ArgumentException( $"Buffer must have the required usage flag '{GpuBuffer.UsageFlags.IndirectDrawArguments}'", nameof( indirectBuffer ) );
	}

	/// <summary>
	/// Validates an indirect draw stride and converts an element offset into a byte offset.
	/// A stride of 0 means the draw arguments are tightly packed at their natural size.
	/// </summary>
	private static uint GetIndirectBufferOffset( uint elementOffset, uint stride, int naturalSize )
	{
		if ( stride != 0 )
		{
			if ( stride % 4 != 0 )
				throw new ArgumentException( $"Indirect draw stride ({stride}) must be a multiple of 4.", nameof( stride ) );

			if ( stride < (uint)naturalSize )
				throw new ArgumentException( $"Indirect draw stride ({stride}) must be at least the size of the draw argument struct ({naturalSize}).", nameof( stride ) );
		}

		return elementOffset * (stride != 0 ? stride : (uint)naturalSize);
	}

	private static NativeEngine.VertexLayout GetVertexLayout<T>() where T : unmanaged
	{
		var vertexType = VertexLayout.Get<T>();
		if ( vertexType.IsNull )
			throw new ArgumentException( $"{nameof( T )} vertex layout is invalid.", nameof( T ) );

		return vertexType;
	}

	private static bool SetRenderState<T>( GpuBuffer<T> vertexBuffer, Material material, RenderAttributes attributes ) where T : unmanaged
	{
		ValidateVertexBuffer( vertexBuffer );
		ValidateMaterial( material );

		var layout = GetVertexLayout<T>();

		attributes ??= Attributes;
		return RenderTools.SetRenderState( Context, attributes.Get(), material.native.GetMode( SceneLayer ), layout, Stats );
	}

	private static bool SetRenderState( Material material, RenderAttributes attributes )
	{
		ValidateMaterial( material );

		attributes ??= Attributes;
		return RenderTools.SetRenderState( Context, attributes.Get(), material.native.GetMode( SceneLayer ), default, Stats );
	}

	/// <summary>
	/// Draws geometry using a vertex buffer and material.
	/// </summary>
	/// <typeparam name="T">The vertex type used for vertex layout.</typeparam>
	/// <param name="vertexBuffer">The GPU buffer containing vertex data.</param>
	/// <param name="material">The material to use for rendering.</param>
	/// <param name="startVertex">The starting vertex index for rendering.</param>
	/// <param name="vertexCount">The number of vertices to render. If 0, uses all vertices in the buffer.</param>
	/// <param name="attributes">Optional render attributes to apply only for this draw call.</param>
	/// <param name="primitiveType">The type of primitives to render. Defaults to triangles.</param>
	public static void Draw<T>( GpuBuffer<T> vertexBuffer, Material material, int startVertex = 0, int vertexCount = 0, RenderAttributes attributes = null, PrimitiveType primitiveType = PrimitiveType.Triangles ) where T : unmanaged
	{
		AssertRenderBlock();

		if ( !SetRenderState( vertexBuffer, material, attributes ) )
			return;

		if ( vertexCount <= 0 )
			vertexCount = vertexBuffer.ElementCount;

		if ( vertexCount <= 0 )
			return;

		if ( startVertex < 0 || startVertex >= vertexBuffer.ElementCount )
			throw new ArgumentOutOfRangeException( nameof( startVertex ), $"{nameof( startVertex )} is out of bounds of the vertex buffer." );

		if ( startVertex + vertexCount > vertexBuffer.ElementCount )
			throw new ArgumentOutOfRangeException( nameof( vertexCount ), $"{nameof( vertexCount )} exceeds the bounds of the vertex buffer." );

		Context.BindVertexBuffer( 0, vertexBuffer.native, 0 );
		Context.Draw( (RenderPrimitiveType)primitiveType, startVertex, vertexCount );
	}

	/// <summary>
	/// Draws indexed geometry using vertex and index buffers.
	/// </summary>
	/// <typeparam name="T">The vertex type used for vertex layout.</typeparam>
	/// <param name="vertexBuffer">The GPU buffer containing vertex data.</param>
	/// <param name="indexBuffer">The GPU buffer containing index data.</param>
	/// <param name="material">The material to use for rendering.</param>
	/// <param name="startIndex">The starting index for rendering.</param>
	/// <param name="indexCount">The number of indices to render. If 0, uses all indices in the buffer.</param>
	/// <param name="attributes">Optional render attributes to apply only for this draw call.</param>
	/// <param name="primitiveType">The type of primitives to render. Defaults to triangles.</param>
	public static void Draw<T>( GpuBuffer<T> vertexBuffer, GpuBuffer indexBuffer, Material material, int startIndex = 0, int indexCount = 0, RenderAttributes attributes = null, PrimitiveType primitiveType = PrimitiveType.Triangles ) where T : unmanaged
	{
		AssertRenderBlock();

		ValidateIndexBuffer( indexBuffer );

		if ( !SetRenderState( vertexBuffer, material, attributes ) )
			return;

		if ( indexCount <= 0 )
			indexCount = indexBuffer.ElementCount;

		if ( indexCount <= 0 )
			return;

		if ( startIndex < 0 || startIndex >= indexBuffer.ElementCount )
			throw new ArgumentOutOfRangeException( nameof( startIndex ), $"{nameof( startIndex )} is out of bounds of the index buffer." );

		if ( startIndex + indexCount > indexBuffer.ElementCount )
			throw new ArgumentOutOfRangeException( nameof( indexCount ), $"{nameof( indexCount )} exceeds the bounds of the index buffer." );

		Context.BindVertexBuffer( 0, vertexBuffer.native, 0 );
		Context.BindIndexBuffer( indexBuffer.native, indexBuffer.ElementSize, 0 );
		Context.DrawIndexed( (RenderPrimitiveType)primitiveType, startIndex, indexCount, 0, 0 );
	}

	internal static void DrawInstancedIndirect<T>( GpuBuffer<T> vertexBuffer, Material material, GpuBuffer indirectBuffer, uint indirectElementOffset = 0, RenderAttributes attributes = null, PrimitiveType primitiveType = PrimitiveType.Triangles, uint drawCount = 1, uint stride = 0 ) where T : unmanaged
	{
		AssertRenderBlock();

		ValidateIndirectBuffer( indirectBuffer );

		var bufferOffset = GetIndirectBufferOffset( indirectElementOffset, stride, Marshal.SizeOf<GpuBuffer.IndirectDrawArguments>() );

		if ( !SetRenderState( vertexBuffer, material, attributes ) )
			return;

		Context.BindVertexBuffer( 0, vertexBuffer.native, 0 );
		Context.DrawInstancedIndirect( (RenderPrimitiveType)primitiveType, indirectBuffer.native, bufferOffset, drawCount, stride );
	}

	internal static void DrawInstancedIndirect( Material material, GpuBuffer indirectBuffer, uint indirectElementOffset = 0, RenderAttributes attributes = null, PrimitiveType primitiveType = PrimitiveType.Triangles, uint drawCount = 1, uint stride = 0 )
	{
		AssertRenderBlock();

		ValidateIndirectBuffer( indirectBuffer );

		var bufferOffset = GetIndirectBufferOffset( indirectElementOffset, stride, Marshal.SizeOf<GpuBuffer.IndirectDrawArguments>() );

		if ( !SetRenderState( material, attributes ) )
			return;

		Context.DrawInstancedIndirect( (RenderPrimitiveType)primitiveType, indirectBuffer.native, bufferOffset, drawCount, stride );
	}

	internal static void DrawIndexedInstanced( GpuBuffer indexBuffer, Material material, int instanceCount, RenderAttributes attributes = null, PrimitiveType primitiveType = PrimitiveType.Triangles )
	{
		AssertRenderBlock();

		ValidateIndexBuffer( indexBuffer );

		if ( !SetRenderState( material, attributes ) )
			return;

		Context.BindIndexBuffer( indexBuffer.native, indexBuffer.ElementSize, 0 );
		Context.DrawIndexedInstanced( (RenderPrimitiveType)primitiveType, 0, indexBuffer.ElementCount, instanceCount, 0, 0 );
	}

	internal static void DrawIndexedInstancedIndirect<T>( GpuBuffer<T> vertexBuffer, GpuBuffer indexBuffer, Material material, GpuBuffer indirectBuffer, uint indirectElementOffset = 0, RenderAttributes attributes = null, PrimitiveType primitiveType = PrimitiveType.Triangles, uint drawCount = 1, uint stride = 0 ) where T : unmanaged
	{
		AssertRenderBlock();

		ValidateIndirectBuffer( indirectBuffer );
		ValidateIndexBuffer( indexBuffer );

		var bufferOffset = GetIndirectBufferOffset( indirectElementOffset, stride, Marshal.SizeOf<GpuBuffer.IndirectDrawIndexedArguments>() );

		if ( !SetRenderState( vertexBuffer, material, attributes ) )
			return;

		Context.BindVertexBuffer( 0, vertexBuffer.native, 0 );
		Context.BindIndexBuffer( indexBuffer.native, indexBuffer.ElementSize, 0 );
		Context.DrawIndexedInstancedIndirect( (RenderPrimitiveType)primitiveType, indirectBuffer.native, bufferOffset, drawCount, stride );
	}

	internal static void DrawIndexedInstancedIndirect( GpuBuffer indexBuffer, Material material, GpuBuffer indirectBuffer, uint indirectElementOffset = 0, RenderAttributes attributes = null, PrimitiveType primitiveType = PrimitiveType.Triangles, uint drawCount = 1, uint stride = 0 )
	{
		AssertRenderBlock();

		ValidateIndirectBuffer( indirectBuffer );
		ValidateIndexBuffer( indexBuffer );

		var bufferOffset = GetIndirectBufferOffset( indirectElementOffset, stride, Marshal.SizeOf<GpuBuffer.IndirectDrawIndexedArguments>() );

		if ( !SetRenderState( material, attributes ) )
			return;

		Context.BindIndexBuffer( indexBuffer.native, indexBuffer.ElementSize, 0 );
		Context.DrawIndexedInstancedIndirect( (RenderPrimitiveType)primitiveType, indirectBuffer.native, bufferOffset, drawCount, stride );
	}
}

using NativeEngine;
using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// The device under <see cref="Graphics"/>: what its statics, <see cref="Rendering.CommandList"/>, compute shaders and
/// GPU buffers record with. Each render block (<see cref="Graphics.Scope"/>) draws on one; outside a block, buffer
/// uploads and readbacks go to <see cref="Default"/>. <see cref="Source2RenderBackend"/> records into Source 2's render
/// context. Callers validate their arguments before they get here, so a backend only lowers the call.
/// </summary>
internal abstract class RenderBackend
{
	/// <summary>
	/// The process's device: Source 2's.
	/// </summary>
	public static RenderBackend Default { get; } = Source2RenderBackend.Instance;

	//
	// Targets and viewport
	//

	/// <summary>Where draws land, in the bound target's pixels, with the depth range. Set as a rect, it keeps the range.</summary>
	public abstract RenderViewport GetViewport();
	public abstract void SetViewport( in RenderViewport viewport );
	public abstract void SetViewport( Rect rect );

	/// <summary>Bind a render target's colour in a block with no view (<see cref="Graphics.RenderTarget"/>).</summary>
	public abstract void BindRenderTarget( RenderTarget target );

	/// <summary>Clear what's bound.</summary>
	public abstract void Clear( Color color, bool clearColor, bool clearDepth, bool clearStencil );

	/// <summary>Fill a whole texture with one colour.</summary>
	public abstract void ClearTexture( Texture texture, Color color );

	//
	// Draws
	//

	/// <summary>
	/// Draw vertices of <paramref name="vertexType"/> from the CPU, with optional 16-bit indices
	/// (<see cref="Graphics.Draw(Span{Vertex}, int, Material, RenderAttributes, Graphics.PrimitiveType)"/>).
	/// </summary>
	public abstract void DrawVertices( Type vertexType, int vertexSize, IntPtr vertices, int vertexCount, IntPtr indices, int indexCount, Material material, RenderAttributes attributes, Graphics.PrimitiveType primitiveType );

	/// <summary>
	/// Set up a material and attributes for the buffer draws after it, reading vertices of
	/// <paramref name="vertexType"/> (null for none). False when the material can't draw here, and nothing should.
	/// </summary>
	public abstract bool SetRenderState( Material material, RenderAttributes attributes, Type vertexType );

	/// <summary>Buffer draws, after <see cref="SetRenderState"/>.</summary>
	public abstract void Draw( GpuBuffer vertexBuffer, Graphics.PrimitiveType primitiveType, int startVertex, int vertexCount );
	public abstract void DrawIndexed( GpuBuffer vertexBuffer, GpuBuffer indexBuffer, Graphics.PrimitiveType primitiveType, int startIndex, int indexCount );
	public abstract void DrawIndexedInstanced( GpuBuffer indexBuffer, Graphics.PrimitiveType primitiveType, int instanceCount );

	/// <summary>Indirect buffer draws, after <see cref="SetRenderState"/>. <paramref name="vertexBuffer"/> may be null.</summary>
	public abstract void DrawInstancedIndirect( GpuBuffer vertexBuffer, Graphics.PrimitiveType primitiveType, GpuBuffer indirectBuffer, uint bufferOffset, uint drawCount, uint stride );
	public abstract void DrawIndexedInstancedIndirect( GpuBuffer vertexBuffer, GpuBuffer indexBuffer, Graphics.PrimitiveType primitiveType, GpuBuffer indirectBuffer, uint bufferOffset, uint drawCount, uint stride );

	//
	// Models
	//

	/// <summary>Instances of a model at transforms from the CPU; <paramref name="lodLevel"/> null draws native's default.</summary>
	public abstract void DrawModel( Model model, ReadOnlySpan<Transform> transforms, int? lodLevel, RenderAttributes attributes );

	/// <summary><paramref name="count"/> instances of a model, which find their own transforms.</summary>
	public abstract void DrawModel( Model model, int count, RenderAttributes attributes );

	/// <summary>Instances of a model counted by indirect arguments, optionally with transforms from a buffer.</summary>
	public abstract void DrawModelIndirect( Model model, GpuBuffer arguments, int argumentsOffset, RenderAttributes attributes );
	public abstract void DrawModelIndirect( Model model, GpuBuffer transforms, GpuBuffer arguments, int argumentsOffset, int lodLevel, RenderAttributes attributes );

	//
	// Copies, grabs and mips
	//

	/// <summary>Copy one mip and slice of a texture into another of the same size and format.</summary>
	public abstract void CopyTexture( Texture source, Texture destination, int sourceMip, int sourceSlice, int destinationMip, int destinationSlice );

	/// <summary>Copy (resolving) what's bound inside <paramref name="viewport"/> into a texture (<see cref="Graphics.GrabFrameTexture(string, RenderAttributes, Graphics.DownsampleMethod, int)"/>).</summary>
	public abstract void ResolveColor( Texture destination, Rect viewport );
	public abstract void ResolveDepth( Texture destination, Rect viewport );

	/// <summary>Fill a texture's mips after <paramref name="initialMip"/> from it (<see cref="Graphics.GenerateMipMaps"/>).</summary>
	public abstract void GenerateMipMaps( Texture texture, Graphics.DownsampleMethod method, int initialMip );

	//
	// Compute
	//

	/// <summary>Run a compute shader over a number of threads, or over the groups an indirect buffer holds at a byte offset.</summary>
	public abstract void Dispatch( Material computeMaterial, RenderAttributes attributes, int threadsX, int threadsY, int threadsZ );
	public abstract void DispatchIndirect( Material computeMaterial, RenderAttributes attributes, GpuBuffer indirectBuffer, uint bufferOffset );

	//
	// Barriers
	//

	/// <summary>Move a texture (or one mip, -1 for all) to a state.</summary>
	public abstract void ResourceBarrierTransition( Texture texture, ResourceState state, int mip );

	/// <summary>Move a buffer to a state, from an unknown one or from a known one.</summary>
	public abstract void ResourceBarrierTransition( GpuBuffer buffer, ResourceState state );
	public abstract void ResourceBarrierTransition( GpuBuffer buffer, ResourceState before, ResourceState after );

	/// <summary>Make shader writes visible to later shader reads and writes, keeping the layout.</summary>
	public abstract void UavBarrier( Texture texture );
	public abstract void UavBarrier( GpuBuffer buffer );

	//
	// Buffers
	//

	/// <summary>Write bytes into a buffer at a byte offset.</summary>
	public abstract void SetBufferData( GpuBuffer buffer, IntPtr data, int size, int offset );

	/// <summary>Read bytes from a buffer once the GPU has written them.</summary>
	public abstract void ReadBufferAsync( GpuBuffer buffer, AsyncGPUReadback.BufferReadDelegate callback, int offset, int size );

	/// <summary>Copy bytes from one buffer to another.</summary>
	public abstract void CopyBuffer( GpuBuffer source, GpuBuffer destination, uint sourceOffset, uint destinationOffset, uint size );

	/// <summary>Fill a buffer with one 32-bit value.</summary>
	public abstract void FillBuffer( GpuBuffer buffer, uint value );

	/// <summary>An append or counter buffer's hidden count: copied into another buffer, or set.</summary>
	public abstract void CopyStructureCount( GpuBuffer source, GpuBuffer destination, uint destinationOffset );
	public abstract void SetStructureCount( GpuBuffer buffer, uint value );
}

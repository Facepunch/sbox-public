using NativeEngine;

namespace Sandbox;

/// <summary>
/// A ray tracing shader,
/// enabling advanced rendering techniques like real-time ray tracing for reflections, 
/// global illumination, and shadows.
/// </summary>
/// <seealso cref="GpuBuffer{T}"/>
/// <seealso cref="ComputeShader"/>
internal class RayTracingShader
{
	/// <summary>
	/// Attributes that are passed to the ray tracing shader on dispatch.
	/// </summary>
	public RenderAttributes Attributes { get; } = new RenderAttributes();

	private Material RayTracingMaterial;

	/// <summary>
	/// Create a ray tracing shader from the specified path.
	/// </summary>
	public RayTracingShader( string path )
	{
		var material = Material.FromShader( path );
		Assert.NotNull( material, $"Failed to load ray tracing shader material from path: {path}" );
		RayTracingMaterial = material;
	}

	/// <summary>
	/// Dispatches the ray tracing shader using explicit thread counts.
	/// </summary>
	/// <remarks>
	/// The specified thread counts represent the dispatch dimensions for the ray generation shader.
	/// <para>
	/// When called outside a graphics context, the dispatch runs immediately.  
	/// When called inside a graphics context, the dispatch runs async.
	/// </para>
	/// </remarks>
	/// <param name="attributes">Render attributes to use for this dispatch.</param>
	/// <param name="threadsX">The number of threads to dispatch in the X dimension.</param>
	/// <param name="threadsY">The number of threads to dispatch in the Y dimension.</param>
	/// <param name="threadsZ">The number of threads to dispatch in the Z dimension.</param>
	public void DispatchRaysWithAttributes( RenderAttributes attributes, int threadsX = 1, int threadsY = 1, int threadsZ = 1 )
	{
		if ( threadsX < 1 ) throw new ArgumentException( $"Cannot be less than 1", nameof( threadsX ) );
		if ( threadsY < 1 ) throw new ArgumentException( $"Cannot be less than 1", nameof( threadsY ) );
		if ( threadsZ < 1 ) throw new ArgumentException( $"Cannot be less than 1", nameof( threadsZ ) );

		// Dispatch ray tracing using RenderTools.TraceRays
		var mode = RayTracingMaterial.native.GetMode();
		RenderTools.TraceRays( Graphics.Context, attributes.Get(), mode, (uint)threadsX, (uint)threadsY, (uint)threadsZ );
	}

	/// <summary>
	/// Dispatches the ray tracing shader using the default attributes.
	/// </summary>
	public void DispatchRays( int threadsX = 1, int threadsY = 1, int threadsZ = 1 )
	{
		DispatchRaysWithAttributes( Attributes, threadsX, threadsY, threadsZ );
	}

	/// <summary>
	/// Dispatches the ray tracing shader by reading dispatch arguments from an indirect buffer.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <paramref name="indirectBuffer"/> must be created with <see cref="GpuBuffer.UsageFlags.IndirectDrawArguments"/>  
	/// and have an element size of 12 bytes (3 uint32 values for X, Y, Z dimensions).
	/// </para>
	/// <para>
	/// <paramref name="indirectElementOffset"/> is an element index into <paramref name="indirectBuffer"/>, not a byte offset.
	/// </para>
	/// <para>
	/// When called outside a graphics context, the dispatch runs immediately.  
	/// When called inside a graphics context, the dispatch runs async.
	/// </para>
	/// </remarks>
	/// <param name="indirectBuffer">The GPU buffer containing one or more dispatch argument entries.</param>
	/// <param name="indirectElementOffset">The index of the dispatch arguments element to use (each element = 12 bytes).</param>
	public void DispatchRaysIndirect( GpuBuffer indirectBuffer, uint indirectElementOffset = 0 )
	{
		DispatchRaysIndirectWithAttributes( Attributes, indirectBuffer, indirectElementOffset );
	}

	/// <inheritdoc cref="DispatchRaysIndirect"/>
	public void DispatchRaysIndirectWithAttributes( RenderAttributes attributes, GpuBuffer indirectBuffer, uint indirectElementOffset = 0 )
	{
		if ( !indirectBuffer.IsValid() )
			throw new ArgumentException( $"Invalid buffer", nameof( indirectBuffer ) );

		if ( indirectBuffer.ElementSize != 12 )
			throw new ArgumentException( $"Buffer element size must be 12 bytes", nameof( indirectBuffer ) );

		if ( indirectElementOffset >= indirectBuffer.ElementCount )
			throw new ArgumentOutOfRangeException( nameof( indirectElementOffset ), "Indirect element offset exceeds buffer bounds" );

		if ( !indirectBuffer.Usage.Contains( GpuBuffer.UsageFlags.IndirectDrawArguments ) )
			throw new ArgumentException( $"Buffer must have the required usage flag '{GpuBuffer.UsageFlags.IndirectDrawArguments}'", nameof( indirectBuffer ) );

		// Use RenderTools.TraceRaysIndirect when it becomes available, for now use the material mode directly
		var mode = RayTracingMaterial.native.GetMode();
		RenderTools.TraceRaysIndirect( Graphics.Context, attributes.Get(), mode, indirectBuffer.native, indirectElementOffset * 12 );
	}
}

using NativeEngine;
using Sandbox.VR;

namespace Sandbox;

/// <summary>
/// A compute shader is a program that runs on the GPU, often with data provided to/from the CPU by means of a <see cref="GpuBuffer{T}"/> or a <see cref="Texture"/>.
/// </summary>
/// <seealso cref="GpuBuffer{T}"/>
public class ComputeShader
{
	/// <summary>
	/// Attributes that are passed to the compute shader on dispatch.
	/// </summary>
	public RenderAttributes Attributes { get; } = new RenderAttributes();

	Material ComputeMaterial;

	/// <summary>
	/// Create a compute shader from the specified path.
	/// </summary>
	public ComputeShader( string path )
	{
		var material = Material.FromShader( path );
		Assert.NotNull( material );
		ComputeMaterial = material;
	}

	internal void Dispose()
	{
		ComputeMaterial.Dispose();
	}

	/// <summary>
	/// Dispatch this compute shader using explicit thread counts.
	/// </summary>
	/// <remarks>
	/// The specified thread counts will be automatically divided by the thread group size
	/// defined in the shader to compute the final dispatch group count.
	/// <para>
	/// When called outside a graphics context, the dispatch runs immediately.  
	/// When called inside a graphics context, the dispatch runs async.
	/// </para>
	/// </remarks>
	/// <param name="threadsX">The number of threads to dispatch in the X dimension.</param>
	/// <param name="threadsY">The number of threads to dispatch in the Y dimension.</param>
	/// <param name="threadsZ">The number of threads to dispatch in the Z dimension.</param>
	public void Dispatch( int threadsX = 32, int threadsY = 32, int threadsZ = 32 )
	{
		DispatchWithAttributes( Attributes, threadsX, threadsY, threadsZ );
	}

	/// <summary>
	/// Dispatch this compute shader by reading thread group counts (x, y, z)
	/// from an indirect buffer of type <see cref="GpuBuffer.IndirectDispatchArguments"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <paramref name="indirectBuffer"/> must be created with <see cref="GpuBuffer.UsageFlags.IndirectDrawArguments"/>  
	/// and have an element size of 12 bytes.
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
	public void DispatchIndirect( GpuBuffer indirectBuffer, uint indirectElementOffset = 0 )
	{
		DispatchIndirectWithAttributes( Attributes, indirectBuffer, indirectElementOffset );
	}

	/// <inheritdoc cref="Dispatch"/>
	public void DispatchWithAttributes( RenderAttributes attributes, int threadsX = 32, int threadsY = 32, int threadsZ = 32 )
	{
		if ( threadsX < 1 ) throw new ArgumentException( $"Cannot be less than 1", nameof( threadsX ) );
		if ( threadsY < 1 ) throw new ArgumentException( $"Cannot be less than 1", nameof( threadsY ) );
		if ( threadsZ < 1 ) throw new ArgumentException( $"Cannot be less than 1", nameof( threadsZ ) );

		RenderTools.Compute( Graphics.Context, attributes.Get(), ComputeMaterial.native.GetMode(), threadsX, threadsY, threadsZ );
	}

	/// <inheritdoc cref="DispatchIndirect"/>
	public void DispatchIndirectWithAttributes( RenderAttributes attributes, GpuBuffer indirectBuffer, uint indirectElementOffset = 0 )
	{
		if ( !indirectBuffer.IsValid() )
			throw new ArgumentException( $"Invalid buffer", nameof( indirectBuffer ) );

		if ( indirectBuffer.ElementSize != 12 )
			throw new ArgumentException( $"Buffer element size must 12 bytes", nameof( indirectBuffer ) );

		if ( indirectElementOffset >= indirectBuffer.ElementCount )
			throw new ArgumentOutOfRangeException( nameof( indirectElementOffset ), "Indirect element offset exceeds buffer bounds" );

		if ( !indirectBuffer.Usage.Contains( GpuBuffer.UsageFlags.IndirectDrawArguments ) )
			throw new ArgumentException( $"Buffer must have the required usage flag '{GpuBuffer.UsageFlags.IndirectDrawArguments}'", nameof( indirectBuffer ) );

		RenderTools.ComputeIndirect( Graphics.Context, attributes.Get(), ComputeMaterial.native.GetMode(), indirectBuffer.native, indirectElementOffset * 12 );
	}
}

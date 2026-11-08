using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Basalt;

/// <summary>
/// A buffer on the GPU, made by the <see cref="GpuDevice"/> when it's constructed.
/// </summary>
public class GpuBuffer : IDisposable
{
	/// <summary>
	/// What a buffer is bound as.
	/// </summary>
	[Flags]
	public enum UsageFlags
	{
		/// <summary>A vertex stream.</summary>
		Vertex = 0x0001,

		/// <summary>An index buffer.</summary>
		Index = 0x0002,

		/// <summary>A raw buffer addressed in bytes.</summary>
		ByteAddress = 0x0010,

		/// <summary>A structured buffer.</summary>
		Structured = 0x0020,

		/// <summary>An append buffer.</summary>
		Append = 0x0040,

		/// <summary>Indirect draw or dispatch arguments.</summary>
		IndirectDrawArguments = 0x0100,
	}

	/// <summary>
	/// One indexed indirect draw's arguments, as the GPU reads them.
	/// </summary>
	[StructLayout( LayoutKind.Sequential )]
	public struct IndirectDrawIndexedArguments
	{
		/// <summary>Indices to draw.</summary>
		public uint IndexCount;

		/// <summary>Instances to draw.</summary>
		public uint InstanceCount;

		/// <summary>The first index.</summary>
		public uint FirstIndex;

		/// <summary>Added to each index.</summary>
		public int BaseVertex;

		/// <summary>The first instance.</summary>
		public uint FirstInstance;
	}

	/// <summary>
	/// How many elements it holds.
	/// </summary>
	public int ElementCount { get; }

	/// <summary>
	/// Each element's size in bytes.
	/// </summary>
	public int ElementSize { get; }

	/// <summary>
	/// What it's bound as.
	/// </summary>
	public UsageFlags Usage { get; }

	/// <summary>
	/// The device's buffer.
	/// </summary>
	internal IDisposable Resource { get; private set; }

	/// <summary>
	/// Create a buffer of <paramref name="elementCount"/> elements of <paramref name="elementSize"/> bytes.
	/// </summary>
	public GpuBuffer( int elementCount, int elementSize, UsageFlags usage = UsageFlags.Structured, string name = null )
	{
		ArgumentOutOfRangeException.ThrowIfLessThan( elementCount, 1 );
		ArgumentOutOfRangeException.ThrowIfLessThan( elementSize, 1 );

		ElementCount = elementCount;
		ElementSize = elementSize;
		Usage = usage;
		Resource = RenderHost.Current.Device.CreateBuffer( elementCount, elementSize, usage, name );
	}

	/// <summary>
	/// Copy data into it now, outside any recorded work.
	/// </summary>
	public void SetData<T>( ReadOnlySpan<T> data ) where T : unmanaged
	{
		ObjectDisposedException.ThrowIf( Resource is null, this );
		RenderHost.Current.Device.SetData( this, MemoryMarshal.AsBytes( data ) );
	}

	/// <summary>
	/// Release it. The device keeps it alive for work already submitted.
	/// </summary>
	public void Dispose()
	{
		Resource?.Dispose();
		Resource = null;
	}
}

/// <summary>
/// A buffer of <typeparamref name="T"/>.
/// </summary>
public sealed class GpuBuffer<T> : GpuBuffer where T : unmanaged
{
	/// <summary>
	/// Create a buffer of <paramref name="elementCount"/> <typeparamref name="T"/>s.
	/// </summary>
	public GpuBuffer( int elementCount, UsageFlags usage = UsageFlags.Structured, string name = null ) : base( elementCount, Unsafe.SizeOf<T>(), usage, name )
	{
	}

	/// <summary>
	/// Copy elements into it now, outside any recorded work.
	/// </summary>
	public void SetData( Span<T> data ) => SetData<T>( data );
}

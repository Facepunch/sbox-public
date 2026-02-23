using NativeEngine;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sandbox;

/// <summary>
/// A GPU data buffer intended for use with a <see cref="ComputeShader"/>.
/// 
/// You can read and write arbitrary data to and from the CPU and GPU.
/// This allows for efficient parallel data processing on the GPU.
/// 
/// Different GPU buffer types can be used depending on the provided <see cref="UsageFlags"/>.
/// Using the default <see cref="UsageFlags.Structured"/> type buffers map to StructuredBuffer&lt;T&gt; and RWStructuredBuffer&lt;T&gt; in HLSL.
/// </summary>
///
/// <example>
/// This example shows how to use the GpuBuffer class to send data to a compute shader:
/// <code>
/// struct MyData
/// {
///     public float Value;
/// }
/// 
/// // Allocate the GPU buffer
/// using (var buffer = new GpuBuffer&lt;MyData&gt;( 2 ))
/// {
///		// Upload data to the GPU buffer
///		var data = new MyData[] { new MyData { Value = 1.0f }, new MyData { Value = 2.0f } };
///		buffer.SetData( data );
/// 
///     // Pass the buffer to a compute shader
///     ComputeShader.Attributes.Set( "myData", buffer );
///     
///     // Dispatch the shader
///     ComputeShader.Dispatch();
/// }
/// </code>
/// </example>
/// 
/// <example>
/// This example shows how to retrieve data from a GPU using the GpuBuffer class:
/// <code>
/// struct MyData
/// {
///     public float Value;
/// }
/// 
/// using (var buffer = new GpuBuffer&lt;MyData&gt;( 8 ))
/// {
///     // Pass the buffer to a compute shader
///     ComputeShader.Attributes.Set( "myData", buffer );
///     
///     // Dispatch the shader
///     ComputeShader.Dispatch();
///     
///		// Retrieve the data from the GPU
///		var data = new MyData[ 8 ];
///		buffer.GetData( data, 0, 8 );
/// }
/// </code>
/// </example>
///
/// <seealso cref="ComputeShader"/>
/// <seealso cref="RenderAttributes.Set(in StringToken, in GpuBuffer)"/>
public partial class GpuBuffer : IValid, IDisposable
{
	internal RenderBufferHandle_t native;

	/// <summary>
	/// Number of elements in the buffer.
	/// </summary>
	public int ElementCount { get; private set; }

	/// <summary>
	/// Size of a single element in the buffer.
	/// </summary>
	public int ElementSize { get; private set; }

	/// <summary>
	/// What sort of buffer this is
	/// </summary>
	public UsageFlags Usage { get; private set; }

	public bool IsValid => native != IntPtr.Zero;

	/// <summary>
	/// Creates a new GPU buffer with a specified number of elements and a specific buffer type.
	/// </summary>
	/// <param name="elementCount">The total number of elements that the GpuBuffer can hold. This represents the buffer's size in terms of elements, not bytes.</param>
	/// <param name="elementSize">The total number of elements that the GpuBuffer can hold. This represents the buffer's size in terms of elements, not bytes.</param>
	/// <param name="flags">Defines the usage pattern of the GPU buffer. This can affect performance depending on how the buffer is utilized.</param>
	/// <param name="debugName">Test</param>
	public GpuBuffer( int elementCount, int elementSize, UsageFlags flags = UsageFlags.Structured, string debugName = null )
	{
		Initialize( elementCount, elementSize, flags, debugName );
	}

	protected GpuBuffer() { }

	~GpuBuffer()
	{
		Dispose( false );
	}

	protected void Initialize( int elementCount, int elementSize, UsageFlags usageFlags = UsageFlags.Structured, string debugName = null )
	{
		if ( elementCount <= 0 ) throw new ArgumentException( "Element count must be greater than zero." );
		if ( elementSize <= 0 ) throw new ArgumentException( "Element size must be greater than zero." );

		// Structured buffers element size must be 4 byte aligned
		if ( usageFlags.HasFlag( UsageFlags.Structured ) )
		{
			if ( elementSize % 4 != 0 )
			{
				throw new InvalidOperationException( $"Structured buffers element size must be divisible by 4" );
			}
		}

		// short or int
		if ( usageFlags.HasFlag( UsageFlags.Index ) )
		{
			if ( elementSize != 2 && elementSize != 4 )
			{
				throw new InvalidOperationException( $"Index buffers element size must be 2 or 4 bytes" );
			}
		}

		// Buffers can usually be as big as VRAM, but this seems reasonable until someone complains
		if ( (ulong)elementSize * (ulong)elementCount > UInt32.MaxValue )
		{
			throw new InvalidOperationException( $"Buffer can't exceed 4GB" );
		}

		BufferDesc bufferDesc = new()
		{
			m_nElementCount = elementCount,
			m_nElementSizeInBytes = elementSize
		};

		// Map managed to native enums
		RenderBufferFlags nativeFlags = (RenderBufferFlags)usageFlags;

		// Always allow unordered access from shaders when we can.. This might be presumptuous but I've never not needed this
		nativeFlags |= RenderBufferFlags.RENDER_BUFFER_USAGE_SHADER_RESOURCE;
		if ( usageFlags != UsageFlags.ByteAddress )
		{
			nativeFlags |= RenderBufferFlags.RENDER_BUFFER_USAGE_UNORDERED_ACCESS;
		}

		// Semistatic lets GPU & CPU write to it
		// There are other types we could potentially expose
		native = g_pRenderDevice.CreateGPUBuffer( RenderBufferType.RENDER_BUFFER_TYPE_SEMISTATIC, bufferDesc, nativeFlags, debugName );

		ElementCount = elementCount;
		ElementSize = elementSize;
		Usage = usageFlags;
	}

	/// <summary>
	/// Destroys the GPU buffer, don't use it no more
	/// </summary>
	public void Dispose()
	{
		ThreadSafe.AssertIsMainThread();

		Dispose( true );
		GC.SuppressFinalize( this );
	}

	void Dispose( bool disposing )
	{
		if ( native == IntPtr.Zero )
			return;

		// Set our pointer to default immediately, keep a copy so we can dispose on main thread
		var copy = native;
		native = IntPtr.Zero;

		// Queue on the main thread in-case this is disposed from GC (different thread)
		MainThread.Queue( () =>
		{
			g_pRenderDevice.DestroyGPUBuffer( copy );
		} );
	}

	/// <summary>
	/// Retrieves the GPU buffer and copies them into a provided Span.
	/// </summary>
	/// <remarks>
	/// This operation is synchronous and will block until the data has been fully downloaded from the GPU.
	/// </remarks>
	/// <param name="data">A Span of type T which the GPU buffer's contents will be copied into.</param>
	public void GetData<T>( Span<T> data ) where T : unmanaged
	{
		ObjectDisposedException.ThrowIf( native == IntPtr.Zero, this );

		GetData( data, 0, data.Length );
	}

	/// <summary>
	/// Retrieves a number of elements from the GPU buffer and copies them into a provided Span.
	/// </summary>
	/// <remarks>
	/// This operation is synchronous and will block until the specified range of data has been fully downloaded from the GPU.
	/// </remarks>
	/// <param name="data">A Span of type T which the GPU buffer's contents will be copied into.</param>
	/// <param name="start">The starting index from which to begin retrieving data. This index is in terms of elements, not bytes.</param>
	/// <param name="count">The number of elements to retrieve from the GPU buffer. This count is also in terms of elements, not bytes.</param>
	public void GetData<T>( Span<T> data, int start, int count ) where T : unmanaged
	{
		ObjectDisposedException.ThrowIf( native == IntPtr.Zero, this );

		if ( count <= 0 || count > data.Length )
			throw new ArgumentOutOfRangeException( nameof( count ) );

		if ( !SandboxedUnsafe.IsAcceptablePod( typeof( T ) ) )
			throw new InvalidOperationException( $"{typeof( T )} is not allowed in GPU buffers" );

		var arrayElementSize = Unsafe.SizeOf<T>();

		GetDataInternal( MemoryMarshal.Cast<T, byte>( data ), (uint)(start * arrayElementSize), (uint)(count * arrayElementSize) );
	}

	private unsafe void GetDataInternal( Span<byte> data, uint startInBytes, uint countInBytes )
	{
		fixed ( byte* dataPtr = data )
		{
			g_pRenderDevice.ReadBuffer( native, startInBytes, (IntPtr)dataPtr, countInBytes );
		}
	}

	/// <summary>
	/// Asynchronously retrieves data from the GPU buffer and provides it to the callback.
	/// </summary>
	/// <remarks>
	/// This operation is asynchronous and won't block the calling thread while data is downloaded from the GPU.
	/// The data span is only valid during the callback execution.
	/// </remarks>
	/// <typeparam name="T">The type of data to retrieve.</typeparam>
	/// <param name="callback">Callback that receives the data when the read operation completes.</param>
	public void GetDataAsync<T>( Action<ReadOnlySpan<T>> callback ) where T : unmanaged
	{
		ObjectDisposedException.ThrowIf( native == IntPtr.Zero, this );

		GetDataAsync( callback, 0, ElementCount );
	}

	/// <summary>
	/// Asynchronously retrieves a number of elements from the GPU buffer and provides it to the callback.
	/// </summary>
	/// <remarks>
	/// This operation is asynchronous and won't block the calling thread while data is downloaded from the GPU.
	/// The data span is only valid during the callback execution.
	/// </remarks>
	/// <typeparam name="T">The type of data to retrieve.</typeparam>
	/// <param name="callback">Callback that receives the data when the read operation completes.</param>
	/// <param name="start">The starting index from which to begin retrieving data. This index is in terms of elements, not bytes.</param>
	/// <param name="count">The number of elements to retrieve from the GPU buffer. This count is also in terms of elements, not bytes.</param>
	public void GetDataAsync<T>( Action<ReadOnlySpan<T>> callback, int start, int count ) where T : unmanaged
	{
		ObjectDisposedException.ThrowIf( native == IntPtr.Zero, this );

		if ( count <= 0 ) throw new ArgumentOutOfRangeException( nameof( count ) );

		if ( !SandboxedUnsafe.IsAcceptablePod( typeof( T ) ) )
			throw new InvalidOperationException( $"{typeof( T )} is not allowed in GPU buffers" );

		var offsetInBytes = start * ElementSize;
		var sizeInBytes = count * ElementSize;

		Graphics.Context.ReadBufferAsync( this, bytes =>
		{
			// Reinterpret the byte span as a span of T
			var typedSpan = MemoryMarshal.Cast<byte, T>( bytes );
			callback( typedSpan );
		}, offsetInBytes, sizeInBytes );
	}

	/// <summary>
	/// Synchronously uploads data from a Span to the GPU, replacing the existing data in this GpuBuffer.
	/// </summary>
	/// <remarks>
	/// This operation is synchronous; it will block until the data has been fully uploaded to the GPU.
	/// </remarks>
	/// <param name="data">The Span of data to upload. It should contain items of type T, which is a struct.</param>
	/// <param name="elementOffset">The offset in terms of elements (not bytes) at which to start uploading data (default is 0).</param>
	public void SetData<T>( Span<T> data, int elementOffset = 0 ) where T : unmanaged
	{
		ObjectDisposedException.ThrowIf( native == IntPtr.Zero, this );

		if ( !SandboxedUnsafe.IsAcceptablePod( typeof( T ) ) )
			throw new InvalidOperationException( $"{typeof( T )} is not allowed in GPU buffers" );

		SetDataInternal( MemoryMarshal.Cast<T, byte>( data ), elementOffset * Unsafe.SizeOf<T>() );
	}

	/// <summary>
	/// Synchronously uploads data from a List to the GPU, replacing the existing data in this GpuBuffer.
	/// </summary>
	/// <remarks>
	/// This operation is synchronous; it will block until the data has been fully uploaded to the GPU.
	/// </remarks>
	/// <param name="data">The List of data to upload. It should contain items of type T, which is a struct.</param>
	/// <param name="elementOffset">The offset in terms of elements (not bytes) at which to start uploading data (default is 0).</param>
	public void SetData<T>( List<T> data, int elementOffset = 0 ) where T : unmanaged
	{
		SetData<T>( CollectionsMarshal.AsSpan( data ), elementOffset );
	}

	private unsafe void SetDataInternal( Span<byte> data, int elementOffset )
	{
		if ( data.Length > ElementCount * ElementSize )
		{
			throw new ArgumentOutOfRangeException( $"SetData length: {data.Length} exceeds size of GpuBuffer: {ElementCount * ElementSize}" );
		}

		fixed ( byte* dataPtr = data )
		{
			RenderTools.SetGPUBufferData( Graphics.Context, native, (IntPtr)dataPtr, (uint)data.Length, (uint)elementOffset );
		}
	}

	/// <summary>
	/// For <see cref="UsageFlags.Append"/> buffers there is a hidden uint 32-bit atomic counter in the buffer that contains the number of 
	/// writes to the buffer after invocation of the compute shader.  In order to get the value of the counter, the data needs to be copied to
	/// another GPU buffer that can be used.
	/// </summary>
	public void CopyStructureCount( GpuBuffer destBuffer, int destBufferOffset = 0 )
	{
		ObjectDisposedException.ThrowIf( native == IntPtr.Zero, this );
		ObjectDisposedException.ThrowIf( destBuffer.native == IntPtr.Zero, destBuffer );

		if ( !Usage.HasFlag( UsageFlags.Append ) && !Usage.HasFlag( UsageFlags.Structured ) )
			throw new InvalidOperationException( $"GpuBuffer must be created with UsageFlags.Append or UsageFlags.Structured to CopyStructureCount." );

		RenderTools.CopyGPUBufferHiddenStructureCount( Graphics.Context, native, destBuffer.native, (uint)destBufferOffset );
	}

	/// <summary>
	/// Sets the counter value for <see cref="UsageFlags.Append"/> or <see cref="UsageFlags.Counter"/> structured buffers.
	/// </summary>
	public void SetCounterValue( uint counterValue )
	{
		ObjectDisposedException.ThrowIf( native == IntPtr.Zero, this );

		if ( !Usage.HasFlag( UsageFlags.Append ) && !Usage.HasFlag( UsageFlags.Structured ) ) throw new InvalidOperationException( $"GpuBuffer must be created with UsageFlags.Append or UsageFlags.Structured to SetCounterValue." );

		RenderTools.SetGPUBufferHiddenStructureCount( Graphics.Context, native, counterValue );
	}
}

/// <summary>
/// A typed GpuBuffer
/// </summary>
/// <typeparam name="T">
/// The type of data that the GpuBuffer will store.
/// Must be a <see href="https://docs.microsoft.com/en-us/dotnet/framework/interop/blittable-and-non-blittable-types">blittable</see> value type.
/// </typeparam>
public class GpuBuffer<T> : GpuBuffer where T : unmanaged
{
	public GpuBuffer( int elementCount, UsageFlags flags = UsageFlags.Structured, string debugName = null ) : base()
	{
		if ( !SandboxedUnsafe.IsAcceptablePod( typeof( T ) ) )
			throw new InvalidOperationException( $"{typeof( T )} is not allowed in GPU buffers" );

		Initialize( elementCount, Unsafe.SizeOf<T>(), flags, debugName );
	}

	public void GetData( Span<T> data ) => GetData<T>( data );
	public void GetData( Span<T> data, int start, int count ) => GetData<T>( data, start, count );
	public void SetData( Span<T> data, int elementOffset = 0 ) => SetData<T>( data, elementOffset );
	public void GetDataAsync( Action<ReadOnlySpan<T>> callback ) => GetDataAsync<T>( callback );
	public void GetDataAsync( Action<ReadOnlySpan<T>> callback, int start, int count ) => GetDataAsync<T>( callback, start, count );
}

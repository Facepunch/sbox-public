namespace Sandbox.SceneRenderer.Gpu;

/// <summary>
/// Rotating upload buffers for in-flight frames. Copies record before consumers on the frame context.
/// </summary>
internal sealed class UploadRing<T> : IDisposable where T : unmanaged
{
	const int FramesInFlight = 3;

	readonly GpuBuffer<T>[] buffers = new GpuBuffer<T>[FramesInFlight];
	readonly string name;
	readonly GpuBuffer.UsageFlags usage;
	int frame;

	/// <summary>
	/// Create a buffer ring with the requested usage.
	/// </summary>
	public UploadRing( string name, GpuBuffer.UsageFlags usage = GpuBuffer.UsageFlags.Structured )
	{
		this.name = name;
		this.usage = usage;
	}

	/// <summary>
	/// The buffer the last <see cref="Upload"/> went into.
	/// </summary>
	public GpuBuffer<T> Current { get; private set; }

	/// <summary>
	/// Copy <paramref name="data"/> into the next buffer round, growing it to fit, and make it current.
	/// </summary>
	public GpuBuffer<T> Upload( RenderContext context, ReadOnlySpan<T> data, int minimumCapacity = 1 )
	{
		frame = (frame + 1) % FramesInFlight;

		var needed = Math.Max( data.Length, minimumCapacity );
		ref var buffer = ref buffers[frame];
		if ( buffer is null || buffer.ElementCount < needed )
		{
			buffer?.Dispose();
			buffer = new GpuBuffer<T>( Math.Max( needed, (buffer?.ElementCount ?? 0) * 2 ), usage, name );
		}

		if ( data.Length > 0 ) context.UploadBuffer( buffer, data );
		Current = buffer;
		return buffer;
	}

	public void Dispose()
	{
		for ( int i = 0; i < buffers.Length; i++ )
		{
			buffers[i]?.Dispose();
			buffers[i] = null;
		}

		Current = null;
	}
}

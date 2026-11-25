namespace Borealis.Gpu;

/// <summary>
/// Rotating upload buffers for in-flight frames. Copies record before consumers on the frame context.
/// </summary>
internal sealed class UploadRing<T> : IDisposable where T : unmanaged
{
	const int FramesInFlight = 3;

	readonly GpuBuffer<T>[] buffers = new GpuBuffer<T>[FramesInFlight];
	(int Length, int Version)[] prefixes;
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
	/// The buffer the last upload went into.
	/// </summary>
	public GpuBuffer<T> Current { get; private set; }

	/// <summary>
	/// Copy <paramref name="data"/> into the next buffer round, growing it to fit, and make it current.
	/// </summary>
	public GpuBuffer<T> Upload( GpuContext context, ReadOnlySpan<T> data, int minimumCapacity = 1 )
	{
		var buffer = Advance( Math.Max( data.Length, minimumCapacity ) );
		if ( data.Length > 0 ) context.UploadBuffer( buffer, data );
		if ( prefixes is not null ) prefixes[frame] = default;
		return buffer;
	}

	/// <summary>
	/// Upload the whole span on a new or stale buffer, otherwise only the part after the versioned prefix.
	/// </summary>
	public GpuBuffer<T> UploadWithPrefix( GpuContext context, ReadOnlySpan<T> data, int prefixLength, int prefixVersion, out bool uploaded )
	{
		var buffer = Advance( Math.Max( data.Length, 1 ) );
		prefixes ??= new (int, int)[FramesInFlight];
		var prefix = (prefixLength, prefixVersion);
		var start = prefixes[frame] == prefix ? prefixLength : 0;
		uploaded = start < data.Length;
		if ( uploaded ) context.UploadBuffer( buffer, data[start..], start * System.Runtime.CompilerServices.Unsafe.SizeOf<T>() );
		prefixes[frame] = prefix;
		return buffer;
	}

	GpuBuffer<T> Advance( int needed )
	{
		frame = (frame + 1) % FramesInFlight;

		ref var buffer = ref buffers[frame];
		if ( buffer is null || buffer.ElementCount < needed )
		{
			buffer?.Dispose();
			buffer = new GpuBuffer<T>( Math.Max( needed, (buffer?.ElementCount ?? 0) * 2 ), usage, name );
			if ( prefixes is not null ) prefixes[frame] = default;
		}

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

		prefixes = null;
		Current = null;
	}
}

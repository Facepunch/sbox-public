using Borealis.Gpu;

namespace Sandbox;

/// <summary>
/// Texture readbacks that don't wait, for recording the screen.
/// </summary>
internal sealed partial class BorealisGpuRenderBackend
{
	// Readback buffers of textureReadSize bytes that callbacks are done with
	System.Collections.Concurrent.ConcurrentQueue<BufferHandle> textureReadBuffers = new();
	int textureReadSize;

	// Earlier sizes' queues, which buffers still being read return to, freed from here
	readonly List<System.Collections.Concurrent.ConcurrentQueue<BufferHandle>> retiredReadBuffers = new();

	/// <summary>
	/// Read mip 0 of <paramref name="texture"/> back without waiting: copied to a readback buffer at this point of the block
	/// being recorded, and handed to <paramref name="callback"/> on a worker once the GPU has done it, as
	/// <see cref="ReadBufferAsync"/> hands back buffers. Its bytes are the texture's format, rows tightly packed.
	/// </summary>
	internal void ReadTextureAsync( Texture texture, AsyncGPUReadback.BufferReadDelegate callback )
	{
		_ = Recording;
		var handle = TextureOf( texture );
		var info = gpu.InfoOf( handle );
		var size = info.Width * info.Height * info.Format.BytesPerBlock();
		// A frame's worth of host memory each time: buffers of the size last read come back to be used again
		foreach ( var retired in retiredReadBuffers )
		{
			while ( retired.TryDequeue( out var old ) ) gpu.Destroy( old );
		}

		if ( size != textureReadSize )
		{
			retiredReadBuffers.Add( textureReadBuffers );
			textureReadBuffers = new();
			textureReadSize = size;
		}

		var readback = new Readback
		{
			Buffer = textureReadBuffers.TryDequeue( out var free ) ? free : gpu.CreateBuffer( new BufferInfo { Size = size, Memory = MemoryLocation.Readback, Name = "Texture readback" } ),
			Size = size,
			Callback = callback,
			Frame = gpu.FrameNumber,
			ReturnTo = textureReadBuffers,
		};

		context.CopyToBuffer( handle, readback.Buffer );
		blockReadbacks.Add( readback );
	}
}

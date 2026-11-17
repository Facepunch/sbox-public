using Borealis.Gpu;

namespace Sandbox;

/// <summary>
/// Texture readbacks that don't wait, for recording the screen.
/// </summary>
internal sealed partial class BorealisGpuRenderBackend
{
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
		var readback = new Readback
		{
			Buffer = gpu.CreateBuffer( new BufferInfo { Size = size, Memory = MemoryLocation.Readback, Name = "Texture readback" } ),
			Size = size,
			Callback = callback,
			Frame = gpu.FrameNumber,
		};

		context.CopyToBuffer( handle, readback.Buffer );
		blockReadbacks.Add( readback );
	}
}

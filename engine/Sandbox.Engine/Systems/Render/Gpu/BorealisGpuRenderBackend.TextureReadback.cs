using Borealis.Gpu;
using System.Collections.Concurrent;
using GpuFormat = Borealis.Gpu.Format;

namespace Sandbox;

/// <summary>
/// Texture readbacks: ones that wait (<see cref="Texture.GetPixels(int)"/> and the rest), and ones that don't, for recording
/// the screen.
/// </summary>
internal sealed partial class BorealisGpuRenderBackend
{
	// The readback buffer reads that wait share, kept up to KeptPixelReadback bytes; larger reads make one of their own
	BufferHandle pixelReadback;
	long pixelReadbackSize;
	const long KeptPixelReadback = 4 << 20;

	/// <summary>
	/// A rectangle of one mip and slice (an array layer, a cube face, a volume's slice) of a texture's backing, after
	/// everything submitted so far, converted on the CPU into the engine format asked for: what Source 2 reads back, in
	/// every format the engine makes textures in, compressed and depth ones too. The same size only, and not inside a block,
	/// which hasn't been submitted. Waits for the GPU, as Source 2's read does.
	/// </summary>
	public override unsafe bool ReadPixels( Texture texture, NativeRect source, int slice, int mip, NativeRect destination, IntPtr data, ImageFormat format, int stride )
	{
		FromMain();
		if ( list is not null ) return false;
		if ( source.w != destination.w || source.h != destination.h || source.w <= 0 || source.h <= 0 ) return false;

		var handle = TextureOf( texture );
		var info = gpu.InfoOf( handle );
		var volume = info.Dimension == TextureDimension.Texture3D;
		var slices = volume ? Math.Max( info.Depth >> mip, 1 ) : ArrayLayersOf( info );
		if ( mip < 0 || mip >= info.Mips || slice < 0 || slice >= slices || info.Samples > 1 ) return false;
		if ( ReadFormatOf( info.Format ) is not { } read ) return false;

		int width = Math.Max( info.Width >> mip, 1 ), height = Math.Max( info.Height >> mip, 1 );
		if ( source.x < 0 || source.y < 0 || source.x + source.w > width || source.y + source.h > height ) return false;

		var pixel = NativeEngine.ImageLoader.GetMemRequired( 1, 1, 1, 1, format );
		if ( pixel <= 0 ) return false;
		stride = stride > 0 ? stride : destination.w * pixel;

		long sliceBytes = info.Format.IsDepth() ? (long)width * height * read.Texel : (long)info.Format.RowPitch( width ) * info.Format.Rows( height );
		var size = sliceBytes * (volume ? slices : 1);

		var readback = AcquirePixelReadback( size, out var kept );
		try
		{
			var bytes = ReadTextureToBuffer( handle, readback, mip, volume ? 0 : slice )
				.Slice( (int)(volume ? sliceBytes * slice : 0), (int)sliceBytes );
			return CopyPixels( bytes, read.Format, info.Format.IsCompressed(), width, height, source, destination, data, format, pixel, stride );
		}
		finally
		{
			if ( !kept ) gpu.Destroy( readback );
		}
	}

	BufferHandle AcquirePixelReadback( long size, out bool kept )
	{
		kept = size <= KeptPixelReadback;
		if ( !kept ) return gpu.CreateBuffer( new BufferInfo { Size = size, Memory = MemoryLocation.Readback, Name = "Pixel readback" } );

		if ( pixelReadbackSize < size )
		{
			var capacity = Math.Min( Math.Max( size, 64 << 10 ), KeptPixelReadback );
			var buffer = gpu.CreateBuffer( new BufferInfo { Size = capacity, Memory = MemoryLocation.Readback, Name = "Pixel readback" } );
			if ( pixelReadback.IsValid ) gpu.Destroy( pixelReadback );
			pixelReadback = buffer;
			pixelReadbackSize = capacity;
		}

		return pixelReadback;
	}

	Span<byte> ReadTextureToBuffer( TextureHandle handle, BufferHandle readback, int mip = 0, int layer = 0 )
	{
		SubmitUploads();
		var copy = gpu.BeginCommandList( QueueType.Graphics, "Readback" );
		context.Begin( copy );
		context.CopyToBuffer( handle, readback, mip, layer );
		context.Finish();
		using ( Sandbox.Rendering.RenderSync.Readback( "Texture.GetPixels" ) ) gpu.Wait( gpu.Submit( copy ) );
		return gpu.Map( readback );
	}

	/// <summary>
	/// The engine form of what a backing's copy to a buffer holds, for converting it, and its bytes a texel (per block when
	/// compressed); null for formats with none. A depth backing copies its depth alone: a float, or 16 bits.
	/// </summary>
	static (ImageFormat Format, int Texel)? ReadFormatOf( GpuFormat format ) => format switch
	{
		GpuFormat.D32Float or GpuFormat.D32FloatS8Uint => (ImageFormat.R32F, 4),
		GpuFormat.D16Unorm => (ImageFormat.R16, 2),
		_ => ImageFormatOf( format ) is { } engine ? (engine, format.BytesPerBlock()) : null,
	};

	// Callbacks return buffers here regardless of the capture size at completion.
	readonly ConcurrentQueue<BufferHandle> returnedTextureReadBuffers = new();
	readonly Queue<BufferHandle> textureReadBuffers = new();
	int textureReadSize;

	void RecycleTextureReadBuffers()
	{
		while ( returnedTextureReadBuffers.TryDequeue( out var buffer ) )
		{
			if ( gpu.SizeOf( buffer ) == textureReadSize ) textureReadBuffers.Enqueue( buffer );
			else gpu.Destroy( buffer );
		}
	}

	void DestroyTextureReadBuffers()
	{
		while ( textureReadBuffers.TryDequeue( out var buffer ) ) gpu.Destroy( buffer );
		while ( returnedTextureReadBuffers.TryDequeue( out var returned ) ) gpu.Destroy( returned );
	}

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

		if ( size != textureReadSize )
		{
			while ( textureReadBuffers.TryDequeue( out var old ) ) gpu.Destroy( old );
			textureReadSize = size;
		}
		if ( textureReadBuffers.Count == 0 ) RecycleTextureReadBuffers();

		var readback = new Readback
		{
			Buffer = textureReadBuffers.TryDequeue( out var free ) ? free : gpu.CreateBuffer( new BufferInfo { Size = size, Memory = MemoryLocation.Readback, Name = "Texture readback" } ),
			Size = size,
			Callback = callback,
			Frame = gpu.FrameNumber,
			ReturnTo = returnedTextureReadBuffers,
		};

		context.CopyToBuffer( handle, readback.Buffer );
		blockReadbacks.Add( readback );
	}
}

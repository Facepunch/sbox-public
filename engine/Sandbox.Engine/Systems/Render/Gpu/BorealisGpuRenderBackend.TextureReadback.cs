using Borealis.Gpu;
using System.Buffers;
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
		// With Source 2's device kept (the editor): what native drew (a bake's captures) is read from its copy, and so are
		// volumes, which aren't read back here (a DDGI bake's relocation, made on the CPU)
		if ( Graphics.HasSource2Device && (texture.PixelsOnSource2 || (texture.Depth > 1 && !texture.Desc.IsCube && texture.Desc.m_nFlags.Contains( NativeEngine.RuntimeTextureSpecificationFlags.TSPEC_VOLUME_TEXTURE ))) )
			return base.ReadPixels( texture, source, slice, mip, destination, data, format, stride );

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

		// A compressed mip decodes whole blocks: its rows and columns rounded up to 4, then cut to the rectangle
		var compressed = info.Format.IsCompressed();
		int decodedWidth = compressed ? (width + 3) & ~3 : width, decodedHeight = compressed ? (height + 3) & ~3 : height;
		long sliceBytes = info.Format.IsDepth() ? (long)width * height * read.Texel : (long)info.Format.RowPitch( width ) * info.Format.Rows( height );
		var size = sliceBytes * (volume ? slices : 1);

		var kept = size <= KeptPixelReadback;
		if ( kept && pixelReadbackSize < size )
		{
			if ( pixelReadback.IsValid ) gpu.Destroy( pixelReadback );
			pixelReadbackSize = Math.Min( Math.Max( size, 64 << 10 ), KeptPixelReadback );
			pixelReadback = gpu.CreateBuffer( new BufferInfo { Size = pixelReadbackSize, Memory = MemoryLocation.Readback, Name = "Pixel readback" } );
		}

		var readback = kept ? pixelReadback : gpu.CreateBuffer( new BufferInfo { Size = size, Memory = MemoryLocation.Readback, Name = "Pixel readback" } );
		var converted = ArrayPool<byte>.Shared.Rent( decodedWidth * decodedHeight * pixel );
		try
		{
			SubmitUploads();
			var copy = gpu.BeginCommandList( QueueType.Graphics, "Readback" );
			context.Begin( copy );
			context.CopyToBuffer( handle, readback, mip, volume ? 0 : slice );
			context.Finish();
			gpu.Wait( gpu.Submit( copy ) );

			ReadOnlySpan<byte> bytes = gpu.Map( readback ).Slice( (int)(volume ? sliceBytes * slice : 0), (int)sliceBytes );

			// Already in the format asked for: the rows as they are
			if ( read.Format != format || compressed )
			{
				fixed ( byte* src = bytes )
				fixed ( byte* dst = converted )
				{
					if ( !NativeEngine.ImageLoader.ConvertImageFormat( (IntPtr)src, read.Format, (IntPtr)dst, format, decodedWidth, decodedHeight, 0, decodedWidth * pixel ) ) return false;
				}

				bytes = converted;
			}

			var row = source.w * pixel;
			for ( int y = 0; y < source.h; y++ )
			{
				var line = bytes.Slice( ((source.y + y) * decodedWidth + source.x) * pixel, row );
				line.CopyTo( new Span<byte>( (byte*)data + (long)(destination.y + y) * stride + (long)destination.x * pixel, row ) );
			}

			return true;
		}
		finally
		{
			ArrayPool<byte>.Shared.Return( converted );
			if ( !kept ) gpu.Destroy( readback );
		}
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

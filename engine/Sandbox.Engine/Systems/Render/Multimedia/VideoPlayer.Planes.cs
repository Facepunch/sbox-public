using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// Video on Borealis.Gpu (<c>-borealisgpu</c>): the decoder hands each frame's planes to managed code on the CPU, and they're
/// converted to RGB on the device by <c>yuv_resolve_cs</c>, as the decoder converts them on Source 2's.
/// </summary>
public sealed partial class VideoPlayer
{
	// The frame's planes as the device reads them
	Texture planeY, planeU, planeV, planeAlpha, rgbaSource;
	ComputeShader yuvResolve;
	RenderAttributes resolveAttributes;
	byte[] packed;
	Vector2Int frameSize;

	/// <summary>
	/// Whether the decoder hands its frames over on the CPU (<see cref="OnPlanesInternal"/>) rather than converting them on
	/// Source 2's device: on Borealis.Gpu, where Source 2's render system is empty.
	/// </summary>
	internal bool WantsPlanes() => RenderBackend.Default is BorealisGpuRenderBackend;

	/// <summary>
	/// A decoded frame's planes, valid for this call: Y, U and V (U and V at half size) with alpha when the video has it, or
	/// RGBA in <paramref name="y"/> alone (WebP). Uploaded to the device and converted into <see cref="Texture"/> there, with
	/// its mips, in the render block being recorded or one of their own. Something that wants the pixels on the CPU
	/// (<see cref="OnTextureData"/>) gets them read back from the device, outside a block, as the decoder reads them back on
	/// Source 2's.
	/// </summary>
	internal void OnPlanesInternal( IntPtr y, IntPtr u, IntPtr v, IntPtr alpha, int width, int height, int strideY, int strideU, int strideV, int strideAlpha, bool fullRange, bool bt709 )
	{
		if ( width < 1 || height < 1 || y == IntPtr.Zero || RenderBackend.Default is not BorealisGpuRenderBackend backend ) return;

		var rgba = u == IntPtr.Zero || v == IntPtr.Zero;
		MakeFrame( width, height );

		if ( rgba )
		{
			Upload( backend, ref rgbaSource, ImageFormat.RGBA8888, y, width, height, strideY, 4 );
		}
		else
		{
			Upload( backend, ref planeY, ImageFormat.I8, y, width, height, strideY, 1 );
			Upload( backend, ref planeU, ImageFormat.I8, u, width / 2, height / 2, strideU, 1 );
			Upload( backend, ref planeV, ImageFormat.I8, v, width / 2, height / 2, strideV, 1 );
			if ( alpha != IntPtr.Zero ) Upload( backend, ref planeAlpha, ImageFormat.I8, alpha, width, height, strideAlpha, 1 );
		}

		// Made outside any block: materials can't be made while drawing. A first frame inside one waits for the next
		if ( !rgba && yuvResolve is null )
		{
			if ( Graphics.IsActive ) return;
			yuvResolve = new ComputeShader( "yuv_resolve_cs" );
		}

		if ( Graphics.IsActive )
		{
			Convert( rgba, alpha != IntPtr.Zero, width, height, fullRange, bt709 );
			return;
		}

		using ( Graphics.Scope.CreateOn( backend ) )
			Convert( rgba, alpha != IntPtr.Zero, width, height, fullRange, bt709 );

		if ( OnTextureData is { } wants )
		{
			var pixels = backend.ReadPixels( Texture );
			wants( System.Runtime.InteropServices.MemoryMarshal.AsBytes( pixels.AsSpan() ), new Vector2( width, height ) );
		}
	}

	/// <summary>The uploaded planes into <see cref="Texture"/>, then its mips, in the block being recorded.</summary>
	void Convert( bool rgba, bool alpha, int width, int height, bool fullRange, bool bt709 )
	{
		if ( rgba )
		{
			Graphics.CopyTexture( rgbaSource, Texture );
		}
		else
		{
			var attributes = resolveAttributes ??= RenderAttributes.CreateManaged();
			attributes.Clear();
			attributes.Set( "TextureY", planeY );
			attributes.Set( "TextureU", planeU );
			attributes.Set( "TextureV", planeV );
			attributes.Set( "OutputRGB", Texture );
			attributes.SetCombo( "D_LIMITED_RANGE", fullRange ? 0 : 1 );
			attributes.SetCombo( "D_BT709", bt709 ? 1 : 0 );
			attributes.SetCombo( "D_ALPHA", alpha ? 1 : 0 );
			if ( alpha ) attributes.Set( "TextureAlpha", planeAlpha );
			yuvResolve.DispatchWithAttributes( attributes, width, height, 1 );
		}

		Graphics.GenerateMipMaps( Texture );
	}

	/// <summary>
	/// <see cref="Texture"/> made the video's size, an RGBA8 target with 8 mips (fewer for a small one) as the decoder makes
	/// one on Source 2's device, when it isn't yet.
	/// </summary>
	void MakeFrame( int width, int height )
	{
		if ( frameSize == new Vector2Int( width, height ) ) return;
		frameSize = new Vector2Int( width, height );

		using var made = Texture.CreateRenderTarget().WithSize( width, height ).WithFormat( ImageFormat.RGBA8888 ).WithMips( Math.Min( 8, 1 + System.Numerics.BitOperations.Log2( (uint)Math.Max( width, height ) ) ) ).Create( "video_rgb" );
		Texture.CopyFrom( made );
		Texture.IsLoaded = true;
	}

	/// <summary>A plane into its texture on the device, its rows packed when the decoder's stride is wider.</summary>
	unsafe void Upload( BorealisGpuRenderBackend backend, ref Texture texture, ImageFormat format, IntPtr data, int width, int height, int stride, int bytesPerPixel )
	{
		if ( width < 1 || height < 1 ) return;
		if ( texture is null || texture.Width != width || texture.Height != height )
		{
			texture?.Dispose();
			// Made with pixels, so its copy on the device is made in its own format, which the writes then go into
			texture = Texture.Create( width, height, format ).WithName( "video plane" ).WithData( new byte[width * height * bytesPerPixel] ).Finish();
		}

		var row = width * bytesPerPixel;
		var size = row * height;
		ReadOnlySpan<byte> bytes;
		if ( stride == row )
		{
			bytes = new ReadOnlySpan<byte>( (void*)data, size );
		}
		else
		{
			if ( packed is null || packed.Length < size ) packed = new byte[size];
			for ( int i = 0; i < height; i++ ) new ReadOnlySpan<byte>( (byte*)data + (long)i * stride, row ).CopyTo( packed.AsSpan( i * row ) );
			bytes = packed.AsSpan( 0, size );
		}

		// Its backing first, so the write lands in it rather than being taken up by the pixels it was made with
		backend.TextureOf( texture );
		backend.Update( texture, bytes, 0, 0, 0, width, height, 1 );
	}

	void DisposePlanes()
	{
		planeY?.Dispose();
		planeU?.Dispose();
		planeV?.Dispose();
		planeAlpha?.Dispose();
		rgbaSource?.Dispose();
		planeY = planeU = planeV = planeAlpha = rgbaSource = null;
	}
}

namespace Sandbox.Rendering.Bridge;

/// <summary>
/// Renders to bitmaps on Borealis.Gpu (<c>-borealisgpu</c>): thumbnails, icons, map previews, game code's
/// <c>SceneUtility.RenderGameObjectToBitmap</c>.
/// </summary>
internal sealed partial class SceneBridge
{
	/// <summary>
	/// A bitmap render on the device, as native's (<c>CCameraRenderer::RenderToBitmap</c>): the camera rendered as a texture
	/// render (<see cref="RenderIntoTexture"/>) into an 8-bit target the bitmap's size, unflipped, then read back and written
	/// into the bitmap, waiting for the GPU as native does. What it draws loads before it renders, as native's does, rather
	/// than asynchronously as the game window's models do. Inside a render block it renders as a child view does, without the
	/// camera's stages (<see cref="RenderBitmapInBlock"/>).
	/// </summary>
	public void RenderToBitmap( SceneCamera camera, Bitmap bitmap, in ViewSetup setup )
	{
		if ( !bitmap.IsValid() || Gpu is not { } gpu ) return;

		var size = bitmap.Size;
		if ( size.x <= 1 || size.y <= 1 ) return;

		if ( !Graphics.IsActive ) camera.OnPreRender( size );

		var output = Output;
		var bitmapTarget = output.BitmapTarget;
		if ( bitmapTarget is null || bitmapTarget.Width != size.x || bitmapTarget.Height != size.y )
		{
			bitmapTarget?.Dispose();
			output.BitmapTarget = bitmapTarget = Texture.CreateRenderTarget().WithSize( size.x, size.y ).WithFormat( ImageFormat.RGBA8888 ).Create( "Borealis.Gpu bitmap" );
		}

		// A bitmap is never flipped (native's RenderPixels clears the flips)
		var view = setup;
		view.FlipX = null;
		view.FlipY = null;

		// Native loads what it draws before it renders, and so does this render: what a bitmap shows is usually new to the device
		var device = MirrorContent.Gpu;
		var async = device?.AsyncModels ?? false;
		if ( device is not null ) device.AsyncModels = false;
		try
		{
			if ( Graphics.IsActive )
			{
				RenderBitmapInBlock( camera, bitmap, bitmapTarget, view );
				return;
			}

			if ( !RenderIntoTexture( gpu, camera, bitmapTarget, size, view, out _, bitmap ) ) return;
		}
		finally
		{
			if ( device is not null ) device.AsyncModels = async;
		}

		Write( bitmap, gpu.ReadPixels( bitmapTarget ) );
	}

	/// <summary>RGBA8 pixels into a bitmap, as they are into an 8-bit one, as half floats into a float one.</summary>
	static unsafe void Write( Bitmap bitmap, Color32[] pixels )
	{
		var count = bitmap.Width * bitmap.Height;
		if ( pixels.Length < count ) return;

		var destination = bitmap.GetPointer();
		if ( bitmap.BytesPerPixel == 4 )
		{
			System.Runtime.InteropServices.MemoryMarshal.AsBytes( pixels.AsSpan( 0, count ) ).CopyTo( new Span<byte>( destination, count * 4 ) );
			return;
		}

		if ( bitmap.BytesPerPixel != 8 ) return;
		var halves = new Span<Half>( destination, count * 4 );
		for ( int i = 0; i < count; i++ )
		{
			var p = pixels[i];
			halves[i * 4 + 0] = (Half)(p.r / 255f);
			halves[i * 4 + 1] = (Half)(p.g / 255f);
			halves[i * 4 + 2] = (Half)(p.b / 255f);
			halves[i * 4 + 3] = (Half)(p.a / 255f);
		}
	}
}

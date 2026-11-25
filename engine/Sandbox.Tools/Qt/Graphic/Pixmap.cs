using Native;
using System;

namespace Qt
{
	internal enum AspectRatioMode
	{
		IgnoreAspectRatio,
		KeepAspectRatio,
		KeepAspectRatioByExpanding
	}

	internal enum TransformationMode
	{
		FastTransformation,
		SmoothTransformation
	}

	internal enum QImageFormat
	{
		Invalid,
		Mono,
		MonoLSB,
		Indexed8,
		RGB32,
		ARGB32,
		ARGB32_Premultiplied,
		RGB16,
		ARGB8565_Premultiplied,
		RGB666,
		ARGB6666_Premultiplied,
		RGB555,
		ARGB8555_Premultiplied,
		RGB888,
		RGB444,
		ARGB4444_Premultiplied,
		RGBX8888,
		RGBA8888,
		RGBA8888_Premultiplied,
		BGR30,
		A2BGR30_Premultiplied,
		RGB30,
		A2RGB30_Premultiplied,
		Alpha8,
		Grayscale8,
		RGBX64,
		RGBA64,
		RGBA64_Premultiplied,
		Grayscale16,
		BGR888,
	}
}

namespace Editor
{
	/// <summary>
	/// A pixel map, or just a simple image.
	/// </summary>
	public class Pixmap
	{
		static bool HasGraphics => (!Sandbox.Application.IsHeadless && !Sandbox.Application.IsUnitTest);

		internal QPixmap ptr;


		/// <summary>
		/// Create a new empty pixel map. It can then be drawn to via the <see cref="Paint"/> class, like so:
		/// <code>
		/// var myPixMap = new Pixmap( 16, 16 );
		///
		/// Paint.Target( myPixMap );
		///  Paint.Antialiasing = true;
		///  Paint.ClearPen();
		///  Paint.SetBrush( Color.Red );
		///  Paint.DrawRect( new Rect( 0, myPixMap.Size ), 2 );
		/// Paint.Target( null );
		/// </code>
		/// </summary>
		public Pixmap( int width, int height )
		{
			if ( !HasGraphics )
				return;

			ptr = QPixmap.Create( width, height );
			// new QPixmap() is filled with uninitialized data, so initialize it
			Clear( Color.Transparent );
		}

		/// <inheritdoc cref="Pixmap.Pixmap(int, int)"/>
		public Pixmap( Vector2 size ) : this( (int)size.x, (int)size.y )
		{
		}

		internal Pixmap( QPixmap ptr )
		{
			this.ptr = ptr;
		}

		~Pixmap()
		{
			MainThread.QueueDispose( ptr );
			ptr = default;
		}

		/// <summary>
		/// Width of the pixel map.
		/// </summary>
		public int Width => ptr.width();

		/// <summary>
		/// Height of the pixel map.
		/// </summary>
		public int Height => ptr.height();

		/// <summary>
		/// Whether this pixel map supports the alpha channel.
		/// </summary>
		public bool HasAlpha => ptr.hasAlpha();

		/// <summary>
		/// THe size of this pixel map.
		/// </summary>
		public Vector2 Size => new( Width, Height );

		/// <summary>
		/// Load an image from a file on disk, specifically from "core/tools/images".
		/// </summary>
		public static Pixmap FromFile( string filename )
		{
			if ( !HasGraphics )
				return null;

			if ( !filename.Contains( ":" ) ) filename = $"toolimages:{filename}";

			var ptr = QPixmap.CreateFromFile( filename );
			if ( ptr.IsNull ) return null;
			return new Pixmap( ptr );
		}

		/// <summary>
		/// Create a pixmap from a bitmap
		/// </summary>
		public static Pixmap FromBitmap( Bitmap bitmap )
		{
			if ( !HasGraphics )
				return null;

			Pixmap p = new Pixmap( bitmap.Width, bitmap.Height );
			p.UpdateFromPixels( bitmap );
			return p;
		}

		/// <summary>
		/// Create a pixmap from a texture.
		/// </summary>
		public static unsafe Pixmap FromTexture( Texture texture, bool withAlpha = true )
		{
			if ( !HasGraphics )
				return null;

			if ( texture is null || !texture.IsValid )
				return null;

			// Its first mip and slice, read back from Borealis.Gpu
			var (width, height) = texture.ReadSize( 0 );
			var outputFormat = withAlpha ? ImageFormat.BGRA8888 : ImageFormat.BGR888;
			var targetMemoryRequired = NativeEngine.ImageLoader.GetMemRequired( width, height, 1, 1, outputFormat );

			if ( width <= 0 || height <= 0 || targetMemoryRequired <= 0 )
				return null;

			var data = new byte[targetMemoryRequired];

			try
			{
				texture.GetPixels<byte>( (0, 0, width, height), 0, 0, data, outputFormat );
			}
			catch ( Exception )
			{
				return null;
			}

			var pixmap = new Pixmap( width, height );
			pixmap.Clear( Color.Transparent );
			pixmap.UpdateFromPixels( data, width, height, outputFormat );

			return pixmap;
		}

		internal static Pixmap FromNative( QPixmap pixmap )
		{
			return new Pixmap( pixmap );
		}

		/// <summary>
		/// Fill the pixel map with given color.
		/// </summary>
		public void Clear( Color color )
		{
			ptr.fill( color );
		}

		/// <summary>
		/// Duplicate a sub-rectangle of the image at re-draw it at given coordinates.
		/// </summary>
		/// <param name="x">Position to re-draw the duplicated image at on the X axis, from the left edge.</param>
		/// <param name="y">Position to re-draw the duplicated image at on the Y axis, from the top edge.</param>
		/// <param name="r">The area on the image to duplicate.</param>
		public void Scroll( int x, int y, Rect r )
		{
			ptr.scroll( x, y, (int)r.Left, (int)r.Top, (int)r.Width, (int)r.Height );
		}

		/// <summary>
		/// Duplicate the entire image and re-draw it at given coordinates.
		/// </summary>
		/// <param name="x">Position to re-draw the duplicated image at on the X axis, from the left edge.</param>
		/// <param name="y">Position to re-draw the duplicated image at on the Y axis, from the top edge.</param>
		public void Scroll( int x, int y )
		{
			Scroll( x, y, new Rect( 0, 0, Width, Height ) );
		}

		/// <summary>
		/// Returns a new pixel map that contains resized version of this image with given dimensions.
		/// Will try to preserve aspect ratio.
		/// </summary>
		public Pixmap Resize( Vector2 size )
		{
			var scaled = ptr.scaled( (int)size.x, (int)size.y, Qt.AspectRatioMode.KeepAspectRatioByExpanding, Qt.TransformationMode.SmoothTransformation );
			if ( scaled.IsNull ) return null;

			return new Pixmap( scaled );
		}

		/// <inheritdoc cref="Resize(Vector2)"/>
		public Pixmap Resize( int x, int y ) => Resize( new( x, y ) );

		/// <summary>
		/// Writes raw pixels to the pixel map.
		/// </summary>
		/// <param name="data">The raw image data in given <paramref name="format"/>.</param>
		/// <param name="width">Width of the image in the raw data.</param>
		/// <param name="height">Height of the image in the raw data.</param>
		/// <param name="format">The format the <paramref name="data"/> is in.</param>
		/// <returns>Whether the process was successful or not.</returns>
		/// <exception cref="System.Exception">Thrown when given an unsupported <paramref name="format"/>.</exception>
		public unsafe bool UpdateFromPixels( ReadOnlySpan<byte> data, int width, int height, ImageFormat format = ImageFormat.BGRA8888 )
		{
			fixed ( byte* d = data )
			{
				if ( format == ImageFormat.BGRA8888 )
				{
					return ptr.FromPixels( (IntPtr)d, width, height, Qt.QImageFormat.ARGB32 ); // Wtf this enum is named 
				}
				else if ( format == ImageFormat.BGR888 )
				{
					return ptr.FromPixels( (IntPtr)d, width, height, Qt.QImageFormat.BGR888 );
				}
				else if ( format == ImageFormat.RGBA8888 )
				{
					return ptr.FromPixels( (IntPtr)d, width, height, Qt.QImageFormat.RGBA8888 );
				}
				else if ( format == ImageFormat.RGB888 )
				{
					return ptr.FromPixels( (IntPtr)d, width, height, Qt.QImageFormat.RGB888 );
				}
			}

			throw new System.Exception( $"Unhandled format {format}" );
		}

		/// <summary>
		/// Copy from a bitmap
		/// </summary>
		public unsafe bool UpdateFromPixels( Bitmap bitmap )
		{
			if ( bitmap.IsFloatingPoint )
				return false;

			ptr.FromPixels( (IntPtr)bitmap.GetPointer(), bitmap.Width, bitmap.Height, Qt.QImageFormat.RGBA8888 );
			return true;
		}

		/// <summary>
		/// Writes raw pixels to the pixel map. Only BGRA8888 is currently supported.
		/// </summary>
		/// <param name="data">The raw image data in given <paramref name="format"/>.</param>
		/// <param name="size">Size of the image in the raw data.</param>
		/// <param name="format">The format the <paramref name="data"/> is in.</param>
		/// <returns>Whether the process was successful or not.</returns>
		/// <exception cref="System.Exception">Thrown when given an unsupported <paramref name="format"/>.</exception>
		public unsafe bool UpdateFromPixels( ReadOnlySpan<byte> data, Vector2 size, ImageFormat format = ImageFormat.BGRA8888 ) => UpdateFromPixels( data, (int)size.x, (int)size.y, format );

		/// <summary>
		/// Returns the raw bytes of a PNG file that contains this pixel maps image.
		/// Internally writes and deletes a file, so be careful using it often.
		/// </summary>
		public byte[] GetPng()
		{
			var target = FileSystem.Temporary.GetFullPath( $"/{System.Guid.NewGuid()}.temp" );
			try
			{
				ptr.save( target, "png", 50 );
				return System.IO.File.ReadAllBytes( target );
			}
			finally
			{
				System.IO.File.Delete( target );
			}
		}

		/// <summary>
		/// Save the pixel map as a PNG file at given location.
		/// </summary>
		/// <param name="filename">A full, valid absolute target path. Will not create directories on its own.</param>
		/// <returns>Whether the file was created or not.</returns>
		public bool SavePng( string filename )
		{
			return ptr.save( filename, "png", 1 );
		}

		/// <summary>
		/// Save the pixel map as a JPEG file at given location.
		/// </summary>
		/// <param name="filename">A full, valid absolute target path. Will not create directories on its own.</param>
		/// <param name="quality">JPEG quality, 0 to 100.</param>
		/// <returns>Whether the file was created or not.</returns>
		public bool SaveJpg( string filename, int quality = 70 )
		{
			return ptr.save( filename, "jpg", quality );
		}

		/// <summary>
		/// Returns the raw bytes of a JPEG file that contains this pixel maps image.
		/// Internally writes and deletes a file, so be careful using it often.
		/// </summary>
		/// <param name="quality">JPEG quality, 0 to 100.</param>
		public byte[] GetJpeg( int quality )
		{
			var target = FileSystem.Temporary.GetFullPath( $"/{System.Guid.NewGuid()}.temp" );
			try
			{
				ptr.save( target, "jpg", quality );
				return System.IO.File.ReadAllBytes( target );
			}
			finally
			{
				System.IO.File.Delete( target );
			}
		}

		/// <summary>
		/// Returns the raw bytes of a BMP file that contains this pixel maps image.
		/// Internally writes and deletes a file, so be careful using it often.
		/// </summary>
		public byte[] GetBmp( int quality )
		{
			var target = FileSystem.Temporary.GetFullPath( $"/{System.Guid.NewGuid()}.temp" );
			try
			{
				ptr.save( target, "bmp", quality );
				return System.IO.File.ReadAllBytes( target );
			}
			finally
			{
				System.IO.File.Delete( target );
			}
		}

		// ARGB
		Color32[] pixels;

		void DirtyPixels()
		{
			pixels = null;
		}

		int PixelSize => 4;
		int Stride => Width * PixelSize;
		int ByteSize => Height * Stride;

		unsafe void NeedPixels()
		{
			if ( pixels is not null )
				return;

			pixels = new Color32[Width * Height];

			fixed ( void* bytes = pixels )
			{
				ptr.getpixels( (IntPtr)bytes, pixels.Length * 4 /* RGBA */ );
			}
		}

		public Color GetPixel( int x, int y )
		{
			NeedPixels();

			var p = x + (y * Width);
			return pixels[p];
		}
	}

}

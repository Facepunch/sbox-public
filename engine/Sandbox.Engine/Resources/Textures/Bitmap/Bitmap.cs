using SkiaSharp;
using System.Runtime.InteropServices;

namespace Sandbox;

public sealed partial class Bitmap : IDisposable, IValid
{
	private SKBitmap _bitmap;
	private SKCanvas _canvas;

	public int Width => _bitmap.Width;
	public int Height => _bitmap.Height;
	public int BytesPerPixel => _bitmap.BytesPerPixel;
	public int ByteCount => _bitmap.ByteCount;
	public Rect Rect => new( 0, 0, Width, Height );


	/// <summary>
	/// The width and height of the bitmap
	/// </summary>
	public Vector2Int Size => new( Width, Height );


	public Vector2 Center => new Vector2( Width, Height ) * 0.5f;

	/// <summary>Whether the backing pixels use half-float RGBA storage.</summary>
	public bool IsFloatingPoint => GetColorType() == SKColorType.RgbaF16;

	public bool IsValid => _bitmap is not null && _canvas is not null;

	private const int MaxDimension = 16384;

	public Bitmap( int width, int height, bool floatingPoint = false )
	{
		if ( width <= 0 || height <= 0 )
			throw new ArgumentOutOfRangeException( "Dimensions must be positive." );

		if ( width > MaxDimension || height > MaxDimension )
			throw new ArgumentOutOfRangeException( $"Dimensions cannot exceed {MaxDimension}." );
		_ = checked(width * height * (floatingPoint ? 8 : 4));

		var colorType = floatingPoint ? SKColorType.RgbaF16 : SKColorType.Rgba8888;
		var info = new SKImageInfo( width, height, colorType, SKAlphaType.Unpremul );

		_bitmap = new SKBitmap( info );
		InitializeCanvas();
	}

	/// <summary>
	/// Used internally for resizing operations
	/// </summary>
	internal Bitmap( SKBitmap bitmap )
	{
		ArgumentNullException.ThrowIfNull( bitmap );
		_bitmap = bitmap;
		InitializeCanvas();
	}

	void InitializeCanvas()
	{
		try
		{
			_ = GetBuffer();
			_canvas = new SKCanvas( _bitmap );
		}
		catch
		{
			Dispose();
			throw;
		}
	}

	public void Dispose()
	{
		_canvas?.Dispose();
		_canvas = default;

		_bitmap?.Dispose();
		_bitmap = default;
	}

	/// <summary>
	/// Clears the bitmap to the specified color.
	/// </summary>
	/// <param name="color">The color to fill the bitmap with.</param>
	public void Clear( Color color )
	{
		_canvas.Clear( color.ToSkF() );
	}

	/// <summary>Imports pixels in this bitmap's format, converting premultiplied input during the copy.</summary>
	internal unsafe void SetPixelData( ReadOnlySpan<byte> pixels, bool premultiplied )
	{
		var destination = GetBuffer();
		if ( pixels.Length != destination.Length )
			throw new ArgumentException( "Pixel data must match the bitmap allocation.", nameof( pixels ) );
		if ( !premultiplied )
		{
			pixels.CopyTo( destination );
			return;
		}

		fixed ( byte* source = pixels )
		{
			using var pixmap = new SKPixmap( _bitmap.Info.WithAlphaType( SKAlphaType.Premul ), (IntPtr)source, _bitmap.RowBytes );
			if ( !pixmap.ReadPixels( _bitmap.Info, _bitmap.GetPixels(), _bitmap.RowBytes ) )
				throw new InvalidOperationException( "Could not import premultiplied bitmap pixels." );
		}
	}

	/// <summary>
	/// Retrieves the pixel data of the bitmap as an array of colors.
	/// </summary>
	public Color[] GetPixels()
	{
		var buffer = GetBuffer();
		if ( !IsFloatingPoint ) return _bitmap.Pixels.Select( p => p.FromSk() ).ToArray();

		var raw = MemoryMarshal.Cast<byte, Color.Rgba16>( buffer );
		var colors = new Color[raw.Length];
		for ( int i = 0; i < raw.Length; i++ ) colors[i] = raw[i].ToColor();
		return colors;
	}

	/// <summary>Retrieves the pixel data as half-float colors.</summary>
	public Color.Rgba16[] GetPixels16()
	{
		var buffer = GetBuffer();
		if ( IsFloatingPoint ) return MemoryMarshal.Cast<byte, Color.Rgba16>( buffer ).ToArray();
		return _bitmap.Pixels.Select( p => (Color.Rgba16)p.FromSk() ).ToArray();
	}

	/// <summary>Retrieves the pixel data as 32-bit colors.</summary>
	public Color32[] GetPixels32()
	{
		var buffer = GetBuffer();
		if ( !IsFloatingPoint ) return _bitmap.Pixels.Select( p => (Color32)p.FromSk() ).ToArray();

		var raw = MemoryMarshal.Cast<byte, Color.Rgba16>( buffer );
		var colors = new Color32[raw.Length];
		for ( int i = 0; i < raw.Length; i++ ) colors[i] = raw[i].ToColor();
		return colors;
	}

	public void SetPixels( Color[] colors )
	{
		var buffer = GetBuffer();
		if ( colors is null || colors.Length != checked(Width * Height) )
			throw new ArgumentException( "Colors array must match the size of the bitmap." );

		if ( IsFloatingPoint )
		{
			var raw = MemoryMarshal.Cast<byte, Color.Rgba16>( buffer );
			for ( int i = 0; i < raw.Length; i++ ) raw[i] = new Color.Rgba16( colors[i] );
		}
		else
		{
			var skColors = new SKColor[colors.Length];
			for ( int i = 0; i < colors.Length; i++ ) skColors[i] = colors[i].ToSk();
			_bitmap.Pixels = skColors;
		}
	}

	/// <summary>
	/// Retrieves the color of a specific pixel in the bitmap.
	/// </summary>
	/// <param name="x">The x-coordinate of the pixel.</param>
	/// <param name="y">The y-coordinate of the pixel.</param>
	/// <returns>The color of the pixel at the specified coordinates.</returns>
	public Color GetPixel( int x, int y )
	{
		AssertBounds( x, y, 1, 1 );

		var buffer = GetBuffer();
		if ( IsFloatingPoint ) return MemoryMarshal.Cast<byte, Color.Rgba16>( buffer )[checked(y * Width + x)].ToColor();
		return _bitmap.GetPixel( x, y ).FromSk();
	}

	/// <summary>
	/// Sets the color of a specific pixel in the bitmap.
	/// </summary>
	/// <param name="x">The x-coordinate of the pixel.</param>
	/// <param name="y">The y-coordinate of the pixel.</param>
	/// <param name="color">The color to set the pixel to.</param>
	public void SetPixel( int x, int y, Color color )
	{
		AssertBounds( x, y, 1, 1 );

		var buffer = GetBuffer();
		if ( IsFloatingPoint )
		{
			MemoryMarshal.Cast<byte, Color.Rgba16>( buffer )[checked(y * Width + x)] = new Color.Rgba16( color );
		}
		else
		{
			_bitmap.SetPixel( x, y, color.ToSk() );
		}
	}

	/// <summary>
	/// Low level, get a span of the bitmap data.
	/// </summary>
	internal Span<byte> GetBuffer()
	{
		var colorType = GetColorType();
		int bytesPerPixel = colorType == SKColorType.RgbaF16 ? 8 : 4;
		int rowBytes = checked(Width * bytesPerPixel);
		int requiredBytes = checked(rowBytes * Height);
		if ( Width <= 0 || Height <= 0 || _bitmap.BytesPerPixel != bytesPerPixel
			|| _bitmap.RowBytes != rowBytes || requiredBytes > _bitmap.ByteCount || _bitmap.GetPixels() == IntPtr.Zero )
			throw new InvalidOperationException( "Bitmap storage does not match its pixel format and dimensions." );

		var pixels = _bitmap.GetPixelSpan();
		if ( pixels.Length < requiredBytes )
			throw new InvalidOperationException( "Bitmap pixel allocation is smaller than its dimensions." );
		return pixels[..requiredBytes];
	}

	SKColorType GetColorType()
	{
		ObjectDisposedException.ThrowIf( _bitmap is null, this );
		return _bitmap.ColorType switch
		{
			SKColorType.Rgba8888 => SKColorType.Rgba8888,
			SKColorType.RgbaF16 => SKColorType.RgbaF16,
			_ => throw new InvalidOperationException( $"Unsupported bitmap pixel format: {_bitmap.ColorType}." )
		};
	}

	/// <summary>Gets the pointer after validating the backing pixel allocation.</summary>
	internal unsafe void* GetPointer()
	{
		_ = GetBuffer();
		return (void*)_bitmap.GetPixels();
	}

	/// <summary>
	/// Asserts that the specified region is within the bounds of the bitmap.
	/// Throws an exception if the bounds are out of range.
	/// </summary>
	/// <param name="x">The x-coordinate of the starting point.</param>
	/// <param name="y">The y-coordinate of the starting point.</param>
	/// <param name="width">The width of the region to check.</param>
	/// <param name="height">The height of the region to check.</param>
	private void AssertBounds( int x, int y, int width, int height )
	{
		ObjectDisposedException.ThrowIf( !IsValid, this );
		if ( x < 0 || y < 0 || width < 0 || height < 0 || width > Width || height > Height
			|| x > Width - width || y > Height - height )
		{
			throw new ArgumentOutOfRangeException( nameof( x ), "Specified region is out of bounds." );
		}
	}

	/// <summary>
	/// Copy the bitmap to a new one without any changes.
	/// </summary>
	public Bitmap Clone()
	{
		var newBitmap = _bitmap.Copy();
		return new Bitmap( newBitmap );
	}


	/// <summary>
	/// Returns true if this bitmap is completely opaque (no alpha)
	/// This does a pixel by pixel search, so it's not the fastest.
	/// </summary>
	public bool IsOpaque()
	{
		var buffer = GetBuffer();
		if ( _bitmap.AlphaType == SKAlphaType.Opaque ) return true;

		if ( IsFloatingPoint )
		{
			foreach ( var pixel in MemoryMarshal.Cast<byte, Color.Rgba16>( buffer ) )
			{
				if ( (float)pixel.a < 1.0f ) return false;
			}
		}
		else
		{
			for ( int i = 3; i < buffer.Length; i += 4 )
			{
				if ( buffer[i] != 255 ) return false;
			}
		}
		return true;
	}
}

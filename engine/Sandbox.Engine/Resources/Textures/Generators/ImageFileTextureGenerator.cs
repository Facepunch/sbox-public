using Sandbox.UI;
using System.Text.Json.Serialization;
using System.Threading;

namespace Sandbox.Resources;

/// <summary>
/// Load images from disk and convert them to textures
/// </summary>
[Order( -100 )]
[Title( "Image File" )]
[Icon( "image" )]
[ClassName( "imagefile" )]
public class ImageFileGenerator : TextureGenerator
{
	/// <summary>
	/// The path to the image file, relative to any other assets in the project.
	/// </summary>
	[Header( "Image" )]
	[KeyProperty, TextureImagePath]
	public string FilePath { get; set; }

	/// <summary>
	/// The maximum size of the image in pixels. If the imported image is larger than this (after cropping), it will be downscaled to fit.
	/// </summary>
	public int MaxSize { get; set; } = 4096;

	/// <summary>
	/// When enabled, the output texture will be a normal map generated from the heightmap of the image.
	/// </summary>
	[Header( "Normal Map" ), Title( "Height to Normal" )]
	public bool ConvertHeightToNormals { get; set; }

	/// <summary>
	/// The scale of the normal map when using <see cref="ConvertHeightToNormals"/>. If negative, the normal map will be inverted.
	/// </summary>
	[ShowIf( "ConvertHeightToNormals", true )]
	public float NormalScale { get; set; } = 1;

	/// <summary>
	/// How much to rotate the image by, in degrees. This is applied after cropping and padding.
	/// </summary>
	[Header( "Adjust" ), Range( 0, 360 )]
	public float Rotate { get; set; } = 0.0f;

	/// <summary>
	/// Whether or not to flip the image vertically. This is done after everything else has been applied.
	/// </summary>
	public bool FlipVertical { get; set; } = false;

	/// <summary>
	/// Whether or not to flip the image horizontally. This is done after everything else has been applied.
	/// </summary>
	public bool FlipHorizontal { get; set; } = false;

	/// <summary>
	/// How many pixels from each edge to crop from the image. If negative values are used, the image will be expanded instead of cropped.
	/// </summary>
	public Margin Cropping { get; set; }

	/// <summary>
	/// How many pixels of padding from each edge. After the image has been cropped,
	/// padding is added without affecting the size of the image (scaling the original image down to fit padded margins).
	/// </summary>
	public Margin Padding { get; set; }

	/// <summary>
	/// Whether or not to invert the colors of the image.
	/// </summary>
	[Header( "Effects" )]
	public bool InvertColor { get; set; } = false;

	/// <summary>
	/// The color the image should be tinted. This effectively multiplies the color of each pixel by this color (including alpha).
	/// </summary>
	public Color Tint { get; set; } = Color.White;

	/// <summary>
	/// The intensity of the blur effect. If 0, no blur is applied.
	/// </summary>
	[Range( 0, 32 )]
	public float Blur { get; set; } = 0.0f;

	/// <summary>
	/// The intensity of the sharpen effect. If 0, no sharpening is applied.
	/// </summary>
	[Range( 0, 10 )]
	public float Sharpen { get; set; } = 0.0f;

	/// <summary>
	/// The brightness of the image.
	/// </summary>
	[Range( 0, 2 )]
	public float Brightness { get; set; } = 1.0f;

	/// <summary>
	/// The contrast of the image.
	/// </summary>
	[Range( 0, 2 )]
	public float Contrast { get; set; } = 1.0f;

	/// <summary>
	/// The saturation of the image.
	/// </summary>
	[Range( 0, 2 )]
	public float Saturation { get; set; } = 1.0f;

	/// <summary>
	/// How much to adjust the hue of the image, in degrees. If 0, no hue adjustment is applied.
	/// </summary>
	[Range( 0, 360 )]
	public float Hue { get; set; } = 0.0f;

	/// <summary>
	/// When enabled, every pixel in the image will be re-colored to the <see cref="TargetColor"/> (interpolated by the alpha).
	/// </summary>
	[ToggleGroup( "Colorize" )]
	public bool Colorize { get; set; }

	/// <summary>
	/// When <see cref="Colorize"/> is enabled, this is the target color that every pixel in the image will be re-colored to.
	/// </summary>
	[Group( "Colorize" )]
	public Color TargetColor { get; set; } = Color.White;

	[Hide]
	public override bool CacheToDisk => true;

	// Don't continually load from disk, cache that shit
	[JsonIgnore, Hide]
	int _loadedCacheHash;

	[JsonIgnore, Hide]
	Bitmap _cacheLoaded;

	[JsonIgnore, Hide]
	int loadCount = 0;

	async Task<Bitmap> LoadCached()
	{
		if ( string.IsNullOrWhiteSpace( FilePath ) ) return default;
		var path = FilePath.NormalizeFilename();

		if ( string.IsNullOrEmpty( FilePath ) ) return default;
		if ( !EngineFileSystem.Mounted.FileExists( path ) )
		{
			Log.Warning( $"ImageFileGenerator could not find file: {path}" );
			return default;
		}

		var size = EngineFileSystem.Mounted.FileSize( path );

		var hash = HashCode.Combine( size, FilePath, Cropping );
		if ( hash == _loadedCacheHash )
			return _cacheLoaded?.Clone();

		var bytes = await EngineFileSystem.Mounted.ReadAllBytesAsync( path );

		// Create the bitmap and crop it if needed
		var bitmap = Bitmap.CreateFromBytes( bytes );
		var newRect = new Rect( Cropping.Left, Cropping.Top, bitmap.Width - Cropping.Right - Cropping.Left, bitmap.Height - Cropping.Bottom - Cropping.Top );
		if ( newRect.Width > 0 && newRect.Height > 0 )
		{
			bitmap = bitmap.Crop( newRect.SnapToGrid() );
		}

		if ( loadCount > 3 )
		{
			_loadedCacheHash = hash;
			_cacheLoaded = bitmap?.Clone();
		}

		loadCount++;

		return bitmap;
	}

	protected override async ValueTask<Texture> CreateTexture( Options options, CancellationToken ct )
	{
		if ( string.IsNullOrWhiteSpace( FilePath ) )
			return null;

		//
		// A regular texture!
		//

		// Trim compiled names!
		if ( FilePath.EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) )
			FilePath = FilePath[..^2];

		if ( FilePath.EndsWith( ".vtex", StringComparison.OrdinalIgnoreCase ) )
		{
			await MainThread.Wait();
			return Texture.Load( FilePath );
		}

		var bitmap = await LoadCached();
		if ( bitmap is null ) return Texture.Invalid;

		//
		// Tell the compiler we're using this file, so to add it as a compile reference
		//
		if ( options.Compiler is not null )
		{
			options.Compiler.Context.AddCompileReference( FilePath );
		}

		if ( !Padding.IsNearlyZero() )
		{
			bitmap.InsertPadding( Padding );
		}


#pragma warning disable CA2000 // Dispose objects before losing scope
		// Disposal is handled after creating the new bitmap to keep the source alive during processing
		if ( Rotate != 0 )
		{
			var rotated = bitmap.Rotate( Rotate );
			bitmap.Dispose();
			bitmap = rotated;
		}

		if ( bitmap.Width > MaxSize || bitmap.Height > MaxSize )
		{
			float scale = Math.Min( (float)MaxSize / bitmap.Width, (float)MaxSize / bitmap.Height );
			int newWidth = (int)(bitmap.Width * scale);
			int newHeight = (int)(bitmap.Height * scale);

			newWidth = newWidth.Clamp( 1, 1024 * 8 );
			newHeight = newHeight.Clamp( 1, 1024 * 8 );

			var resized = bitmap.Resize( newWidth, newHeight );
			bitmap.Dispose();
			bitmap = resized;
		}

		if ( Tint != Color.White || Tint.a != 1 )
		{
			bitmap.Tint( Tint );
		}
		if ( Sharpen > 0.0f ) bitmap.Sharpen( Sharpen, true );
		if ( Blur > 0.0f ) bitmap.Blur( Blur, true );
		if ( Brightness != 1 || Contrast != 1 || Saturation != 1 || Hue != 0 ) bitmap.Adjust( Brightness, Contrast, Saturation, Hue );
		if ( InvertColor ) bitmap.InvertColor();

		if ( Colorize )
		{
			bitmap.Colorize( TargetColor );
		}

		//	if ( Posterize != 0 ) bitmap.Posterize( Posterize );

		if ( FlipHorizontal )
		{
			var flipped = bitmap.FlipHorizontal();
			bitmap.Dispose();
			bitmap = flipped;
		}
		if ( FlipVertical )
		{
			var flipped = bitmap.FlipVertical();
			bitmap.Dispose();
			bitmap = flipped;
		}

		if ( ConvertHeightToNormals )
		{
			var normalMap = bitmap.HeightmapToNormalMap( NormalScale );
			bitmap.Dispose();
			bitmap = normalMap;
		}
#pragma warning restore CA2000 // Dispose objects before losing scope

		var tex = bitmap.ToTexture();
		bitmap?.Dispose();

		return tex;
	}

	public override EmbeddedResource? CreateEmbeddedResource()
	{
		// if we're a vtex, don't create an embedded resource
		if ( FilePath.EndsWith( ".vtex", StringComparison.OrdinalIgnoreCase ) )
			return null;

		return base.CreateEmbeddedResource();
	}
}

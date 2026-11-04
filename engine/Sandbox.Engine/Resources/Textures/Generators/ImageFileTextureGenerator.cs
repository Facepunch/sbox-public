using Sandbox.UI;
using System.Text.Json.Nodes;
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
	/// Store the image losslessly, without block compression or mips. For pixel art and UI that compression artifacts would spoil;
	/// otherwise the texture is compressed, which is much smaller on disk and in memory.
	/// </summary>
	public bool Lossless { get; set; }

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

	[Hide]
	public override ImageFormat? FormatOverride => Lossless && !ConvertHeightToNormals ? ImageFormat.RGBA8888 : null;

	public override void Deserialize( JsonObject obj )
	{
		// Saved before lossless was opt-in, when every image file was stored losslessly
		Lossless = obj is not null && !obj.ContainsKey( nameof( Lossless ) );
		base.Deserialize( obj );
	}

	// Don't continually load from disk, cache that shit
	[JsonIgnore, Hide]
	int _loadedCacheHash;

	[JsonIgnore, Hide]
	Bitmap _cacheLoaded;

	[JsonIgnore, Hide]
	int loadCount = 0;

	[JsonIgnore, Hide]
	readonly Lock _cacheLock = new();

	/// <summary>
	/// Read through the caller's filesystem context, then decode and clone cached pixels on workers.
	/// </summary>
	async Task<Bitmap> LoadCached( string filePath, Margin cropping, CancellationToken ct )
	{
		if ( string.IsNullOrWhiteSpace( filePath ) ) return default;
		var path = filePath.NormalizeFilename();

		if ( string.IsNullOrEmpty( filePath ) ) return default;
		if ( !EngineFileSystem.Mounted.FileExists( path ) )
		{
			Log.Warning( $"ImageFileGenerator could not find file: {path}" );
			return default;
		}

		var size = EngineFileSystem.Mounted.FileSize( path );

		var hash = HashCode.Combine( size, filePath, cropping );
		var cached = await Task.Run( () =>
		{
			lock ( _cacheLock )
			{
				return hash == _loadedCacheHash ? _cacheLoaded?.Clone() : null;
			}
		}, ct );

		if ( cached is not null )
			return cached;

		var bytes = await EngineFileSystem.Mounted.ReadAllBytesAsync( path );

		return await Task.Run( () =>
		{
			// Create the bitmap and crop it if needed
			var bitmap = Bitmap.CreateFromBytes( bytes );
			var newRect = new Rect( cropping.Left, cropping.Top, bitmap.Width - cropping.Right - cropping.Left, bitmap.Height - cropping.Bottom - cropping.Top );
			if ( newRect.Width > 0 && newRect.Height > 0 )
			{
				bitmap = bitmap.Crop( newRect.SnapToGrid() );
			}

			// Cache operations only hold this lock on workers, never across an await.
			lock ( _cacheLock )
			{
				if ( loadCount > 3 )
				{
					_loadedCacheHash = hash;
					_cacheLoaded = bitmap?.Clone();
				}

				loadCount++;
			}

			return bitmap;
		}, ct );
	}

	internal override IEnumerable<string> CompiledReads
	{
		get
		{
			var path = FilePath?.Trim();
			if ( path is not null && path.EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) ) path = path[..^2];
			if ( path is not null && path.EndsWith( ".vtex", StringComparison.OrdinalIgnoreCase ) ) yield return path;
		}
	}

	protected override async ValueTask<Texture> CreateTexture( Options options, CancellationToken ct )
	{
		var filePath = TrimCompiledName();
		if ( string.IsNullOrWhiteSpace( filePath ) )
			return null;

		if ( filePath.EndsWith( ".vtex", StringComparison.OrdinalIgnoreCase ) )
		{
			await MainThread.Wait();
			return Texture.Load( filePath );
		}

		using var bitmap = await LoadImage( filePath, options, ct );
		return bitmap?.ToTexture() ?? Texture.Invalid;
	}

	/// <summary>
	/// The processed image file. Missing, it's the 1x1 magenta of <see cref="Texture.Invalid"/>. A compiled texture has no
	/// image here: its pixels are read back from the texture, which takes a render device.
	/// </summary>
	protected override async ValueTask<Bitmap> CreateBitmap( Options options, CancellationToken ct )
	{
		var filePath = TrimCompiledName();
		if ( string.IsNullOrWhiteSpace( filePath ) || filePath.EndsWith( ".vtex", StringComparison.OrdinalIgnoreCase ) )
			return null;

		var bitmap = await LoadImage( filePath, options, ct );
		if ( bitmap is not null ) return bitmap;

		var missing = new Bitmap( 1, 1 );
		missing.Clear( new Color( 1, 0, 1 ) );
		return missing;
	}

	/// <summary>
	/// The path, without a compiled file's "_c", which is trimmed from the generator too.
	/// </summary>
	string TrimCompiledName()
	{
		var filePath = FilePath;
		if ( filePath is not null && filePath.EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) )
			FilePath = filePath = filePath[..^2];

		return filePath;
	}

	/// <summary>
	/// The image file with every adjustment applied, or null if it isn't there.
	/// </summary>
	async Task<Bitmap> LoadImage( string filePath, Options options, CancellationToken ct )
	{
		// Keep an in-flight generation independent of edits to the generator.
		var settings = (Cropping, Padding, Rotate, MaxSize, Tint, Sharpen, Blur,
			Brightness, Contrast, Saturation, Hue, InvertColor, Colorize, TargetColor,
			FlipHorizontal, FlipVertical, ConvertHeightToNormals, NormalScale);

		// Before reading: an image that isn't there yet rebuilds this once it is
		options.Compiler?.Context.AddCompileReference( filePath, optional: true );

		var bitmap = await LoadCached( filePath, settings.Cropping, ct );
		if ( bitmap is null ) return null;

		try
		{
			ct.ThrowIfCancellationRequested();

			await Task.Run( () =>
			{
				ct.ThrowIfCancellationRequested();

				if ( !settings.Padding.IsNearlyZero() )
				{
					bitmap.InsertPadding( settings.Padding );
				}
#pragma warning disable CA2000 // Dispose objects before losing scope
				// Disposal is handled after creating the new bitmap to keep the source alive during processing
				if ( settings.Rotate != 0 )
				{
					var rotated = bitmap.Rotate( settings.Rotate );
					bitmap.Dispose();
					bitmap = rotated;
				}

				if ( bitmap.Width > settings.MaxSize || bitmap.Height > settings.MaxSize )
				{
					float scale = Math.Min( (float)settings.MaxSize / bitmap.Width, (float)settings.MaxSize / bitmap.Height );
					int newWidth = (int)(bitmap.Width * scale);
					int newHeight = (int)(bitmap.Height * scale);

					newWidth = newWidth.Clamp( 1, 1024 * 8 );
					newHeight = newHeight.Clamp( 1, 1024 * 8 );

					var resized = bitmap.Resize( newWidth, newHeight );
					bitmap.Dispose();
					bitmap = resized;
				}

				if ( settings.Tint != Color.White || settings.Tint.a != 1 )
				{
					bitmap.Tint( settings.Tint );
				}
				if ( settings.Sharpen > 0.0f ) bitmap.Sharpen( settings.Sharpen, true );
				if ( settings.Blur > 0.0f ) bitmap.Blur( settings.Blur, true );
				if ( settings.Brightness != 1 || settings.Contrast != 1 || settings.Saturation != 1 || settings.Hue != 0 ) bitmap.Adjust( settings.Brightness, settings.Contrast, settings.Saturation, settings.Hue );
				if ( settings.InvertColor ) bitmap.InvertColor();

				if ( settings.Colorize )
				{
					bitmap.Colorize( settings.TargetColor );
				}

				//	if ( Posterize != 0 ) bitmap.Posterize( Posterize );

				if ( settings.FlipHorizontal )
				{
					var flipped = bitmap.FlipHorizontal();
					bitmap.Dispose();
					bitmap = flipped;
				}
				if ( settings.FlipVertical )
				{
					var flipped = bitmap.FlipVertical();
					bitmap.Dispose();
					bitmap = flipped;
				}

				if ( settings.ConvertHeightToNormals )
				{
					var normalMap = bitmap.HeightmapToNormalMap( settings.NormalScale );
					bitmap.Dispose();
					bitmap = normalMap;
				}
#pragma warning restore CA2000 // Dispose objects before losing scope

			}, ct );

			ct.ThrowIfCancellationRequested();
			return bitmap;
		}
		catch
		{
			bitmap.Dispose();
			throw;
		}
	}

	public override EmbeddedResource? CreateEmbeddedResource()
	{
		// if we're a vtex, don't create an embedded resource
		if ( FilePath.EndsWith( ".vtex", StringComparison.OrdinalIgnoreCase ) )
			return null;

		return base.CreateEmbeddedResource();
	}
}

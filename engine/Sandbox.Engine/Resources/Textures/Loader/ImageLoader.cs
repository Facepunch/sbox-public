using NativeEngine;
using SkiaSharp;

namespace Sandbox.TextureLoader;

internal static class Image
{
	internal static readonly HashSet<string> Extensions = new( System.StringComparer.OrdinalIgnoreCase )
	{
		".png",
		".jpg",
		".gif",
		".webp",
		".tga",
		".psd",
		".tif",
		".ies",
	};

	internal static bool IsAppropriate( string url )
	{
		var split = url.Split( '?' )[0];
		var extension = System.IO.Path.GetExtension( split );

		return Extensions.Contains( extension );
	}

	public static Texture Load( System.IO.Stream stream, string debugName )
	{
		System.Runtime.CompilerServices.RuntimeHelpers.EnsureSufficientExecutionStack();

		var data = SKData.Create( stream );
		var codec = SKCodec.Create( data );

		if ( codec == null )
		{
			Log.Warning( $"Error loading image: {debugName}" );
			return default;
		}

		var frameCount = codec.FrameCount;
		if ( frameCount > 1 )
		{
			var animation = new Texture.Animation( codec );
			var texture = CreateTexture( animation.Bitmap, debugName );
			animation.Texture = new System.WeakReference<Texture>( texture );
			Texture.Animations.Add( animation );

			return texture;
		}
		else
		{
			using var image = SKImage.FromEncodedData( data );
			using var bitmap = SKBitmap.FromImage( image );

			var texture = CreateTexture( bitmap, debugName );

			codec.Dispose();
			data.Dispose();

			return texture;
		}
	}

	internal static unsafe Texture Load( BaseFileSystem filesystem, string filename )
	{
		if ( !filesystem.FileExists( filename ) )
			return null;

		var bm = FloatBitMap_t.Create();

		try
		{
			if ( !bm.LoadFromFile( filename, FBMGammaType_t.FBM_GAMMA_LINEAR ) )
			{
				return null;
			}

			int width = bm.Width();
			int height = bm.Height();
			int numMips = (int)Math.Log2( Math.Min( width, height ) ) + 1;
			var format = ImageFormat.RGBA8888;
			var dataSize = ImageLoader.GetMemRequired( width, height, 1, 1, format );
			var data = new byte[dataSize];

			fixed ( byte* pData = data )
			{
				uint FLOAT_BITMAP_PREFER_RUNTIME_FRIENDLY_DXT_ENCODER = 1;

				if ( !bm.WriteToBuffer( (IntPtr)pData, data.Length, format, false, false, FLOAT_BITMAP_PREFER_RUNTIME_FRIENDLY_DXT_ENCODER ) )
				{
					return default;
				}
			}

			var texture = Texture.Create( width, height, format )
				.WithName( filename )
				.WithData( data, dataSize )
				.WithMips( numMips )
				.Finish();

			return texture;
		}
		finally
		{
			bm.Delete();
		}
	}

	private static Texture CreateTexture( SKBitmap bitmap, string name )
	{
		var format = bitmap.ColorType switch
		{
			SKColorType.Rgba8888 => ImageFormat.RGBA8888,
			SKColorType.Bgra8888 => ImageFormat.BGRA8888,
			SKColorType.Gray8 => ImageFormat.I8,
			_ => throw new System.Exception( $"bitmap.ColorType is {bitmap.ColorType} - unsupported" ),
		};

		int numMips = (int)Math.Log2( Math.Min( bitmap.Width, bitmap.Height ) ) + 1;

		var texture = Texture.Create( bitmap.Width, bitmap.Height, format )
			.WithName( name )
			.WithData( bitmap.GetPixels(), bitmap.ByteCount )
			.WithMips( numMips )
			.Finish();

		return texture;
	}

	public static Texture Load( BaseFileSystem filesystem, string filename, bool warnOnMissing = true )
	{
		filename = filename.Normalize();

		try
		{
			Texture tex = default;

			var extension = System.IO.Path.GetExtension( filename );
			if ( extension == ".tga" || extension == ".psd" || extension == ".tif" )
			{
				tex = Load( filesystem, filename );
			}
			else if ( extension == ".ies" )
			{
				var bytes = filesystem.ReadAllBytes( filename ).ToArray();
				if ( Bitmap.CreateFromIesBytes( bytes ) is { } ies )
				{
					tex = ies.ToTexture();
				}
			}
			else
			{
				using var stream = filesystem.OpenRead( filename );
				tex = Load( stream, filename );
			}

			tex?.SetIdFromResourcePath( filename );
			return tex;
		}
		catch ( System.IO.FileNotFoundException e )
		{
			if ( warnOnMissing )
			{
				Log.Warning( $"Image.Load: {filename} not found ({e.Message})" );
			}
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Image.Load: {filename}: {e.Message}" );
		}

		return null;
	}
}

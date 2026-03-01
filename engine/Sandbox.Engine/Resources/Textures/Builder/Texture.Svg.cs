using SkiaSharp;
using Svg.Skia;

namespace Sandbox;

public partial class Texture
{
	/// <summary>
	/// Create a texture from an SVG source
	/// </summary>
	public static Texture CreateFromSvgSource( string svgContents, int? width, int? height, Color? color )
	{
		var hash = HashCode.Combine( svgContents, width, height, color );
		var cacheName = $"svg.{hash}";

		if ( Game.Resources.Get<Texture>( cacheName ) is Texture existing )
			return existing;

		try
		{
			var svgDocument = Svg.SvgDocument.FromSvg<Svg.SvgDocument>( svgContents.Trim() );

			int nativeWidth = svgDocument.Width.Value.FloorToInt();
			int nativeHeight = svgDocument.Height.Value.FloorToInt();

			var resolvedWidth = width ?? nativeWidth;
			var resolvedHeight = height ?? nativeHeight;

			resolvedWidth = Math.Min( resolvedWidth, 4096 );
			resolvedHeight = Math.Min( resolvedHeight, 4096 );

			if ( width.HasValue && !height.HasValue )
			{
				resolvedHeight = (width.Value * nativeHeight) / nativeWidth;
			}

			if ( height.HasValue && !width.HasValue )
			{
				resolvedWidth = (height.Value * nativeWidth) / nativeHeight;
			}

			using ( var svg = new SKSvg() )
			using ( var bitmap = new SKBitmap( resolvedWidth, resolvedHeight, SKColorType.Bgra8888, SKAlphaType.Unpremul ) )
			using ( var canvas = new SKCanvas( bitmap ) )
			{
				svg.FromSvgDocument( svgDocument );

				using var paint = new SKPaint();

				if ( color != null )
				{
					paint.ColorFilter = SKColorFilter.CreateBlendMode( color.Value.ToSk(), SKBlendMode.SrcIn );
				}

				var bounds = svg.Picture.CullRect;
				var scaleRatio = Math.Min( resolvedWidth / bounds.Width, resolvedHeight / bounds.Height );
				var midX = bounds.Left + bounds.Width / 2f;
				var midY = bounds.Top + bounds.Height / 2f;

				canvas.Translate( resolvedWidth / 2, resolvedHeight / 2 );
				canvas.Scale( scaleRatio );
				canvas.Translate( -midX, -midY );
				canvas.DrawPicture( svg.Picture, paint );

				var tx = Texture.Create( resolvedWidth, resolvedHeight, ImageFormat.BGRA8888 )
							.WithName( $"skiasvg" )
							.WithData( bitmap.GetPixels(), resolvedWidth * resolvedHeight * bitmap.BytesPerPixel )
							.WithMips()
							.WithDynamicUsage()
				.Finish();

				if ( tx.IsValid() )
				{
					tx.RegisterWeakResourceId( cacheName );
				}

				return tx;
			}
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Error when loading svg: {e.Message}" );
			return Texture.Invalid;
		}
	}
}

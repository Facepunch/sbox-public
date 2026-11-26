using SkiaSharp;
using Svg.Skia;

namespace Sandbox;

public partial class Bitmap
{
	/// <summary>
	/// Create a bitmap from an SVG, with optional size
	/// </summary>
	public static Bitmap CreateFromSvgString( string svgContents, int? width, int? height, Vector2? scale = default, Vector2? offset = default, float? rotation = default )
	{
		var svgDocument = SvgSanitizer.Parse( svgContents );

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

		var bitmap = new SKBitmap( resolvedWidth, resolvedHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul );

		using ( var svg = new SKSvg() )
		using ( var canvas = new SKCanvas( bitmap ) )
		{
			svg.FromSvgDocument( svgDocument );

			using var paint = new SKPaint();

			var bounds = svg.Picture.CullRect;
			var scaleRatio = Math.Min( resolvedWidth / bounds.Width, resolvedHeight / bounds.Height );
			var midX = bounds.Left + bounds.Width / 2f;
			var midY = bounds.Top + bounds.Height / 2f;

			if ( offset.HasValue )
			{
				canvas.Translate( offset.Value.x, offset.Value.y );
			}

			canvas.Translate( resolvedWidth / 2, resolvedHeight / 2 );
			canvas.Scale( scaleRatio );

			if ( scale.HasValue )
			{
				canvas.Scale( scale.Value.x, scale.Value.y );
			}

			if ( rotation.HasValue )
			{
				canvas.RotateDegrees( rotation.Value );
			}

			canvas.Translate( -midX, -midY );

			canvas.DrawPicture( svg.Picture, paint );
		}

		return new Bitmap( bitmap );
	}
}

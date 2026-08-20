
using SkiaSharp;
using System;
using System.Linq;

namespace Topten.RichTextKit
{
	/// <summary>
	/// Desribes a gradient to apply to the text
	/// </summary>
	public class TextGradient
	{

		public SKPoint Center { get; set; }
		public RadialSizeMode SizeMode { get; set; }
		public GradientType GradientType { get; set; }
		public SKColorF[] Colors { get; set; }
		public float[] Positions { get; set; }
		public float Angle { get; set; }

		public static TextGradient Linear( SKColorF[] colors, float[] positions, float angle )
		{
			return new TextGradient()
			{
				GradientType = GradientType.Linear,
				Colors = colors,
				Positions = positions,
				Angle = angle
			};
		}

		public static TextGradient Radial( SKColorF[] colors, float[] positions, float angle, SKPoint center, RadialSizeMode sizeMode )
		{
			return new TextGradient()
			{
				GradientType = GradientType.Radial,
				Colors = colors,
				Positions = positions,
				Angle = angle,
				Center = center,
				SizeMode = sizeMode
			};
		}

		internal SKShader CreateShader( float width, float height, float offsetx = 0 )
		{
			var rotation = SKMatrix.CreateRotationDegrees( 180 + Angle, width * .5f, height * .5f );
			var startPoint = new SKPoint( width * .5f, 0 );
			var endPoint = new SKPoint( width * .5f, height );

			startPoint = rotation.MapPoint( startPoint );
			endPoint = rotation.MapPoint( endPoint );

			var localMatrix = SKMatrix.CreateTranslation( offsetx, 0 );

			if ( GradientType == GradientType.Linear )
			{
				var sx = Math.Abs( endPoint.X - startPoint.X );
				var sy = Math.Abs( endPoint.Y - startPoint.Y );
				if ( sx == 0 ) sx = 1;
				if ( sy == 0 ) sy = 1;

				sx = width / sx;
				sy = height / sy;

				var localScale = SKMatrix.CreateScale( sx, sy, width * .5f, height * .5f );
				localMatrix = SKMatrix.Concat( localMatrix, localScale );

				// SKColorF stops with a null colour space stay unclamped, so HDR (>1) colours survive onto an F16 surface
				return SKShader.CreateLinearGradient( startPoint, endPoint, Colors, null, Positions, SKShaderTileMode.Clamp, localMatrix );
			}

			if ( GradientType == GradientType.Radial )
			{
				var center = new SKPoint( width * Center.X, height * Center.Y );
				var radius = Math.Max( width, height );
				var scaleY = 1.0f;
				var scaleX = 1.0f;

				var top = center.Y;
				var bottom = height - center.Y;
				var left = center.X;
				var right = width - center.X;

				var tl = center;
				var tr = new SKPoint( width, 0 ) - center;
				var br = new SKPoint( width, height ) - center;
				var bl = new SKPoint( 0, height ) - center;

				switch ( SizeMode )
				{
					case RadialSizeMode.Circle:
					case RadialSizeMode.FarthestSide:
						scaleX = Math.Max( left, right ) / width;
						scaleY = Math.Max( top, bottom ) / height;
						break;
					case RadialSizeMode.FarthestCorner:
						var furthestCorner = new SKPoint[] { tl, tr, br, bl }.OrderByDescending( x => x.Length ).First();
						scaleX = furthestCorner.X / (width * .71f);
						scaleY = furthestCorner.Y / (height * .71f);
						break;
					case RadialSizeMode.ClosestSide:
						scaleX = Math.Min( left, right ) / width;
						scaleY = Math.Min( top, bottom ) / height;
						break;
					case RadialSizeMode.ClosestCorner:
						var closestCorner = new SKPoint[] { tl, tr, br, bl }.OrderBy( x => x.Length ).First();
						scaleX = closestCorner.X / (width * .71f);
						scaleY = closestCorner.Y / (height * .71f);
						break;
				}

				if ( SizeMode != RadialSizeMode.Circle )
				{
					scaleX *= width / radius;
					scaleY *= height / radius;
				}

				localMatrix = SKMatrix.Concat( localMatrix, SKMatrix.CreateScale( scaleX, scaleY, center.X, center.Y ) );

				return SKShader.CreateRadialGradient( center, radius, Colors, null, Positions, SKShaderTileMode.Clamp, localMatrix );
			}

			return null;
		}

	}

	public enum GradientType
	{
		Linear = 0,
		Radial = 1
	}

	public enum RadialSizeMode
	{
		FarthestSide = 0,
		FarthestCorner = 1,
		ClosestSide = 2,
		ClosestCorner = 3,
		Circle = 4
	}

}

using Sandbox.UI;
using SkiaSharp;

namespace Sandbox;

public static partial class TextRendering
{
	/// <summary>
	/// We'll expose this at some point, but will probably be as Sandbox.TextBlock - and then need to think about ownership and caching
	/// </summary>
	internal class TextBlock : IDisposable
	{
		public Texture Texture;

		public TextFlag Flags;
		public Vector2 Clip;
		public bool IsEmpty;
		public Rendering.FilterMode FilterMode;
		internal int CacheKey;

		public RealTimeSince TimeSinceUsed;

		Scope _scope;
		Margin _effectMargin = default;

		public TextBlock( Scope scope, Vector2 clip, TextFlag flag )
		{
			Assert.False( Application.IsHeadless );

			Flags = flag;
			Clip = clip;

			Initialize( scope );
		}

		public TextBlock()
		{
		}

		internal void Initialize( Scope scope )
		{
			_scope = scope;
			IsEmpty = string.IsNullOrEmpty( _scope.Text );
			FilterMode = scope.FilterMode;
			TimeSinceUsed = 0;
			_effectMargin = default;

			if ( scope.Outline.Enabled && scope.Outline.Size > 0 )
			{
				_effectMargin.Left = MathF.Max( _effectMargin.Left, scope.Outline.Size ).CeilToInt();
				_effectMargin.Right = MathF.Max( _effectMargin.Left, scope.Outline.Size ).CeilToInt();
				_effectMargin.Top = MathF.Max( _effectMargin.Left, scope.Outline.Size ).CeilToInt();
				_effectMargin.Bottom = MathF.Max( _effectMargin.Left, scope.Outline.Size ).CeilToInt();
			}

			if ( scope.OutlineUnder.Enabled && scope.OutlineUnder.Size > 0 )
			{
				_effectMargin.Left = MathF.Max( _effectMargin.Left, scope.OutlineUnder.Size ).CeilToInt();
				_effectMargin.Right = MathF.Max( _effectMargin.Left, scope.OutlineUnder.Size ).CeilToInt();
				_effectMargin.Top = MathF.Max( _effectMargin.Left, scope.OutlineUnder.Size ).CeilToInt();
				_effectMargin.Bottom = MathF.Max( _effectMargin.Left, scope.OutlineUnder.Size ).CeilToInt();
			}

			if ( scope.Shadow.Enabled )
			{
				_effectMargin.Left = MathF.Max( _effectMargin.Left, scope.Shadow.Size + -scope.Shadow.Offset.x ).CeilToInt();
				_effectMargin.Right = MathF.Max( _effectMargin.Right, scope.Shadow.Size + scope.Shadow.Offset.x ).CeilToInt();
				_effectMargin.Top = MathF.Max( _effectMargin.Top, scope.Shadow.Size + -scope.Shadow.Offset.y ).CeilToInt();
				_effectMargin.Bottom = MathF.Max( _effectMargin.Bottom, scope.Shadow.Size + scope.Shadow.Offset.y ).CeilToInt();
			}

			if ( scope.ShadowUnder.Enabled )
			{
				_effectMargin.Left = MathF.Max( _effectMargin.Left, scope.ShadowUnder.Size + -scope.ShadowUnder.Offset.x ).CeilToInt();
				_effectMargin.Right = MathF.Max( _effectMargin.Right, scope.ShadowUnder.Size + scope.ShadowUnder.Offset.x ).CeilToInt();
				_effectMargin.Top = MathF.Max( _effectMargin.Top, scope.ShadowUnder.Size + -scope.ShadowUnder.Offset.y ).CeilToInt();
				_effectMargin.Bottom = MathF.Max( _effectMargin.Bottom, scope.ShadowUnder.Size + scope.ShadowUnder.Offset.y ).CeilToInt();
			}

			// don't let shit get crazy
			_effectMargin.Left = MathF.Min( _effectMargin.Left, 512 );
			_effectMargin.Right = MathF.Min( _effectMargin.Right, 512 );
			_effectMargin.Top = MathF.Min( _effectMargin.Top, 512 );
			_effectMargin.Bottom = MathF.Min( _effectMargin.Bottom, 512 );
		}

		public virtual void Dispose()
		{
			Texture?.Dispose();
			Texture = null;
		}

		Topten.RichTextKit.TextAlignment GetAlignment()
		{
			if ( Flags.Contains( TextFlag.Left ) ) return Topten.RichTextKit.TextAlignment.Left;
			if ( Flags.Contains( TextFlag.CenterHorizontally ) ) return Topten.RichTextKit.TextAlignment.Center;
			if ( Flags.Contains( TextFlag.Right ) ) return Topten.RichTextKit.TextAlignment.Right;

			return Topten.RichTextKit.TextAlignment.Left;
		}

		public void Render( SKCanvas canvas, Rect targetRect )
		{
			if ( IsEmpty )
				return;

			var block = new Topten.RichTextKit.TextBlock();
			block.FontMapper = FontManager.Instance;

			block.Alignment = GetAlignment();

			if ( Flags.Contains( TextFlag.SingleLine ) ) // should we remove any newlines?
			{
				block.MaxLines = 1;
			}

			if ( !Flags.Contains( TextFlag.DontClip ) )
			{
				block.MaxWidth = targetRect.Width;
				block.MaxHeight = targetRect.Height;
			}

			var style = new Topten.RichTextKit.Style();
			_scope.ToStyle( style );

			block.AddText( _scope.Text, style );

			var o = new Topten.RichTextKit.TextPaintOptions
			{
				Edging = _scope.FontSmooth switch
				{
					FontSmooth.Never => SKFontEdging.Alias,
					_ => SKFontEdging.Antialias,
				},

				Hinting = SKFontHinting.Full
			};

			if ( !IsEmpty )
			{
				var rect = new Rect( 0, 0, block.MeasuredWidth, block.MeasuredHeight );
				rect = targetRect.Align( rect.Size, Flags );

				SKPoint drawPosition = new SKPoint( rect.Left, rect.Top );

				block.Paint( canvas, drawPosition, o );
			}
		}

		public void MakeReady()
		{
			TimeSinceUsed = 0;

			if ( Texture != null )
				return;

			// todo - we could probably expose shadows and outlines.. but later down the road.

			var block = new Topten.RichTextKit.TextBlock();
			block.FontMapper = FontManager.Instance;
			block.MaxWidth = Clip.x;
			block.MaxHeight = Clip.y;
			block.Alignment = GetAlignment();

			if ( Flags.Contains( TextFlag.SingleLine ) ) // should we remove any newlines?
			{
				block.MaxLines = 1;
			}

			if ( Flags.Contains( TextFlag.DontClip ) )
			{
				block.MaxWidth = null;
				block.MaxHeight = null;
			}

			//
			// Build text block
			//
			var style = new Topten.RichTextKit.Style();
			_scope.ToStyle( style );

			block.AddText( IsEmpty ? "." : _scope.Text, style );

			//
			// Build Text
			//

			var pad = block.MeasuredPadding;

			int width = block.MeasuredWidth.CeilToInt().Clamp( 2, 4096 );
			int height = block.MeasuredHeight.CeilToInt().Clamp( 2, 4096 );

			if ( style.LetterSpacing < 0 )
				width += Math.Abs( (int)MathF.Floor( style.LetterSpacing ) );

			// Ink that reaches past the measured rect (italic tails, accents, tight bearings) needs room too
			var overhang = block.MeasuredOverhang;
			var margin = _effectMargin + new Margin( MathF.Ceiling( overhang.Left ), MathF.Ceiling( overhang.Top ), MathF.Ceiling( overhang.Right ), MathF.Ceiling( overhang.Bottom ) );

			var marginEdge = margin.EdgeSize;
			width += marginEdge.x.CeilToInt();
			height += marginEdge.y.CeilToInt();

			// Straight alpha, like every other texture. Skia's raster pipeline blends into an unpremultiplied
			// target fine, and over a transparent clear the result is exact.
			//
			// HDR colours (any channel > 1) go to an extended-range half float surface — RgbaF16 (not RgbaF16Clamped)
			// is the one Skia leaves unclamped — so the values reach the shader intact. Everything else stays 8-bit.
			var isHdr = _scope.IsHdr;
			var colorType = isHdr ? SkiaSharp.SKColorType.RgbaF16 : SkiaSharp.SKColorType.Bgra8888;
			var imageFormat = isHdr ? ImageFormat.RGBA16161616F : ImageFormat.BGRA8888;

			using ( var bitmap = new SkiaSharp.SKBitmap( width, height, colorType, SkiaSharp.SKAlphaType.Unpremul ) )
			using ( var canvas = new SkiaSharp.SKCanvas( bitmap ) )
			{
				var o = new Topten.RichTextKit.TextPaintOptions
				{
					Edging = _scope.FontSmooth switch
					{
						FontSmooth.Never => SKFontEdging.Alias,
						_ => SKFontEdging.Antialias,
					},

					Hinting = SKFontHinting.Full
				};

				if ( !IsEmpty )
				{
					SKPoint drawPosition = new SKPoint( margin.Left - pad.Left, margin.Top - pad.Top );

					block.Paint( canvas, drawPosition, o );
				}

				bitmap.RepairTransparentTexels( style.TextColor );

				// Always use the max number of mips
				var mips = (int)MathF.Log2( MathF.Min( width, height ) ) + 1;
				mips = mips.Clamp( 1, 8 );

				Texture = Texture.Create( width, height, imageFormat )
									.WithName( "textblock" )
									.WithData( bitmap.GetPixels(), width * height * bitmap.BytesPerPixel )
									.WithStaticUsage()
									.WithMips( mips )
									.Finish();
			}

			// Re-register so Tick() can evict this block again if it was evicted while
			// a CommandList still held a reference and triggered a texture rebuild.
			if ( CacheKey != 0 ) Dictionary.TryAdd( CacheKey, this );
		}


	}
}

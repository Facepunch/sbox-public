using Sandbox.UI;
using SkiaSharp;
using Topten.RichTextKit;

namespace Sandbox;

public partial class Bitmap
{
	/// <summary>
	/// Draws text onto this bitmap, laid out as the UI lays it out, its glyphs drawn here on the CPU: each run's shadows
	/// and outlines under every run's fill, as the UI draws them. Text uses the scope's colours, not the current pen.
	/// </summary>
	[System.Diagnostics.CodeAnalysis.SuppressMessage( "Reliability", "CA2000", Justification = "Each paint is disposed by its using scope on every loop iteration, including exceptions." )]
	public void DrawText( TextRendering.Scope scope, Rect rect, TextFlag flags = TextFlag.Center )
	{
		if ( string.IsNullOrEmpty( scope.Text ) ) return;

		// A layout of its own: the UI's cached blocks aren't made in a process without one (a content build's worker)
		using var block = new TextRendering.TextBlock { Clip = rect.Size == default ? 8096 : rect.Size, Flags = flags };
		block.Initialize( scope );
		block.EnsureLayout();

		var layout = block.Layout;
		var origin = rect.Align( new Vector2( layout.MeasuredWidth, layout.MeasuredHeight ), flags ).Position;
		var aliased = scope.FontSmooth == FontSmooth.Never;

		foreach ( var line in layout.Lines )
		{
			foreach ( var run in line.Runs )
			{
				if ( !HasGlyphs( run ) || run.Style.TextEffects is null ) continue;

				foreach ( var effect in run.Style.TextEffects )
				{
					using var blur = effect.BlurSize > 0 ? SKMaskFilter.CreateBlur( SKBlurStyle.Normal, effect.BlurSize ) : null;
					using var paint = new SKPaint();
					paint.ColorF = effect.Color;
					paint.IsAntialias = !aliased;
					paint.MaskFilter = blur;
					if ( effect.Width > 0 )
					{
						paint.Style = SKPaintStyle.StrokeAndFill;
						paint.StrokeWidth = effect.Width;
						paint.StrokeJoin = SKStrokeJoin.Round;
					}

					DrawRun( run, origin + new Vector2( effect.Offset.X, effect.Offset.Y ), paint, aliased );
				}
			}
		}

		foreach ( var line in layout.Lines )
		{
			foreach ( var run in line.Runs )
			{
				if ( !HasGlyphs( run ) ) continue;

				using var paint = new SKPaint();
				paint.ColorF = run.Style.TextColor;
				paint.IsAntialias = !aliased;
				DrawRun( run, origin, paint, aliased );
			}
		}
	}

	static bool HasGlyphs( FontRun run ) => run.RunKind is not (FontRunKind.Tabs or FontRunKind.TrailingWhitespace) && run.Glyphs.Length > 0;

	/// <summary>
	/// A run's glyphs at their laid out positions (relative to the block, whose top left is at <paramref name="origin"/>).
	/// Colour glyphs (emoji) keep their own colours.
	/// </summary>
	unsafe void DrawRun( FontRun run, Vector2 origin, SKPaint paint, bool aliased )
	{
		using var font = new SKFont( run.Typeface, run.Style.FontSize )
		{
			Hinting = SKFontHinting.None,
			Edging = aliased ? SKFontEdging.Alias : SKFontEdging.Antialias,
			Subpixel = true,
		};

		fixed ( ushort* glyphs = run.Glyphs.Underlying )
		{
			using var blob = SKTextBlob.CreatePositioned( (IntPtr)(glyphs + run.Glyphs.Start), run.Glyphs.Length * sizeof( ushort ), SKTextEncoding.GlyphId, font, run.GlyphPositions.AsSpan() );
			if ( blob is not null ) _canvas.DrawText( blob, origin.x, origin.y, paint );
		}
	}
}

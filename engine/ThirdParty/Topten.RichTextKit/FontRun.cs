#define USE_SKTEXTBLOB
// RichTextKit
// Copyright © 2019-2020 Topten Software. All Rights Reserved.
// 
// Licensed under the Apache License, Version 2.0 (the "License"); you may 
// not use this product except in compliance with the License. You may obtain 
// a copy of the License at
// 
// http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software 
// distributed under the License is distributed on an "AS IS" BASIS, WITHOUT 
// WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the 
// License for the specific language governing permissions and limitations 
// under the License.

using SkiaSharp;
using Topten.RichTextKit.Utils;

namespace Topten.RichTextKit
{
	/// <summary>
	/// Represents a font run - a physical sequence of laid glyphs all with
	/// the same font and style attributes.
	/// </summary>
	public class FontRun
	{
		/// <summary>
		/// The kind of font run.
		/// </summary>
		public FontRunKind RunKind = FontRunKind.Normal;

		/// <summary>
		/// The style run this typeface run was derived from.
		/// </summary>
		public StyleRun StyleRun;

		/// <summary>
		/// Get the code points of this run
		/// </summary>
		public Slice<int> CodePoints => CodePointBuffer.SubSlice( Start, Length );

		/// <summary>
		/// Code point index of the start of this run
		/// </summary>
		public int Start;

		/// <summary>
		/// The length of this run in codepoints
		/// </summary>
		public int Length;

		/// <summary>
		/// The index of the first character after this run
		/// </summary>
		public int End => Start + Length;

		/// <summary>
		/// The user supplied style for this run
		/// </summary>
		public IStyle Style;

		/// <summary>
		/// The direction of this run
		/// </summary>
		public TextDirection Direction;

		/// <summary>
		/// The typeface of this run (use this over Style.Fontface)
		/// </summary>
		public SKTypeface Typeface;

		/// <summary>
		/// The glyph indicies
		/// </summary>
		public Slice<ushort> Glyphs;

		/// <summary>
		/// The glyph positions (relative to the entire text block)
		/// </summary>
		public Slice<SKPoint> GlyphPositions;

		/// <summary>
		/// The cluster numbers for each glyph
		/// </summary>
		public Slice<int> Clusters;

		/// <summary>
		/// The x-coords of each code point, relative to this text run
		/// </summary>
		public Slice<float> RelativeCodePointXCoords;

		/// <summary>
		/// Get the x-coord of a code point
		/// </summary>
		/// <remarks>
		/// For LTR runs this will be the x-coordinate to the left, or RTL
		/// runs it will be the x-coordinate to the right.
		/// </remarks>
		/// <param name="codePointIndex">The code point index (relative to the entire text block)</param>
		/// <returns>The x-coord relative to the entire text block</returns>
		public float GetXCoordOfCodePointIndex( int codePointIndex )
		{
			if ( this.RunKind == FontRunKind.Ellipsis )
				codePointIndex = 0;

			// Check in range
			if ( codePointIndex < Start || codePointIndex > End )
				throw new ArgumentOutOfRangeException( nameof( codePointIndex ) );

			// End of run?
			if ( codePointIndex == End )
				return XCoord + (Direction == TextDirection.LTR ? Width : 0);

			// Lookup
			return XCoord + RelativeCodePointXCoords[codePointIndex - Start];
		}

		/// <summary>
		/// The ascent of the font used in this run
		/// </summary>
		public float Ascent;

		/// <summary>
		/// The descent of the font used in this run
		/// </summary>
		public float Descent;


		/// <summary>
		/// The leading of the font used in this run
		/// </summary>
		public float Leading;


		/// <summary>
		/// The height of text in this run (ascent + descent)
		/// </summary>
		public float TextHeight => -Ascent + Descent;

		/// <summary>
		/// Calculate the half leading height for text in this run
		/// </summary>
		public float HalfLeading => (TextHeight * (Style.LineHeight - 1) + Leading) / 2;

		/// <summary>
		/// Width of this typeface run
		/// </summary>
		public float Width;

		/// <summary>
		/// Horizontal position of this run, relative to the left margin
		/// </summary>
		public float XCoord;

		/// <summary>
		/// The calculated width of a single space
		/// </summary>
		public float SpaceWidth;

		/// <summary>
		/// The line that owns this font run 
		/// </summary>
		public TextLine Line { get; internal set; }

		/// <summary>
		/// Get the next font run from this one
		/// </summary>
		public FontRun NextRun
		{
			get
			{
				var allRuns = Line.TextBlock.FontRuns as List<FontRun>;
				int index = allRuns.IndexOf( this );
				if ( index < 0 || index + 1 >= Line.Runs.Count )
					return null;
				return Line.Runs[index + 1];
			}
		}

		/// <summary>
		/// Get the previous font run from this one
		/// </summary>
		public FontRun PreviousRun
		{
			get
			{
				var allRuns = Line.TextBlock.FontRuns as List<FontRun>;
				int index = allRuns.IndexOf( this );
				if ( index - 1 < 0 )
					return null;
				return Line.Runs[index - 1];
			}
		}

		/// <summary>
		/// For debugging
		/// </summary>
		/// <returns>Debug string</returns>
		public override string ToString()
		{
			switch ( RunKind )
			{
				case FontRunKind.Normal:
					return $"{Start} - {End} @ {XCoord} - {XCoord + Width} = '{Utf32Utils.FromUtf32( CodePoints )}'";

				default:
					return $"{Start} - {End} @ {XCoord} - {XCoord + Width} {RunKind}'";
			}
		}

		/// <summary>
		/// Moves all glyphs by the specified offset amount
		/// </summary>
		/// <param name="dx">The x-delta to move glyphs by</param>
		/// <param name="dy">The y-delta to move glyphs by</param>
		public void MoveGlyphs( float dx, float dy )
		{
			for ( int i = 0; i < GlyphPositions.Length; i++ )
			{
				GlyphPositions[i].X += dx;
				GlyphPositions[i].Y += dy;
			}
			_textBlob?.Dispose();
			_textBlob = null;
		}

		/// <summary>
		/// Calculates the leading width of all character from the start of the run (either 
		/// the left or right depending on run direction) to the specified code point
		/// </summary>
		/// <param name="codePoint">The code point index to measure to</param>
		/// <returns>The distance from the start to the specified code point</returns>
		public float LeadingWidth( int codePoint )
		{
			// At either end?
			if ( codePoint == this.End )
				return this.Width;
			if ( codePoint == 0 )
				return 0;

			// Internal, calculate the leading width (ie from code point 0 to code point N)
			int codePointIndex = codePoint - this.Start;
			if ( this.Direction == TextDirection.LTR )
			{
				return this.RelativeCodePointXCoords[codePointIndex];
			}
			else
			{
				return this.Width - this.RelativeCodePointXCoords[codePointIndex];
			}

		}

		/// <summary>
		/// Calculate the position at which to break a text run
		/// </summary>
		/// <param name="maxWidth">The max width available</param>
		/// <param name="force">Whether to force the use of at least one glyph</param>
		/// <returns>The code point position to break at</returns>
		internal int FindBreakPosition( float maxWidth, bool force )
		{
			int lastFittingCodePoint = this.Start;
			int firstNonZeroWidthCodePoint = -1;
			var prevWidth = 0f;
			for ( int i = this.Start; i < this.End; i++ )
			{
				var width = this.LeadingWidth( i );
				if ( prevWidth != width )
				{
					if ( firstNonZeroWidthCodePoint < 0 )
						firstNonZeroWidthCodePoint = i;

					if ( width < maxWidth )
					{
						lastFittingCodePoint = i;
					}
					else
					{
						break;
					}
				}
				prevWidth = width;
			}

			if ( lastFittingCodePoint > this.Start || !force )
				return lastFittingCodePoint;

			if ( firstNonZeroWidthCodePoint > this.Start )
				return firstNonZeroWidthCodePoint;

			// Split at the end
			return this.End;
		}

		/// <summary>
		/// Split a typeface run into two separate runs, truncating this run at 
		/// the specified code point index and returning a new run containing the
		/// split off part.
		/// </summary>
		/// <param name="splitAtCodePoint">The code point index to split at</param>
		/// <returns>A new typeface run for the split off part</returns>
		internal FontRun Split( int splitAtCodePoint )
		{
			if ( this.Direction == TextDirection.LTR )
			{
				return SplitLTR( splitAtCodePoint );
			}
			else
			{
				return SplitRTL( splitAtCodePoint );
			}
		}

		/// <summary>
		/// Split a LTR typeface run into two separate runs, truncating the passed
		/// run (LHS) and returning a new run containing the split off part (RHS)
		/// </summary>
		/// <param name="splitAtCodePoint">To code point position to split at</param>
		/// <returns>The RHS run after splitting</returns>
		private FontRun SplitLTR( int splitAtCodePoint )
		{
			// Check split point is internal to the run
			System.Diagnostics.Debug.Assert( this.Direction == TextDirection.LTR );
			System.Diagnostics.Debug.Assert( splitAtCodePoint > this.Start );
			System.Diagnostics.Debug.Assert( splitAtCodePoint < this.End );

			// Work out the split position
			int codePointSplitPos = splitAtCodePoint - this.Start;

			// Work out the width that we're slicing off
			float sliceLeftWidth = this.RelativeCodePointXCoords[codePointSplitPos];
			float sliceRightWidth = this.Width - sliceLeftWidth;

			// Work out the glyph split position
			int glyphSplitPos = 0;
			for ( glyphSplitPos = 0; glyphSplitPos < this.Clusters.Length; glyphSplitPos++ )
			{
				if ( this.Clusters[glyphSplitPos] >= splitAtCodePoint )
					break;
			}

			// Create the other run
			var newRun = FontRun.Pool.Value.Get();
			newRun.StyleRun = this.StyleRun;
			newRun.CodePointBuffer = this.CodePointBuffer;
			newRun.Direction = this.Direction;
			newRun.Ascent = this.Ascent;
			newRun.Descent = this.Descent;
			newRun.Leading = this.Leading;
			newRun.Style = this.Style;
			newRun.Typeface = this.Typeface;
			newRun.Start = splitAtCodePoint;
			newRun.Length = this.End - splitAtCodePoint;
			newRun.Width = sliceRightWidth;
			newRun.RelativeCodePointXCoords = this.RelativeCodePointXCoords.SubSlice( codePointSplitPos );
			newRun.GlyphPositions = this.GlyphPositions.SubSlice( glyphSplitPos );
			newRun.Glyphs = this.Glyphs.SubSlice( glyphSplitPos );
			newRun.Clusters = this.Clusters.SubSlice( glyphSplitPos );
			newRun.SpaceWidth = this.SpaceWidth;

			// Adjust code point positions
			for ( int i = 0; i < newRun.RelativeCodePointXCoords.Length; i++ )
			{
				newRun.RelativeCodePointXCoords[i] -= sliceLeftWidth;
			}

			// Adjust glyph positions
			for ( int i = 0; i < newRun.GlyphPositions.Length; i++ )
			{
				newRun.GlyphPositions[i].X -= sliceLeftWidth;
			}

			// Update this run
			this.RelativeCodePointXCoords = this.RelativeCodePointXCoords.SubSlice( 0, codePointSplitPos );
			this.Glyphs = this.Glyphs.SubSlice( 0, glyphSplitPos );
			this.GlyphPositions = this.GlyphPositions.SubSlice( 0, glyphSplitPos );
			this.Clusters = this.Clusters.SubSlice( 0, glyphSplitPos );
			this.Width = sliceLeftWidth;
			this.Length = codePointSplitPos;
			this._textBlob?.Dispose();
			this._textBlob = null;

			// Return the new run
			return newRun;
		}

		/// <summary>
		/// Split a RTL typeface run into two separate runs, truncating the passed
		/// run (RHS) and returning a new run containing the split off part (LHS)
		/// </summary>
		/// <param name="splitAtCodePoint">To code point position to split at</param>
		/// <returns>The LHS run after splitting</returns>
		private FontRun SplitRTL( int splitAtCodePoint )
		{
			// Check split point is internal to the run
			System.Diagnostics.Debug.Assert( this.Direction == TextDirection.RTL );
			System.Diagnostics.Debug.Assert( splitAtCodePoint > this.Start );
			System.Diagnostics.Debug.Assert( splitAtCodePoint < this.End );

			// Work out the split position
			int codePointSplitPos = splitAtCodePoint - this.Start;

			// Work out the width that we're slicing off
			float sliceLeftWidth = this.RelativeCodePointXCoords[codePointSplitPos];
			float sliceRightWidth = this.Width - sliceLeftWidth;

			// Work out the glyph split position
			int glyphSplitPos = 0;
			for ( glyphSplitPos = this.Clusters.Length; glyphSplitPos > 0; glyphSplitPos-- )
			{
				if ( this.Clusters[glyphSplitPos - 1] >= splitAtCodePoint )
					break;
			}

			// Create the other run
			var newRun = FontRun.Pool.Value.Get();
			newRun.StyleRun = this.StyleRun;
			newRun.CodePointBuffer = this.CodePointBuffer;
			newRun.Direction = this.Direction;
			newRun.Ascent = this.Ascent;
			newRun.Descent = this.Descent;
			newRun.Leading = this.Leading;
			newRun.Style = this.Style;
			newRun.Typeface = this.Typeface;
			newRun.Start = splitAtCodePoint;
			newRun.Length = this.End - splitAtCodePoint;
			newRun.Width = sliceLeftWidth;
			newRun.RelativeCodePointXCoords = this.RelativeCodePointXCoords.SubSlice( codePointSplitPos );
			newRun.GlyphPositions = this.GlyphPositions.SubSlice( 0, glyphSplitPos );
			newRun.Glyphs = this.Glyphs.SubSlice( 0, glyphSplitPos );
			newRun.Clusters = this.Clusters.SubSlice( 0, glyphSplitPos );
			newRun.SpaceWidth = this.SpaceWidth;

			// Update this run
			this.RelativeCodePointXCoords = this.RelativeCodePointXCoords.SubSlice( 0, codePointSplitPos );
			this.Glyphs = this.Glyphs.SubSlice( glyphSplitPos );
			this.GlyphPositions = this.GlyphPositions.SubSlice( glyphSplitPos );
			this.Clusters = this.Clusters.SubSlice( glyphSplitPos );
			this.Width = sliceRightWidth;
			this.Length = codePointSplitPos;
			this._textBlob?.Dispose();
			this._textBlob = null;

			// Adjust code point positions
			for ( int i = 0; i < this.RelativeCodePointXCoords.Length; i++ )
			{
				this.RelativeCodePointXCoords[i] -= sliceLeftWidth;
			}

			// Adjust glyph positions
			for ( int i = 0; i < this.GlyphPositions.Length; i++ )
			{
				this.GlyphPositions[i].X -= sliceLeftWidth;
			}

			// Return the new run
			return newRun;
		}

		/// <summary>
		/// The global list of code points
		/// </summary>
		internal Buffer<int> CodePointBuffer;

		/// <summary>
		/// Calculate any overhang for this text line
		/// </summary>
		/// <param name="right"></param>
		/// <param name="leftOverhang"></param>
		/// <param name="rightOverhang"></param>
		internal void UpdateOverhang( float right, ref float leftOverhang, ref float rightOverhang )
		{
			if ( RunKind == FontRunKind.TrailingWhitespace )
				return;

			if ( Glyphs.Length == 0 )
				return;

			using ( var paint = new SKPaint() )
			using ( var font = new SKFont() )
			{
				float glyphScale = 1;
				if ( Style.FontVariant == FontVariant.SuperScript )
				{
					glyphScale = 0.65f;
				}
				if ( Style.FontVariant == FontVariant.SubScript )
				{
					glyphScale = 0.65f;
				}

				font.Typeface = Typeface;
				font.Size = Style.FontSize * glyphScale;
				font.Subpixel = true;
				paint.IsAntialias = true;
				font.Edging = SKFontEdging.SubpixelAntialias;

				unsafe
				{
					fixed ( ushort* pGlyphs = Glyphs.Underlying )
					{
						font.GetGlyphWidths( (IntPtr)(pGlyphs + Start), sizeof( ushort ) * Glyphs.Length, SKTextEncoding.GlyphId, out var bounds );
						if ( bounds != null )
						{
							for ( int i = 0; i < bounds.Length; i++ )
							{
								float gx = GlyphPositions[i].X;

								var loh = -(gx + bounds[i].Left);
								if ( loh > leftOverhang )
									leftOverhang = loh;

								var roh = (gx + bounds[i].Right + 1) - right;
								if ( roh > rightOverhang )
									rightOverhang = roh;
							}
						}
					}
				}
			}
		}

		internal unsafe float CreateTextBlob( PaintTextContext ctx, bool withEdging = true )
		{
			fixed ( ushort* pGlyphs = Glyphs.Underlying )
			{
				float glyphScale = 1;
				float glyphVOffset = 0;
				if ( Style.FontVariant == FontVariant.SuperScript )
				{
					glyphScale = 0.65f;
					glyphVOffset = -Style.FontSize * 0.35f;
				}
				if ( Style.FontVariant == FontVariant.SubScript )
				{
					glyphScale = 0.65f;
					glyphVOffset = Style.FontSize * 0.1f;
				}

				// Create the font
				if ( _font == null )
				{
					_font = new SKFont( this.Typeface, this.Style.FontSize * glyphScale );
				}
				_font.Hinting = ctx.Options.Hinting;
				_font.Edging = withEdging ? ctx.Options.Edging : SKFontEdging.Antialias;
				_font.Subpixel = ctx.Options.SubpixelPositioning;

				// Create the SKTextBlob (if necessary)
				if ( _textBlob == null )
				{
					_textBlob = SKTextBlob.CreatePositioned(
						(IntPtr)(pGlyphs + Glyphs.Start),
						Glyphs.Length * sizeof( ushort ),
						SKTextEncoding.GlyphId,
						_font,
						GlyphPositions.AsSpan() );
				}

				return glyphVOffset;
			}
		}

		internal void DrawStrokeLine( UnderlineType underlineType, SKCanvas skCanvas, SKPaint sKPaint, SKPoint startPoint, SKPoint endPoint, bool isOverline )
		{
			if ( underlineType == UnderlineType.Solid )
			{
				skCanvas.DrawLine( startPoint, endPoint, sKPaint );
			}
			else if ( underlineType == UnderlineType.Dashed )
			{
				float strokeWidth = sKPaint.StrokeWidth;
				SKPathEffect previousPathEffect = sKPaint.PathEffect;
				{
					sKPaint.PathEffect = SKPathEffect.CreateDash( new float[] { strokeWidth * 3.0f, strokeWidth * 3.0f }, strokeWidth );
					skCanvas.DrawLine( startPoint, endPoint, sKPaint );
				}
				sKPaint.PathEffect = previousPathEffect;
			}
			else if ( underlineType == UnderlineType.Dotted )
			{
				float strokeWidth = sKPaint.StrokeWidth;
				SKStrokeCap sKStrokeCap = sKPaint.StrokeCap;
				bool hasAA = sKPaint.IsAntialias;
				SKPathEffect previousPathEffect = sKPaint.PathEffect;
				{
					sKPaint.StrokeCap = SKStrokeCap.Round;
					sKPaint.IsAntialias = true;
					sKPaint.PathEffect = SKPathEffect.CreateDash( new float[] { 0.0f, strokeWidth * 2.0f }, 0.0f );
					skCanvas.DrawLine( startPoint, endPoint, sKPaint );
				}
				sKPaint.IsAntialias = hasAA;
				sKPaint.StrokeCap = sKStrokeCap;
				sKPaint.PathEffect = previousPathEffect;
			}
			else if ( underlineType == UnderlineType.Double )
			{
				float strokeWidth = sKPaint.StrokeWidth;
				SKPathEffect previousPathEffect = sKPaint.PathEffect;
				{
					SKPoint skOffset = new SKPoint( 0, strokeWidth * 2.0f );
					if ( isOverline )
						skOffset.Y *= -1.0f;

					skCanvas.DrawLine( startPoint, endPoint, sKPaint );
					skCanvas.DrawLine( startPoint + skOffset, endPoint + skOffset, sKPaint );
				}
				sKPaint.PathEffect = previousPathEffect;
			}
			else if ( underlineType == UnderlineType.Wavy )
			{
				// Since skia doesn't have this, we gotta make it ourselves
				using ( SKPath path = new SKPath() )
				{
					float totalWidth = endPoint.X - startPoint.X;
					path.MoveTo( startPoint );
					for ( float i = 0; i < totalWidth; i++ )
					{
						path.LineTo( startPoint.X + i, startPoint.Y + (float)(Math.Sin( i * 0.25f ) * 1.25f) );
					}

					bool hasAA = sKPaint.IsAntialias;
					SKPaintStyle sKPaintStyle = sKPaint.Style;
					SKStrokeCap sKStrokeCap = sKPaint.StrokeCap;
					{
						sKPaint.IsAntialias = true;
						sKPaint.StrokeCap = SKStrokeCap.Round;
						sKPaint.Style = SKPaintStyle.Stroke;
						skCanvas.DrawPath( path, sKPaint );
					}
					sKPaint.IsAntialias = hasAA;
					sKPaint.StrokeCap = sKStrokeCap;
					sKPaint.Style = sKPaintStyle;
				}
			}
		}

		/// <summary>
		/// Paint this font run
		/// </summary>
		/// <param name="ctx"></param>
		internal void Paint( PaintTextContext ctx )
		{
			// Paint selection?
			if ( ctx.PaintSelectionBackground != null && RunKind != FontRunKind.Ellipsis )
			{
				bool paintStartHandle = false;
				bool paintEndHandle = false;

				float selStartXCoord;
				if ( ctx.SelectionStart < Start )
					selStartXCoord = Direction == TextDirection.LTR ? 0 : Width;
				else if ( ctx.SelectionStart >= End )
					selStartXCoord = Direction == TextDirection.LTR ? Width : 0;
				else
				{
					paintStartHandle = true;
					selStartXCoord = RelativeCodePointXCoords[ctx.SelectionStart - this.Start];
				}

				float selEndXCoord;
				if ( ctx.SelectionEnd < Start )
					selEndXCoord = Direction == TextDirection.LTR ? 0 : Width;
				else if ( ctx.SelectionEnd >= End )
				{
					selEndXCoord = Direction == TextDirection.LTR ? Width : 0;
					paintEndHandle = ctx.SelectionEnd == End;
				}
				else
				{
					selEndXCoord = RelativeCodePointXCoords[ctx.SelectionEnd - this.Start];
					paintEndHandle = true;
				}

				if ( selStartXCoord != selEndXCoord )
				{
					var tl = new SKPoint( selStartXCoord + this.XCoord, Line.YCoord );
					var br = new SKPoint( selEndXCoord + this.XCoord, Line.YCoord + Line.Height );

					// Align coords to pixel boundaries
					// Not needed - disabled antialias on SKPaint instead
					/*
                    if (ctx.Canvas.TotalMatrix.TryInvert(out var inverse))
                    {
                        tl = ctx.Canvas.TotalMatrix.MapPoint(tl);
                        br = ctx.Canvas.TotalMatrix.MapPoint(br);
                        tl = new SKPoint((float)Math.Round(tl.X), (float)Math.Round(tl.Y));
                        br = new SKPoint((float)Math.Round(br.X), (float)Math.Round(br.Y));
                        tl = inverse.MapPoint(tl);
                        br = inverse.MapPoint(br);
                    }
                    */

					var rect = new SKRect( tl.X, tl.Y, br.X, br.Y );
					ctx.Canvas.DrawRect( rect, ctx.PaintSelectionBackground );

					// Paint selection handles?
					if ( ctx.PaintSelectionHandle != null )
					{
						if ( paintStartHandle )
						{
							rect = new SKRect( tl.X - 1 * ctx.SelectionHandleScale, tl.Y, tl.X + 1 * ctx.SelectionHandleScale, br.Y );
							ctx.Canvas.DrawRect( rect, ctx.PaintSelectionHandle );
							ctx.Canvas.DrawCircle( new SKPoint( tl.X, tl.Y ), 5 * ctx.SelectionHandleScale, ctx.PaintSelectionHandle );
						}
						if ( paintEndHandle )
						{
							rect = new SKRect( br.X - 1 * ctx.SelectionHandleScale, tl.Y, br.X + 1 * ctx.SelectionHandleScale, br.Y );
							ctx.Canvas.DrawRect( rect, ctx.PaintSelectionHandle );
							ctx.Canvas.DrawCircle( new SKPoint( br.X, br.Y ), 5 * ctx.SelectionHandleScale, ctx.PaintSelectionHandle );
						}
					}
				}
			}

			if ( RunKind == FontRunKind.Tabs )
				return;

			if ( RunKind == FontRunKind.TrailingWhitespace )
				return;

			_textBlob = null;
			var glyphVOffset = CreateTextBlob( ctx );

			using var paint = new SKPaint();

			paint.Color = Style.TextColor;
			paint.Shader = ctx.Shader;

			ctx.Canvas.DrawText( _textBlob, 0, 0, paint );

			PaintUnderline( ctx, paint );
			PaintStrikeThrough( ctx, paint, glyphVOffset );
		}

		internal void PaintStrikeThrough( PaintTextContext ctx, SKPaint paint, float glyphVOffset )
		{
			var strokeWidth = Style.StrokeThickness ?? _font.Metrics.StrikeoutThickness ?? 0;

			if ( strokeWidth <= 0 ) return;
			if ( Style.StrikeThrough == StrikeThroughStyle.None ) return;
			if ( RunKind != FontRunKind.Normal ) return;

			paint.Color = Style.UnderlineColor ?? Style.TextColor;
			paint.StrokeWidth = MathF.Max( strokeWidth, 1 );

			var strikeYPos = Line.YCoord + Line.BaseLine + (_font.Metrics.StrikeoutPosition ?? 0) + glyphVOffset + Style.StrikeThroughOffset;
			DrawStrokeLine( Style.UnderlineStrokeType, ctx.Canvas, paint, new SKPoint( XCoord, strikeYPos ), new SKPoint( XCoord + Width, strikeYPos ), false );
		}

		internal void PaintUnderline( PaintTextContext ctx, SKPaint paint )
		{
			var strokeWidth = Style.StrokeThickness ?? _font.Metrics.UnderlineThickness ?? 0;

			if ( strokeWidth <= 0 ) return;
			if ( Style.Underline == UnderlineStyle.None || RunKind != FontRunKind.Normal ) return;

			paint.StrokeWidth = MathF.Max( strokeWidth, 1 );
			paint.Color = Style.UnderlineColor ?? Style.TextColor;

			var underlineYPos = Line.YCoord + Line.BaseLine + (_font.Metrics.UnderlinePosition ?? 0);
			var bHasUnderline = false;

			if ( (Style.Underline & UnderlineStyle.Gapped) != 0 )
			{
				var flUnderlineOffset = underlineYPos + Style.UnderlineOffset;
				var interceptPositions = _textBlob.GetIntercepts( flUnderlineOffset - paint.StrokeWidth / 2, flUnderlineOffset + paint.StrokeWidth );
				var x = XCoord;

				if ( Style.StrokeInkSkip )
				{
					for ( int i = 0; i < interceptPositions.Length; i += 2 )
					{
						var b = interceptPositions[i] - paint.StrokeWidth;
						if ( x < b )
						{
							DrawStrokeLine( Style.UnderlineStrokeType, ctx.Canvas, paint, new SKPoint( x, flUnderlineOffset ), new SKPoint( b, flUnderlineOffset ), false );
						}
						x = interceptPositions[i + 1] + paint.StrokeWidth;
					}
				}
				if ( x < XCoord + Width )
				{
					DrawStrokeLine( Style.UnderlineStrokeType, ctx.Canvas, paint, new SKPoint( x, flUnderlineOffset ), new SKPoint( XCoord + Width, flUnderlineOffset ), false );
				}

				bHasUnderline = true;
			}

			if ( (Style.Underline & UnderlineStyle.Overline) != 0 )
			{
				var flOverlineOffset = Line.YCoord + Style.OverlineOffset;
				var interceptPositions = _textBlob.GetIntercepts( flOverlineOffset - paint.StrokeWidth / 2, flOverlineOffset + paint.StrokeWidth );
				var x = XCoord;
				if ( Style.StrokeInkSkip )
				{
					for ( int i = 0; i < interceptPositions.Length; i += 2 )
					{
						float b = interceptPositions[i] - paint.StrokeWidth;
						if ( x < b )
						{
							DrawStrokeLine( Style.UnderlineStrokeType, ctx.Canvas, paint, new SKPoint( x, flOverlineOffset ), new SKPoint( b, flOverlineOffset ), true );
						}
						x = interceptPositions[i + 1] + paint.StrokeWidth;
					}
				}

				if ( x < XCoord + Width )
				{
					DrawStrokeLine( Style.UnderlineStrokeType, ctx.Canvas, paint, new SKPoint( x, flOverlineOffset ), new SKPoint( x + Width, flOverlineOffset ), true );
				}

				bHasUnderline = true;
			}

			if ( !bHasUnderline || (Style.Underline & UnderlineStyle.Solid) != 0 )
			{
				float flUnderlineOffset = underlineYPos + Style.UnderlineOffset;
				if ( (Style.Underline & UnderlineStyle.ImeInput) != 0 )
				{
					paint.PathEffect = SKPathEffect.CreateDash( new float[] { paint.StrokeWidth, paint.StrokeWidth }, paint.StrokeWidth );
				}
				if ( (Style.Underline & UnderlineStyle.ImeConverted) != 0 )
				{
					paint.PathEffect = SKPathEffect.CreateDash( new float[] { paint.StrokeWidth, paint.StrokeWidth }, paint.StrokeWidth );
				}
				if ( (Style.Underline & UnderlineStyle.ImeConverted) != 0 )
				{
					paint.StrokeWidth *= 2;
				}
				DrawStrokeLine( Style.UnderlineStrokeType, ctx.Canvas, paint, new SKPoint( XCoord, flUnderlineOffset ), new SKPoint( XCoord + Width, flUnderlineOffset ), false );
				paint.PathEffect = null;
			}
		}

		/// <summary>
		/// Paint background of this font run
		/// </summary>
		/// <param name="ctx"></param>
		internal void PaintBackground( PaintTextContext ctx )
		{
			if ( RunKind == FontRunKind.TrailingWhitespace ) return;

			if ( Style.BackgroundColor != SKColor.Empty && RunKind == FontRunKind.Normal )
			{
				var rect = new SKRect( XCoord, Line.YCoord,
					XCoord + Width, Line.YCoord + Line.Height );
				using ( var skPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = Style.BackgroundColor } )
				{
					ctx.Canvas.DrawRect( rect, skPaint );
				}
			}

			if ( Style.TextEffects == null || Style.TextEffects.Count() == 0 )
				return;

			//
			// alex: Override text blob with one that does not have aliasing
			// otherwise we get issues with aliased fonts and things like
			// text strokes.
			//
			_textBlob = null;
			var glyphVOffset = CreateTextBlob( ctx, false );

			if ( _textBlob == null )
				return;

			using var effectPaint = new SKPaint();
			foreach ( var effect in Style.TextEffects )
			{
				// Aliased 1 pixel outline needs to be rendered in a different way to look good.
				if ( ctx.Options.Edging == SKFontEdging.Alias &&
					 effect.PaintStyle == SKPaintStyle.StrokeAndFill &&
					 effect.Width == 1 )
				{
					PaintPixelOutline( ctx, effect.Color );

					continue;
				}

				effectPaint.Style = effect.PaintStyle;
				effectPaint.StrokeWidth = effect.Width;
				effectPaint.StrokeJoin = effect.StrokeJoin;
				effectPaint.StrokeMiter = effect.StrokeMiter;
				effectPaint.Color = effect.Color;
				effectPaint.ImageFilter = effect.BlurSize > 0 ? SKImageFilter.CreateDropShadow( effect.Offset.X, effect.Offset.Y, effect.BlurSize, effect.BlurSize, effect.Color ) : null;

				ctx.Canvas.DrawText( _textBlob, 0, 0, effectPaint );
				PaintUnderline( ctx, effectPaint );
				PaintStrikeThrough( ctx, effectPaint, glyphVOffset );
			}
		}

		static readonly SKPoint[] PixelOutlineOffsets =
		{
			new( -1,  0 ),
			new( 1,  0 ),
			new( 0, -1 ),
			new( 0,  1 ),
			new( -1, -1 ),
			new( -1,  1 ),
			new( 1, -1 ),
			new( 1,  1 ),
		};

		unsafe void PaintPixelOutline( PaintTextContext ctx, SKColor color )
		{
			using var pixelPaint = new SKPaint
			{
				Color = color,
				IsAntialias = false,
			};

			// Override font edging.
			var edging = _font.Edging;
			_font.Edging = SKFontEdging.Alias;

			fixed ( ushort* pGlyphs = Glyphs.Underlying )
			{
				var textBlob = SKTextBlob.CreatePositioned(
					(IntPtr)(pGlyphs + Glyphs.Start),
					Glyphs.Length * sizeof( ushort ),
					SKTextEncoding.GlyphId,
					_font,
					GlyphPositions.AsSpan() );

				foreach ( var o in PixelOutlineOffsets )
				{
					ctx.Canvas.DrawText( textBlob, o.X, o.Y, pixelPaint );
				}
			}

			// Restore font edging.
			_font.Edging = edging;
		}

		SKTextBlob _textBlob;
		SKFont _font;

		void Reset()
		{
			RunKind = FontRunKind.Normal;
			CodePointBuffer = null;
			Style = null;
			Typeface = null;
			Line = null;
			_textBlob = null;
			_font = null;
		}

		internal static ThreadLocal<ObjectPool<FontRun>> Pool = new ThreadLocal<ObjectPool<FontRun>>( () => new ObjectPool<FontRun>()
		{
			Cleaner = ( r ) => r.Reset()
		} );
	}
}

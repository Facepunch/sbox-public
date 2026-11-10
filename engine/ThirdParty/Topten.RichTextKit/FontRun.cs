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
		/// Grow the overhang by however far this run's glyph ink reaches past the text rectangle. Glyph positions
		/// are absolute within the block after layout, so bounds can be compared directly.
		/// </summary>
		internal void UpdateOverhang( SKRect textRect, ref SKRect overhang )
		{
			if ( RunKind == FontRunKind.TrailingWhitespace )
				return;

			if ( Glyphs.Length == 0 )
				return;

			using ( var font = new SKFont() )
			{
				float glyphScale = 1;
				if ( Style.FontVariant == FontVariant.SuperScript || Style.FontVariant == FontVariant.SubScript )
				{
					glyphScale = 0.65f;
				}

				font.Typeface = Typeface;
				font.Size = Style.FontSize * glyphScale;
				font.Subpixel = true;
				font.Edging = SKFontEdging.SubpixelAntialias;

				unsafe
				{
					fixed ( ushort* pGlyphs = Glyphs.Underlying )
					{
						font.GetGlyphWidths( (IntPtr)(pGlyphs + Glyphs.Start), sizeof( ushort ) * Glyphs.Length, SKTextEncoding.GlyphId, out var bounds );
						if ( bounds == null )
							return;

						for ( int i = 0; i < bounds.Length; i++ )
						{
							var pos = GlyphPositions[i];

							overhang.Left = Math.Max( overhang.Left, textRect.Left - (pos.X + bounds[i].Left) );
							overhang.Right = Math.Max( overhang.Right, (pos.X + bounds[i].Right) - textRect.Right );
							overhang.Top = Math.Max( overhang.Top, textRect.Top - (pos.Y + bounds[i].Top) );
							overhang.Bottom = Math.Max( overhang.Bottom, (pos.Y + bounds[i].Bottom) - textRect.Bottom );
						}
					}
				}
			}
		}

		void Reset()
		{
			RunKind = FontRunKind.Normal;
			CodePointBuffer = null;
			Style = null;
			Typeface = null;
			Line = null;
		}

		internal static ThreadLocal<ObjectPool<FontRun>> Pool = new ThreadLocal<ObjectPool<FontRun>>( () => new ObjectPool<FontRun>()
		{
			Cleaner = ( r ) => r.Reset()
		} );
	}
}

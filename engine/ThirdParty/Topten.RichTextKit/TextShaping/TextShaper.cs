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

using HarfBuzzSharp;
using SkiaSharp;
using System.Runtime.InteropServices;
using Topten.RichTextKit.Utils;

namespace Topten.RichTextKit
{
	/// <summary>
	/// Helper class for shaping text
	/// </summary>
	internal class TextShaper : IDisposable
	{
		/// <summary>
		/// Cache of shapers for typefaces
		/// </summary>
		static Dictionary<SKTypeface, TextShaper> _shapers = new Dictionary<SKTypeface, TextShaper>();

		/// <summary>
		/// Get the text shaper for a particular type face
		/// </summary>
		/// <param name="typeface">The typeface being queried for</param>
		/// <returns>A TextShaper</returns>
		public static TextShaper ForTypeface( SKTypeface typeface )
		{
			lock ( _shapers )
			{
				if ( !_shapers.TryGetValue( typeface, out var shaper ) )
				{
					shaper = new TextShaper( typeface );
					_shapers.Add( typeface, shaper );
				}

				return shaper;
			}
		}

		/// <summary>
		/// Constructs a new TextShaper 
		/// </summary>
		/// <param name="typeface">The typeface of this shaper</param>
		private TextShaper( SKTypeface typeface )
		{
			// Store typeface
			_typeface = typeface;

			// Load the typeface stream to a HarfBuzz font
			int index;
			using ( var blob = GetHarfBuzzBlob( typeface.OpenStream( out index ) ) )
			using ( var face = new Face( blob, (uint)index ) )
			{
				face.UnitsPerEm = typeface.UnitsPerEm;

				_font = new HarfBuzzSharp.Font( face );
				_font.SetScale( overScale, overScale );
				_font.SetFunctionsOpenType();
			}

			// Get font metrics for this typeface
			using ( var paint = new SKPaint() )
			using ( var font = new SKFont() )
			{
				font.Typeface = typeface;
				font.Size = overScale;

				_fontMetrics = font.Metrics;

				// This is a temporary hack until SkiaSharp exposes
				// a way to check if a font is fixed pitch.  For now
				// we just measure and `i` and a `w` and see if they're
				// the same width.
				float[] widths = font.GetGlyphWidths( "iw", out var rects );
				_isFixedPitch = widths != null && widths.Length > 1 && widths[0] == widths[1];
				if ( _isFixedPitch )
					_fixedCharacterWidth = widths[0];

			}
		}

		/// <summary>
		/// Dispose this text shaper
		/// </summary>
		public void Dispose()
		{
			if ( _font != null )
			{
				_font.Dispose();
				_font = null;
			}
		}

		/// <summary>
		/// The HarfBuzz font for this shaper
		/// </summary>
		HarfBuzzSharp.Font _font;

		/// <summary>
		/// The typeface for this shaper
		/// </summary>
		SKTypeface _typeface;

		/// <summary>
		/// Font metrics for the font
		/// </summary>
		SKFontMetrics _fontMetrics;

		/// <summary>
		/// True if this font face is fixed pitch
		/// </summary>
		bool _isFixedPitch;

		/// <summary>
		/// Fixed pitch character width
		/// </summary>
		float _fixedCharacterWidth;

		/// <summary>
		/// A set of re-usable result buffers to store the result of text shaping operation
		/// </summary>
		public class ResultBufferSet
		{
			public void Clear()
			{
				GlyphIndicies.Clear();
				GlyphPositions.Clear();
				Clusters.Clear();
				CodePointXCoords.Clear();
			}

			public Buffer<ushort> GlyphIndicies = new Buffer<ushort>();
			public Buffer<SKPoint> GlyphPositions = new Buffer<SKPoint>();
			public Buffer<int> Clusters = new Buffer<int>();
			public Buffer<float> CodePointXCoords = new Buffer<float>();
		}

		/// <summary>
		/// Returned as the result of a text shaping operation
		/// </summary>
		public struct Result
		{
			/// <summary>
			/// The glyph indicies of all glyphs required to render the shaped text
			/// </summary>
			public Slice<ushort> GlyphIndicies;

			/// <summary>
			/// The position of each glyph
			/// </summary>
			public Slice<SKPoint> GlyphPositions;

			/// <summary>
			/// One entry for each glyph, showing the code point index
			/// of the characters it was derived from
			/// </summary>
			public Slice<int> Clusters;

			/// <summary>
			/// The end position of the rendered text
			/// </summary>
			public SKPoint EndXCoord;

			/// <summary>
			/// The X-Position of each passed code point
			/// </summary>
			public Slice<float> CodePointXCoords;

			/// <summary>
			/// The ascent of the font
			/// </summary>
			public float Ascent;

			/// <summary>
			/// The descent of the font
			/// </summary>
			public float Descent;

			/// <summary>
			/// The leading of the font
			/// </summary>
			public float Leading;

			/// <summary>
			/// The XMin for the font
			/// </summary>
			public float XMin;
		}

		/// <summary>
		/// Over scale used for all font operations
		/// </summary>
		const int overScale = 512;



		/// <summary>
		/// Shape an array of utf-32 code points replacing each grapheme cluster with a replacement character
		/// </summary>
		/// <param name="bufferSet">A re-usable text shaping buffer set that results will be allocated from</param>
		/// <param name="codePoints">The utf-32 code points to be shaped</param>
		/// <param name="style">The user style for the text</param>
		/// <param name="clusterAdjustment">A value to add to all reported cluster numbers</param>
		/// <returns>A TextShaper.Result representing the shaped text</returns>
		public Result ShapeReplacement( ResultBufferSet bufferSet, Slice<int> codePoints, IStyle style, int clusterAdjustment )
		{
			var clusters = GraphemeClusterAlgorithm.GetBoundaries( codePoints ).ToArray();
			var glyph = _typeface.GetGlyph( style.ReplacementCharacter );
			using var font = new SKFont( _typeface, overScale );
			float glyphScale = style.FontSize / overScale;

			float[] widths = new float[1];
			SKRect[] bounds = new SKRect[1];
			font.GetGlyphWidths( (new ushort[] { glyph }).AsSpan(), widths.AsSpan(), bounds.AsSpan() );

			var r = new Result();
			r.GlyphIndicies = bufferSet.GlyphIndicies.Add( (int)clusters.Length - 1, false );
			r.GlyphPositions = bufferSet.GlyphPositions.Add( (int)clusters.Length - 1, false );
			r.Clusters = bufferSet.Clusters.Add( (int)clusters.Length - 1, false );
			r.CodePointXCoords = bufferSet.CodePointXCoords.Add( codePoints.Length, false );
			r.CodePointXCoords.Fill( 0 );

			float xCoord = 0;
			for ( int i = 0; i < clusters.Length - 1; i++ )
			{
				r.GlyphPositions[i].X = xCoord * glyphScale;
				r.GlyphPositions[i].Y = 0;
				r.GlyphIndicies[i] = codePoints[clusters[i]] == 0x2029 ? (ushort)0 : glyph;
				r.Clusters[i] = clusters[i] + clusterAdjustment;

				for ( int j = clusters[i]; j < clusters[i + 1]; j++ )
				{
					r.CodePointXCoords[j] = r.GlyphPositions[i].X;
				}

				xCoord += widths[0] + style.LetterSpacing / glyphScale;
			}

			// Also return the end cursor position
			r.EndXCoord = new SKPoint( xCoord * glyphScale, 0 );

			ApplyFontMetrics( ref r, style.FontSize );

			return r;
		}


		/// <summary>
		/// Shape an array of utf-32 code points
		/// </summary>
		/// <param name="bufferSet">A re-usable text shaping buffer set that results will be allocated from</param>
		/// <param name="codePoints">The utf-32 code points to be shaped</param>
		/// <param name="style">The user style for the text</param>
		/// <param name="direction">LTR or RTL direction</param>
		/// <param name="clusterAdjustment">A value to add to all reported cluster numbers</param>
		/// <param name="asFallbackFor">The type face this font is a fallback for</param>
		/// <param name="textAlignment">The text alignment of the paragraph, used to control placement of glyphs within character cell when letter spacing used</param>
		/// <returns>A TextShaper.Result representing the shaped text</returns>
		public Result Shape( ResultBufferSet bufferSet, Slice<int> codePoints, IStyle style, TextDirection direction, int clusterAdjustment, SKTypeface asFallbackFor, TextAlignment textAlignment )
		{
			// Work out if we need to force this to a fixed pitch and if
			// so the unscale character width we need to use
			float forceFixedPitchWidth = 0;
			if ( asFallbackFor != _typeface && asFallbackFor != null )
			{
				var originalTypefaceShaper = ForTypeface( asFallbackFor );
				if ( originalTypefaceShaper._isFixedPitch )
				{
					forceFixedPitchWidth = originalTypefaceShaper._fixedCharacterWidth;
				}
			}

			// Work out how much to shift glyphs in the character cell when using letter spacing
			// The idea here is to align the glyphs within the character cell the same way as the
			// text block alignment so that left/right aligned text still aligns with the margin
			// and centered text is still centered (and not shifted slightly due to the extra 
			// space that would be at the right with normal letter spacing).
			float glyphLetterSpacingAdjustment = 0;
			switch ( textAlignment )
			{
				case TextAlignment.Right:
					glyphLetterSpacingAdjustment = style.LetterSpacing;
					break;

				case TextAlignment.Center:
					glyphLetterSpacingAdjustment = style.LetterSpacing / 2;
					break;
			}


			using ( var buffer = new HarfBuzzSharp.Buffer() )
			{
				// Setup buffer
				buffer.AddUtf32( codePoints.AsSpan(), 0, -1 );

				// Setup directionality (if supplied)
				switch ( direction )
				{
					case TextDirection.LTR:
						buffer.Direction = Direction.LeftToRight;
						break;

					case TextDirection.RTL:
						buffer.Direction = Direction.RightToLeft;
						break;

					default:
						throw new ArgumentException( nameof( direction ) );
				}

				// Guess other attributes
				buffer.GuessSegmentProperties();

				// Shape it
				_font.Shape( buffer );

				// RTL?
				bool rtl = buffer.Direction == Direction.RightToLeft;

				// Work out glyph scaling and offsetting for super/subscript
				float glyphScale = style.FontSize / overScale;
				float glyphVOffset = 0;
				if ( style.FontVariant == FontVariant.SuperScript )
				{
					glyphScale *= 0.65f;
					glyphVOffset -= style.FontSize * 0.35f;
				}
				if ( style.FontVariant == FontVariant.SubScript )
				{
					glyphScale *= 0.65f;
					glyphVOffset += style.FontSize * 0.1f;
				}

				// Create results and get buffes
				var r = new Result();
				r.GlyphIndicies = bufferSet.GlyphIndicies.Add( (int)buffer.Length, false );
				r.GlyphPositions = bufferSet.GlyphPositions.Add( (int)buffer.Length, false );
				r.Clusters = bufferSet.Clusters.Add( (int)buffer.Length, false );
				r.CodePointXCoords = bufferSet.CodePointXCoords.Add( codePoints.Length, false );
				r.CodePointXCoords.Fill( 0 );

				// Convert points
				var gp = buffer.GlyphPositions;
				var gi = buffer.GlyphInfos;
				float cursorX = 0;
				float cursorY = 0;
				float cursorXCluster = 0;
				for ( int i = 0; i < buffer.Length; i++ )
				{
					r.GlyphIndicies[i] = (ushort)gi[i].Codepoint;
					r.Clusters[i] = (int)gi[i].Cluster + clusterAdjustment;


					// Update code point positions
					if ( !rtl )
					{
						// First cluster, different cluster, or same cluster with lower x-coord
						if ( i == 0 ||
							(r.Clusters[i] != r.Clusters[i - 1]) ||
							(cursorX < r.CodePointXCoords[r.Clusters[i] - clusterAdjustment]) )
						{
							r.CodePointXCoords[r.Clusters[i] - clusterAdjustment] = cursorX;
						}
					}

					// Get the position
					var pos = gp[i];

					// Update glyph position
					r.GlyphPositions[i] = new SKPoint(
						cursorX + pos.XOffset * glyphScale + glyphLetterSpacingAdjustment,
						cursorY - pos.YOffset * glyphScale + glyphVOffset
						);

					// Update cursor position
					cursorX += pos.XAdvance * glyphScale;
					cursorY += pos.YAdvance * glyphScale;

					// Ensure paragraph separator character (0x2029) has some
					// width so it can be seen as part of the selection in the editor.
					if ( pos.XAdvance == 0 && codePoints[(int)gi[i].Cluster] == 0x2029 )
					{
						cursorX += style.FontSize * 2 / 3;
					}

					if ( i + 1 == gi.Length || gi[i].Cluster != gi[i + 1].Cluster )
					{
						cursorX += style.LetterSpacing;
					}

					if ( codePoints[(int)gi[i].Cluster] == ' ' )
					{
						cursorX += style.WordSpacing;
					}

					// Are we falling back for a fixed pitch font and is the next character a 
					// new cluster?  If so advance by the width of the original font, not this
					// fallback font
					if ( forceFixedPitchWidth != 0 )
					{
						// New cluster?
						if ( i + 1 >= buffer.Length || gi[i].Cluster != gi[i + 1].Cluster )
						{
							// Work out fixed pitch position of next cluster
							cursorXCluster += forceFixedPitchWidth * glyphScale;
							if ( cursorXCluster > cursorX )
							{
								// Nudge characters to center them in the fixed pitch width
								if ( i == 0 || gi[i - 1].Cluster != gi[i].Cluster )
								{
									r.GlyphPositions[i].X += (cursorXCluster - cursorX) / 2;
								}

								// Use fixed width character position
								cursorX = cursorXCluster;
							}
							else
							{
								// Character is wider (probably an emoji) so we 
								// allow it to exceed the fixed pitch character width
								cursorXCluster = cursorX;
							}
						}
					}

					// Store RTL cursor position
					if ( rtl )
					{
						// First cluster, different cluster, or same cluster with lower x-coord
						if ( i == 0 ||
							(r.Clusters[i] != r.Clusters[i - 1]) ||
							(cursorX > r.CodePointXCoords[r.Clusters[i] - clusterAdjustment]) )
						{
							r.CodePointXCoords[r.Clusters[i] - clusterAdjustment] = cursorX;
						}
					}
				}

				// Finalize cursor positions by filling in any that weren't
				// referenced by a cluster
				if ( rtl )
				{
					r.CodePointXCoords[0] = cursorX;
					for ( int i = codePoints.Length - 2; i >= 0; i-- )
					{
						if ( r.CodePointXCoords[i] == 0 )
							r.CodePointXCoords[i] = r.CodePointXCoords[i + 1];
					}
				}
				else
				{
					for ( int i = 1; i < codePoints.Length; i++ )
					{
						if ( r.CodePointXCoords[i] == 0 )
							r.CodePointXCoords[i] = r.CodePointXCoords[i - 1];
					}
				}

				// Also return the end cursor position
				r.EndXCoord = new SKPoint( cursorX, cursorY );

				// And some other useful metrics
				ApplyFontMetrics( ref r, style.FontSize );

				// Done
				return r;
			}
		}

		private void ApplyFontMetrics( ref Result result, float fontSize )
		{
			// And some other useful metrics
			result.Ascent = _fontMetrics.Ascent * fontSize / overScale;
			result.Descent = _fontMetrics.Descent * fontSize / overScale;
			result.Leading = _fontMetrics.Leading * fontSize / overScale;
			result.XMin = _fontMetrics.XMin * fontSize / overScale;
		}

		private static Blob GetHarfBuzzBlob( SKStreamAsset asset )
		{
			if ( asset == null )
				throw new ArgumentNullException( nameof( asset ) );

			Blob blob;

			var size = asset.Length;
			var memoryBase = asset.GetMemoryBase();
			if ( memoryBase != IntPtr.Zero )
			{
				// the underlying stream is really a mamory block
				// so save on copying and just use that directly
				blob = new Blob( memoryBase, size, MemoryMode.ReadOnly, () => asset.Dispose() );
			}
			else
			{
				// this could be a forward-only stream, so we must copy
				var ptr = Marshal.AllocCoTaskMem( size );
				asset.Read( ptr, size );
				blob = new Blob( ptr, size, MemoryMode.ReadOnly, () => Marshal.FreeCoTaskMem( ptr ) );
			}

			// make immutable for performance?
			blob.MakeImmutable();

			return blob;
		}
	}
}

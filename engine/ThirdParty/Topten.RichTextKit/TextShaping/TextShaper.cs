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
using Sandbox.UI;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
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
		private TextShaper( SKTypeface typeface, HarfBuzzSharp.Font parent = null )
		{
			// Store typeface
			_typeface = typeface;

			// All variants share the original font data and have their own shaping coordinates.
			if ( parent is not null )
			{
				_font = new HarfBuzzSharp.Font( parent );
			}
			else
			{
				using var blob = typeface.OpenStream( out var index ).ToHarfBuzzBlob();
				using var face = new Face( blob, (uint)index );
				face.UnitsPerEm = typeface.UnitsPerEm;
				_font = new HarfBuzzSharp.Font( face );
			}
			_font.SetScale( overScale, overScale );
			_font.SetFunctionsOpenType();
			if ( typeface.VariationDesignPositionCount > 0 )
				_font.SetVariations( typeface.VariationDesignPosition.Select( p => new Variation { Tag = p.Axis, Value = p.Value } ).ToArray() );

			// Get font metrics for this typeface
			using ( var font = new SKFont( typeface, overScale ) )
			{
				_fontMetrics = font.Metrics;

				// Use the measured cell width for fallback, including fonts without fixed-pitch metadata.
				Span<float> widths = stackalloc float[2];
				font.GetGlyphWidths( "iw", widths, default );
				_isFixedPitch = widths[0] == widths[1];
				if ( _isFixedPitch )
					_fixedCharacterWidth = widths[0];
			}
		}

		internal static void RegisterVariation( SKTypeface original, SKTypeface variant )
		{
			lock ( _shapers )
			{
				if ( !_shapers.ContainsKey( variant ) )
					_shapers.Add( variant, new TextShaper( variant, ForTypeface( original )._font ) );
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

		private sealed record SpaceMeasurement( float FontSize, float Width );
		private volatile SpaceMeasurement _lastSpaceMeasurement;

		internal float GetSpaceWidth( float fontSize )
		{
			var cached = _lastSpaceMeasurement;
			if ( cached?.FontSize == fontSize )
				return cached.Width;

			using var font = new SKFont( _typeface, fontSize );
			// Existing tab stops use glyph ID 32.
			float width = font.MeasureText( [(ushort)32] );
			_lastSpaceMeasurement = new SpaceMeasurement( fontSize, width );
			return width;
		}

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

		static readonly Feature[] _tabularNumbersFeature = new[]
		{
			new Feature( new Tag( 't', 'n', 'u', 'm' ), 1 )
		};



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
			using var font = new SKFont( _typeface, overScale );
			var glyph = font.GetGlyph( style.ReplacementCharacter );
			float glyphScale = style.FontSize / overScale;

			float width = font.MeasureText( [glyph] );

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

				xCoord += width + style.LetterSpacing / glyphScale;
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
				if ( style.FontVariantNumeric == FontVariantNumeric.TabularNums )
				{
					_font.Shape( buffer, _tabularNumbersFeature );
				}
				else
				{
					_font.Shape( buffer );
				}

				// RTL?
				bool rtl = direction == TextDirection.RTL;

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

				var glyphInfos = buffer.GetGlyphInfoSpan();
				var glyphPositions = buffer.GetGlyphPositionSpan();

				var r = new Result();
				r.GlyphIndicies = bufferSet.GlyphIndicies.Add( glyphInfos.Length, false );
				r.GlyphPositions = bufferSet.GlyphPositions.Add( glyphInfos.Length, false );
				r.Clusters = bufferSet.Clusters.Add( glyphInfos.Length, false );
				r.CodePointXCoords = bufferSet.CodePointXCoords.Add( codePoints.Length, false );
				r.CodePointXCoords.Fill( 0 );

				// Convert points
				float cursorX = 0;
				float cursorY = 0;
				float cursorXCluster = 0;
				for ( int i = 0; i < glyphInfos.Length; i++ )
				{
					r.GlyphIndicies[i] = (ushort)glyphInfos[i].Codepoint;
					r.Clusters[i] = (int)glyphInfos[i].Cluster + clusterAdjustment;


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
					var pos = glyphPositions[i];

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
					if ( pos.XAdvance == 0 && codePoints[(int)glyphInfos[i].Cluster] == 0x2029 )
					{
						cursorX += style.FontSize * 2 / 3;
					}

					bool endsCluster = i + 1 == glyphInfos.Length || glyphInfos[i].Cluster != glyphInfos[i + 1].Cluster;
					if ( endsCluster )
					{
						cursorX += style.LetterSpacing;
					}

					if ( codePoints[(int)glyphInfos[i].Cluster] == ' ' )
					{
						cursorX += style.WordSpacing;
					}

					// Are we falling back for a fixed pitch font and is the next character a 
					// new cluster?  If so advance by the width of the original font, not this
					// fallback font
					if ( forceFixedPitchWidth != 0 )
					{
						// New cluster?
						if ( endsCluster )
						{
							// Work out fixed pitch position of next cluster
							cursorXCluster += forceFixedPitchWidth * glyphScale;
							if ( cursorXCluster > cursorX )
							{
								// Nudge characters to center them in the fixed pitch width
								if ( i == 0 || glyphInfos[i - 1].Cluster != glyphInfos[i].Cluster )
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
	}
}

using Sandbox;
using System;
using System.Collections.Generic;
using SkiaSharp;

namespace UITests;

/// <summary>
/// The glyph encoder feeds the GPU text shader: outlines as quadratic curves in em space, plus the
/// CPU-side queries text layout makes of them.
/// </summary>
[TestClass]
public class GpuFontGlyphTest
{
	static readonly SKTypeface Typeface = SKTypeface.FromFamilyName( "Arial" ) ?? SKTypeface.Default;

	static GpuFontGlyphCache.Glyph Encode( char ch )
	{
		using var font = new SKFont( Typeface, 100 );
		return GpuFontGlyphCache.Get( Typeface, font.GetGlyph( ch ) );
	}

	[TestMethod]
	public void EncodesOutlinesInEmSpace()
	{
		var o = Encode( 'O' );

		Assert.IsFalse( o.IsEmpty );
		Assert.IsTrue( o.CurveCount >= 8, "an O is two rings of curves" );
		Assert.IsTrue( o.BandCount >= 1 );
		Assert.IsTrue( o.Bounds.z - o.Bounds.x > 0.3f && o.Bounds.z - o.Bounds.x < 1.0f, "width in ems" );
		Assert.IsTrue( o.Bounds.y < 0 && o.Bounds.w >= 0, "sits on the baseline, y down" );

		Assert.IsTrue( Encode( ' ' ).IsEmpty, "a space has no outline" );
	}

	/// <summary>
	/// Decoration lines skipping ink break around where the outline crosses their band.
	/// </summary>
	[TestMethod]
	public void InterceptsFindTheStems()
	{
		var h = Encode( 'H' );
		var spans = new List<Vector2>();
		float mid = (h.Bounds.y + h.Bounds.w) * 0.5f;

		// Through the crossbar: one solid span
		GpuFontGlyphCache.Intercepts( h, mid - 0.01f, mid + 0.01f, spans );
		Assert.AreEqual( 1, spans.Count );

		// Above it: the two stems
		spans.Clear();
		GpuFontGlyphCache.Intercepts( h, h.Bounds.y + 0.05f, h.Bounds.y + 0.07f, spans );
		Assert.AreEqual( 2, spans.Count );
		Assert.IsTrue( spans[0].y < spans[1].x, "sorted, non overlapping" );
	}

	// GpuFontRootCode: which roots of a quadratic crossing y = 0 count, from the signs of its y values. Bit 0: first root, bit 8: second.
	static uint RootCode( float y1, float y2, float y3 )
	{
		uint i1 = BitConverter.SingleToUInt32Bits( y1 ) >> 31;
		uint i2 = BitConverter.SingleToUInt32Bits( y2 ) >> 30;
		uint i3 = BitConverter.SingleToUInt32Bits( y3 ) >> 29;
		uint shift = (i2 & 2u) | (i1 & ~2u);
		shift = (i3 & 4u) | (shift & ~4u);
		return (0x2E74u >> (int)shift) & 0x0101u;
	}

	// GpuFontSolveHoriz: x of the two points where the curve, relative to the pixel, crosses y = 0
	static Vector2 SolveHoriz( Vector2 p1, Vector2 p2, Vector2 p3 )
	{
		var a = p1 - p2 * 2f + p3;
		var b = p1 - p2;
		float d = MathF.Sqrt( MathF.Max( b.y * b.y - a.y * p1.y, 0f ) );
		float ra = 1f / a.y;
		float t1 = (b.y - d) * ra;
		float t2 = (b.y + d) * ra;

		if ( MathF.Abs( a.y ) < 1f / 65536f )
			t1 = t2 = p1.y / (2f * b.y);

		return new Vector2( (a.x * t1 - b.x * 2f) * t1 + p1.x, (a.x * t2 - b.x * 2f) * t2 + p1.x );
	}

	/// <summary>
	/// Winding at a point from the band data, walked the way GpuFontRay does: pick the band from the across-axis,
	/// stream its curves (swapping axes for a vertical ray), count the crossings ahead of the point, stop at the
	/// sorted early break.
	/// </summary>
	static int WalkWinding( in GpuFontGlyphCache.Glyph glyph, Vector2 em, bool vertical )
	{
		var (curves, bands) = GpuFontGlyphCache.EncodedForTests;

		float across = vertical ? em.x : em.y;
		float bandMin = vertical ? glyph.Bounds.x : glyph.Bounds.y;
		float bandSize = MathF.Max( (vertical ? glyph.Bounds.z : glyph.Bounds.w) - bandMin, 1e-6f );

		int band = Math.Clamp( (int)((across - bandMin) / bandSize * glyph.BandCount), 0, glyph.BandCount - 1 );
		int header = glyph.BandOffset + (vertical ? glyph.BandCount * 2 : 0) + band * 2;
		int offset = (int)bands[header] * 3;
		int count = (int)bands[header + 1];
		var origin = vertical ? new Vector2( em.y, em.x ) : em;

		int winding = 0;
		for ( int i = 0; i < count; i++ )
		{
			int c = offset + i * 3;
			var p1 = curves[c];
			var p2 = curves[c + 1];
			var p3 = curves[c + 2];
			if ( vertical ) (p1, p2, p3) = (new Vector2( p1.y, p1.x ), new Vector2( p2.y, p2.x ), new Vector2( p3.y, p3.x ));
			p1 -= origin; p2 -= origin; p3 -= origin;

			// Sorted by far edge, so once one is behind the point the rest are too
			if ( MathF.Max( p1.x, MathF.Max( p2.x, p3.x ) ) < -0.001f )
				break;

			uint code = RootCode( p1.y, p2.y, p3.y );
			if ( code == 0 )
				continue;

			var r = SolveHoriz( p1, p2, p3 );
			if ( (code & 1u) != 0 && r.x > 0 ) winding++;
			if ( code > 1u && r.y > 0 ) winding--;
		}

		return vertical ? -winding : winding;
	}

	/// <summary>
	/// The band data must agree with the outline: both walks wind non-zero exactly where Skia's path
	/// contains the point, for a straight and a curved glyph.
	/// </summary>
	[TestMethod]
	public void BandWalkWindsLikeTheOutline()
	{
		int upem = Typeface.UnitsPerEm;
		using var font = new SKFont( Typeface, upem ) { Hinting = SKFontHinting.None };

		foreach ( var ch in "HOgm8@" )
		{
			var id = font.GetGlyph( ch );
			var glyph = GpuFontGlyphCache.Get( Typeface, id );
			using var path = font.GetGlyphPath( id );
			Assert.IsFalse( glyph.IsEmpty );

			bool Inside( float x, float y ) => path.Contains( x * upem, y * upem );

			var size = new Vector2( glyph.Bounds.z - glyph.Bounds.x, glyph.Bounds.w - glyph.Bounds.y );
			int inked = 0;
			for ( int y = 0; y < 24; y++ )
			{
				for ( int x = 0; x < 24; x++ )
				{
					var p = new Vector2( glyph.Bounds.x + size.x * (x + 0.5f) / 24f, glyph.Bounds.y + size.y * (y + 0.5f) / 24f );

					// Right on the outline the path and the walk round differently, so skip a point within a hair of it
					const float e = 0.001f;
					bool inside = Inside( p.x, p.y );
					if ( inside != Inside( p.x - e, p.y ) || inside != Inside( p.x + e, p.y ) || inside != Inside( p.x, p.y - e ) || inside != Inside( p.x, p.y + e ) )
						continue;

					Assert.AreEqual( inside, WalkWinding( glyph, p, vertical: false ) != 0, $"'{ch}': horizontal walk at {p}" );
					Assert.AreEqual( inside, WalkWinding( glyph, p, vertical: true ) != 0, $"'{ch}': vertical walk at {p}" );
					if ( inside ) inked++;
				}
			}

			Assert.IsTrue( inked > 24, $"'{ch}': the glyph has ink" );
		}
	}

	[TestMethod]
	public void PlainGlyphsHaveNoColorLayers()
	{
		using var font = new SKFont( Typeface, 100 );
		Assert.IsNull( GpuFontGlyphCache.GetLayers( Typeface, font.GetGlyph( 'A' ) ) );
	}

	[TestMethod]
	public void EmojiComeAsColorLayers()
	{
		using var emoji = SKFontManager.Default.MatchCharacter( 0x1F600 );
		if ( emoji is null ) Assert.Inconclusive( "no emoji font on this machine" );

		using var font = new SKFont( emoji, 100 );
		var layers = GpuFontGlyphCache.GetLayers( emoji, font.GetGlyph( 0x1F600 ) );
		if ( layers is null ) Assert.Inconclusive( "emoji font has no COLR v0 layers" );

		Assert.IsTrue( layers.Length > 1 );
		foreach ( var (glyph, color) in layers )
		{
			Assert.IsFalse( GpuFontGlyphCache.Get( emoji, glyph ).IsEmpty, "every layer is an outline" );
			Assert.IsTrue( color is null || color.Value.a > 0 );
		}
	}
}

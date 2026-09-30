#ifndef UI_TEXT_HLSL
#define UI_TEXT_HLSL

#include "ui/rounded_rect.hlsl"	// GaussErf

// Must match GPUBoxInstance in GPUBoxInstance.cs
struct BoxInstanceData
{
	float4 Rect;
	float4 Color;
	float4 BorderRadius;	// horizontal radii ( top-left, top-right, bottom-left, bottom-right )
	float4 BorderRadiusV;	// vertical radii, same order
	float4 BorderSize;		// left, top, right, bottom
	float4 BorderColorL;
	float4 BorderColorT;
	float4 BorderColorR;
	float4 BorderColorB;
	int TextureIndex;
	int SamplerIndex;
	int BackgroundRepeat;
	float BackgroundAngle;
	float4 BackgroundRect;
	float4 BackgroundTint;
	int BorderImageIndex;
	int BorderImageSamplerIndex;
	int BorderImageMode;
	int BorderImageFill;
	float4 BorderImageSlice;
	float4 BorderImageTint;
	int Flags;
	int ScissorIndex;
	int Mode;
	int TransformIndex;
	int InverseScissorIndex;
	int TextMaskIndex;
	int TextMaskSamplerIndex;
	int BackgroundClip;
	float4 BackgroundClipRect;	// box clip: the inset. text clip: where the mask sits.
	int ShapeIndex;				// into BorderShapeBuffer, or -1 for a plain rounded rect
};

// Must match GPUGradientInstance in GPUBoxInstance.cs. Stop colors are straight
// alpha in sRGB space; Angle is radians - 0 points down the panel for a linear
// gradient, straight up for a conic one.
struct GradientData
{
	float4 StopColors[8];
	float StopOffsets[8];
	int Count;
	float Angle;
	int Type;			// 0 linear, 1 radial, 2 conic
	int SizeMode;		// radial: 0 farthest-side, 1 farthest-corner, 2 closest-side, 3 closest-corner, 4 circle
	float2 Center;		// radial and conic
	int CenterUnits;	// bit 0/1 set when that centre axis is a fraction of the box, not pixels
	int Circle;			// radial: 1 for a circle instead of an ellipse
	int StopUnits;		// bit per stop, set when that offset is a pixel length not a fraction
	int Corner;			// linear: 1 top-left, 2 top-right, 3 bottom-left, 4 bottom-right, 0 for an angle
};

// Glyph evaluation straight from the outline ---------------------------------------------------------------------------------------------------------------
//
// Glyphs are quadratic Béziers in em space, y down, three points per curve in GlyphCurves. Each glyph has bands
// (GlyphBands: triplet offset, count per horizontal band, then per vertical band) holding copies of the curves a ray
// through that band can cross, the one reaching furthest along the ray first so the walk can stop early.
// Coverage is Lengyel's dual ray method ("GPU-Centered Font Rendering Directly from Glyph Outlines", JCGT 2017).
// GlyphTable holds every encoded glyph, see GpuFontGlyphCache.cs.

StructuredBuffer<float2> GlyphCurves < Attribute( "GlyphCurves" ); >;
StructuredBuffer<uint> GlyphBands < Attribute( "GlyphBands" ); >;

struct GpuFontGlyph
{
	float4 Bounds;		// em: min x, min y, max x, max y
	int BandOffset;
	int BandCount;
	int CurveStart;
	int CurveCount;
	int Index;
};

StructuredBuffer<GpuFontGlyph> GlyphTable < Attribute( "GlyphTable" ); >;

// Which roots of a quadratic crossing y = 0 count, from the signs of its y values. Bit 0: first root, bit 8: second.
uint GpuFontRootCode( float y1, float y2, float y3 )
{
	uint i1 = asuint( y1 ) >> 31U;
	uint i2 = asuint( y2 ) >> 30U;
	uint i3 = asuint( y3 ) >> 29U;
	uint shift = ( i2 & 2U ) | ( i1 & ~2U );
	shift = ( i3 & 4U ) | ( shift & ~4U );
	return ( 0x2E74U >> shift ) & 0x0101U;
}

// Cumulative distribution of the unit gaussian
float GaussCdf( float x )
{
	return 0.5 + 0.5 * GaussErf( float2( x * 0.70710678, 0 ) ).x;
}

// x of the two points where the curve, relative to the pixel, crosses y = 0
float2 GpuFontSolveHoriz( float2 p1, float2 p2, float2 p3 )
{
	float2 a = p1 - p2 * 2.0 + p3;
	float2 b = p1 - p2;
	float d = sqrt( max( b.y * b.y - a.y * p1.y, 0.0 ) );
	float ra = 1.0 / a.y;
	float t1 = ( b.y - d ) * ra;
	float t2 = ( b.y + d ) * ra;

	if ( abs( a.y ) < 1.0 / 65536.0 )
		t1 = t2 = p1.y / ( 2.0 * b.y );

	return float2( ( a.x * t1 - b.x * 2.0 ) * t1 + p1.x, ( a.x * t2 - b.x * 2.0 ) * t2 + p1.x );
}

// A ray from the point along +x (or +y). cov is the filtered winding - a one pixel box, or a gaussian of sigma
// pixels for shadows - wgt how near the closest crossing is, winding the integer crossings ahead of the point.
// The vertical ray swaps axes, which mirrors the outline, so its signs come back flipped.
void GpuFontRay( GpuFontGlyph glyph, float2 em, float pixelsPerEm, bool vertical, float sigma, out float cov, out float wgt, out int winding )
{
	cov = 0.0;
	wgt = 0.0;
	winding = 0;
	if ( glyph.BandCount <= 0 ) return;

	float2 bmin = glyph.Bounds.xy;
	float2 bsize = max( glyph.Bounds.zw - bmin, 1e-6 );

	float across = vertical ? em.x : em.y;
	float bandMin = vertical ? bmin.x : bmin.y;
	float bandSize = vertical ? bsize.x : bsize.y;

	int band = clamp( int( ( across - bandMin ) / bandSize * glyph.BandCount ), 0, glyph.BandCount - 1 );
	int header = glyph.BandOffset + ( vertical ? glyph.BandCount * 2 : 0 ) + band * 2;
	uint offset = GlyphBands[header] * 3;
	uint count = min( GlyphBands[header + 1], 4096u ); // bad data must never hang the GPU
	float2 origin = vertical ? em.yx : em;

	// A curve entirely this far behind the pixel can't touch it, in em so the loop compares without a multiply
	float xcut = -max( 0.5, sigma * 3.0 ) / pixelsPerEm;

	[loop]
	for ( uint i = 0; i < count; i++ )
	{
		uint c = offset + i * 3;
		float2 p1 = GlyphCurves[c], p2 = GlyphCurves[c + 1], p3 = GlyphCurves[c + 2];

		if ( vertical ) { p1 = p1.yx; p2 = p2.yx; p3 = p3.yx; }

		p1 -= origin; p2 -= origin; p3 -= origin;

		// Sorted by far edge, so once one is behind the pixel the rest are too
		if ( max( p1.x, max( p2.x, p3.x ) ) < xcut )
			break;

		uint code = GpuFontRootCode( p1.y, p2.y, p3.y );
		if ( code == 0 )
			continue;

		float2 r = GpuFontSolveHoriz( p1, p2, p3 ) * pixelsPerEm;

		float2 step = sigma > 0.0 ? float2( GaussCdf( r.x / sigma ), GaussCdf( r.y / sigma ) ) : saturate( r + 0.5 );

		if ( ( code & 1U ) != 0U )
		{
			cov += step.x;
			wgt = max( wgt, saturate( 1.0 - abs( r.x ) * 2.0 ) );
			winding += r.x > 0.0 ? 1 : 0;
		}

		if ( code > 1U )
		{
			cov -= step.y;
			wgt = max( wgt, saturate( 1.0 - abs( r.y ) * 2.0 ) );
			winding -= r.y > 0.0 ? 1 : 0;
		}
	}

	if ( vertical )
	{
		cov = -cov;
		winding = -winding;
	}
}

// Antialiased coverage of the glyph at an em space point, with the pixel footprint given in em
float GpuFontCoverage( GpuFontGlyph glyph, float2 em, float pixelsPerEm )
{
	float xcov, xwgt, ycov, ywgt;
	int w;
	GpuFontRay( glyph, em, pixelsPerEm, false, 0.0, xcov, xwgt, w );
	GpuFontRay( glyph, em, pixelsPerEm, true, 0.0, ycov, ywgt, w );

	float weighted = abs( xcov * xwgt + ycov * ywgt ) / max( xwgt + ywgt, 1.0 / 65536.0 );
	float fallback = min( abs( xcov ), abs( ycov ) );
	return saturate( max( weighted, fallback ) );
}

// Signed distance in em to the outline, negative inside. Curves are flattened to segments; every curve of the
// glyph is visited, so this is for outlines and shadows, not the fill.
float GpuFontSignedDistance( GpuFontGlyph glyph, float2 em )
{
	const int segments = 8;
	float best = 1e30;

	int curveCount = min( glyph.CurveCount, 4096 );

	[loop]
	for ( int c = 0; c < curveCount; c++ )
	{
		int i = ( glyph.CurveStart + c ) * 3;
		float2 p1 = GlyphCurves[i] - em;
		float2 p2 = GlyphCurves[i + 1] - em;
		float2 p3 = GlyphCurves[i + 2] - em;

		float2 prev = p1;
		[unroll]
		for ( int s = 1; s <= segments; s++ )
		{
			float t = s / (float)segments;
			float u = 1.0 - t;
			float2 next = p1 * ( u * u ) + p2 * ( 2.0 * u * t ) + p3 * ( t * t );

			float2 ab = next - prev;
			float k = saturate( -dot( prev, ab ) / max( dot( ab, ab ), 1e-12 ) );
			float2 q = prev + ab * k;
			best = min( best, dot( q, q ) );

			prev = next;
		}
	}

	float d = sqrt( best );

	bool inside = false;
	if ( all( em >= glyph.Bounds.xy ) && all( em <= glyph.Bounds.zw ) )
	{
		float cov, wgt;
		int winding;
		GpuFontRay( glyph, em, 1.0, false, 0.0, cov, wgt, winding );
		inside = winding != 0;
	}

	return inside ? -d : d;
}

// Text instances --------------------------------------------------------------------------------------------------------------------------------------------
//
// Text rides the box instance. Mode 4 is a glyph, mode 5 a decoration line; GpuFontText.cs packs them like this:
//   glyph: BackgroundRect = origin x, origin y (layout px), pixels per em, dilation px. Flags = the GlyphTable index.
//   line:  BackgroundRect = x0, x1, y0, y1. BorderRadius = centre y, thickness, double line offset, UnderlineType (0 solid 1 dotted 2 dashed 3 double 4 wavy).
//   both:  BorderRadiusV.x = blur sigma px. BorderImageMode = flags. BorderImageSlice = the rect a text gradient spans, TextureIndex the gradient.
// Coverage is evaluated at a layout space position p with a pixel footprint ps, so the same code serves the
// batched UI shader (ps from screen derivatives) and everything compositing a block through TextComposite.

#define TEXT_ALIASED 1	// GpuFontText.FlagAliased

// The pixel's footprint in layout space, from the screen derivatives of a layout position. Never zero, so
// the coverage divides below stay finite.
float TextFootprint( float2 p )
{
	return max( 0.5 * ( length( ddx( p ) ) + length( ddy( p ) ) ), 0.0001 );
}

// How much of [lo, hi] a pixel of footprint ps centred at p covers
float TextBoxCoverage( float lo, float hi, float p, float ps )
{
	return saturate( ( min( hi, p + ps * 0.5 ) - max( lo, p - ps * 0.5 ) ) / ps );
}

// The same interval seen through a gaussian of the given sigma
float TextBlurredBoxCoverage( float lo, float hi, float p, float sigma )
{
	return GaussCdf( ( p - lo ) / sigma ) - GaussCdf( ( p - hi ) / sigma );
}

// A blur sigma widened by the pixel footprint, so a sigma under a pixel still spans pixels instead of falling between them
float TextBlurSigma( float sigma, float ps )
{
	return sqrt( sigma * sigma + 0.12 * ps * ps );
}

float TextLineCoverage( BoxInstanceData inst, float2 p, float ps )
{
	float x0 = inst.BackgroundRect.x, x1 = inst.BackgroundRect.y, y0 = inst.BackgroundRect.z, y1 = inst.BackgroundRect.w;
	float y = inst.BorderRadius.x;
	float t = inst.BorderRadius.y;
	int style = int( inst.BorderRadius.w );
	float sigma = inst.BorderRadiusV.x;

	if ( sigma > 0.0 )
	{
		// A blurred line is close enough to a blurred box whatever its style
		float s = TextBlurSigma( sigma, ps );
		return TextBlurredBoxCoverage( x0, x1, p.x, s ) * TextBlurredBoxCoverage( y0, y1, p.y, s );
	}

	if ( style == 1 )
	{
		// Dots of the line's thickness every two thicknesses: round caps on zero length dashes
		float k = round( ( p.x - x0 ) / ( 2.0 * t ) );
		float cx = x0 + k * 2.0 * t;
		if ( cx < x0 - 0.001 || cx > x1 + 0.001 ) return 0.0;
		float d = length( p - float2( cx, y ) ) - t * 0.5;
		return saturate( 0.5 - d / ps );
	}

	if ( style == 4 )
	{
		// The wave Skia drew as a polyline: sin( x / 4 ) * 1.25, stroked with round caps
		float u = p.x - x0;
		float f = y + sin( u * 0.25 ) * 1.25;
		float slope = cos( u * 0.25 ) * 0.3125;
		float d = abs( p.y - f ) / sqrt( 1.0 + slope * slope ) - t * 0.5;
		return saturate( 0.5 - d / ps ) * TextBoxCoverage( x0 - t * 0.5, x1 + t * 0.5, p.x, ps );
	}

	float xcov = TextBoxCoverage( x0, x1, p.x, ps );
	float cov = xcov * TextBoxCoverage( y0, y1, p.y, ps );

	if ( style == 3 )
	{
		// Second line two thicknesses away, above for an overline
		float off = inst.BorderRadius.z;
		cov = max( cov, xcov * TextBoxCoverage( y0 + off, y1 + off, p.y, ps ) );
	}
	else if ( style == 2 )
	{
		// Three on, three off, starting a thickness in
		if ( frac( ( p.x - x0 + t ) / ( 6.0 * t ) ) >= 0.5 ) cov = 0.0;
	}

	return cov;
}

// A blurred glyph: rows around the pixel, each blurred analytically along x by the ray and weighted by the
// gaussian in y. Rows sit on a grid in glyph space rather than relative to the pixel, so neighbouring pixels
// share them and the quadrature error stays smooth instead of banding. Rows step by half a sigma, which keeps the
// blurred edge within an eighth of a sigma of where it belongs, and rows the outline never reaches cost nothing.
float TextGlyphShadow( GpuFontGlyph glyph, float2 em, float scale, float sigma, float ps )
{
	float sx = TextBlurSigma( sigma, ps );
	float step = max( ps, sigma * 0.5 );
	int n = int( ceil( 2.5 * sx / step ) );

	float y = em.y * scale;
	float base = floor( y / step ) * step;
	float2 reach = glyph.Bounds.yw * scale;

	float sum = 0.0, wsum = 0.0;

	[loop]
	for ( int k = -n; k <= n + 1; k++ )
	{
		float row = base + k * step;
		float dy = row - y;
		float w = exp( -( dy * dy ) / ( 2.0 * sx * sx ) );
		wsum += w;
		if ( row < reach.x || row > reach.y )
			continue;

		float cov, wgt;
		int winding;
		GpuFontRay( glyph, em + float2( 0, dy / scale ), scale, false, sx, cov, wgt, winding );
		sum += saturate( abs( cov ) ) * w;
	}

	return sum / wsum;
}

// Antialiased coverage of a glyph at em, drawn at scale pixels per em with a pixel footprint of ps, dilated by
// dilate pixels or blurred by sigma pixels
float GlyphCoverage( GpuFontGlyph glyph, float2 em, float scale, float dilate, float sigma, float ps, bool aliased )
{
	// A hard shadow carries a token sigma, see TextBlock.cs
	if ( sigma > 0.05 )
		return TextGlyphShadow( glyph, em, scale, sigma, ps );

	float cov;
	int n = ps > 1.05 ? min( int( ceil( ps ) ), 4 ) : 1; // a footprint of 1.0000001 is not minified
	if ( dilate > 0.0 )
	{
		float d = GpuFontSignedDistance( glyph, em ) * scale - dilate;
		cov = saturate( 0.5 - d / ps );
	}
	else if ( any( abs( em - ( glyph.Bounds.xy + glyph.Bounds.zw ) * 0.5 ) > ( glyph.Bounds.zw - glyph.Bounds.xy ) * 0.5 + 0.51 * ps / scale ) )
	{
		// The quad is a pixel wider than the outline, further than half a footprint out nothing can be covered
		cov = 0.0;
	}
	else if ( n > 1 )
	{
		// Minified: a ray only filters along itself, so integrate across the pixel's footprint as well
		float sub = ps / n;
		cov = 0.0;

		[loop]
		for ( int y = 0; y < n; y++ )
		{
			[loop]
			for ( int x = 0; x < n; x++ )
				cov += GpuFontCoverage( glyph, em + ( float2( x + 0.5, y + 0.5 ) * sub - ps * 0.5 ) / scale, scale / sub );
		}

		cov /= n * n;
	}
	else
	{
		cov = GpuFontCoverage( glyph, em, scale / ps );
	}

	return aliased ? ( cov >= 0.5 ? 1.0 : 0.0 ) : cov;
}

// Coverage of a text instance at layout position p with pixel footprint ps
float TextCoverage( BoxInstanceData inst, float2 p, float ps )
{
	if ( inst.Mode == 5 )
		return TextLineCoverage( inst, p, ps );

	float2 origin = inst.BackgroundRect.xy;
	float size = inst.BackgroundRect.z;		// pixels per em
	float dilate = inst.BackgroundRect.w;
	float sigma = inst.BorderRadiusV.x;

	return GlyphCoverage( GlyphTable[inst.Flags], ( p - origin ) / size, size, dilate, sigma, ps, ( inst.BorderImageMode & TEXT_ALIASED ) != 0 );
}

// The text gradient exactly as RichTextKit built its Skia shader, so gradients keep looking the same.
// rect is x, y, w, h in layout space; p a layout position.
float4 EvaluateTextGradient( GradientData g, float4 rect, float2 p )
{
	float w = rect.z, h = rect.w;
	p -= rect.xy;

	float t;
	if ( g.Type == 0 )
	{
		float2 c = float2( w, h ) * 0.5;
		float angle = radians( 180.0 + g.Angle );
		float2x2 rot = float2x2( cos( angle ), -sin( angle ), sin( angle ), cos( angle ) );
		float2 s = c + mul( rot, float2( w * 0.5, 0 ) - c );
		float2 e = c + mul( rot, float2( w * 0.5, h ) - c );

		float2 span = abs( e - s );
		span = float2( span.x == 0.0 ? 1.0 : span.x, span.y == 0.0 ? 1.0 : span.y );
		float2 sc = float2( w, h ) / span;

		float2 q = c + ( p - c ) / sc;
		t = dot( q - s, e - s ) / max( dot( e - s, e - s ), 1e-6 );
	}
	else
	{
		float2 c;
		c.x = ( g.CenterUnits & 1 ) ? g.Center.x * w : g.Center.x;
		c.y = ( g.CenterUnits & 2 ) ? g.Center.y * h : g.Center.y;

		float radius = max( w, h );
		float2 nearSide = float2( min( c.x, w - c.x ), min( c.y, h - c.y ) );
		float2 farSide = float2( max( c.x, w - c.x ), max( c.y, h - c.y ) );

		float2 sc;
		if ( g.SizeMode == 1 || g.SizeMode == 3 )
		{
			float2 corners[4] = { c, float2( w, 0 ) - c, float2( w, h ) - c, float2( 0, h ) - c };
			float2 pick = corners[0];
			for ( int i = 1; i < 4; i++ )
			{
				bool better = g.SizeMode == 1 ? length( corners[i] ) > length( pick ) : length( corners[i] ) < length( pick );
				if ( better ) pick = corners[i];
			}
			sc = pick / ( float2( w, h ) * 0.71 );
		}
		else if ( g.SizeMode == 2 )
			sc = nearSide / float2( w, h );
		else
			sc = farSide / float2( w, h );

		if ( g.SizeMode != 4 )
			sc *= float2( w, h ) / radius;

		float2 q = c + ( p - c ) / sc;
		t = length( q - c ) / radius;
	}

	t = saturate( t );

	int last = g.Count - 1;
	if ( t <= g.StopOffsets[0] ) return g.StopColors[0];
	if ( t >= g.StopOffsets[last] ) return g.StopColors[last];

	float4 col = g.StopColors[last];
	[loop]
	for ( int s = 0; s < last; s++ )
	{
		float o1 = g.StopOffsets[s + 1];
		if ( t <= o1 )
		{
			float o0 = g.StopOffsets[s];
			col = lerp( g.StopColors[s], g.StopColors[s + 1], saturate( ( t - o0 ) / max( o1 - o0, 1e-6 ) ) );
			break;
		}
	}

	return col;
}

// A whole text block composited per pixel ----------------------------------------------------------------------------------------------------------------
//
// GpuFontText.Upload puts a block's instances in TextInstances and bins them into 16px tiles in TextTiles. Anything
// that draws a block as one quad - the texture rasterizer, text particles - walks its tile here and composites the
// instances in order, straight alpha in the block's own (sRGB encoded) space like Skia did.

#define TEXT_TILE_SIZE 16	// GpuFontText.TileSize

StructuredBuffer<BoxInstanceData> TextInstances < Attribute( "TextInstances" ); >;
StructuredBuffer<uint> TextTiles < Attribute( "TextTiles" ); >;

// One block's placement, from GpuFontText.Bind( attributes, placement )
int TextInstanceOffset < Attribute( "TextInstanceOffset" ); >;
int TextTileOffset < Attribute( "TextTileOffset" ); >;
int TextTilesX < Attribute( "TextTilesX" ); >;
int TextWidth < Attribute( "TextWidth" ); >;
int TextHeight < Attribute( "TextHeight" ); >;

// p is a position in the block's pixel space, ps the pixel footprint there. Transparent texels take baseColor,
// so bilinear filtering never drags black into glyph edges.
float4 TextComposite( float2 p, float ps, int instanceOffset, int tileOffset, int tilesX, int2 size, float3 baseColor )
{
	if ( any( size <= 0 ) ) return float4( baseColor, 0.0 );

	int2 tile = clamp( int2( p ) / TEXT_TILE_SIZE, 0, ( size + TEXT_TILE_SIZE - 1 ) / TEXT_TILE_SIZE - 1 );
	int tileIndex = tileOffset + tile.y * tilesX + tile.x;
	uint first = TextTiles[tileIndex];
	uint last = min( TextTiles[tileIndex + 1], first + 65536u ); // bad data must never hang the GPU

	float4 acc = 0.0;

	[loop]
	for ( uint i = first; i < last; i++ )
	{
		BoxInstanceData inst = TextInstances[instanceOffset + TextTiles[i]];
		if ( any( p < inst.Rect.xy ) || any( p > inst.Rect.xy + inst.Rect.zw ) )
			continue;

		float4 col = inst.Color;
		float cov = inst.Mode == 0
			? TextBoxCoverage( inst.Rect.x, inst.Rect.x + inst.Rect.z, p.x, ps ) * TextBoxCoverage( inst.Rect.y, inst.Rect.y + inst.Rect.w, p.y, ps )
			: TextCoverage( inst, p, ps );

		float a = saturate( col.a ) * cov;
		if ( a <= 0.0 ) continue;

		acc.rgb = col.rgb * a + acc.rgb * ( 1.0 - a );
		acc.a = a + acc.a * ( 1.0 - a );
	}

	return acc.a > 0.0 ? float4( acc.rgb / acc.a, acc.a ) : float4( baseColor, 0.0 );
}

#endif // UI_TEXT_HLSL

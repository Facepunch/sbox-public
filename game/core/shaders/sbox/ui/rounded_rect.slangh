#ifndef ROUNDED_RECT_HLSL
#define ROUNDED_RECT_HLSL

// Rounded rectangle distance and coverage --------------------------------------------------------------------------------------------------------------
//
// Pixels of the box's own space, y down. Corners are quarter ellipses like CSS, radii packed
// ( top-left, top-right, bottom-left, bottom-right ) the way BorderRadii.ToVector4() emits them.

// Exact for circles; for ellipses the usual first order approximation - exact on the edge with a unit gradient
// there, which is all coverage needs.
float RoundedRectCornerDistance( float2 q, float2 r )
{
	if ( r.x == r.y )
		return length( q ) - r.x;

	r = max( r, 0.0001 );
	float k0 = length( q / r );
	float k1 = length( q / ( r * r ) );
	return k0 * ( k0 - 1.0 ) / k1;
}

float RoundedRectSdf( float2 p, float2 b, float4 radiiH, float4 radiiV )
{
	// Only the two radii on a side have to fit, so opposite corners can both claim a point. The shape is the
	// intersection of every corner that does, hence the max rather than the first match.
	float2 e = abs( p ) - b;
	float d = max( e.x, e.y );

	if ( p.x < -b.x + radiiH.x && p.y < -b.y + radiiV.x )
	{
		float2 r = float2( radiiH.x, radiiV.x );
		d = max( d, RoundedRectCornerDistance( float2( -b.x + r.x - p.x, -b.y + r.y - p.y ), r ) );
	}

	if ( p.x > b.x - radiiH.y && p.y < -b.y + radiiV.y )
	{
		float2 r = float2( radiiH.y, radiiV.y );
		d = max( d, RoundedRectCornerDistance( float2( p.x - b.x + r.x, -b.y + r.y - p.y ), r ) );
	}

	if ( p.x < -b.x + radiiH.z && p.y > b.y - radiiV.z )
	{
		float2 r = float2( radiiH.z, radiiV.z );
		d = max( d, RoundedRectCornerDistance( float2( -b.x + r.x - p.x, p.y - b.y + r.y ), r ) );
	}

	if ( p.x > b.x - radiiH.w && p.y > b.y - radiiV.w )
	{
		float2 r = float2( radiiH.w, radiiV.w );
		d = max( d, RoundedRectCornerDistance( float2( p.x - b.x + r.x, p.y - b.y + r.y ), r ) );
	}

	return d;
}

float RoundedRectSdf( float2 p, float2 b, float4 radii )
{
	return RoundedRectSdf( p, b, radii, radii );
}

float RoundedRectSdfUv( float2 uv, float2 size, float4 radiiH, float4 radiiV )
{
	return RoundedRectSdf( ( uv - 0.5 ) * size, size * 0.5, radiiH, radiiV );
}

float RoundedRectSdfUv( float2 uv, float2 size, float4 radii )
{
	return RoundedRectSdf( ( uv - 0.5 ) * size, size * 0.5, radii, radii );
}

// Coverage from a signed distance - a one pixel ramp on the edge, taken from the distance's screen space
// gradient so it stays one screen pixel under transforms and on world panels.
float SdfCoverage( float d )
{
	float grad = length( float2( ddx( d ), ddy( d ) ) );
	return saturate( 0.5 - d / max( grad, 0.0001 ) );
}

// Coverage of the band between two nested shapes, e.g. a border
float SdfBandCoverage( float dOuter, float dInner )
{
	return saturate( SdfCoverage( dOuter ) - SdfCoverage( dInner ) );
}

// Gaussian blurred rounded rect ---------------------------------------------------------------------------------------------------------------------------
//
// The box-shadow shape: a rounded rect blurred by a gaussian of the given sigma (CSS: half the blur radius), evaluated
// analytically. Along x each row of the box is a segment, whose blur is a difference of error functions; those are
// then integrated along y with a few gaussian-weighted samples. After Evan Wallace's "Fast Rounded Rectangle Shadows",
// extended to per-corner elliptical radii.

// Error function, good to a few 1e-4
float2 GaussErf( float2 x )
{
	float2 s = sign( x );
	float2 a = abs( x );
	x = 1.0 + ( 0.278393 + ( 0.230389 + 0.078108 * ( a * a ) ) * a ) * a;
	x *= x;
	return s - s / ( x * x );
}

float Gaussian( float x, float sigma )
{
	return exp( -( x * x ) / ( 2.0 * sigma * sigma ) ) / ( 2.50662827463 * sigma );
}

// How far a corner pulls its side in at a row, dy being the row's distance from that corner's end of the box
float RoundedRectCornerInset( float dy, float2 r )
{
	if ( dy >= r.y ) return 0.0;

	float t = ( r.y - dy ) / max( r.y, 0.0001 );
	return r.x - r.x * sqrt( saturate( 1.0 - t * t ) );
}

float2 RoundedRectRowExtent( float y, float2 b, float4 radiiH, float4 radiiV )
{
	// Both corners on a side, not the one on this half of the box: a vertical radius over half the height
	// reaches past the centre line.
	float dTop = y + b.y;
	float dBottom = b.y - y;

	float insetL = max( RoundedRectCornerInset( dTop, float2( radiiH.x, radiiV.x ) ),
						RoundedRectCornerInset( dBottom, float2( radiiH.z, radiiV.z ) ) );

	float insetR = max( RoundedRectCornerInset( dTop, float2( radiiH.y, radiiV.y ) ),
						RoundedRectCornerInset( dBottom, float2( radiiH.w, radiiV.w ) ) );

	return float2( -b.x + insetL, b.x - insetR );
}

float RoundedRectShadowRow( float x, float y, float sigma, float2 b, float4 radiiH, float4 radiiV )
{
	float2 extent = RoundedRectRowExtent( y, b, radiiH, radiiV );
	float2 integral = 0.5 * GaussErf( ( x - extent ) * ( 0.70710678 / sigma ) );
	return integral.x - integral.y;
}

float RoundedRectShadow( float2 p, float2 b, float4 radiiH, float4 radiiV, float sigma )
{
	float low = p.y - b.y;
	float high = p.y + b.y;
	float start = clamp( -3.0 * sigma, low, high );
	float end = clamp( 3.0 * sigma, low, high );

	const int nSamples = 8;
	float step = ( end - start ) / nSamples;
	float y = start + step * 0.5;
	float value = 0.0;

	[unroll]
	for ( int i = 0; i < nSamples; i++ )
	{
		value += RoundedRectShadowRow( p.x, p.y - y, sigma, b, radiiH, radiiV ) * Gaussian( y, sigma ) * step;
		y += step;
	}

	return value;
}

#endif // ROUNDED_RECT_HLSL

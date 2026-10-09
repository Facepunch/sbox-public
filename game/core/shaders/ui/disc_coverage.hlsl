#ifndef UI_DISC_COVERAGE_HLSL
#define UI_DISC_COVERAGE_HLSL

// Position derivatives, rather than derivatives of abs(distance), keep thin centerlines stable.
float UIPixelWidth( float2 normal, float2 pixelX, float2 pixelY )
{
	return max( length( float2( dot( normal, pixelX ), dot( normal, pixelY ) ) ), 0.000001 );
}

float2 UINormal( float2 v )
{
	float size = length( v );
	return size > 0.000001 ? v / size : float2( 1, 0 );
}

float UIDiscCoverage( float2 p, float width, float2 pixelX, float2 pixelY )
{
	// Clamp each principal screen axis separately and preserve area, rather than inflating an already wide axis.
	float scale = max( max( abs( pixelX.x ), abs( pixelX.y ) ), max( abs( pixelY.x ), abs( pixelY.y ) ) );
	scale = max( scale, 0.000001 );
	float2 x = pixelX / scale, y = pixelY / scale;
	float xx = x.x * x.x + y.x * y.x, xy = x.x * x.y + y.x * y.y, yy = x.y * x.y + y.y * y.y;
	float largest = 0.5 * ( xx + yy + sqrt( ( xx - yy ) * ( xx - yy ) + 4.0 * xy * xy ) );
	float2 major = abs( xy ) > 0.000001 || xx > yy ? UINormal( float2( largest - yy, xy ) ) : float2( 0, 1 );
	float2 minor = float2( -major.y, major.x );
	float2 footprint = float2( sqrt( largest ), abs( x.x * y.y - x.y * y.x ) / max( sqrt( largest ), 0.000001 ) ) * scale;
	float2 diameter = max( width, max( footprint, 0.000001 ) );
	float2 radii = diameter * 0.5;
	float2 q = float2( dot( p, major ), dot( p, minor ) );
	float k0 = length( q / radii );
	float smallestRadius = min( radii.x, radii.y );
	float2 gradient = ( q / radii ) * ( smallestRadius / radii );
	float k1 = length( gradient );
	float distance;
	float2 normal;
	if ( k0 > 0.000001 && k1 > 0.0 )
	{
		distance = k0 * ( k0 - 1.0 ) * smallestRadius / k1;
		normal = ( gradient.x * major + gradient.y * minor ) / k1;
	}
	else
	{
		distance = -smallestRadius;
		normal = radii.x < radii.y ? major : minor;
	}
	float thinness = ( width / diameter.x ) * ( width / diameter.y );
	return saturate( 0.5 - distance / UIPixelWidth( normal, pixelX, pixelY ) ) * thinness;
}

#endif

#ifndef UI_BORDER_STYLE_HLSL
#define UI_BORDER_STYLE_HLSL

#include "ui/disc_coverage.hlsl"

// Must match Sandbox.UI.BorderStyle. One style applies to the whole border.
#define BORDER_SOLID 0
#define BORDER_NONE 1
#define BORDER_HIDDEN 2
#define BORDER_DOTTED 3
#define BORDER_DASHED 4
#define BORDER_DOUBLE 5
#define BORDER_GROOVE 6
#define BORDER_RIDGE 7
#define BORDER_INSET 8
#define BORDER_OUTSET 9

// Arc length of (a cos(t), b sin(t)), integrated with eight-point Gauss-Legendre quadrature.
float BorderEllipseLength( float2 radii, float angle )
{
	if ( radii.x == radii.y ) return radii.x * angle;
	static const float4 nodes = float4( 0.1834346425, 0.5255324099, 0.7966664774, 0.9602898565 );
	static const float4 weights = float4( 0.3626837834, 0.3137066459, 0.2223810345, 0.1012285363 );
	float result = 0.0;
	[unroll] for ( int i = 0; i < 4; i++ )
	{
		float2 t = angle * 0.5 * float2( 1.0 - nodes[i], 1.0 + nodes[i] );
		float2 sine = sin( t ), cosine = cos( t );
		result += weights[i] * ( length( radii * float2( sine.x, cosine.x ) ) + length( radii * float2( sine.y, cosine.y ) ) );
	}
	return result * angle * 0.5;
}

struct BorderContour
{
	float Along;
	float Length;
	float Across;
	float2 Normal;
};

// Walk the four straight sides and elliptical corners of the border's midline.
// Work is constant per pixel, independent of the number of dashes or dots.
BorderContour RoundedBorderContour( float2 p, BoxInstanceData inst )
{
	float4 inset = inst.BorderSize * 0.5;
	float2 size = max( inst.Rect.zw - inset.xy - inset.zw, 0.0 );
	p -= inset.xy;
	float4 h = max( inst.BorderRadius - inset.xzxz, 0.0 );
	float4 v = max( inst.BorderRadiusV - inset.yyww, 0.0 );
	float4 keep = step( 0.0001, h ) * step( 0.0001, v );
	h *= keep; v *= keep;
	// Clockwise, starting on the top edge immediately after the top-left corner.
	float2 starts[4] = { float2( h.x, 0 ), float2( size.x, v.y ), float2( size.x - h.w, size.y ), float2( 0, size.y - v.z ) };
	float2 ends[4] = { float2( size.x - h.y, 0 ), float2( size.x, size.y - v.w ), float2( h.z, size.y ), float2( 0, v.x ) };
	float2 centers[4] = { float2( size.x - h.y, v.y ), float2( size.x - h.w, size.y - v.w ), float2( h.z, size.y - v.z ), float2( h.x, v.x ) };
	float2 axes[4] = { float2( 0, -1 ), float2( 1, 0 ), float2( 0, 1 ), float2( -1, 0 ) };
	float2 radii[4] = { float2( v.y, h.y ), float2( h.w, v.w ), float2( v.z, h.z ), float2( h.x, v.x ) };
	BorderContour result = (BorderContour)0;
	float nearest = 1e30;
	[unroll] for ( int i = 0; i < 4; i++ )
	{
		float2 delta = ends[i] - starts[i];
		float extent = length( delta );
		float t = saturate( dot( p - starts[i], delta ) / max( dot( delta, delta ), 0.000001 ) );
		float2 relative = p - lerp( starts[i], ends[i], t );
		float distance = dot( relative, relative );
		if ( distance < nearest )
		{
			nearest = distance;
			result.Along = result.Length + t * extent;
			result.Normal = axes[i];
			result.Across = dot( relative, result.Normal );
		}
		result.Length += extent;
		if ( all( radii[i] > 0.0 ) )
		{
			float2 nextAxis = axes[( i + 1 ) % 4];
			float2 q = p - centers[i];
			float2 unit = float2( dot( q, axes[i] ), dot( q, nextAxis ) ) / radii[i];
			float angle = clamp( atan2( unit.y, unit.x ), 0.0, 1.57079632679 );
			// Radial projection is not the closest point on an ellipse. Refine the angle
			// along its normal, otherwise wide elliptical corners stretch dots into wedges.
			float2 local = float2( dot( q, axes[i] ), dot( q, nextAxis ) );
			float2 r = radii[i];
			if ( r.x != r.y )
			{
				[unroll] for ( int iteration = 0; iteration < 5; iteration++ )
				{
					float sine = sin( angle ), cosine = cos( angle );
					float derivative = ( r.y * r.y - r.x * r.x ) * sine * cosine + r.x * local.x * sine - r.y * local.y * cosine;
					float curvature = ( r.y * r.y - r.x * r.x ) * ( cosine * cosine - sine * sine ) + r.x * local.x * cosine + r.y * local.y * sine;
					if ( curvature > 0.000001 ) angle = clamp( angle - clamp( derivative / curvature, -0.25, 0.25 ), 0.0, 1.57079632679 );
				}
			}
			float2 radial = float2( cos( angle ), sin( angle ) );
			float2 point = centers[i] + axes[i] * radial.x * radii[i].x + nextAxis * radial.y * radii[i].y;
			relative = p - point;
			distance = dot( relative, relative );
			if ( distance < nearest )
			{
				nearest = distance;
				result.Along = result.Length + BorderEllipseLength( radii[i], angle );
				result.Normal = UINormal( axes[i] * radial.x / radii[i].x + nextAxis * radial.y / radii[i].y );
				result.Across = dot( relative, result.Normal );
			}
			result.Length += BorderEllipseLength( radii[i], 1.57079632679 );
		}
	}
	return result;
}

// Integral of a periodic box, for antialiasing dash ends even when periods become subpixel.
float BorderPulseIntegral( float x, float duty )
{
	return floor( x ) * duty + min( frac( x ), duty );
}

float BorderPattern( int style, float width, BorderContour contour, float2 pixelX, float2 pixelY )
{
	float target = width * ( style == BORDER_DOTTED ? 2.0 : 6.0 );
	float count = max( 1.0, round( contour.Length / max( target, 0.0001 ) ) );
	float period = max( contour.Length / count, 0.0001 );
	float2 tangent = float2( -contour.Normal.y, contour.Normal.x );
	float pixelAlong = UIPixelWidth( tangent, pixelX, pixelY );
	float phase = frac( contour.Along / period );
	if ( style == BORDER_DASHED )
	{
		float footprint = pixelAlong / period;
		float center = phase + 0.25;
		return saturate( ( BorderPulseIntegral( center + footprint * 0.5, 0.5 ) - BorderPulseIntegral( center - footprint * 0.5, 0.5 ) ) / footprint );
	}
	float along = ( frac( phase + 0.5 ) - 0.5 ) * period;
	float2 offset = tangent * along + contour.Normal * contour.Across;
	return UIDiscCoverage( offset, width, pixelX, pixelY );
}

float4 ApplyBorderStyle( float4 color, int style, float width, BorderContour contour,
	float2 pixelX, float2 pixelY, float middle, float doubleMask, float light )
{
	if ( style == BORDER_NONE || style == BORDER_HIDDEN ) color.a = 0.0;
	if ( style == BORDER_DOTTED || style == BORDER_DASHED )
		color.a *= BorderPattern( style, width, contour, pixelX, pixelY );
	if ( style == BORDER_DOUBLE ) color.a *= doubleMask;
	if ( style >= BORDER_GROOVE )
	{
		if ( style == BORDER_RIDGE || style == BORDER_OUTSET ) light = 1.0 - light;
		if ( style == BORDER_GROOVE || style == BORDER_RIDGE ) light = lerp( light, 1.0 - light, middle );
		color.rgb = lerp( color.rgb * 0.55, lerp( color.rgb, max( color.rgb, 1.0 ), 0.3 ), light );
	}
	return color;
}

float4 StyledBoxBorder( float2 p, BoxInstanceData inst )
{
	float2 size = inst.Rect.zw;
	float2 centered = p - size * 0.5;
	int style = inst.GetBorderStyle();
	BorderContour contour = (BorderContour)0;
	if ( style == BORDER_DOTTED || style == BORDER_DASHED ) contour = RoundedBorderContour( p, inst );
	float middle = 0.0, light = 0.0;
	if ( style >= BORDER_GROOVE )
	{
		light = BorderSideColor( p, size, inst.BorderSize, 0.0, 0.0, 1.0, 1.0 ).r;
		if ( style == BORDER_GROOVE || style == BORDER_RIDGE )
			middle = SdfCoverage( InsetBoxSdf( centered, size, inst.BorderRadius, inst.BorderRadiusV, inst.BorderSize * 0.5 ) );
	}
	float doubleMask = 1.0;
	if ( style == BORDER_DOUBLE )
	{
		float first = SdfCoverage( InsetBoxSdf( centered, size, inst.BorderRadius, inst.BorderRadiusV, inst.BorderSize / 3.0 ) );
		float second = SdfCoverage( InsetBoxSdf( centered, size, inst.BorderRadius, inst.BorderRadiusV, inst.BorderSize * ( 2.0 / 3.0 ) ) );
		doubleMask = saturate( 1.0 - first + second );
	}
	float4 color = BorderSideColor( p, size, inst.BorderSize, inst.BorderColorL, inst.BorderColorT, inst.BorderColorR, inst.BorderColorB );
	// Use one period around the perimeter, including borders with unequal side widths.
	float width = max( max( inst.BorderSize.x, inst.BorderSize.y ), max( inst.BorderSize.z, inst.BorderSize.w ) );
	return ApplyBorderStyle( color, style, width, contour, ddx( p ), ddy( p ), middle, doubleMask, light );
}

// border-shape uses the left color and greatest width, matching its existing solid border.
float4 StyledShapeBorder( float2 p, BoxInstanceData inst, BorderShapeData shape, float distance )
{
	float width = max( max( inst.BorderSize.x, inst.BorderSize.y ), max( inst.BorderSize.z, inst.BorderSize.w ) );
	BorderContour contour = (BorderContour)0;
	contour.Across = distance + width * 0.5;
	if ( shape.Kind == 2 )
	{
		float2 q = p - shape.Circle.xy;
		contour.Normal = UINormal( q );
		float radius = max( shape.Circle.z - width * 0.5, 0.0 );
		contour.Length = radius * 6.28318530718;
		contour.Along = ( atan2( q.y, q.x ) + 3.14159265359 ) * radius;
	}
	else
	{
		float2 points[8] = { shape.Polygon01.xy, shape.Polygon01.zw, shape.Polygon23.xy, shape.Polygon23.zw,
			shape.Polygon45.xy, shape.Polygon45.zw, shape.Polygon67.xy, shape.Polygon67.zw };
		float nearest = 1e30;
		[unroll] for ( int i = 0; i < 8; i++ )
		{
			if ( i >= shape.PolygonCount ) break;
			float2 a = points[i], delta = points[( i + 1 ) % shape.PolygonCount] - a;
			float t = saturate( dot( p - a, delta ) / max( dot( delta, delta ), 0.000001 ) );
			float2 q = p - a - delta * t;
			float length = sqrt( dot( delta, delta ) );
			if ( dot( q, q ) < nearest )
			{
				nearest = dot( q, q );
				contour.Along = contour.Length + t * length;
				contour.Normal = UINormal( q ) * ( distance < 0.0 ? -1.0 : 1.0 );
			}
			contour.Length += length;
		}
	}
	float middle = SdfCoverage( distance + width * 0.5 );
	float doubleMask = saturate( 1.0 - SdfCoverage( distance + width / 3.0 ) + SdfCoverage( distance + width * ( 2.0 / 3.0 ) ) );
	float light = contour.Normal.x + contour.Normal.y > 0.0 ? 1.0 : 0.0;
	return ApplyBorderStyle( inst.BorderColorL, inst.GetBorderStyle(), width, contour, ddx( p ), ddy( p ), middle, doubleMask, light );
}

#endif

#ifndef UI_SHAPE_PATH_HLSL
#define UI_SHAPE_PATH_HLSL

#include "ui/disc_coverage.hlsl"

float PathBoxDistance( float2 q, out float2 normal )
{
	float2 outside = max( q, 0.0 );
	normal = any( outside > 0.0 ) ? UINormal( outside ) : ( q.x > q.y ? float2( 1, 0 ) : float2( 0, 1 ) );
	return length( outside ) + min( max( q.x, q.y ), 0.0 );
}

// Traverse only nodes that can contain a nearer edge or cross the even-odd ray.
float PathPolygonSdf( float2 p, BorderShapeData shape )
{
	float distanceSquared = 1e30;
	float sign = 1.0;
	[loop]
	for ( int i = 0; i < shape.PathNodeCount; )
	{
		PathNodeData node = PathNodeBuffer[shape.PathNodeOffset + i];
		float2 delta = max( max( node.Bounds.xy - p, p - node.Bounds.zw ), 0.0 );
		bool ray = p.y >= node.Bounds.y && p.y < node.Bounds.w && p.x < node.Bounds.z;
		if ( dot( delta, delta ) >= distanceSquared && !ray ) { i = node.Next; continue; }
		i++;
		if ( node.Primitive < 0 ) continue;
		float4 edge = PathBuffer[shape.PathOffset + node.Primitive].A;
		float2 a = edge.xy, b = edge.zw;
		float2 e = b - a, w = p - a;
		float2 nearest = w - e * saturate( dot( w, e ) / max( dot( e, e ), 0.000001 ) );
		distanceSquared = min( distanceSquared, dot( nearest, nearest ) );
		bool3 crossing = bool3( p.y >= a.y, p.y < b.y, e.x * w.y > e.y * w.x );
		if ( all( crossing ) || all( !crossing ) ) sign = -sign;
	}
	return sign * sqrt( distanceSquared );
}

float2 PathJoinVertex( PathPrimitiveData primitive, int index )
{
	if ( index == 0 ) return primitive.A.zw;
	if ( index == 1 ) return primitive.B.xy;
	if ( index == 2 ) return primitive.B.zw;
	if ( index == 3 ) return primitive.C.xy;
	return primitive.C.zw;
}

float PathJoinDistance( float2 p, PathPrimitiveData primitive, float radius, out float2 normal )
{
	float distanceSquared = 1e30;
	float sign = 1.0;
	normal = float2( 1, 0 );
	float2 a = PathJoinVertex( primitive, primitive.Count - 1 );
	[unroll]
	for ( int i = 0; i < 5; i++ )
	{
		if ( i >= primitive.Count ) break;
		float2 b = PathJoinVertex( primitive, i ) * ( i < primitive.Count - 2 ? radius : 1.0 );
		float2 e = b - a, w = p - a;
		float2 nearest = w - e * saturate( dot( w, e ) / max( dot( e, e ), 0.000001 ) );
		float d = dot( nearest, nearest );
		if ( d < distanceSquared )
		{
			distanceSquared = d;
			normal = d > 0.00000001 ? UINormal( nearest ) : UINormal( float2( -e.y, e.x ) );
		}
		bool3 crossing = bool3( p.y >= a.y, p.y < b.y, e.x * w.y > e.y * w.x );
		if ( all( crossing ) || all( !crossing ) ) sign = -sign;
		a = b;
	}
	return sign * sqrt( distanceSquared );
}

// Distance to a pointed cap's exterior edges. The central base attaches to the shaft, not an AA edge.
// p.x points outward; attachmentStart clips the inner attachment of strokes wider than an arc's diameter.
float PathPointedCapDistance( float2 p, float radius, int style, float attachmentStart, out float2 normal )
{
	float size = radius * ( style == UI_CAP_ARROW ? 2.0 : 1.0 );
	float side = p.y < 0.0 ? -1.0 : 1.0;
	float2 e = float2( size, -size );
	float2 w = float2( p.x, abs( p.y ) - size );
	float2 nearest = w - e * saturate( ( w.x - w.y ) / ( 2.0 * size ) );
	nearest.y *= side;
	float distanceSquared = dot( nearest, nearest );
	normal = distanceSquared > 0.00000001 ? UINormal( nearest ) : UINormal( float2( 1, side ) );
	[unroll]
	for ( int shoulder = 0; shoulder < 2; shoulder++ )
	{
		float v = shoulder == 0 ? clamp( p.y, radius, size ) : clamp( p.y, -size, attachmentStart );
		float2 delta = float2( p.x, p.y - v );
		float d = dot( delta, delta );
		if ( d < distanceSquared )
		{
			distanceSquared = d;
			normal = d > 0.00000001 ? UINormal( delta ) : float2( -1, 0 );
		}
	}
	bool inside = p.x >= 0.0 && p.x + abs( p.y ) <= size;
	return sqrt( distanceSquared ) * ( inside ? -1.0 : 1.0 );
}

float PathPointedSegmentDistance( float2 p, float segmentLength, float radius, float2 caps, out float2 normal )
{
	// Only the shaft's side edges and the caps' exposed edges belong to the contour.
	float side = p.y < 0.0 ? -1.0 : 1.0;
	float2 nearest = float2( p.x - clamp( p.x, 0.0, segmentLength ), p.y - side * radius );
	float distance = length( nearest );
	normal = distance > 0.000001 ? nearest / distance : float2( 0, side );
	bool inside = p.x >= 0.0 && p.x <= segmentLength && abs( p.y ) <= radius;
	[unroll]
	for ( int cap = 0; cap < 2; cap++ )
	{
		int style = (int)( cap == 0 ? caps.x : caps.y );
		float2 q = float2( cap == 0 ? -p.x : p.x - segmentLength, p.y );
		float2 capNormal;
		float capDistance;
		if ( style == UI_CAP_TRIANGLE || style == UI_CAP_ARROW )
		{
			capDistance = PathPointedCapDistance( q, radius, style, -radius, capNormal );
			inside = inside || capDistance <= 0.0;
			capDistance = abs( capDistance );
		}
		else
		{
			float2 delta = float2( q.x, q.y - clamp( q.y, -radius, radius ) );
			capDistance = length( delta );
			capNormal = capDistance > 0.000001 ? delta / capDistance : float2( 1, 0 );
		}
		if ( capDistance < distance )
		{
			distance = capDistance;
			normal = float2( capNormal.x * ( cap == 0 ? -1.0 : 1.0 ), capNormal.y );
		}
	}
	return distance * ( inside ? -1.0 : 1.0 );
}

float PathArcDistance( float2 p, PathPrimitiveData primitive, float radius, out float2 normal )
{
	float arcRadius = primitive.A.z;
	float start = primitive.A.w;
	float sweep = primitive.B.x;
	float direction = sweep < 0.0 ? -1.0 : 1.0;
	float2 a = float2( cos( start ), sin( start ) );
	float2 b = float2( cos( start + sweep ), sin( start + sweep ) );
	float angle = atan2( ( a.x * p.y - a.y * p.x ) * direction, dot( a, p ) );
	if ( angle < 0.0 ) angle += 6.28318530718;
	bool inside = angle <= abs( sweep );
	float radial = length( p ) - arcRadius;
	normal = UINormal( p ) * ( radial < 0.0 ? -1.0 : 1.0 );
	float distance = max( radial - radius, arcRadius > radius ? -radial - radius : -1e20 );
	if ( primitive.Count == UI_CAP_RING ) return distance;

	float2 qa = p - a * arcRadius, qb = p - b * arcRadius;
	bool nearStart = dot( qa, qa ) < dot( qb, qb );
	float2 q = nearStart ? qa : qb;

	if ( primitive.Count == UI_CAP_ROUND )
	{
		if ( inside ) return distance;
		normal = UINormal( q );
		return length( q ) - radius;
	}
	// A butt cap is the intersection with the angular sector. Distance to rays also handles reflex arcs.
	float2 rayA = p - a * max( dot( p, a ), 0.0 );
	float2 rayB = p - b * max( dot( p, b ), 0.0 );
	bool nearestA = dot( rayA, rayA ) < dot( rayB, rayB );
	float2 ray = nearestA ? rayA : rayB;
	float angular = length( ray ) * ( inside ? -1.0 : 1.0 );
	bool pointed = primitive.Count == UI_CAP_TRIANGLE || primitive.Count == UI_CAP_ARROW;
	// Extended caps cover the angular cuts. Those attachment edges are not exterior AA boundaries.
	if ( ( primitive.Count != UI_CAP_SQUARE && !pointed ) || !inside )
	{
		if ( angular > distance )
		{
			float2 endpointNormal = nearestA ? a : b;
			normal = dot( ray, ray ) > 0.00000001 ? UINormal( ray ) : float2( -endpointNormal.y, endpointNormal.x );
		}
		distance = max( distance, angular );
	}
	if ( primitive.Count == UI_CAP_SQUARE )
	{
		[unroll]
		for ( int cap = 0; cap < 2; cap++ )
		{
			float2 radialNormal = cap == 0 ? a : b;
			float2 tangent = float2( -radialNormal.y, radialNormal.x ) * direction * ( cap == 0 ? -1.0 : 1.0 );
			float2 relative = p - radialNormal * arcRadius;
			float u = dot( relative, tangent ), v = dot( relative, radialNormal );
			float back = -u;
			// Inside the outward half-rectangle, its back edge is shared with the arc body.
			if ( u >= 0.0 && arcRadius + v >= 0.0 ) back = -1e20;
			float2 boxNormal;
			float capDistance = PathBoxDistance( float2( max( back, u - radius ), abs( v ) - radius ), boxNormal );
			if ( capDistance < distance )
			{
				distance = capDistance;
				normal = boxNormal.x * tangent * ( back > u - radius ? -1.0 : 1.0 )
					+ boxNormal.y * radialNormal * ( v < 0.0 ? -1.0 : 1.0 );
			}
		}
	}
	return distance;
}

float PathRoundJoinDistance( float2 p, PathPrimitiveData primitive, float radius, out float2 normal )
{
	// A disk overlaps the segment boxes across all construction edges, including near reversals.
	// Clip only at their far ends: these constraints are inactive when both segments reach past the disk.
	float distance = length( p ) - radius;
	normal = UINormal( p );
	float incomingEnd = -dot( p, primitive.C.xy ) - primitive.B.x;
	float outgoingEnd = dot( p, primitive.C.zw ) - primitive.B.y;
	if ( incomingEnd > distance )
	{
		distance = incomingEnd;
		normal = -primitive.C.xy;
	}
	if ( outgoingEnd > distance )
	{
		distance = outgoingEnd;
		normal = primitive.C.zw;
	}
	return distance;
}

float PathStrokeCoverage( BorderShapeData shape, float2 p, float2 pixelX, float2 pixelY )
{
	float coverage = 0.0;
	[loop]
	for ( int i = 0; i < shape.PathNodeCount; )
	{
		PathNodeData node = PathNodeBuffer[shape.PathNodeOffset + i];
		float2 margin = abs( pixelX ) + abs( pixelY );
		if ( any( p < node.Bounds.xy - margin ) || any( p > node.Bounds.zw + margin ) ) { i = node.Next; continue; }
		i++;
		if ( node.Primitive < 0 ) continue;
		PathPrimitiveData primitive = PathBuffer[shape.PathOffset + node.Primitive];
		float2 q = p - primitive.A.xy;
		if ( primitive.Kind == UI_PATH_DISC )
		{
			coverage = max( coverage, UIDiscCoverage( q, shape.Circle.z, pixelX, pixelY ) );
			continue;
		}
		float2 normal = UINormal( q );
		float2 tangent = float2( 0, 1 );
		float segmentLength = 0.0;
		if ( primitive.Kind == UI_PATH_SEGMENT )
		{
			float2 delta = primitive.A.zw - primitive.A.xy;
			segmentLength = length( delta );
			tangent = delta / max( segmentLength, 0.000001 );
			normal = float2( -tangent.y, tangent.x );
		}

		float radius = shape.Circle.z * 0.5;
		float distance;
		if ( primitive.Kind == UI_PATH_SEGMENT )
		{
			float u = dot( q, tangent ), v = dot( q, normal );
			float2 transverse = normal;
			bool pointedStart = primitive.B.x == UI_CAP_TRIANGLE || primitive.B.x == UI_CAP_ARROW;
			bool pointedEnd = primitive.B.y == UI_CAP_TRIANGLE || primitive.B.y == UI_CAP_ARROW;
			if ( pointedStart || pointedEnd )
			{
				distance = PathPointedSegmentDistance( float2( u, v ), segmentLength, radius, primitive.B.xy, normal );
				normal = normal.x * tangent + normal.y * transverse;
			}
			else
			{
				float left = -u - ( ( primitive.Count & UI_CAP_SQUARE_START ) ? radius : 0.0 );
				float right = u - segmentLength - ( ( primitive.Count & UI_CAP_SQUARE_END ) ? radius : 0.0 );
				float2 boxNormal;
				distance = PathBoxDistance( float2( max( left, right ), abs( v ) - radius ), boxNormal );
				normal = boxNormal.x * tangent * ( left > right ? -1.0 : 1.0 ) + boxNormal.y * transverse * ( v < 0.0 ? -1.0 : 1.0 );
			}
		}
		else if ( primitive.Kind == UI_PATH_JOIN )
			distance = PathJoinDistance( q, primitive, radius, normal );
		else if ( primitive.Kind == UI_PATH_ROUND_JOIN )
			distance = PathRoundJoinDistance( q, primitive, radius, normal );
		else
			distance = PathArcDistance( q, primitive, radius, normal );

		float filterWidth = UIPixelWidth( normal, pixelX, pixelY );
		// Integrate the two sides of a thin stroke; its geometry remains in layout coordinates.
		float edge = saturate( 0.5 - distance / filterWidth ) - saturate( 0.5 - ( distance + shape.Circle.z ) / filterWidth );
		// Union all candidates before blending the paint. Crossings do not accumulate opacity.
		coverage = max( coverage, edge );
		if ( primitive.Kind == UI_PATH_ARC && ( primitive.Count == UI_CAP_TRIANGLE || primitive.Count == UI_CAP_ARROW ) )
		{
			[unroll]
			for ( int cap = 0; cap < 2; cap++ )
			{
				float angle = primitive.A.w + ( cap == 0 ? 0.0 : primitive.B.x );
				float2 radial = float2( cos( angle ), sin( angle ) );
				float2 outward = float2( -radial.y, radial.x ) * ( primitive.B.x < 0.0 ? -1.0 : 1.0 ) * ( cap == 0 ? -1.0 : 1.0 );
				float2 relative = q - radial * primitive.A.z;
				float capRadius = shape.Circle.z * 0.5;
				float2 capNormal;
				float capDistance = PathPointedCapDistance( float2( dot( relative, outward ), dot( relative, radial ) ),
					capRadius, primitive.Count, max( -capRadius, -primitive.A.z ), capNormal );
				capNormal = capNormal.x * outward + capNormal.y * radial;
				float capFilter = UIPixelWidth( capNormal, pixelX, pixelY );
				float capEdge = saturate( 0.5 - capDistance / capFilter ) - saturate( 0.5 - ( capDistance + shape.Circle.z ) / capFilter );
				coverage = max( coverage, capEdge );
			}
		}
	}
	return coverage;
}

#endif

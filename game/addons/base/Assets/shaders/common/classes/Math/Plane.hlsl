#ifndef COMMON_CLASSES_MATH_PLANE_HLSL
#define COMMON_CLASSES_MATH_PLANE_HLSL

#include "common.fxc"

struct Plane
{
    float3 Normal;
    float  Distance;

    static Plane FromPoints( float3 p0, float3 p1, float3 p2 )
    {
        Plane plane;
        plane.Normal = normalize( cross( p1 - p0, p2 - p0 ) );
        plane.Distance = dot( plane.Normal, p0 );
        return plane;
    }

    static Plane From( float3 p0, float3 p1, float3 p2 )
    {
        return FromPoints( p0, p1, p2 );
    }

    static Plane FromNormal( float3 normal, float distance )
    {
        Plane plane;
        plane.Normal = normalize( normal );
        plane.Distance = distance;
        return plane;
    }

    float SignedDistance( float3 p )
    {
        return dot( Normal, p ) - Distance;
    }

    bool PointOutside( float3 p )
    {
        return SignedDistance( p ) < 0.0f;
    }

    bool SphereOutside( float3 centre, float radius )
    {
        return SignedDistance( centre ) < -radius;
    }

    Plane TransformToLocal( float4x3 worldToLocal )
    {
        Plane planeLocal;
        planeLocal.Normal = normalize( mul( float4( Normal, 0.0f ), worldToLocal ).xyz );
        planeLocal.Distance = dot( planeLocal.Normal, mul( float4( Normal * Distance, 1.0f ), worldToLocal ).xyz );
        return planeLocal;
    }

    bool AABBOutside( float3 aabbMin, float3 aabbMax )
    {
        float3 center = ( aabbMin + aabbMax ) * 0.5f;
        float3 extents = ( aabbMax - aabbMin ) * 0.5f;
        float radius = dot( abs( Normal ), extents );
        return SignedDistance( center ) < -radius;
    }

    bool AABBOutside( float3 aabbMin, float3 aabbMax, float4x3 worldToLocal )
    {
        Plane planeLocal = TransformToLocal( worldToLocal );

        [unroll]
        for ( int i = 0; i < 8; ++i )
        {
            float3 corner = float3(
                ( i & 1 ) ? aabbMax.x : aabbMin.x,
                ( i & 2 ) ? aabbMax.y : aabbMin.y,
                ( i & 4 ) ? aabbMax.z : aabbMin.z );

            if ( !planeLocal.PointOutside( corner ) )
                return false;
        }

        return true;
    }

    bool ConeOutside( float3 apex, float3 direction, float3 up, float height, float outerConeCosine )
    {
        float radius = height * sqrt( max( 0.0f, 1.0f - outerConeCosine * outerConeCosine ) ) / max( outerConeCosine, 1e-4f );

        float3 base = apex + direction * height;
        float3 right = cross( direction, up );

        float3 b1 = base + up * radius + right * radius;
        float3 b2 = base + up * radius - right * radius;
        float3 b3 = base - up * radius + right * radius;
        float3 b4 = base - up * radius - right * radius;

        return PointOutside( apex ) && PointOutside( b1 ) && PointOutside( b2 ) && PointOutside( b3 ) && PointOutside( b4 );
    }
};

#endif

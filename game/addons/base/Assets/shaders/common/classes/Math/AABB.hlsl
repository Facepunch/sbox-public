#ifndef COMMON_CLASSES_MATH_AABB_HLSL
#define COMMON_CLASSES_MATH_AABB_HLSL

#include "common/classes/Math/Plane.hlsl"

struct AABB
{
    float3 Min;
    float3 Max;

    static AABB FromMinMax( float3 minValue, float3 maxValue )
    {
        AABB box;
        box.Min = minValue;
        box.Max = maxValue;
        return box;
    }

    static AABB FromCenterExtents( float3 center, float3 extents )
    {
        AABB box;
        box.Min = center - extents;
        box.Max = center + extents;
        return box;
    }

    float3 Center()
    {
        return ( Min + Max ) * 0.5f;
    }

    float3 Extents()
    {
        return ( Max - Min ) * 0.5f;
    }

    bool ContainsPoint( float3 position )
    {
        return all( position >= Min ) && all( position <= Max );
    }

    bool OutsidePlane( Plane plane )
    {
        return plane.AABBOutside( Min, Max );
    }

    AABB Expand( float amount )
    {
        AABB box;
        box.Min = Min - amount;
        box.Max = Max + amount;
        return box;
    }

    float3 ClosestPoint( float3 position )
    {
        return clamp( position, Min, Max );
    }

    float DistanceSq( float3 position )
    {
        float3 closest = ClosestPoint( position );
        float3 delta = position - closest;
        return dot( delta, delta );
    }
};

#endif

#ifndef COMMON_CLASSES_MATH_FRUSTUM_HLSL
#define COMMON_CLASSES_MATH_FRUSTUM_HLSL

#include "common.fxc"
#include "common/classes/Math/Plane.hlsl"

struct Frustum
{
    Plane Planes[6]; // 0-3: sides (left, right, top, bottom), 4: far, 5: near

    static Frustum FromClusterTile( uint2 tileCoord, float2 depthRange, float2 invTileCount )
    {
        float2 uv00 = invTileCount * float2( tileCoord );
        float2 uv10 = invTileCount * float2( tileCoord.x + 1, tileCoord.y );
        float2 uv01 = invTileCount * float2( tileCoord.x, tileCoord.y + 1 );
        float2 uv11 = invTileCount * float2( tileCoord.x + 1, tileCoord.y + 1 );

        float3 nearCorners[4];
        float3 farCorners[4];
        float2 corners[4] = { uv00, uv10, uv01, uv11 };

        [unroll]
        for ( int i = 0; i < 4; ++i )
        {
            float2 uv = float2( corners[i].x, 1.0f - corners[i].y );
            float2 ndc = uv * 2.0f - 1.0f;

            float4 homFar = mul( g_matProjectionToWorld, float4( ndc, 0.0f, 1.0f ) );
            farCorners[i] = g_vCameraPositionWs + homFar.xyz / homFar.w;

            float4 homNear = mul( g_matProjectionToWorld, float4( ndc, 1.0f, 1.0f ) );
            nearCorners[i] = g_vCameraPositionWs + homNear.xyz / homNear.w;
        }

        Frustum f;
        f.Planes[0] = Plane::From( nearCorners[2], farCorners[2], farCorners[0] ); // left
        f.Planes[1] = Plane::From( nearCorners[1], farCorners[1], farCorners[3] ); // right
        f.Planes[2] = Plane::From( nearCorners[0], farCorners[0], farCorners[1] ); // top
        f.Planes[3] = Plane::From( nearCorners[3], farCorners[3], farCorners[2] ); // bottom

        f.Planes[4].Normal = -g_vCameraDirWs.xyz;
        f.Planes[4].Distance = dot( f.Planes[4].Normal, g_vCameraPositionWs + g_vCameraDirWs.xyz * depthRange.y );

        f.Planes[5].Normal = g_vCameraDirWs.xyz;
        f.Planes[5].Distance = dot( f.Planes[5].Normal, g_vCameraPositionWs + g_vCameraDirWs.xyz * depthRange.x );

        return f;
    }

    bool PointInside( float3 p )
    {
        [unroll] for ( int i = 0; i < 6; ++i )
            if ( Planes[i].PointOutside( p ) ) return false;
        return true;
    }

    bool SphereInside( float3 center, float radius )
    {
        [unroll] for ( int i = 0; i < 6; ++i )
            if ( Planes[i].SphereOutside( center, radius ) ) return false;
        return true;
    }

    bool ConeInside( float3 apex, float3 dir, float3 up, float height, float outerCos )
    {
        [unroll] for ( int i = 0; i < 6; ++i )
            if ( Planes[i].ConeOutside( apex, dir, up, height, outerCos ) ) return false;
        return true;
    }

    bool AABBInside( float3 mins, float3 maxs, float4x3 worldToLocal )
    {
        [unroll] for ( int i = 0; i < 6; ++i )
            if ( Planes[i].AABBOutside( mins, maxs, worldToLocal ) ) return false;
        return true;
    }

};

#endif

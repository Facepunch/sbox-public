#ifndef SCREEN_SPACE_TRACE_HLSL
#define SCREEN_SPACE_TRACE_HLSL

#include "common/classes/Depth.hlsl"
#include "common/classes/Normals.hlsl"

struct TraceResult
{
    float2 HitUv;
    float Confidence; // Zero on a miss; fades at screen edges and the thickness limit.
};

struct ScreenSpace
{
    static float CellExit( float2 position, float2 direction, float cellSize )
    {
        float2 edge = ( floor( position / cellSize ) + ( direction > 0 ) ) * cellSize;
        float2 distance = ( edge - position ) * sign( direction ) / max( abs( direction ), 1e-20 );
        return min( direction.x == 0 ? 1e30 : distance.x, direction.y == 0 ? 1e30 : distance.y );
    }

    static TraceResult Trace( float3 positionWs, float3 directionWs, uint maxSteps = 64 )
    {
        TraceResult result = (TraceResult)0;
        uint width, height, levels;
        g_tDepthChain.GetDimensions( 0, width, height, levels );
        float2 screenSize = float2( width, height );

        float4 clip = Position4WsToPs( float4( positionWs, 1 ) );
        if ( clip.w <= 0 || clip.z <= 0 || clip.z >= clip.w )
            return result;

        float3 ndc = clip.xyz / clip.w;
        float4 clipDirection = Position4WsToPs( float4( directionWs, 0 ) );
        // Project the direction analytically: projecting a second point can cross the camera plane.
        float3 direction = clipDirection.xyz - ndc * clipDirection.w;
        direction.xy *= float2( 0.5, -0.5 ) * screenSize;
        float pixelSpeed = max( abs( direction.x ), abs( direction.y ) );
        if ( pixelSpeed < 1e-6 || !all( isfinite( direction ) ) )
            return result;
        direction /= pixelSpeed;

        float3 origin = float3( ( ndc.xy * float2( 0.5, -0.5 ) + 0.5 ) * screenSize, ndc.z );
        float limit = direction.z == 0 ? 1e30 : ( ( direction.z > 0 ? 1 : 0 ) - origin.z ) / direction.z;
        // Skip only the source pixel; the epsilon is in pixels, not a fraction of a coarse cell.
        float t = CellExit( origin.xy, direction.xy, 1 ) + 0.001;
        int mip = 0;

        [loop]
        for ( uint i = 0; i < maxSteps; i++ )
        {
            float3 pos = origin + direction * t;
            if ( !all( pos.xy >= 0 ) || !all( pos.xy < screenSize ) || t >= limit )
                return result;

            float cellSize = float( 1u << mip );
            int2 cell = int2( pos.xy / cellSize );
            // The 2x2 reduction drops odd trailing rows/columns. Trace those at a finer mip.
            if ( any( cell >= int2( uint2( width, height ) >> mip ) ) )
            {
                mip--;
                continue;
            }

            float exit = t + CellExit( pos.xy, direction.xy, cellSize );
            float end = min( exit, limit );
            float surfaceZ = Depth::Normalize( g_tDepthChain.Load( int3( cell, mip ) ).y );

            // Reverse Z: skip only when the entire segment is in front of the nearest depth.
            if ( surfaceZ > 0 && min( pos.z, origin.z + direction.z * end ) <= surfaceZ )
            {
                if ( mip > 0 )
                {
                    mip--;
                    continue;
                }

                float hitT = pos.z <= surfaceZ ? t : ( surfaceZ - origin.z ) / direction.z;
                float3 hit = origin + direction * hitT;
                float surfaceDepth = Depth::Linearize( surfaceZ, hit.xy );
                float behind = max( 0, Depth::Linearize( hit.z, hit.xy ) - surfaceDepth );
                // ponytail: one depth layer with bounded thickness; hidden surfaces need geometry tracing.
                float thickness = clamp( surfaceDepth * 0.04, 6, 20 );
                // Rasterized depth can overlap a ray leaving its own surface. Skip it and keep tracing.
                if ( hitT < exit && hitT < limit && behind < thickness && dot( Normals::Sample( cell ), directionWs ) < 0 )
                {
                    result.HitUv = hit.xy / screenSize;
                    float2 border = smoothstep( 0, 0.05, min( result.HitUv, 1 - result.HitUv ) );
                    result.Confidence = ( 1 - behind / thickness ) * border.x * border.y;
                    return result;
                }
            }

            t = exit + 0.001;
            mip = min( mip + 1, int( levels ) - 1 );
        }
        return result;
    }
};

#endif // SCREEN_SPACE_TRACE_HLSL

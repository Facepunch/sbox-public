#ifndef MSAA_UTILS_HLSL
#define MSAA_UTILS_HLSL

#include "common/classes/Depth.hlsl"
class MSAAUtils
{
    // Gets the gather lane whose texel center is closest to the current MSAA sample position.
    // Used to composite a non-MSAA texture into a MSAA buffer: first filters to texels that
    // match our depth, then picks the spatially nearest one via branchless paired tournament.
    static int GetSampleIndex( float4 vPositionSs, float2 uv )
    {
        float4 depths = g_tDepthChain.GatherRed( g_sBilinearClamp, uv );
        float4 depthDiffs = abs( vPositionSs.z - depths );

        // Only consider lanes within epsilon of the closest depth match
        float minDiff = min( min( depthDiffs.x, depthDiffs.y ), min( depthDiffs.z, depthDiffs.w ) );
        float4 valid = step( depthDiffs, minDiff + 1e-5 );

        // Squared sub-pixel distance from fragment to each gather texel center
        // Texel centers: lane0(0.25,0.25) lane1(0.75,0.25) lane2(0.25,0.75) lane3(0.75,0.75)
        float2 dx = frac( vPositionSs.x ) - float2( 0.25, 0.75 );
        float2 dy = frac( vPositionSs.y ) - float2( 0.25, 0.75 );
        float4 distSq = dx.xyxy * dx.xyxy + dy.xxyy * dy.xxyy;

        // Large penalty knocks out depth-mismatched lanes
        float4 scores = distSq + ( 1.0 - valid ) * 1e8;

        // Branchless argmin: paired tournament builds a 2-bit index
        float2 sel  = step( scores.yw + 1e-6, scores.xz );   // per-pair winner (bit0 candidates)
        float2 best = lerp( scores.xz, scores.yw, sel );      // per-pair best score
        float  pair = step( best.y + 1e-6, best.x );          // winning pair    (bit1)

        return (int)( lerp( sel.x, sel.y, pair ) + pair * 2.0 );
    }
};

#endif // MSAA_UTILS_HLSL
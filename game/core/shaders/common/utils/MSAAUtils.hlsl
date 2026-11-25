#ifndef MSAA_UTILS_HLSL
#define MSAA_UTILS_HLSL

#include "common/classes/Depth.hlsl"
struct MSAAUtils
{
    // UV to feed a Gather on a non-MSAA full-res texture. Anchored a quarter texel off the
    // quad boundary so the 2x2 quad selection is numerically stable (a corner-exact uv can
    // flip quads per pixel and shimmer), with this pixel's own texel on lane y.
    static float2 GetGatherUv( float4 vPositionSs )
    {
        return ( vPositionSs.xy ) * g_vInvViewportSize.xy;
    }

    // Gets the gather lane to composite a non-MSAA texture into an MSAA buffer.
    // With uv from GetGatherUv, the gather quad's lanes are x=(0,+1) y=(+1,+1) z=(+1,0) w=(0,0)
    // texels, so lane y is this pixel's own texel. Prefer it; when its depth doesn't match this
    // sample's depth (MSAA edge), fall back to the neighbour lane with the closest depth.
    static int GetSampleIndex( float4 vPositionSs, float2 uv )
    {
        float4 depthDiffs = abs( vPositionSs.z - g_tDepthChain.GatherRed( g_sBilinearClamp, uv ) );

        // Branchless argmin: pairwise tournament, index assembled as bit0 + 2*bit1
        float2 sel  = step( depthDiffs.yw, depthDiffs.xz ); // winner within pairs (x,y) and (z,w)
        float2 best = min( depthDiffs.xz, depthDiffs.yw );  // each pair's best score
        float  pair = step( best.y, best.x );               // which pair won

        return (int)dot( float2( lerp( sel.x, sel.y, pair ), pair ), float2( 1, 2 ) );
    }

    // Samples a full-resolution single-channel texture, matching depth at MSAA edges.
    static float SampleRed( Texture2D tTex, float4 vPositionSs )
    {
        float2 uv = GetGatherUv( vPositionSs );

        // Without MSAA, the texture already has a value for this pixel; no depth match is needed.
        if ( g_nMSAASampleCount == 1 )
            return tTex.SampleLevel( g_sPointClamp, uv, 0 ).r;

        // Where the four texels agree (away from the texture's edges: nearly everywhere in a mask) the lane doesn't matter, so the depth
        // gather that picks it is only taken where they don't
        float4 values = tTex.GatherRed( g_sBilinearClamp, uv );
        [branch]
        if ( all( values.xzw == values.yyy ) )
            return values.y;
        return values[ GetSampleIndex( vPositionSs, uv ) ];
    }
};

#endif // MSAA_UTILS_HLSL

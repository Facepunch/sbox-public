//-------------------------------------------------------------------------------------------------------------------------------------------------------------
HEADER
{
	DevShader = true;
	Description = "Cone-traced screen space reflections";
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
MODES
{
	Default();
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
FEATURES
{
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
COMMON
{
	#include "system.fxc" // This should always be the first include in COMMON
	#include "common.fxc"
	#include "math_general.fxc"
	#include "encoded_normals.fxc"

	#define PASS_INTERSECT 1
	#define PASS_BILATERAL_UPSCALE 2

	DynamicCombo( D_PASS, 1..2, Sys( PC ) );
	DynamicCombo( D_MSAA_NORMALS, 0..1, Sys( PC ) );
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
CS
{
    #include "common/classes/Depth.hlsl"
    #include "common/classes/ScreenSpaceTrace.hlsl"
    #include "common/classes/Normals.hlsl"
    #include "common/classes/Motion.hlsl"

    int                    PreviousFrameColorIndex < Attribute( "PreviousFrameColorIndex" ); >;
    float                  RoughnessCutoff         < Attribute( "RoughnessCutoff" ); Default( 0.5 ); >;
    RWTexture2D<float4>    OutRadiance             < Attribute( "OutRadiance" ); >;
    Texture2D<uint>        ClassifiedTiles         < Attribute( "ClassifiedTiles" ); >;
    Texture2D<float4>      Radiance                < Attribute( "Radiance" ); >;
    // Low-res surface the upscale weighs its taps against: linear depth, roughness, encoded normal
    Texture2D<float4>      Guide                   < Attribute( "Guide" ); >;
    RWTexture2D<float4>    OutGuide                < Attribute( "OutGuide" ); >;

    // Scale is 1 / ScaleInv; ScaleInv is 1 = full, 2 = half, 4 = quarter.
    float Scale    < Attribute( "Scale" ); Default( 1 ); >;
    float ScaleInv < Attribute( "ScaleInv" ); Default( 1 ); >;

    #define Dimensions g_vViewportSize

    // The full-res pixel a low-res texel stands for, shared by classify, intersect and upscale
    int2 SurfacePixel( int2 vLowRes )
    {
        return min( vLowRes * (int)ScaleInv + (int)ScaleInv / 2, int2( Dimensions ) - 1 );
    }

    //--------------------------------------------------------------------------------------
    // Classified tile list -> pixel coordinate
    //--------------------------------------------------------------------------------------
    uint2 GetDispatchThreadId( uint2 groupId, uint2 groupThreadId )
    {
        #if ( D_PASS == PASS_BILATERAL_UPSCALE )
            // Each classified tile spawns groupsPerTile full-res dispatch groups
            uint groupsPerAxis = max( 1u, (uint)round( ScaleInv ) );
            uint groupsPerTile = groupsPerAxis * groupsPerAxis;
            uint tileListIndex = groupId.x / groupsPerTile;
            uint subGroupIndex = groupId.x - tileListIndex * groupsPerTile;
            uint2 subGroupCoord = uint2( subGroupIndex % groupsPerAxis, subGroupIndex / groupsPerAxis );
        #else
            uint tileListIndex = groupId.x;
            uint groupsPerAxis = 1u;
            uint2 subGroupCoord = 0;
        #endif

        uint width, height;
        ClassifiedTiles.GetDimensions( width, height );
        if ( tileListIndex >= width * height )
            return ~0u; // Past the list: MainCs bounds-checks it away
        uint packedTileCoord = ClassifiedTiles.Load( int3( tileListIndex % width, tileListIndex / width, 0 ) );
        uint2 tileCoord = uint2( packedTileCoord & 0xFFFFu, ( packedTileCoord >> 16u ) & 0xFFFFu );
        return ( tileCoord * groupsPerAxis + subGroupCoord ) * 8u + groupThreadId;
    }

    // Zero depth marks texels the upscale skips
    float4 SurfaceGuide( int2 vPixel )
    {
        float flDepth = Depth::GetNormalized( vPixel );
        float flRoughness = Roughness::Sample( vPixel );
        if ( flDepth <= 0 || flRoughness >= RoughnessCutoff )
            return 0;

        return float4( Depth::Linearize( flDepth, vPixel + 0.5 ), flRoughness, EncodeNormal2D( normalize( Normals::Sample( vPixel ) ) ) );
    }

    //--------------------------------------------------------------------------------------
    // Intersect: one mirror trace, cone footprint at the hit picks the color mip
    //--------------------------------------------------------------------------------------
    float4 Pass_Intersect( int2 vPixel )
    {
        if ( any( vPixel < 0 ) || any( vPixel >= int2( Dimensions ) ) || Depth::GetNormalized( vPixel ) <= 0 )
            return 0;

        float flRoughness = Roughness::Sample( vPixel );
        if ( flRoughness >= RoughnessCutoff )
            return 0;

        float3 vNormal = normalize( Normals::Sample( vPixel ) );
        float3 vPositionWs = Depth::GetWorldPosition( vPixel + 0.5 );
        float3 vRayWs = normalize( vPositionWs - g_vCameraPositionWs );

        float flFade = 1 - smoothstep( RoughnessCutoff * 0.8, RoughnessCutoff, flRoughness );
        if ( dot( vNormal, -vRayWs ) <= 0.01 )
            return 0;

        float3 vReflectionWs = reflect( vRayWs, vNormal );
        TraceResult trace = ScreenSpace::Trace( vPositionWs, vReflectionWs, 128 );
        if ( trace.Confidence <= 0 )
            return 0;

        float flConfidence = trace.Confidence * flFade;
        // Reproject the surface under the hit pixel rather than the ray's own depth
        int2 vHitPixel = int2( trace.HitUv * Dimensions );
        float3 vHitLastFrameSs = Motion::Get( vHitPixel + 0.5 );
        float2 vHitLastFrameUv = vHitLastFrameSs.xy * g_vInvViewportSize;
        // Motion::Get returns z = 0 for near-camera surfaces that skip reprojection.
        if ( !all( vHitLastFrameUv >= 0 ) || !all( vHitLastFrameUv < 1 ) || !( vHitLastFrameSs.z >= 0 && vHitLastFrameSs.z < 1 ) )
            return 0;

        Texture2D tColor = Bindless::GetTexture2D( PreviousFrameColorIndex );

        // Lobe ~ roughness * hit distance, stretched by 1/NdotV along the plane of incidence; one isotropic mip tap at the axes' geometric mean
        float flDistPx = length( trace.HitUv * Dimensions - ( vPixel + 0.5 ) );
        float flMip = log2( max( flRoughness * flRoughness * flDistPx * rsqrt( dot( vNormal, -vRayWs ) ), 1 ) );

        // Overbright history is Inf in half float and turns NaN in the upscale and mips; keep it finite
        float3 vColor = tColor.SampleLevel( g_sTrilinearClamp, vHitLastFrameUv, flMip ).rgb;
        vColor = select( isnan( vColor ), 0.0f, min( vColor, 65504.0f ) );

        // Premultiplied so the upscale and mip chain don't blend black misses into hits
        return float4( vColor * flConfidence, flConfidence );
    }

    //--------------------------------------------------------------------------------------
    // Bilateral upscale from the 2x2 nearest low-res texels
    //--------------------------------------------------------------------------------------
    static const float kDepthSharpness = 70.0f;
    static const float kRoughnessSharpness = 4.0f;
    static const float kNormalSharpness = 6.0f;

    float4 Pass_BilateralUpscale( uint2 pixel_coordinate )
    {
        // Match the representative pixel selected by SurfacePixel, including even downsample factors.
        float2 lowCoord = ( float2(pixel_coordinate) - (int)ScaleInv / 2 ) * Scale;
        int2 baseCoord = (int2)floor(lowCoord);
        float2 fractional = lowCoord - float2(baseCoord);

        float centerDepth = Depth::GetNormalized(pixel_coordinate);
        float centerRoughness = Roughness::Sample(pixel_coordinate);
        if ( centerDepth <= 0 || centerRoughness >= RoughnessCutoff )
            return 0;
        centerDepth = Depth::Linearize( centerDepth, pixel_coordinate + 0.5 );

        // Near-mirror surfaces can't hide the half-res reconstruction, trace them at full res
        //if ( centerRoughness < 0.1f )
        //    return Pass_Intersect( pixel_coordinate );

        float3 centerNormal = normalize( Normals::Sample(pixel_coordinate) );

        float4 accum = 0.0f;
        float accumWeight = 0.0f;

        uint width, height;
        Radiance.GetDimensions( width, height );
        int2 ssrMax = int2( width, height ) - 1;

        [unroll]
        for (int dy = 0; dy <= 1; ++dy)
        {
            int sampleY = clamp( baseCoord.y + dy, 0, ssrMax.y );

            [unroll]
            for (int dx = 0; dx <= 1; ++dx)
            {
                int sampleX = clamp( baseCoord.x + dx, 0, ssrMax.x );

                float2 bilinear = lerp( 1 - fractional, fractional, float2(dx, dy) );
                float spatialWeight = bilinear.x * bilinear.y;

                float4 lowRadiance = Radiance.Load( int3( sampleX, sampleY, 0 ) );
                float4 guide = Guide.Load( int3( sampleX, sampleY, 0 ) );
                if ( guide.x <= 0 )
                    continue;

                float depthWeight = exp(-abs(centerDepth - guide.x) / max(centerDepth, 1e-4f) * kDepthSharpness);
                float roughWeight = exp(-abs(centerRoughness - guide.y) * kRoughnessSharpness);
                float normalWeight = pow(saturate(dot(centerNormal, DecodeNormal2D(guide.zw))), kNormalSharpness);

                float weight = spatialWeight * depthWeight * roughWeight * normalWeight;

                accum += lowRadiance * weight;
                accumWeight += weight;
            }
        }

        return accumWeight > 1e-6f ? accum / accumWeight : 0;
    }

    //--------------------------------------------------------------------------------------

    [numthreads(8, 8, 1)]
    void MainCs( uint2 groupThreadID : SV_GroupThreadID, uint2 groupID : SV_GroupID )
    {
        uint2 vDispatch = GetDispatchThreadId( groupID, groupThreadID );
        uint width, height;
        OutRadiance.GetDimensions( width, height );
        if ( any( vDispatch >= uint2( width, height ) ) )
            return;

        #if ( D_PASS == PASS_INTERSECT )
            OutGuide[vDispatch] = SurfaceGuide( SurfacePixel( int2( vDispatch ) ) );
            OutRadiance[vDispatch] = Pass_Intersect( SurfacePixel( int2( vDispatch ) ) );
        #else
            if ( any( vDispatch >= uint2( Dimensions ) ) )
                return;
            OutRadiance[vDispatch] = Pass_BilateralUpscale( vDispatch );
        #endif
    }
}

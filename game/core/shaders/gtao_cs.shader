MODES
{
    Default();
    Forward();
}

CS
{
    #include "common.fxc"
    #include "postprocess/shared.hlsl"

    #include "common\classes\Depth.hlsl"

    #include "common\thirdparty\XeGTAO.h"
    #include "common\thirdparty\XeGTAO.hlsl"

    //-------------------------------------------------------------------------------------------------------------------
    //
    // Non-temporal GTAO. Four passes:
    //
    //  ViewDepthChain    view space depth and four MIPs at AO resolution (XeGTAO's prefilter)
    //  MainPass          GTAO at AO resolution. Writes visibility; the passes after it read depth from the chain's MIP 0
    //  DenoiseSpatial    edge-aware 3x3 À-Trous, run 1-3 times with dilated steps, stopping on depth and normal
    //  BilateralUpsample to full resolution when AO runs reduced: bilinear weights times each coarse texel's distance
    //                    to the full resolution pixel's tangent plane
    //
    // AO texel p stands for the full resolution texel AoToFullRes( p ): its depth and normal are that texel's, exactly,
    // so the main pass, the denoiser and the upsampler all agree where a coarse sample sits.
    //
    //-------------------------------------------------------------------------------------------------------------------

    DynamicCombo( D_MSAA_NORMALS, 0..1, Sys( All ) );

    // Bind directly, vary type on MSAA, Normals class does not do this
    #if D_MSAA_NORMALS
        Texture2DMS<float4>  g_tNormalsGBuffer  < Attribute("NormalsGBuffer"); >;
    #else
        Texture2D<float4>    g_tNormalsGBuffer  < Attribute("NormalsGBuffer"); >;
    #endif

    float3 LoadGBufferNormal( int2 screenPos )
    {
        #if D_MSAA_NORMALS
            const float3 n = g_tNormalsGBuffer.Load( screenPos + g_vViewportOffset, 0 ).xyz;
        #else
            const float3 n = g_tNormalsGBuffer.Load( int3( screenPos + g_vViewportOffset, 0 ) ).xyz;
        #endif
        return 2.0f * n - 1.0f;
    }

    //-------------------------------------------------------------------------------------------------------------------

    cbuffer GTAOConstants
    {
        GTAOConstants g_GTAOConsts;
    };

    // Resolution divisor: 1 = full, 2 = half, 4 = quarter, 8 = eighth.
    // AO pipeline runs at (fullViewport / g_nResolutionScale). The final AO texture is also at this reduced size.
    int g_nResolutionScale < Attribute("ResolutionScale"); Default(1); >;

    Texture2D g_tBlueNoise < Attribute("BlueNoise"); >;

    // r_ao_reference: slice count for a full-resolution ground truth, 0 for the real thing
    int g_nReferenceSlices < Attribute("ReferenceSlices"); Default(0); >;

    //-------------------------------------------------------------------------------------------------------------------

    // outputs of the first pass (PrefilterDepths)
#if ( D_PASS == 0)
    RWTexture2D<float>           g_outWorkingDepthMIP0   < Attribute("WorkingDepthMIP0"); > ;   // output viewspace depth MIP (these are views into g_srcWorkingDepth MIP levels)
    RWTexture2D<float>           g_outWorkingDepthMIP1   < Attribute("WorkingDepthMIP1"); > ;   // output viewspace depth MIP (these are views into g_srcWorkingDepth MIP levels)
    RWTexture2D<float>           g_outWorkingDepthMIP2   < Attribute("WorkingDepthMIP2"); > ;   // output viewspace depth MIP (these are views into g_srcWorkingDepth MIP levels)
    RWTexture2D<float>           g_outWorkingDepthMIP3   < Attribute("WorkingDepthMIP3"); > ;   // output viewspace depth MIP (these are views into g_srcWorkingDepth MIP levels)
    RWTexture2D<float>           g_outWorkingDepthMIP4   < Attribute("WorkingDepthMIP4"); > ;   // output viewspace depth MIP (these are views into g_srcWorkingDepth MIP levels)
#endif

    // Viewspace depth with MIPs at AO resolution, from the first pass
    Texture2D<float>             g_srcWorkingDepth       < Attribute("WorkingDepth"); > ;

    // Main pass outputs, at AO resolution
    RWTexture2D<float>           g_outAOZ                < Attribute("OutAOZ"); >;              // visibility

    // Denoise and upsample inputs, at AO resolution
    Texture2D<float>             g_tAOZ                  < Attribute("AOZ"); >;

    // Denoise output, visibility
    RWTexture2D<float>           g_outDenoised           < Attribute("OutDenoised"); >;
    int                          g_nSpatialStep          < Attribute("SpatialStep"); Default(1); >;
    int                          g_nSpatialSteps         < Attribute("SpatialSteps"); Default(1); >;     // 2: steps s and 2s in one dispatch

    // Upsample output, full resolution
    RWTexture2D<float>           g_outFullResAO          < Attribute("FullResAO"); > ;

    SamplerState                PointClamp               < Filter( POINT ); AddressU( CLAMP ); AddressV( CLAMP ); AddressW( CLAMP ); >;

    //-------------------------------------------------------------------------------------------------------------------

    enum GTAOPasses
    {
        ViewDepthChain,
        MainPass,
        DenoiseSpatial,
        BilateralUpsample
    };

    DynamicCombo( D_PASS, 0..3, Sys( All ) );
    DynamicCombo( D_QUALITY, 0..3, Sys( All ) );  // 3 is r_ao_reference

    //-------------------------------------------------------------------------------------------------------------------

    float2 AoViewportSize()
    {
        return max( float2( 1, 1 ), floor( g_vViewportSize.xy / (float)g_nResolutionScale ) );
    }

    // Compute per-viewport GTAO constants from the cbuffer base + engine globals.
    // CommandList API doesn't expose per-viewport parameter changes so we derive them here.
    GTAOConstants GetConstants()
    {
        GTAOConstants consts = g_GTAOConsts;

        // AO-pipeline resolution: full viewport divided by the resolution scale factor.
        float2 aoViewport = AoViewportSize();
        consts.ViewportSize = aoViewport;
        consts.ViewportPixelSize = 1.0f / aoViewport;

        float depthLinearizeMul = ( g_flFarPlane * g_flNearPlane ) / ( g_flFarPlane - g_flNearPlane );
        float depthLinearizeAdd = g_flFarPlane / ( g_flFarPlane - g_flNearPlane );
        consts.DepthUnpackConsts = float2( depthLinearizeMul, depthLinearizeAdd );

        consts.CameraTanHalfFOV = float2( 1.0f / g_matViewToProjection[0][0], 1.0f / g_matViewToProjection[1][1] );

        consts.NDCToViewMul = float2( consts.CameraTanHalfFOV.x * 2.0f, consts.CameraTanHalfFOV.y * -2.0f );
        consts.NDCToViewAdd = float2( consts.CameraTanHalfFOV.x * -1.0f, consts.CameraTanHalfFOV.y * 1.0f );
        consts.NDCToViewMul_x_PixelSize = float2( consts.NDCToViewMul.x * consts.ViewportPixelSize.x, consts.NDCToViewMul.y * consts.ViewportPixelSize.y );

        return consts;
    }

    // Full resolution pixels per AO pixel. Not the integer scale: odd viewport sizes would accumulate a bias.
    float2 FullResPerAo()
    {
        return g_vViewportSize.xy / AoViewportSize();
    }

    // The full resolution texel AO texel p stands for
    int2 AoToFullRes( int2 aoPos )
    {
        int2 fullResPos = int2( ( (float2)aoPos + 0.5 ) * FullResPerAo() - 0.5 );
        return clamp( fullResPos, int2( 0, 0 ), int2( g_vViewportSize.xy ) - 1 );
    }

    // Where AO texel p sits in full resolution pixels: the center of the texel AoToFullRes picks
    float2 AoToFullResCenter( int2 aoPos )
    {
        return (float2)AoToFullRes( aoPos ) + 0.5;
    }

    // Viewspace position (GTAO's convention: +z forward, +y up) of a full resolution pixel position
    float3 ViewPosition( float2 fullResPos, float viewZ, GTAOConstants consts )
    {
        float2 uv = fullResPos / g_vViewportSize.xy;
        return float3( ( consts.NDCToViewMul * uv + consts.NDCToViewAdd ) * viewZ, viewZ );
    }

    //-------------------------------------------------------------------------------------------------------------------

    // Bit trick log2: piecewise linear between powers of two, under 0.09 off. Plenty to pick a depth MIP with.
    float FastLog2( float x )
    {
        return (float)asint( x ) * ( 1.0 / 8388608.0 ) - 127.0;
    }

    lpfloat3 LoadViewNormal( int2 fullResPos )
    {
        lpfloat3 viewnormal = (lpfloat3)Vector3WsToVs( LoadGBufferNormal( fullResPos ) );
        viewnormal.z = -viewnormal.z;
        return viewnormal;
    }

    //-------------------------------------------------------------------------------------------------------------------
    //
    // View depth chain at AO resolution: XeGTAO_PrefilterDepths16x16, but MIP 0 holds exactly the full resolution
    // texels the AO texels stand for (AoToFullRes), linearised. The main pass snaps its offsets to whole AO pixels, so
    // a full resolution MIP 0 only cost bandwidth: at half resolution it was most of this pass's time.
    //
    //-------------------------------------------------------------------------------------------------------------------

    float LoadViewDepth( int2 fullResPos, GTAOConstants consts )
    {
        return XeGTAO_ClampDepth( XeGTAO_ScreenSpaceToViewSpaceDepth( Depth::Get( fullResPos ), consts ) );
    }

    // The prefilter's groups run past the edges of its MIPs; a write past the end of a storage image isn't safe everywhere
    void StoreDepth( RWTexture2D<float> target, uint2 pos, float depth )
    {
        uint2 size;
        target.GetDimensions( size.x, size.y );
        if ( all( pos < size ) )
            target[pos] = depth;
    }

    void PrefilterDepths( uint2 dispatchThreadID, uint2 groupThreadID, GTAOConstants consts,
                          RWTexture2D<float> outDepth0, RWTexture2D<float> outDepth1, RWTexture2D<float> outDepth2, RWTexture2D<float> outDepth3, RWTexture2D<float> outDepth4 )
    {
        // MIP 0: this thread's 2x2 AO texels
        const uint2 baseCoord = dispatchThreadID;
        const int2 pixCoord = int2( baseCoord * 2 );
        const float depth0 = LoadViewDepth( AoToFullRes( pixCoord + int2( 0, 0 ) ), consts );
        const float depth1 = LoadViewDepth( AoToFullRes( pixCoord + int2( 1, 0 ) ), consts );
        const float depth2 = LoadViewDepth( AoToFullRes( pixCoord + int2( 0, 1 ) ), consts );
        const float depth3 = LoadViewDepth( AoToFullRes( pixCoord + int2( 1, 1 ) ), consts );
        StoreDepth( outDepth0, pixCoord + uint2( 0, 0 ), depth0 );
        StoreDepth( outDepth0, pixCoord + uint2( 1, 0 ), depth1 );
        StoreDepth( outDepth0, pixCoord + uint2( 0, 1 ), depth2 );
        StoreDepth( outDepth0, pixCoord + uint2( 1, 1 ), depth3 );

        // MIP 1
        lpfloat dm1 = XeGTAO_DepthMIPFilter( depth0, depth1, depth2, depth3, consts );
        StoreDepth( outDepth1, baseCoord, dm1 );
        g_scratchDepths[ groupThreadID.x ][ groupThreadID.y ] = dm1;

        GroupMemoryBarrierWithGroupSync();

        // MIP 2
        [branch]
        if ( all( ( groupThreadID.xy % 2.xx ) == 0 ) )
        {
            lpfloat inTL = g_scratchDepths[groupThreadID.x+0][groupThreadID.y+0];
            lpfloat inTR = g_scratchDepths[groupThreadID.x+1][groupThreadID.y+0];
            lpfloat inBL = g_scratchDepths[groupThreadID.x+0][groupThreadID.y+1];
            lpfloat inBR = g_scratchDepths[groupThreadID.x+1][groupThreadID.y+1];

            lpfloat dm2 = XeGTAO_DepthMIPFilter( inTL, inTR, inBL, inBR, consts );
            StoreDepth( outDepth2, baseCoord / 2, dm2 );
            g_scratchDepths[ groupThreadID.x ][ groupThreadID.y ] = dm2;
        }

        GroupMemoryBarrierWithGroupSync();

        // MIP 3
        [branch]
        if ( all( ( groupThreadID.xy % 4.xx ) == 0 ) )
        {
            lpfloat inTL = g_scratchDepths[groupThreadID.x+0][groupThreadID.y+0];
            lpfloat inTR = g_scratchDepths[groupThreadID.x+2][groupThreadID.y+0];
            lpfloat inBL = g_scratchDepths[groupThreadID.x+0][groupThreadID.y+2];
            lpfloat inBR = g_scratchDepths[groupThreadID.x+2][groupThreadID.y+2];

            lpfloat dm3 = XeGTAO_DepthMIPFilter( inTL, inTR, inBL, inBR, consts );
            StoreDepth( outDepth3, baseCoord / 4, dm3 );
            g_scratchDepths[ groupThreadID.x ][ groupThreadID.y ] = dm3;
        }

        GroupMemoryBarrierWithGroupSync();

        // MIP 4
        [branch]
        if ( all( ( groupThreadID.xy % 8.xx ) == 0 ) )
        {
            lpfloat inTL = g_scratchDepths[groupThreadID.x+0][groupThreadID.y+0];
            lpfloat inTR = g_scratchDepths[groupThreadID.x+4][groupThreadID.y+0];
            lpfloat inBL = g_scratchDepths[groupThreadID.x+0][groupThreadID.y+4];
            lpfloat inBR = g_scratchDepths[groupThreadID.x+4][groupThreadID.y+4];

            StoreDepth( outDepth4, baseCoord / 8, XeGTAO_DepthMIPFilter( inTL, inTR, inBL, inBR, consts ) );
        }
    }

    //-------------------------------------------------------------------------------------------------------------------
    //
    // GTAO main pass ("Practical Real-Time Strategies for Accurate Indirect Occlusion", Jimenez et al. 2016, Algorithm 1),
    // after XeGTAO_MainPass with the same heuristics and output, but rearranged for the GPU:
    //
    //  - A slice's depth fetches are all issued before any of them is used. Written step by step, the compiler waited on
    //    each step's pair before computing the next step's addresses: three memory round trips a slice, not one.
    //  - No sin, cos, acos or pow in the slice loop. The slice direction is rotated from the last one. n, the projected
    //    normal's angle, is only ever needed as cos(n), which is a dot product, and sin(n), which follows from it.
    //    cos(2h - n) expands to double angle terms of the horizon cosine. The sample distribution power is 2.
    //  - An offset's length is s * radius by construction, so the MIP pick needs no length, and a bit trick log.
    //
    //-------------------------------------------------------------------------------------------------------------------

    #define GTAO_MAX_STEPS 8

    // A depth sample's horizon cosine, faded to the low horizon with distance; XeGTAO's thickness heuristic stretches z
    // before the falloff, so samples behind the center fall off sooner
    float SampleHorizonCos( float2 uv, float sampleZ, float3 pixCenterPos, float3 viewVec, float lowHorizonCos, float thinK, float falloffMul, float falloffAdd, GTAOConstants consts )
    {
        const float3 sampleDelta = float3( ( consts.NDCToViewMul * uv + consts.NDCToViewAdd ) * sampleZ, sampleZ ) - pixCenterPos;
        const float sampleDistSq = dot( sampleDelta, sampleDelta );
        const float shc = dot( sampleDelta, viewVec ) * rsqrt( sampleDistSq );
        const float weight = saturate( XeGTAO_FastSqrt( sampleDistSq + thinK * sampleDelta.z * sampleDelta.z ) * falloffMul + falloffAdd );
        return lerp( lowHorizonCos, shc, weight );
    }

    float GTAO_Visibility( int2 fullResPos, float viewspaceZ, float3 viewspaceNormal, float2 localNoise,
                           int sliceCount, const int stepsPerSlice, GTAOConstants consts )
    {
        float2 fullResPixelSize = 1.0 / g_vViewportSize.xy;
        float2 centerUV = ( (float2)fullResPos + 0.5 ) * fullResPixelSize;

        // Move center pixel slightly towards camera to avoid imprecision artifacts due to depth buffer imprecision
        viewspaceZ *= 0.99920;

        const float3 pixCenterPos = float3( ( consts.NDCToViewMul * centerUV + consts.NDCToViewAdd ) * viewspaceZ, viewspaceZ );
        const float3 viewVec = normalize( -pixCenterPos );

        const float effectRadius = consts.EffectRadius * consts.RadiusMultiplier;
        const float falloffRange = consts.EffectFalloffRange * effectRadius;
        const float falloffFrom = effectRadius * ( 1 - consts.EffectFalloffRange );
        const float falloffMul = -1.0 / falloffRange;
        const float falloffAdd = falloffFrom / falloffRange + 1.0;

        // XeGTAO's thickness heuristic stretches z before the falloff: |(x, y, z * (1 + t))|
        const float thinK = ( 1 + consts.ThinOccluderCompensation ) * ( 1 + consts.ThinOccluderCompensation ) - 1;

        // Screen space radius in AO pixels
        const float pixelViewspaceSize = viewspaceZ * consts.NDCToViewMul_x_PixelSize.x;
        const float screenspaceRadius = effectRadius / pixelViewspaceSize;

        // fade out for small screen radii
        float visibility = saturate( ( 10 - screenspaceRadius ) / 100 ) * 0.5;

        const float pixelTooCloseThreshold = 1.0;
        [branch]
        if ( screenspaceRadius < pixelTooCloseThreshold )
            return 1;

        // the min distance to start sampling from to avoid sampling from the center pixel
        const float minS = pixelTooCloseThreshold / screenspaceRadius;

        // log2 of a sample's pixel distance is log2( s ) + log2( radius ); the MIP offset folds in here once
        const float mipBias = FastLog2( screenspaceRadius ) - consts.DepthMIPSamplingOffset;

        // An AO pixel, in UV. Offsets are snapped to whole AO pixels as XeGTAO snaps them.
        const float2 aoPixelSize = consts.ViewportPixelSize;

        // Slice directions: phi_k = ( k + noise ) * PI / sliceCount, rotated from one to the next
        float stepAngle = XE_GTAO_PI / sliceCount;
        float2 stepRotation = float2( cos( stepAngle ), sin( stepAngle ) );
        float2 direction;
        sincos( localNoise.x * stepAngle, direction.y, direction.x );

        const float NdotV = dot( viewspaceNormal, viewVec );

        [loop]
        for ( int slice = 0; slice < sliceCount; slice++ )
        {
            // lines 5-8 from the paper
            const float3 directionVec = float3( direction, 0 );
            const float2 omega = float2( direction.x, -direction.y ) * screenspaceRadius;

            // line 9: the slice direction projected into the plane facing the view
            const float3 orthoDirectionVec = directionVec - dot( directionVec, viewVec ) * viewVec;

            // line 10: orthogonal to the slice; cross( orthoDirectionVec, viewVec ) is the same as with directionVec
            const float3 axisVec = normalize( cross( directionVec, viewVec ) );

            // line 11-14: the normal projected into the slice, its length and the cosine of its angle n to the view
            const float3 projectedNormalVec = viewspaceNormal - axisVec * dot( viewspaceNormal, axisVec );
            const float projectedNormalVecLengthSq = dot( projectedNormalVec, projectedNormalVec );
            const float projectedNormalVecRcpLength = rsqrt( projectedNormalVecLengthSq );
            float projectedNormalVecLength = projectedNormalVecLengthSq * projectedNormalVecRcpLength;

            // dot( projectedNormalVec, viewVec ) is dot( viewspaceNormal, viewVec ): viewVec is in the slice plane
            const float cosN = saturate( NdotV * projectedNormalVecRcpLength );
            const float signN = dot( orthoDirectionVec, projectedNormalVec ) < 0 ? -1.0 : 1.0;
            const float sinN = signN * sqrt( saturate( 1 - cosN * cosN ) );

            // the lower weight targets cos( n + PI/2 ) and cos( n - PI/2 )
            const float lowHorizonCos0 = -sinN;
            const float lowHorizonCos1 = sinN;

            float horizonCos0 = lowHorizonCos0;
            float horizonCos1 = lowHorizonCos1;

            if ( D_QUALITY == 3 )
            {
                // Reference: every pixel along the slice out to the radius (at most 256), from MIP 0
                const float2 pixelDirection = float2( direction.x, -direction.y );
                const float pixelStep = max( 1.0, screenspaceRadius / 256 );

                [loop]
                for ( float d = pixelStep; d <= screenspaceRadius; d += pixelStep )
                {
                    const float2 uvOffset = round( d * pixelDirection ) * aoPixelSize;
                    const float z0 = g_srcWorkingDepth.SampleLevel( PointClamp, centerUV + uvOffset, 0 ).x;
                    const float z1 = g_srcWorkingDepth.SampleLevel( PointClamp, centerUV - uvOffset, 0 ).x;
                    horizonCos0 = max( horizonCos0, SampleHorizonCos( centerUV + uvOffset, z0, pixCenterPos, viewVec, lowHorizonCos0, thinK, falloffMul, falloffAdd, consts ) );
                    horizonCos1 = max( horizonCos1, SampleHorizonCos( centerUV - uvOffset, z1, pixCenterPos, viewVec, lowHorizonCos1, thinK, falloffMul, falloffAdd, consts ) );
                }
            }
            else
            {
                // Every fetch first...
                float2 sampleUV[GTAO_MAX_STEPS];
                float sampleZ0[GTAO_MAX_STEPS];
                float sampleZ1[GTAO_MAX_STEPS];

                [unroll]
                for ( int step = 0; step < stepsPerSlice; step++ )
                {
                    // R1 sequence (http://extremelearning.com.au/unreasonable-effectiveness-of-quasirandom-sequences/)
                    const float stepBaseNoise = float( slice + step * stepsPerSlice ) * 0.6180339887498948482;
                    const float stepNoise = frac( localNoise.y + stepBaseNoise );

                    // approx line 20 from the paper, with added noise, squared for more samples close in
                    float s = ( step + stepNoise ) / stepsPerSlice;
                    s = s * s;

                    // avoid sampling center pixel
                    s += minS;

                    // point sampling with the MIP picked by distance: linear filtering would blend depths across edges. From the
                    // step's middle, a constant once unrolled, rather than the jittered distance
                    const float stepMiddle = ( step + 0.5 ) / stepsPerSlice;
                    const float mipLevel = clamp( 2 * log2( stepMiddle ) + mipBias, 0, XE_GTAO_DEPTH_MIP_LEVELS );

                    sampleUV[step] = round( s * omega ) * aoPixelSize;
                    sampleZ0[step] = g_srcWorkingDepth.SampleLevel( PointClamp, centerUV + sampleUV[step], mipLevel ).x;
                    sampleZ1[step] = g_srcWorkingDepth.SampleLevel( PointClamp, centerUV - sampleUV[step], mipLevel ).x;
                }

                // ...then the horizons (approx lines 23, 24 from the paper)
                [unroll]
                for ( int step = 0; step < stepsPerSlice; step++ )
                {
                    horizonCos0 = max( horizonCos0, SampleHorizonCos( centerUV + sampleUV[step], sampleZ0[step], pixCenterPos, viewVec, lowHorizonCos0, thinK, falloffMul, falloffAdd, consts ) );
                    horizonCos1 = max( horizonCos1, SampleHorizonCos( centerUV - sampleUV[step], sampleZ1[step], pixCenterPos, viewVec, lowHorizonCos1, thinK, falloffMul, falloffAdd, consts ) );
                }
            }

            // XeGTAO's fudge against slight overdarkening on high slopes
            projectedNormalVecLength = lerp( projectedNormalVecLength, 1, 0.05 );

            // line ~27: h0 = -acos( horizonCos1 ), h1 = acos( horizonCos0 ), and the arc integrals
            //   iarc( h ) = ( cos( n ) + 2 h sin( n ) - cos( 2h - n ) ) / 4
            // with cos( 2h - n ) = cos( 2h ) cos( n ) + sin( 2h ) sin( n ), cos( 2h ) = 2 cos²h - 1 and sin( 2h ) = 2 sin h cos h
            const float h0 = -XeGTAO_FastACos( horizonCos1 );
            const float h1 = XeGTAO_FastACos( horizonCos0 );
            const float sinH0 = -sqrt( saturate( 1 - horizonCos1 * horizonCos1 ) );
            const float sinH1 = sqrt( saturate( 1 - horizonCos0 * horizonCos0 ) );
            const float cos2H0MinusN = ( 2 * horizonCos1 * horizonCos1 - 1 ) * cosN + 2 * sinH0 * horizonCos1 * sinN;
            const float cos2H1MinusN = ( 2 * horizonCos0 * horizonCos0 - 1 ) * cosN + 2 * sinH1 * horizonCos0 * sinN;
            const float iarc0 = ( cosN + 2 * h0 * sinN - cos2H0MinusN ) * 0.25;
            const float iarc1 = ( cosN + 2 * h1 * sinN - cos2H1MinusN ) * 0.25;
            visibility += projectedNormalVecLength * ( iarc0 + iarc1 );

            // next slice
            direction = float2( direction.x * stepRotation.x - direction.y * stepRotation.y, direction.x * stepRotation.y + direction.y * stepRotation.x );
        }

        visibility /= sliceCount;
        visibility = pow( visibility, consts.FinalValuePower );

        // disallow total occlusion (which wouldn't make any sense anyhow since pixel is visible). The small radius fade can
        // push it past 1, which a float target at full resolution would keep
        return clamp( visibility, 0.03, 1.0 );
    }

    // Inside both the viewport and the target written: a write past the end of a storage image isn't safe everywhere
    bool InBounds( uint2 pos, float2 viewport, uint2 target )
    {
        return all( pos < (uint2)viewport ) && all( pos < target );
    }

    //-------------------------------------------------------------------------------------------------------------------
    //
    // Denoise: 3x3 À-Trous, the 1 2 1 kernel at steps 1, 2 and 4, stopping at edges on view depth relative to the
    // center's and on normal similarity. A 16x16 group reads its texels' visibility, depth and G-buffer normals, and the
    // border its steps reach, into groupshared memory once. A dispatch runs one step, or two (s and 2s) back to back in
    // groupshared memory, the first over the group's texels and the second step's reach.
    //
    //-------------------------------------------------------------------------------------------------------------------

    #define DENOISE_GROUP 16
    #define DENOISE_TILE ( DENOISE_GROUP + 2 * 4 )

    groupshared float  g_denoiseAO[2][DENOISE_TILE * DENOISE_TILE];
    groupshared float4 g_denoiseNormalZ[DENOISE_TILE * DENOISE_TILE];       // normal, view depth

    float DenoiseTap( int centerIndex, int stepWidth, int source )
    {
        const float4 center = g_denoiseNormalZ[centerIndex];
        const float depthScale = 1.0 / max( abs( center.w ) * ( 0.01 * stepWidth ), 0.01 );

        float sum = g_denoiseAO[source][centerIndex] * 4.0;
        float sumW = 4.0;

        [unroll]
        for ( int y = -1; y <= 1; y++ )
        {
            [unroll]
            for ( int x = -1; x <= 1; x++ )
            {
                if ( x == 0 && y == 0 )
                    continue;

                const int index = centerIndex + ( y * DENOISE_TILE + x ) * stepWidth;
                const float4 tap = g_denoiseNormalZ[index];

                const float depthW = exp2( -abs( tap.w - center.w ) * depthScale );

                // dot^24 by squaring
                const float nDot = saturate( dot( tap.xyz, center.xyz ) );
                const float n2 = nDot * nDot;
                const float n4 = n2 * n2;
                const float n8 = n4 * n4;

                const float w = ( x == 0 ? 2.0 : 1.0 ) * ( y == 0 ? 2.0 : 1.0 ) * depthW * n8 * n8 * n8;
                sum += g_denoiseAO[source][index] * w;
                sumW += w;
            }
        }

        return sum / sumW;
    }

    float SpatialDenoise( int2 aoPos, uint2 groupThreadId, GTAOConstants consts )
    {
        const int2 aoMax = int2( consts.ViewportSize ) - 1;
        const int stepWidth = clamp( g_nSpatialStep, 1, 4 );
        const bool twoSteps = g_nSpatialSteps > 1 && stepWidth <= 1;
        const int reach = twoSteps ? 3 * stepWidth : stepWidth;
        const int tileSize = DENOISE_GROUP + 2 * reach;
        const int2 tileOrigin = aoPos - int2( groupThreadId ) - reach;
        const uint thread = groupThreadId.y * DENOISE_GROUP + groupThreadId.x;

        for ( uint i = thread; i < (uint)( tileSize * tileSize ); i += DENOISE_GROUP * DENOISE_GROUP )
        {
            const int2 tilePos = int2( i % tileSize, i / tileSize );
            const int2 p = clamp( tileOrigin + tilePos, int2( 0, 0 ), aoMax );
            const int index = tilePos.y * DENOISE_TILE + tilePos.x;
            g_denoiseAO[0][index] = g_tAOZ.Load( int3( p, 0 ) );
            g_denoiseNormalZ[index] = float4( LoadGBufferNormal( AoToFullRes( p ) ), g_srcWorkingDepth.Load( int3( p, 0 ) ) );
        }

        GroupMemoryBarrierWithGroupSync();

        int source = 0;
        int step = stepWidth;

        if ( twoSteps )
        {
            // The first step over the group's texels and what the second step reaches around them
            const int from = reach - 2 * stepWidth;
            const int size = DENOISE_GROUP + 4 * stepWidth;
            for ( uint j = thread; j < (uint)( size * size ); j += DENOISE_GROUP * DENOISE_GROUP )
            {
                const int index = ( from + (int)( j / size ) ) * DENOISE_TILE + from + (int)( j % size );
                g_denoiseAO[1][index] = DenoiseTap( index, stepWidth, 0 );
            }

            GroupMemoryBarrierWithGroupSync();
            source = 1;
            step = 2 * stepWidth;
        }

        const int centerIndex = ( (int)groupThreadId.y + reach ) * DENOISE_TILE + (int)groupThreadId.x + reach;
        return DenoiseTap( centerIndex, step, source );
    }

    //-------------------------------------------------------------------------------------------------------------------
    //
    // Upsample: the 2x2 coarse texels around a full resolution pixel, bilinear weights times each texel's distance to
    // the pixel's tangent plane. The pixel's own depth and normal are the only full resolution reads.
    //
    //-------------------------------------------------------------------------------------------------------------------

    float BilateralUpsampleAO( int2 fullResPixel, GTAOConstants consts )
    {
        const float2 fullPerAo = FullResPerAo();
        const int2 aoMax = int2( consts.ViewportSize ) - 1;

        // The 2x2 texels around the pixel, kept inside the viewport, come from one gather a channel. This picks them at
        // any scale, and the bilinear weights come from where those texels really sit: AoToFullResCenter's offset isn't
        // the same at odd and even scales
        const float2 aoCoord = ( (float2)fullResPixel + 1.0 ) / fullPerAo - 0.5;
        const int2 aoBase = clamp( int2( floor( aoCoord ) ), int2( 0, 0 ), max( aoMax - 1, int2( 0, 0 ) ) );
        const float2 center0 = AoToFullResCenter( aoBase );
        const float2 center1 = AoToFullResCenter( aoBase + 1 );
        const float2 f = saturate( ( (float2)fullResPixel + 0.5 - center0 ) / max( center1 - center0, 1.0 ) );

        float2 textureSize;
        g_tAOZ.GetDimensions( textureSize.x, textureSize.y );
        const float2 gatherUV = ( (float2)aoBase + 1.0 ) / textureSize;

        // Gathers return ( 0, 1 ), ( 1, 1 ), ( 1, 0 ), ( 0, 0 ); swizzled to ( 0, 0 ), ( 1, 0 ), ( 0, 1 ), ( 1, 1 )
        const float4 tapAO = g_tAOZ.GatherRed( PointClamp, gatherUV ).wzxy;
        const float4 tapZ = g_srcWorkingDepth.GatherRed( PointClamp, gatherUV ).wzxy;
        const float4 bilinear = float4( ( 1 - f.x ) * ( 1 - f.y ), f.x * ( 1 - f.y ), ( 1 - f.x ) * f.y, f.x * f.y );

        const float hiZ = LoadViewDepth( fullResPixel, consts );
        const float3 n = LoadViewNormal( fullResPixel );

        // A view position is ( A z, z ) with A linear in the pixel, so its distance along n is z ( A . n.xy + n.z ). The
        // taps are an AO pixel apart, which moves A by NDCToViewMul_x_PixelSize
        const float2 hiA = consts.NDCToViewMul * ( ( (float2)fullResPixel + 0.5 ) / g_vViewportSize.xy ) + consts.NDCToViewAdd;
        const float2 baseA = consts.NDCToViewMul * ( center0 / g_vViewportSize.xy ) + consts.NDCToViewAdd;
        const float2 stepK = consts.NDCToViewMul_x_PixelSize * n.xy;
        const float baseK = dot( baseA, n.xy ) + n.z;
        const float4 tapK = baseK + float4( 0, stepK.x, stepK.y, stepK.x + stepK.y );
        const float4 planeDistance = abs( tapZ * tapK - hiZ * ( dot( hiA, n.xy ) + n.z ) );

        // A crease has no depth step and the tolerance grows with the AO scale, so the taps' own normals keep faces
        // apart as in the denoiser (dot^24)
        const float3 hiN = LoadGBufferNormal( fullResPixel );
        const float4 nDot = saturate( float4(
            dot( LoadGBufferNormal( AoToFullRes( aoBase ) ), hiN ),
            dot( LoadGBufferNormal( AoToFullRes( min( aoBase + int2( 1, 0 ), aoMax ) ) ), hiN ),
            dot( LoadGBufferNormal( AoToFullRes( min( aoBase + int2( 0, 1 ), aoMax ) ) ), hiN ),
            dot( LoadGBufferNormal( AoToFullRes( min( aoBase + int2( 1, 1 ), aoMax ) ) ), hiN ) ) );
        const float4 n2 = nDot * nDot;
        const float4 n4 = n2 * n2;
        const float4 n8 = n4 * n4;

        // Tolerance scaled to a coarse texel's footprint at this depth
        const float planeScale = 1.0 / max( hiZ * consts.NDCToViewMul_x_PixelSize.x * 0.5, 1e-4 );
        const float4 w = ( bilinear + 1e-3 ) * exp2( -planeDistance * planeScale ) * n8 * n8 * n8;
        const float sumW = dot( w, 1 );

        // Every coarse texel is on another surface (thin features): take the closest rather than smear
        if ( sumW > 1e-4 )
            return dot( w, tapAO ) / sumW;

        const float2 nearest01 = planeDistance.x < planeDistance.y ? float2( planeDistance.x, tapAO.x ) : float2( planeDistance.y, tapAO.y );
        const float2 nearest23 = planeDistance.z < planeDistance.w ? float2( planeDistance.z, tapAO.z ) : float2( planeDistance.w, tapAO.w );
        return nearest01.x < nearest23.x ? nearest01.y : nearest23.y;
    }

    //-------------------------------------------------------------------------------------------------------------------

#if D_PASS == 2 // DenoiseSpatial
    [numthreads( DENOISE_GROUP, DENOISE_GROUP, 1 )]
#else
    [numthreads( 8, 8, 1 )]
#endif
    void MainCs( uint2 vDispatchId : SV_DispatchThreadID, uint2 vGroupThreadID : SV_GroupThreadID )
    {
        GTAOConstants sGTAOConsts = GetConstants();

        if ( D_PASS == GTAOPasses::ViewDepthChain )
        {
            #if ( D_PASS == 0 )
                PrefilterDepths( vDispatchId, vGroupThreadID, sGTAOConsts, g_outWorkingDepthMIP0, g_outWorkingDepthMIP1, g_outWorkingDepthMIP2, g_outWorkingDepthMIP3, g_outWorkingDepthMIP4 );
            #endif
        }
        else if ( D_PASS == GTAOPasses::MainPass )
        {
            uint2 target;
            g_outAOZ.GetDimensions( target.x, target.y );
            if ( !InBounds( vDispatchId, sGTAOConsts.ViewportSize, target ) )
                return;

            const int2 aoPos = int2( vDispatchId );
            const int2 fullResPos = AoToFullRes( aoPos );

            // Blue noise rotates the slices: spatially uniform, perceptually much smoother after spatial denoising. The steps
            // don't get a per-pixel offset (y = 0.5, the R1 sequence still varies them per slice): that jitter added a quarter
            // of the shimmer in motion and no accuracy
            const float2 localNoise = g_tBlueNoise[ aoPos % 256 ].rg;

            const float viewspaceZ = g_srcWorkingDepth.Load( int3( aoPos, 0 ) );
            const float3 viewspaceNormal = LoadViewNormal( fullResPos );

            // Steps find the horizon: a slice's horizon is the highest of its samples, so too few miss occluders and AO comes out
            // too light, which no denoiser can fix. Slices are what keep it still in motion: with 3 the rotation pattern swims.
            // High is 4x8: 4x6 is 25-55% further from the per pixel reference and shimmers no less.
            float visibility;
            if ( D_QUALITY == 0 )       visibility = GTAO_Visibility( fullResPos, viewspaceZ, viewspaceNormal, localNoise, 2, 6, sGTAOConsts );
            else if ( D_QUALITY == 1 )  visibility = GTAO_Visibility( fullResPos, viewspaceZ, viewspaceNormal, localNoise, 3, 8, sGTAOConsts );
            else if ( D_QUALITY == 2 )  visibility = GTAO_Visibility( fullResPos, viewspaceZ, viewspaceNormal, localNoise, 4, 8, sGTAOConsts );
            else                        visibility = GTAO_Visibility( fullResPos, viewspaceZ, viewspaceNormal, localNoise, g_nReferenceSlices, 8, sGTAOConsts );

            g_outAOZ[aoPos] = visibility;
        }
        else if ( D_PASS == GTAOPasses::DenoiseSpatial )
        {
            // Every thread fills the tile, so none leaves before the barrier
            const float denoised = SpatialDenoise( int2( vDispatchId ), vGroupThreadID, sGTAOConsts );

            uint2 target;
            g_outDenoised.GetDimensions( target.x, target.y );
            if ( InBounds( vDispatchId, sGTAOConsts.ViewportSize, target ) )
                g_outDenoised[vDispatchId] = denoised;
        }
        else if ( D_PASS == GTAOPasses::BilateralUpsample )
        {
            uint2 target;
            g_outFullResAO.GetDimensions( target.x, target.y );
            if ( !InBounds( vDispatchId, g_vViewportSize.xy, target ) )
                return;

            g_outFullResAO[vDispatchId] = BilateralUpsampleAO( int2( vDispatchId ), sGTAOConsts );
        }
    }
}

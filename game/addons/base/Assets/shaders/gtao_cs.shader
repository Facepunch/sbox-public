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
    #include "common\classes\Motion.hlsl"
    
    #include "common\thirdparty\XeGTAO.h"
    #include "common\thirdparty\XeGTAO.hlsl"

    //-------------------------------------------------------------------------------------------------------------------

    DynamicCombo( D_MSAA_NORMALS, 0..1, Sys( All ) );

    // Bind directly, vary type on MSAA, Normals class does not do this
    #if D_MSAA_NORMALS
        Texture2DMS<float4>  g_tNormalsGBuffer  < Attribute("NormalsGBuffer"); >;
    #else
        Texture2D<float4>    g_tNormalsGBuffer  < Attribute("NormalsGBuffer"); >;
    #endif

    float3 SampleWorldNormal( int2 screenPos )
    {
        #if D_MSAA_NORMALS
            float3 n = g_tNormalsGBuffer.Load( screenPos + g_vViewportOffset, 0 ).xyz;
        #else
            float3 n = g_tNormalsGBuffer.Load( int3( screenPos + g_vViewportOffset, 0 ) ).xyz;
        #endif

        // Zero normals = sky / missing geometry — reconstruct from depth
        if ( all( n == 0 ) )
        {
            float3 c = Depth::GetWorldPosition( screenPos );
            float3 r = Depth::GetWorldPosition( screenPos + int2( 1, 0 ) );
            float3 u = Depth::GetWorldPosition( screenPos + int2( 0, 1 ) );
            return -normalize( cross( r - c, u - c ) );
        }

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

    //-------------------------------------------------------------------------------------------------------------------

    // input output textures for the first pass (XeGTAO_PrefilterDepths16x16)
#if ( D_PASS == 0)
    RWTexture2D<float>           g_outWorkingDepthMIP0   < Attribute("WorkingDepthMIP0"); > ;   // output viewspace depth MIP (these are views into g_srcWorkingDepth MIP levels)
    RWTexture2D<float>           g_outWorkingDepthMIP1   < Attribute("WorkingDepthMIP1"); > ;   // output viewspace depth MIP (these are views into g_srcWorkingDepth MIP levels)
    RWTexture2D<float>           g_outWorkingDepthMIP2   < Attribute("WorkingDepthMIP2"); > ;   // output viewspace depth MIP (these are views into g_srcWorkingDepth MIP levels)
    RWTexture2D<float>           g_outWorkingDepthMIP3   < Attribute("WorkingDepthMIP3"); > ;   // output viewspace depth MIP (these are views into g_srcWorkingDepth MIP levels)
    RWTexture2D<float>           g_outWorkingDepthMIP4   < Attribute("WorkingDepthMIP4"); > ;   // output viewspace depth MIP (these are views into g_srcWorkingDepth MIP levels)
    #endif

    // input output textures for the second pass (XeGTAO_MainPass)
    Texture2D<float>             g_srcWorkingDepth       < Attribute("WorkingDepth"); > ;       // viewspace depth with MIPs, output by XeGTAO_PrefilterDepths16x16 and consumed by XeGTAO_MainPass
    RWTexture2D<float>           g_outWorkingAOTerm      < Attribute("WorkingAOTerm"); > ;      // output AO term (includes bent normals if enabled - packed as R11G11B10 scaled by AO)
    RWTexture2D<float>           g_outWorkingEdges       < Attribute("WorkingEdges"); > ;       // output depth-based edges used by the denoiser

    // input output textures for the third pass
    Texture2D                    g_srcWorkingAOTerm      < Attribute("WorkingAOTerm"); > ;    // coming from previous pass
    Texture2D<float>             g_srcWorkingEdges       < Attribute("WorkingEdges"); > ; // coming from previous pass
    RWTexture2D<float>           g_outAO                 < Attribute("FinalAOTerm"); >;         // final AO term - just 'visibility' or 'visibility + bent normals'
    Texture2D                    g_prevAO                < Attribute("FinalAOTermPrev"); >;

    // same-frame spatial denoiser ping-pong resources (configured per pass by C#)
    Texture2D<float>             g_srcSpatialIn          < Attribute("SpatialIn"); >;
    RWTexture2D<float>           g_outSpatialOut         < Attribute("SpatialOut"); >;
    int                          g_nSpatialStep          < Attribute("SpatialStep"); Default(1); >;

    // input output textures for the bilateral upsample pass
    Texture2D<float>             g_tCoarseAO             < Attribute("CoarseAO"); > ;
    Texture2D<float>             g_tViewDepth            < Attribute("ViewDepth"); > ;
    RWTexture2D<float>           g_outFullResAO          < Attribute("FullResAO"); > ;

    SamplerState                PointClamp               < Filter( POINT ); AddressU( CLAMP ); AddressV( CLAMP ); AddressW( CLAMP ); >;
    SamplerState                BilinearClamp            < Filter( MIN_MAG_MIP_LINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); AddressW( CLAMP ); >;

    //-------------------------------------------------------------------------------------------------------------------

    enum GTAOPasses
    {
        ViewDepthChain,
        MainPass,
        DenoiseSpatial,
        DenoiseTemporal,
        BilateralUpsample
    };

    DynamicCombo( D_PASS, 0..4, Sys( All ) );
    DynamicCombo( D_QUALITY, 0..2, Sys( All ) );

    //-------------------------------------------------------------------------------------------------------------------

    // Compute per-viewport GTAO constants from the cbuffer base + engine globals.
    // CommandList API doesn't expose per-viewport parameter changes so we derive them here.
    GTAOConstants GetConstants()
    {
        GTAOConstants consts = g_GTAOConsts;

        // AO-pipeline resolution: full viewport divided by the resolution scale factor.
        float2 aoViewport = max( float2( 1, 1 ), floor( g_vViewportSize.xy / (float)g_nResolutionScale ) );
        consts.ViewportSize = aoViewport;
        consts.ViewportPixelSize = 1.0f / aoViewport;

        float depthLinearizeMul = ( g_flFarPlane * g_flNearPlane ) / ( g_flFarPlane - g_flNearPlane );
        float depthLinearizeAdd = g_flFarPlane / ( g_flFarPlane - g_flNearPlane );
        consts.DepthUnpackConsts = float2( depthLinearizeMul, depthLinearizeAdd );

        consts.CameraTanHalfFOV = float2( 1.0f / g_matViewToProjection[0][0], 1.0f / g_matViewToProjection[1][1] );

        consts.NDCToViewMul = float2( consts.CameraTanHalfFOV.x * 2.0f, consts.CameraTanHalfFOV.y * -2.0f );
        consts.NDCToViewAdd = float2( consts.CameraTanHalfFOV.x * -1.0f, consts.CameraTanHalfFOV.y * 1.0f );
        consts.NDCToViewMul_x_PixelSize = float2( consts.NDCToViewMul.x * consts.ViewportPixelSize.x, consts.NDCToViewMul.y * consts.ViewportPixelSize.y );

        // Depth chain is always full resolution. Shift the MIP selector so step distances
        // map to the same effective depth resolution at reduced AO-res.
        consts.DepthMIPSamplingOffset += log2( (float)g_nResolutionScale );

        return consts;
    }

    // Convert AO pixel coordinates to full-resolution screen coordinates.
    // Use the actual viewport ratio so odd viewport sizes don't accumulate a bias at reduced AO resolutions.
    float2 AoPosToFullResPos( float2 aoPos, float2 aoViewport )
    {
        float2 fullPerAo = g_vViewportSize.xy / max( aoViewport, 1.0.xx );
        return aoPos * fullPerAo;
    }

    //-------------------------------------------------------------------------------------------------------------------

    // Cross-bilateral 3x3 upsample from coarse AO to full resolution.
    // Gaussian spatial kernel with depth + normal edge-stopping weights.
    // 3x3 footprint eliminates blocking artifacts at high resolution scale factors
    // while preserving sharp AO boundaries across geometric edges.
    // Normal similarity is computed in world-space (dot product is rotation-invariant)
    // so we skip the per-sample Vector3WsToVs matrix multiply entirely.
    float BilateralUpsampleAO( uint2 fullResPixel )
    {
        float scale = (float)g_nResolutionScale;
        float2 aoViewport = max( float2( 1, 1 ), floor( g_vViewportSize.xy / scale ) );
        float2 fullPerAo  = g_vViewportSize.xy / max( aoViewport, 1.0.xx );
        int2   aoMax      = int2( aoViewport ) - 1;
        int2   fullMax    = int2( g_vViewportSize.xy ) - 1;

        // Map full-res pixel center to continuous coarse-resolution coordinates.
        // round() centers the 3x3 kernel on the nearest coarse texel for symmetric coverage.
        float2 aoCoord  = ( (float2)fullResPixel + 0.5 ) / fullPerAo - 0.5;
        int2   aoCenter = clamp( int2( round( aoCoord ) ), int2( 0, 0 ), aoMax );
        float2 subTexel = aoCoord - float2( aoCenter );

        // Full-res depth and world-space normal at this pixel (2 loads total)
        float  hiDepth  = g_tViewDepth.Load( int3( fullResPixel, 0 ) );
        float3 hiNormal = SampleWorldNormal( fullResPixel );

        // Depth edge-stopping: distance-proportional tolerance with exponential falloff
        float depthSigma = max( abs( hiDepth ) * 0.002, 0.05 );

        float totalWeight = 0.0;
        float totalAO     = 0.0;

        // 3x3 cross-bilateral kernel centered on the nearest coarse texel.
        // Each sample is weighted by spatial proximity × depth similarity × normal similarity.
        // 9 taps (vs 4 in a 2x2 bilinear) dramatically reduces blocking at 4x/8x scale.
        [unroll]
        for ( int dy = -1; dy <= 1; dy++ )
        {
            [unroll]
            for ( int dx = -1; dx <= 1; dx++ )
            {
                int2 aoPos = clamp( aoCenter + int2( dx, dy ), int2( 0, 0 ), aoMax );

                // Coarse AO sample
                float ao = g_tCoarseAO.Load( int3( aoPos, 0 ) );

                // Map coarse texel center to full-res for depth/normal reference
                int2 fullPos = clamp( int2( ( float2( aoPos ) + 0.5 ) * fullPerAo ), int2( 0, 0 ), fullMax );
                float  loDepth  = g_tViewDepth.Load( int3( fullPos, 0 ) );
                float3 loNormal = SampleWorldNormal( fullPos );

                // Spatial: Gaussian weight centered on the ideal sub-texel position.
                // exp2(-d²) gives tight falloff — center≈1.0, adjacent≈0.5, corner≈0.25.
                float2 d = float2( dx, dy ) - subTexel;
                float spatialW = exp2( -dot( d, d ) );

                // Depth: exponential falloff proportional to absolute depth difference
                float depthW = exp2( -abs( hiDepth - loDepth ) / depthSigma );

                // Normal: pow(saturate(dot), 16) via repeated squaring (4 muls, no transcendentals)
                float normalW = saturate( dot( loNormal, hiNormal ) );
                normalW *= normalW;   // ^2
                normalW *= normalW;   // ^4
                normalW *= normalW;   // ^8
                normalW *= normalW;   // ^16

                float w = spatialW * depthW * normalW;
                totalAO     += ao * w;
                totalWeight += w;
            }
        }

        // Fallback: if all bilateral weights are rejected (strong depth/normal discontinuity),
        // return the nearest coarse texel to avoid black-fringe artifacts.
        if ( totalWeight < 1e-4 )
            return g_tCoarseAO.Load( int3( aoCenter, 0 ) );

        return totalAO / totalWeight;
    }

    //-------------------------------------------------------------------------------------------------------------------
    lpfloat3 LoadNormal( int2 pos, GTAOConstants consts )
    {
        // Normals G-buffer is full resolution while AO can be reduced; map AO pixel to full-res texel.
        float2 fullResPosF = AoPosToFullResPos( (float2)pos + 0.5, consts.ViewportSize ) - 0.5;
        int2 fullResPos = clamp( (int2)fullResPosF, int2( 0, 0 ), int2( g_vViewportSize.xy ) - 1 );
        lpfloat3 viewnormal = (lpfloat3)Vector3WsToVs( SampleWorldNormal( fullResPos ) );
        viewnormal.z = -viewnormal.z;

        return viewnormal;
    }

    //-------------------------------------------------------------------------------------------------------------------
    float TemporalDenoiseAO( uint2 vDispatchId, GTAOConstants sGTAOConsts )
    {
        float taaBlendAmount = sGTAOConsts.TAABlendAmount;

        // Reproject using the full-resolution depth buffer and motion, then sample the AO-resolution
        // history texture. UVs map identically to either resolution (both sample the same [0,1] region).
        float2 fullResPos = AoPosToFullResPos( (float2)vDispatchId + 0.5, sGTAOConsts.ViewportSize ) - 0.5;
        fullResPos = clamp( fullResPos, 0.0.xx, g_vViewportSize.xy - 1.0.xx );
        float3 prevFramePosSs = Motion::Get( fullResPos );
        float2 vPrevUV = saturate( ( prevFramePosSs.xy + 0.5 ) * g_vInvViewportSize );

        // Neighborhood clamp against AO-resolution current samples to suppress ghosting.
        float4 vMin = 9999;
        float4 vMax = -9999;
        int2 aoMax = int2( sGTAOConsts.ViewportSize ) - 1;
        [unroll] for( int i = -1; i <= 1; i++ )
        [unroll] for( int j = -1; j <= 1; j++ )
        {
            int2 samplePos = clamp( int2( vDispatchId ) + int2( i, j ), int2( 0, 0 ), aoMax );
            float4 s = g_srcWorkingAOTerm[ samplePos ];
            vMin = min( vMin, s );
            vMax = max( vMax, s );
        }

        float4 vPrevSample = g_prevAO.SampleLevel( BilinearClamp, vPrevUV, 0 );
        float4 vPrevClamped = clamp( vPrevSample, vMin, vMax );
        float4 vCurrentSample = g_srcWorkingAOTerm[ vDispatchId ];

        return lerp( vCurrentSample, vPrevClamped, taaBlendAmount ).r;
    }

    //---------------------------------------------------------------------------------------------------------------
    // Loads depth + view-space normal for an AO-resolution position in a single coordinate mapping.
    // Fusing these avoids the duplicate AoPosToFullResPos that separate LoadSpatialDepth / LoadNormal would do.
    void LoadSpatialDepthNormal( int2 aoPos, GTAOConstants consts, out float depth, out lpfloat3 normal )
    {
        float2 fullResPosF = AoPosToFullResPos( (float2)aoPos + 0.5, consts.ViewportSize ) - 0.5;
        int2 fullResPos = clamp( (int2)fullResPosF, int2( 0, 0 ), int2( g_vViewportSize.xy ) - 1 );
        depth = g_srcWorkingDepth.Load( int3( fullResPos, 0 ) );
        normal = (lpfloat3)Vector3WsToVs( SampleWorldNormal( fullResPos ) );
        normal.z = -normal.z;
    }

    float SpatialDenoiseATrous( uint2 aoPos, GTAOConstants consts, int stepWidth )
    {
        int2 pos = int2( aoPos );
        int2 aoMax = int2( consts.ViewportSize ) - 1;

        float centerAO = g_srcSpatialIn.Load( int3( clamp( pos, int2( 0, 0 ), aoMax ), 0 ) );

        float centerDepth;
        lpfloat3 centerNormal;
        LoadSpatialDepthNormal( pos, consts, centerDepth, centerNormal );

        // Keep depth sensitivity proportional to distance so far geometry doesn't over-blur.
        float depthSigma = max( abs( centerDepth ) * ( 0.01 * stepWidth ), 0.01 );

        // Center sample always passes edge tests against itself (dot=1, depthDiff=0), so seed
        // the accumulator with its known weight (4.0) and skip its redundant depth/normal loads.
        float sum  = centerAO * 4.0;
        float sumW = 4.0;

        // 8 neighbors of the 3x3 wavelet kernel (1 2 1 / 2 [4] 2 / 1 2 1) with dilated step.
        // Center (weight 4) handled above; offsets + weights stored as compile-time constants.
        static const int2  offsets[8] = {
            int2( -1, -1 ), int2( 0, -1 ), int2( 1, -1 ),
            int2( -1,  0 ),                int2( 1,  0 ),
            int2( -1,  1 ), int2( 0,  1 ), int2( 1,  1 )
        };
        static const float kWeights[8] = { 1, 2, 1, 2, 2, 1, 2, 1 };

        [unroll]
        for ( int i = 0; i < 8; i++ )
        {
            int2 samplePos = clamp( pos + offsets[i] * stepWidth, int2( 0, 0 ), aoMax );

            float ao = g_srcSpatialIn.Load( int3( samplePos, 0 ) );

            // Fused depth + normal load: single AoPosToFullResPos instead of two separate calls.
            float sampleDepth;
            lpfloat3 sampleNormal;
            LoadSpatialDepthNormal( samplePos, consts, sampleDepth, sampleNormal );

            float depthW = exp2( -abs( sampleDepth - centerDepth ) / depthSigma );

            // pow(dot, 24) via repeated squaring (5 muls, no transcendentals).
            float nDot = saturate( dot( sampleNormal, centerNormal ) );
            float n2  = nDot * nDot;   // ^2
            float n4  = n2  * n2;      // ^4
            float n8  = n4  * n4;      // ^8
            float n16 = n8  * n8;      // ^16
            float normalW = n8 * n16;  // ^24

            float w = kWeights[i] * depthW * normalW;
            sum  += ao * w;
            sumW += w;
        }

        if ( sumW <= 1e-5 )
            return centerAO;

        return sum / sumW;
    }
    
    //-------------------------------------------------------------------------------------------------------------------
    
    [numthreads( 8, 8, 1 )]
    void MainCs( uint2 vDispatchId : SV_DispatchThreadID, uint2 vGroupThreadID : SV_GroupThreadID )
    {
        GTAOConstants sGTAOConsts = GetConstants();

        if ( D_PASS == GTAOPasses::ViewDepthChain )
        {
            #if ( D_PASS == 0 )
                XeGTAO_PrefilterDepths16x16( vDispatchId, vGroupThreadID, sGTAOConsts, g_tDepthChain, PointClamp, g_outWorkingDepthMIP0, g_outWorkingDepthMIP1, g_outWorkingDepthMIP2, g_outWorkingDepthMIP3, g_outWorkingDepthMIP4 );
            #endif
        }
        else if ( D_PASS == GTAOPasses::MainPass )
        {
            lpfloat2 localNoise;
            
            // idk blue noise is perceptually much smoother on spatial denoising
            const bool bUseBlueNoise = true;

            if ( bUseBlueNoise )
            {
                // Blue noise: spatially uniform, temporally animated via Cranley-Patterson rotation
                int2 noiseCoord = ( vDispatchId.xy + int2( sGTAOConsts.NoiseIndex * 7, sGTAOConsts.NoiseIndex * 3 ) ) % 256;
                localNoise = lpfloat2( g_tBlueNoise[ noiseCoord ].rg );
            }
            else
            {
                // Hilbert R2 quasi-random sequence — best low-discrepancy spatiotemporal noise for GTAO
                uint hilbertIndex = HilbertIndex( vDispatchId.x, vDispatchId.y );
                hilbertIndex += 288 * ( sGTAOConsts.NoiseIndex % 64 );
                localNoise = lpfloat2( frac( 0.5 + hilbertIndex * float2( 0.75487766624669276005, 0.5698402909980532659114 ) ) );
            }
            
            const lpfloat3 viewspaceNormal = LoadNormal( vDispatchId.xy, sGTAOConsts );

            // Quality presets matched to Intel XeGTAO reference implementation.
            // Lower counts rely on Hilbert R2 noise distribution + temporal denoising to converge.
            lpfloat sliceCount;
            lpfloat stepsPerSlice;

            if ( D_QUALITY == 0 )
            {
                sliceCount    = 3;  // Low: 6 total samples — fast, relies heavily on temporal accumulation
                stepsPerSlice = 3;
            } 
            else if ( D_QUALITY == 1 )
            {
                sliceCount    = 5;  // Medium: 15 total samples — balanced quality/performance
                stepsPerSlice = 3;
            }
            else if ( D_QUALITY == 2 )
            {
                sliceCount    = 9;  // High: 27 total samples — reference quality (Intel Ultra preset)
                stepsPerSlice = 3;
            }

            XeGTAO_MainPass
            (
                vDispatchId,
                sliceCount,
                stepsPerSlice,
                localNoise,
                viewspaceNormal,
                sGTAOConsts,
                g_srcWorkingDepth,
                PointClamp,
                g_outWorkingAOTerm,
                g_outWorkingEdges
            );
        }
        else if ( D_PASS == GTAOPasses::DenoiseSpatial )
        {
            if ( vDispatchId.x >= sGTAOConsts.ViewportSize.x || vDispatchId.y >= sGTAOConsts.ViewportSize.y )
                return;

            int stepWidth = max( g_nSpatialStep, 1 );
            g_outSpatialOut[vDispatchId] = SpatialDenoiseATrous( vDispatchId, sGTAOConsts, stepWidth );
        }
        else if ( D_PASS == GTAOPasses::DenoiseTemporal )
        {
            g_outAO[vDispatchId] = TemporalDenoiseAO( vDispatchId, sGTAOConsts );
        }
        else if ( D_PASS == GTAOPasses::BilateralUpsample )
        {
            if ( vDispatchId.x >= (uint)g_vViewportSize.x || vDispatchId.y >= (uint)g_vViewportSize.y )
                return;

            g_outFullResAO[vDispatchId] = BilateralUpsampleAO( vDispatchId );
        }
    }
}
// framecopy_mask_cs: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// The frame buffer copy made by its depth-aware mask: depthaware_mask_cs (native's DepthAwareMask_CS.shader), which writes
// last frame's blurred copy, reprojected, where something is in front of the glass, with the frame copied (D_COPY 1) or
// resolved (D_COPY 2 or 3) everywhere else, over a region of the copy (the readers', in a 3D skybox).
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "The frame buffer copy and its depth-aware mask with temporal reprojection, in one pass";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#include "sbox/pass.slangh"

Mode( Default );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "sbox/system.slangh" // This should always be the first include in COMMON

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_CS )
    #include "sbox/view.slangh"
    #include "sbox/math.slangh"

    #include "common/classes/Depth.hlsl"
    #include "common/classes/Motion.hlsl"

    // Input textures
    Texture2D DepthMask < Attribute("RefractionDepthTexture"); > ; // Depth mask for comparison, applied from pipeline
    Texture2D Mip < Attribute("Mip"); > ; // Blurred texture to sample from

    // Output texture
    RWTexture2D<float4> Output < Attribute("Output"); > ;

    // 1 copies a single sample; 2 resolves a variable number, and 3 resolves four samples without a dynamic loop.
    DynamicCombo( D_COPY, 1..3, Sys( ALL ) );
#if ( D_COPY != 1 )
    Texture2DMS<float4> Source < Attribute("MaskSource"); SrgbRead( false ); > ;
#if ( D_COPY == 2 )
    float SourceSamples < Attribute("MaskSamples"); > ;
#endif
#else
    Texture2D Source < Attribute("MaskSource"); SrgbRead( false ); > ;
#endif

    // The pixels this dispatch covers, in the viewport: x, y its first, z, w past its last (the readers' area)
    float4 Region < Attribute("MaskRegion"); > ;
    

    float GetDepthMask(int2 vDispatchId)
    {
        uint2 dims;
        DepthMask.GetDimensions( dims.x, dims.y );
        const uint2 baseCoord = uint2( (vDispatchId.xy + 0.5f) * g_vInvViewportSize * dims );

        float depth = DepthMask[baseCoord].x;

        return depth;
    }
    //-----------------------------------------------------------------------------------------
    // ReprojectDepthMask: Transforms a pixel coordinate from current frame to previous frame
    // using depth-based reprojection
    //
    // Same as Motion::Get but uses depth from refraction object
    //-----------------------------------------------------------------------------------------
    float2 ReprojectDepthMask(int2 vDispatchId, float maskDepth)
    {
        // Get normalized depth value of where the refraction is
        float flDepth = Depth::Normalize( maskDepth );

        // Calculate ray in NDC space
        float3 vRay = float3(vDispatchId * g_vInvViewportSize, flDepth);
        vRay.y = 1.0 - vRay.y;
        vRay.xy = 2.0f * vRay.xy - 1.0f;

        // Transform to world space
        float4 vWorldPos = mul(g_matProjectionToWorld, float4(vRay, 1.0f));
        vWorldPos.xyz /= max(vWorldPos.w, 1e-10); // Avoid division by zero

        // Reproject world position to previous frame's screen space
        const float3 prevFramePosSs = Motion::GetFromWorldPosition(vWorldPos.xyz + g_vCameraPositionWs.xyz);

        // Return reprojected coordinates
        return ( prevFramePosSs.xy + 0.5f );
    }

    //-----------------------------------------------------------------------------------------
    // IsValidCoordinate: Checks if the given reprojected coordinate is within the valid bounds
    //-----------------------------------------------------------------------------------------
    bool IsValidCoordinate(float2 vReadPos)
    {
        float2 vQuarterResolution = g_vViewportSize.xy;
        return vReadPos.x >= 0 && vReadPos.x < vQuarterResolution.x &&
               vReadPos.y >= 0 && vReadPos.y < vQuarterResolution.y;
    }

    //-----------------------------------------------------------------------------------------
    // Processes depth-aware masking at quarter resolution and writes results at full resolution
    //-----------------------------------------------------------------------------------------
    [numthreads(16, 16, 1)]
    [shader( "compute" )] void MainCs(uint2 vThreadId: SV_DispatchThreadID)
    {
        const uint2 vDispatchId = vThreadId + (uint2)Region.xy;
        if( vDispatchId.x >= Region.z || vDispatchId.y >= Region.w )
            return;

        float sceneDepth = g_tDepthChain.Load(int3(vDispatchId.xy, 0)).g;

        // Load depth from our mask texture
        float maskDepth = GetDepthMask( vDispatchId );
        
        // Only pixels in front of the glass can use the reprojected history.
        [branch]
        if (maskDepth < sceneDepth)
        {
            float2 vReadPos = ReprojectDepthMask(vDispatchId, maskDepth);
            if (IsValidCoordinate(vReadPos))
            {
                Output[vDispatchId.xy] = clamp( Mip[vReadPos / 2], 0.0f, 4.0f );
                return;
            }
        }

        const int2 sourcePos = int2( vDispatchId.xy ) + int2( g_vViewportOffset );
#if ( D_COPY == 3 )
        // Keep the generic resolve's accumulation order even after unrolling.
        precise float4 sum = 0;
        [unroll]
        for ( int i = 0; i < 4; i++ )
            sum += Source.Load( sourcePos, i );
        Output[vDispatchId.xy] = sum / 4;
#elif ( D_COPY == 2 )
        const int samples = int( SourceSamples );
        float4 sum = 0;
        for ( int i = 0; i < samples; i++ )
            sum += Source.Load( sourcePos, i );
        Output[vDispatchId.xy] = sum / samples;
#else
        Output[vDispatchId.xy] = Source.Load( int3( sourcePos, 0 ) );
#endif

    }

#endif // CS


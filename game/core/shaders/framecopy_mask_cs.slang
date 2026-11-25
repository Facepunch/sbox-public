// framecopy_mask_cs: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// The frame buffer copy made by its depth-aware mask: depthaware_mask_cs (native's DepthAwareMask_CS.shader), which writes
// last frame's blurred copy, reprojected, where something is in front of the glass, with the frame copied (D_COPY 1) or
// resolved (D_COPY 2) everywhere else, over a region of the copy (the readers', in a 3D skybox).
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

    // D_COPY: the frame itself, copied into Output where the mask keeps it, so the copy and the mask are one pass; 2, a
    // multisampled frame, resolved there (its samples averaged, as a resolve does)
    DynamicCombo( D_COPY, 1..2, Sys( ALL ) );
#if ( D_COPY == 2 )
    Texture2DMS<float4> Source < Attribute("MaskSource"); SrgbRead( false ); > ;
    float SourceSamples < Attribute("MaskSamples"); > ;
#else
    Texture2D Source < Attribute("MaskSource"); SrgbRead( false ); > ;
#endif

    // The pixels this dispatch covers, in the viewport: x, y its first, z, w past its last (the readers' area)
    float4 Region < Attribute("MaskRegion"); > ;
    

    float GetDepthMask(int2 vDispatchId)
    {
        // We render depth mask at quarter res
        const int2 baseCoord = vDispatchId.xy / 4;

        float depth = DepthMask[baseCoord].x;

        return depth;
    }
    //-----------------------------------------------------------------------------------------
    // ReprojectDepthMask: Transforms a pixel coordinate from current frame to previous frame
    // using depth-based reprojection
    //
    // Same as Motion::Get but uses depth from refraction object
    //-----------------------------------------------------------------------------------------
    float2 ReprojectDepthMask(int2 vDispatchId)
    {
        // Get normalized depth value of where the refraction is
        float flDepth = Depth::Normalize( GetDepthMask( vDispatchId ) );

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
        
        // Reproject current pixel to find sampling position in previous frame
        float2 vReadPos = ReprojectDepthMask(vDispatchId);

        // If we have an intersecting object in front of our refraction texture, reproject it with blur
        if (maskDepth < sceneDepth && IsValidCoordinate(vReadPos))
        {
            // Sample the source texture at the reprojected position
            float4 sourceColor = Mip[vReadPos / 2];

            const int2 vWritePos = vDispatchId.xy;
            Output[vWritePos] = clamp( sourceColor, 0.0f, 4.0f );
        }
#if ( D_COPY == 2 )
        else
        {
            const int samples = int( SourceSamples );
            float4 sum = 0;
            for ( int i = 0; i < samples; i++ )
                sum += Source.Load( int2( vDispatchId.xy ), i );
            Output[vDispatchId.xy] = sum / samples;
        }
#else
        else
        {
            Output[vDispatchId.xy] = Source.Load( int3( vDispatchId.xy, 0 ) );
        }
#endif

    }

#endif // CS


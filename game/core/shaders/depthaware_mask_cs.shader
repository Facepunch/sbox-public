//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// DepthAwareMask_CS.shader
//
// This compute shader performs depth-aware reprojection and masking for refraction effects.
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
HEADER
{
    DevShader = true;
    Description = "Compute Shader for depth-aware masking with temporal reprojection";
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

}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
CS
{
    #include "common.fxc"
    #include "math_general.fxc"

    #include "common/classes/Depth.hlsl"
    #include "common/classes/Motion.hlsl"

    // Input textures
    Texture2D DepthMask < Attribute("RefractionDepthTexture"); > ; // Depth mask for comparison, applied from pipeline
    Texture2D Mip < Attribute("Mip"); > ; // Blurred texture to sample from

    // Output texture
    RWTexture2D<float4> Output < Attribute("Output"); > ;
    

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
    void MainCs(uint2 vDispatchId: SV_DispatchThreadID)
    {
        if( vDispatchId.x >= g_vViewportSize.x || vDispatchId.y >= g_vViewportSize.y )
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

    }
}

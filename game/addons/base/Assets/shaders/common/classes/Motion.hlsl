#ifndef MOTION_HLSL
#define MOTION_HLSL

#include "common_samplers.fxc"

// Raw dog motion vectors, use this to get where the offset pixel you're fetching was on last frame
// Right now this is limited to camera motion only, includes depth delta as well
struct Motion
{
    // Reprojects a world position to screen space using the last frame's depth buffer
    static float3 GetFromWorldPosition(float3 worldPosition)
    {
        return ReprojectFromLastFrameSs( worldPosition );   
    }

    // Returns the motion position in Screen Space, for UV just multiply by inverse viewport size
    static float3 Get(float2 screenPosition)
    {
        // Calculate world space position based on the previous projection
        const float3 worldPos = Depth::GetWorldPosition( screenPosition );

        // Reproject the world space position to screen space in the previous frame
        const float3 prevFramePosSs = Motion::GetFromWorldPosition( worldPos );

        return prevFramePosSs;
    }

    //
    // Do a temporal filter on the current and previous frame's texture and using the motion vectors to blend them
    // Your traditional TAA in a generic form
    //
    static float4 TemporalFilter( uint2 vPositionSs, Texture2D tCurrent, Texture2D tPrev, float flBlendWeight = 0.9f )
    {
        // Calculate the previous frame's UV coordinates
        float2 vPrevUV = ( Motion::Get(vPositionSs).xy + 0.5f ) * g_vInvViewportSize;

        // Initialize min and max sample values with arbitrary out-of-range values
        float4 vMin = 9999.0;
        float4 vMax = -9999.0;

        // Sample the current frame's sample values within a bounding box
        for (int i = -1; i <= 1; i++)
        {
            for (int j = -1; j <= 1; j++)
            {
                int2 offset = int2(i, j);
                float4 vSample = tCurrent[vPositionSs + offset];
                vMin = min(vMin, vSample);
                vMax = max(vMax, vSample);
            }
        }

        // Get the previous frame's sample value
        float4 vPrevSample = tPrev.SampleLevel( g_sBilinearClamp, vPrevUV, 0 );

        // Clamp the previous frame's sample value within the computed min and max values
        float4 vPrevSampleClamped = clamp(vPrevSample, vMin, vMax);

        // Get the current frame's sample value
        float4 vCurrentSample = tCurrent[vPositionSs];

        // Blend the clamped sample value with the current frame's sample value
        return lerp(vCurrentSample, vPrevSampleClamped, flBlendWeight);
    }
};
#endif
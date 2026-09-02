#ifndef PIXEL_RAYTRACE_SSR_H
#define PIXEL_RAYTRACE_SSR_H

#include "common/thirdparty/HierarchicalRaymarch.hlsl"

struct TraceResult
{
    float3 HitClipSpace; // Hit position in clip space
    float Confidence;    // Confidence of the hit
    bool ValidHit;       // Was the hit valid?
};

struct ScreenSpace
{
    static TraceResult Trace(const float3 Position, const float3 Direction, uint nMaxSteps = 64 )
    {
        //----------------------------------------------
        float flDistanceToCamera = length(Position - g_vCameraPositionWs);
        float flDepthThickness = sqrt(flDistanceToCamera); // Thickness of the depth buffer, made adaptive, seems to be okay

        bool bValidHit = false;
        bool bBackTracing = true; // Trace behind objects, costlier

        //---------------------------------------------
        // Build our position in clip space and reflection vector from world space ray
        // ---------------------------------------------

        float3 vPositionCs = ProjectPosition(Position, g_matWorldToProjection);
        float3 vReflectCs = ProjectDirection(Position, Direction, vPositionCs.xyz, g_matWorldToProjection);

        //----------------------------------------------
        // Trace the thing ;)
        // ---------------------------------------------

        float3 hit = HierarchicalRaymarch::Trace(
            vPositionCs.xyz,  // origin
            vReflectCs.xyz,   // direction
            g_vViewportSize,  // screen_size
            nMaxSteps,        // max_traversal_intersections
            flDepthThickness, // flThickness
            bBackTracing,     // backTracing
            bValidHit         // valid_hit (output)
        );
        float confidence = bValidHit ? HierarchicalRaymarch::ValidateHit(hit, vPositionCs.xy, Direction, g_vViewportSize, flDepthThickness) : 0;

        //----------------------------------------------
        // Composite result
        // ---------------------------------------------
        TraceResult result;
        result.HitClipSpace = hit;
        result.Confidence = confidence;
        result.ValidHit = bValidHit;

        return result;
    }
};


#endif // PIXEL_RAYTRACE_SSR_H
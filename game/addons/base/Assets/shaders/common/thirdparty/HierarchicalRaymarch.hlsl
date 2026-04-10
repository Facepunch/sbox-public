#ifndef HIERARCHICAL_RAYMARCH_H
#define HIERARCHICAL_RAYMARCH_H
/**********************************************************************
Copyright (c) 2021 Advanced Micro Devices, Inc. All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.  IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
********************************************************************/

/** Project a world‑space point to *normalised* UV‑space (0‑1, origin top‑left). */
float3 ProjectPosition(float3 origin, float4x4 mat)
{
    float4 projected = Position4WsToPs(float4(origin, 1));
    projected.xyz   /= projected.w;
    projected.xy     = projected.xy * 0.5 + 0.5;   // NDC → UV
    projected.y      = 1 - projected.y;            // Flip Y (DX convention)
    
    projected.xy -= g_vInvViewportSize.xy * 0.5f; // Offset to center of pixel

    return projected.xyz;
}

/** Project a **direction** – returns how that vector maps in UV‑space. */
float3 ProjectDirection(float3 origin, float3 dir, float3 ssOrigin, float4x4 mat)
{
    return ProjectPosition(origin + dir, mat) - ssOrigin;
}

/** Inverse of `ProjectPosition` (UV → world), using a *texture‑to‑world* matrix. */
float3 InvProjectPosition(float3 coord, float4x4 mat)
{
    coord.y  = 1 - coord.y;      // UV → NDC
    coord.xy = coord.xy * 2 - 1;
    float4 p = mul(mat, float4(coord, 1));
    return p.xyz / p.w;
}

class HierarchicalRaymarch
{
    /** Read the Hi‑Z depth chain and remap from clip‑space (reverse‑Z) to [0‑1] linear. */
    static float LoadDepth(int2 pixel, int mip)
    {
        return Depth::Normalize( g_tDepthChain.Load(int3(pixel, mip)).y );
    }

    /** Return (width, height) of the requested mip in *pixels*. */
    static float2 GetMipResolution(float2 screenDim, int mip) { return screenDim * exp2(-mip); }

    static void InitialAdvanceRay(
        float3 origin, float3 dir, float3 invDir,
        float2 mipRes, float2 mipResInv,
        float2 floorOffset, float2 uvOffset,
        out float3 pos, out float t)
    {
        float2 mipPos = mipRes * origin.xy;
        float2 xy     = floor(mipPos) + floorOffset;
        xy            = xy * mipResInv + uvOffset;

        float2 tt = xy * invDir.xy - origin.xy * invDir.xy;
        t         = min(tt.x, tt.y);
        pos       = origin + t * dir;
    }

    static bool AdvanceRay(
        float3 origin, float3 dir, float3 invDir,
        float2 mipPos, float2 mipResInv,
        float2 floorOffset, float2 uvOffset,
        float  surfaceZ,
        inout float3 pos,
        inout float   t)
    {
        float2 xyPlane = floor(mipPos) + floorOffset;
        xyPlane        = xyPlane * mipResInv + uvOffset;
        float3 planes  = float3(xyPlane, surfaceZ);

        float3 tt  = planes * invDir - origin * invDir;
        tt.z       = dir.z < 0 ? tt.z : 3.402823466e+38;   // disable Z when ray exits view

        float tMin = min(min(tt.x, tt.y), tt.z);
        bool above = surfaceZ < pos.z;
        bool skipped = asuint(tMin) != asuint(tt.z) && above;

        t   = above ? tMin : t;
        pos = origin + t * dir;
        return skipped;
    }

    static bool BacktraceRay(
        float3 origin, float3 dir, float t,
        float  surfaceZ, float thickness,
        int    finestMip, float2 screenSize,
        inout float3 pos, inout int mip,
        inout float2 mipRes, inout float2 mipResInv,
        inout uint   k)
    {
        float surfaceVs = ConvertDepthPsToVs(1.0 - pos.z);
        float hitVs     = ConvertDepthPsToVs(1.0 - surfaceZ);
        float dist      = hitVs - surfaceVs;         // >0 means below surface

        if (mip == finestMip && dist > thickness)
        {
            mip            = finestMip + 1;
            mipRes         = GetMipResolution(screenSize, mip);
            mipResInv      = rcp(mipRes);

            uint step      = 1u << k++;              // 2^k exponential increment
            t             += step;
            pos            = origin + t * dir;
            return true;
        }
        return false;
    }

    static float3 Trace(
        float3 origin, float3 dir,
        float2 screenSize,
        uint   maxIntersections,
        float  thickness,
        bool   enableBacktrace,
        out bool validHit)
    {
        const bool useMipChain = true; // use mip chain for acceleration
        const int finestMip = 1; // start at mip 1 (2x2 pixels)

        float3 invDir = rcp(dir);


        // Start at the requested mip
        int   mip       = finestMip;
        float2 mipRes   = GetMipResolution(screenSize, mip);
        float2 mipResInv= rcp(mipRes);

        // Offset so the ray starts in the *centre* of its texel box
        float2 uvOffset = g_vInvViewportSize * exp2(finestMip) * g_vInvViewportSize;
        uvOffset        = dir.xy < 0 ? -uvOffset : uvOffset;
        float2 floorOff = dir.xy < 0 ? 0 : 1;

        float  t;
        float3 pos;
        InitialAdvanceRay(origin, dir, invDir, mipRes, mipResInv, floorOff, uvOffset, pos, t);

        uint i      = 0;
        uint k      = 5u; // = 32‑pixel step for first back‑trace

        while (i < maxIntersections && mip >= finestMip )
        {
            if (any(pos.xyz <= 0) || any(pos.xyz >= 1)) break; // outside screen

            float2 mipPos = mipRes * pos.xy;
            if (mip < 2) mipPos = dir.xy < 0 ? floor(mipPos) : ceil(mipPos);

            float z = LoadDepth(mipPos, mip );

            bool skipped = AdvanceRay(origin, dir, invDir, mipPos, mipResInv,
                                      floorOff, uvOffset, z, pos, t);

            if (useMipChain)
            {
                mip       += skipped ? 1 : -1;
                mipRes    *= skipped ? 0.5 : 2.0;
                mipResInv *= skipped ? 2.0 : 0.5;
            }

            if (enableBacktrace && BacktraceRay(origin, dir, t, z, thickness,
                                                finestMip, screenSize, pos,
                                                mip, mipRes, mipResInv, k))
            {
                ++i; continue; // restart loop at coarser level
            }
            ++i;
        }
        
        validHit = (i < maxIntersections) && all(pos.xy >= 0) && all(pos.xy <= 1);
        return pos;
    }

    /**
     * Secondary validation pass – removes obvious false positives and returns
     * a soft confidence factor in [0‑1] for blending with fallback lighting.
     */
    static float ValidateHit(
        float3 hit, float2 uv,
        float3 wsDir, float2 screenSize,
        float thickness )
    {
        // Reject outside frustum
        if (any(hit.xyz <= 0) || any(hit.xyz >= 1)) return 0;

        
        // Skip background (depth == 0 after remap)
        uint2 texel = uint2(screenSize * hit.xy);
        float z     = LoadDepth(texel / 2, 1 );
        if (z == 0.0) return 0;


        // Surface thickness check in view‑space
        float surfaceVs = ConvertDepthPsToVs(1.0 - z);
        float hitVs     = ConvertDepthPsToVs(1.0 - hit.z);
        float dist      = surfaceVs - hitVs;

        // Vignette near the screen border for smoother cutoff
        float2 fov   = 0.05 * float2(screenSize.y / screenSize.x, 1);
        float2 border= smoothstep(0, fov, hit.xy) * (1 - smoothstep(1 - fov, 1, hit.xy));
        float vignette = border.x * border.y;

        float conf = 1 - smoothstep(0, thickness, dist);
        return vignette * conf * conf; // square = slightly sharper falloff
    }
};

#endif // HIERARCHICAL_RAYMARCH_H

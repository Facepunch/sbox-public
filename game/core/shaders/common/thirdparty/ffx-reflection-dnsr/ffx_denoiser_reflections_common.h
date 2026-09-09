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

#ifndef FFX_DNSR_REFLECTIONS_COMMON
#define FFX_DNSR_REFLECTIONS_COMMON

#include "common/thirdparty/ffx-reflection-dnsr/ffx_denoiser_reflections_config.h"

uint FFX_DNSR_Reflections_BitfieldExtract(uint src, uint off, uint bits) {
    uint mask = (1u << bits) - 1;
    return (src >> off) & mask;
}

uint FFX_DNSR_Reflections_BitfieldInsert(uint src, uint ins, uint bits) {
    uint mask = (1u << bits) - 1;
    return (ins & mask) | (src & (~mask));
}

//  LANE TO 8x8 MAPPING
//  ===================
//  00 01 08 09 10 11 18 19
//  02 03 0a 0b 12 13 1a 1b
//  04 05 0c 0d 14 15 1c 1d
//  06 07 0e 0f 16 17 1e 1f
//  20 21 28 29 30 31 38 39
//  22 23 2a 2b 32 33 3a 3b
//  24 25 2c 2d 34 35 3c 3d
//  26 27 2e 2f 36 37 3e 3f
uint2 FFX_DNSR_Reflections_RemapLane8x8(uint lane) {
    return uint2(FFX_DNSR_Reflections_BitfieldInsert(FFX_DNSR_Reflections_BitfieldExtract(lane, 2u, 3u), lane, 1u),
                 FFX_DNSR_Reflections_BitfieldInsert(FFX_DNSR_Reflections_BitfieldExtract(lane, 3u, 3u), FFX_DNSR_Reflections_BitfieldExtract(lane, 1u, 2u), 2u));
}

float FFX_DNSR_Reflections_Luminance(float3 color) { return max(dot(color, float3(0.299, 0.587, 0.114)), 0.001); }

float FFX_DNSR_Reflections_ComputeTemporalVariance(float3 history_radiance, float3 radiance) {
    float history_luminance = FFX_DNSR_Reflections_Luminance(history_radiance);
    float luminance         = FFX_DNSR_Reflections_Luminance(radiance);
    float diff              = abs(history_luminance - luminance) / max(max(history_luminance, luminance), 0.5);
    return diff * diff;
}

uint FFX_DNSR_Reflections_PackFloat16(float2 v) {
    v = clamp(v, -65504.0, 65504.0); // Clamp to avoid overflow
    uint2 p = f32tof16(float2(v));
    return p.x | (p.y << 16);
}

float2 FFX_DNSR_Reflections_UnpackFloat16(uint a) {
    float2 tmp = f16tof32(uint2(a & 0xFFFF, a >> 16));
    return float2(tmp);
}

uint2 FFX_DNSR_Reflections_PackFloat16_4(float4 v) { return uint2(FFX_DNSR_Reflections_PackFloat16(v.xy), FFX_DNSR_Reflections_PackFloat16(v.zw)); }

float4 FFX_DNSR_Reflections_UnpackFloat16_4(uint2 a) { return float4(FFX_DNSR_Reflections_UnpackFloat16(a.x), FFX_DNSR_Reflections_UnpackFloat16(a.y)); }

// Rounds value to the nearest multiple of 8
uint2 FFX_DNSR_Reflections_RoundUp8(uint2 value) {
    uint2 round_down = value & ~0xb111;
    return (round_down == value) ? value : value + 8;
}

// From "Temporal Reprojection Anti-Aliasing"
// https://github.com/playdeadgames/temporal
/**********************************************************************
Copyright (c) [2015] [Playdead]

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
********************************************************************/
floatx FFX_DNSR_Reflections_ClipAABB(floatx aabb_min, floatx aabb_max, floatx prev_sample) {
    // Main idea behind clipping - it prevents clustering when neighbor color space
    // is distant from history sample

    // Here we find intersection between color vector and aabb color box

    // Note: only clips towards aabb center
    floatx aabb_center = 0.5 * (aabb_max + aabb_min);
    floatx extent_clip = 0.5 * (aabb_max - aabb_min) + 0.001;

    // Find color vector
    floatx color_vector = prev_sample - aabb_center;
    // Transform into clip space
    floatx color_vector_clip = color_vector / extent_clip;
    // Find max absolute component
    color_vector_clip       = abs(color_vector_clip);
    float max_abs_unit = max(max(color_vector_clip.x, color_vector_clip.y), color_vector_clip.z);

    if (max_abs_unit > 1.0) {
        return aabb_center + color_vector / max_abs_unit; // clip towards color vector
    } else {
        return prev_sample; // point is inside aabb
    }
}

float3 FFX_DNSR_Reflections_ClipAABB(float3 aabb_min, float3 aabb_max, float3 prev_sample) {
    // Main idea behind clipping - it prevents clustering when neighbor color space
    // is distant from history sample

    // Here we find intersection between color vector and aabb color box

    // Note: only clips towards aabb center
    float3 aabb_center = 0.5 * (aabb_max + aabb_min);
    float3 extent_clip = 0.5 * (aabb_max - aabb_min) + 0.001;

    // Find color vector
    float3 color_vector = prev_sample - aabb_center;
    // Transform into clip space
    float3 color_vector_clip = color_vector / extent_clip;
    // Find max absolute component
    color_vector_clip       = abs(color_vector_clip);
    float max_abs_unit = max(max(color_vector_clip.x, color_vector_clip.y), color_vector_clip.z);

    if (max_abs_unit > 1.0) {
        return aabb_center + color_vector / max_abs_unit; // clip towards color vector
    } else {
        return prev_sample; // point is inside aabb
    }
}

#ifdef FFX_DNSR_REFLECTIONS_ESTIMATES_LOCAL_NEIGHBORHOOD

#    ifndef FFX_DNSR_REFLECTIONS_LOCAL_NEIGHBORHOOD_RADIUS
#        define FFX_DNSR_REFLECTIONS_LOCAL_NEIGHBORHOOD_RADIUS 4
#    endif

float FFX_DNSR_Reflections_LocalNeighborhoodKernelWeight(float i) {
    const float radius = FFX_DNSR_REFLECTIONS_LOCAL_NEIGHBORHOOD_RADIUS + 1.0;
    return exp(-FFX_DNSR_REFLECTIONS_GAUSSIAN_K * (i * i) / (radius * radius));
}

#endif // FFX_DNSR_REFLECTIONS_ESTIMATES_LOCAL_NEIGHBORHOOD

#endif // FFX_DNSR_REFLECTIONS_COMMON
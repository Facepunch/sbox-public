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

#ifndef FFX_DNSR_REFLECTIONS_RESOLVE_TEMPORAL
#define FFX_DNSR_REFLECTIONS_RESOLVE_TEMPORAL

#define FFX_DNSR_REFLECTIONS_ESTIMATES_LOCAL_NEIGHBORHOOD
#include "common/thirdparty/ffx-reflection-dnsr/ffx_denoiser_reflections_common.h"

groupshared float4 g_ffx_dnsr_shared_0[16][16];

struct FFX_DNSR_Reflections_NeighborhoodSample {
    floatx radiance;
};

FFX_DNSR_Reflections_NeighborhoodSample FFX_DNSR_Reflections_LoadFromGroupSharedMemory(int2 idx) {
    floatx unpacked_radiance = g_ffx_dnsr_shared_0[idx.y][idx.x];

    FFX_DNSR_Reflections_NeighborhoodSample sample;
    sample.radiance = unpacked_radiance;
    return sample;
}

struct FFX_DNSR_Reflections_Moments {
    floatx mean;
    floatx variance;
};

FFX_DNSR_Reflections_Moments FFX_DNSR_Reflections_EstimateLocalNeighborhoodInGroup(int2 group_thread_id) {
    FFX_DNSR_Reflections_Moments estimate;
    estimate.mean                 = 0;
    estimate.variance             = 0;
    float accumulated_weight = 0;
    for (int j = -FFX_DNSR_REFLECTIONS_LOCAL_NEIGHBORHOOD_RADIUS; j <= FFX_DNSR_REFLECTIONS_LOCAL_NEIGHBORHOOD_RADIUS; ++j) {
        for (int i = -FFX_DNSR_REFLECTIONS_LOCAL_NEIGHBORHOOD_RADIUS; i <= FFX_DNSR_REFLECTIONS_LOCAL_NEIGHBORHOOD_RADIUS; ++i) {
            int2        new_idx  = group_thread_id + int2(i, j);
            floatx radiance = FFX_DNSR_Reflections_LoadFromGroupSharedMemory(new_idx).radiance;
            float  weight   = FFX_DNSR_Reflections_LocalNeighborhoodKernelWeight(i) * FFX_DNSR_Reflections_LocalNeighborhoodKernelWeight(j);
            accumulated_weight  += weight;
            estimate.mean       += radiance * weight;
            estimate.variance   += radiance * radiance * weight;
        }
    }
    accumulated_weight = max(accumulated_weight, 1.0e-6); // Prevent division by zero
    estimate.mean     /= accumulated_weight;
    estimate.variance /= accumulated_weight;

    estimate.variance = abs(estimate.variance - estimate.mean * estimate.mean);
    return estimate;
}

void FFX_DNSR_Reflections_StoreInGroupSharedMemory(int2 group_thread_id, floatx radiance) {
    g_ffx_dnsr_shared_0[group_thread_id.y][group_thread_id.x] = radiance;
}

void FFX_DNSR_Reflections_LoadNeighborhood(int2 pixel_coordinate, out floatx radiance) { radiance = FFX_DNSR_Reflections_LoadRadiance(pixel_coordinate); }

void FFX_DNSR_Reflections_InitializeGroupSharedMemory(int2 dispatch_thread_id, int2 group_thread_id, int2 screen_size) {
    // Load 16x16 region into shared memory using 4 8x8 blocks.
    int2 offset[4] = {int2(0, 0), int2(8, 0), int2(0, 8), int2(8, 8)};

    // Intermediate storage registers to cache the result of all loads
    floatx radiance[4];

    // Start in the upper left corner of the 16x16 region.
    dispatch_thread_id -= DISPATCH_OFFSET;

    // First store all loads in registers
    for (int i = 0; i < 4; ++i) {
        FFX_DNSR_Reflections_LoadNeighborhood(dispatch_thread_id + offset[i], radiance[i]);
    }

    // Then move all registers to groupshared memory
    for (int j = 0; j < 4; ++j) {
        FFX_DNSR_Reflections_StoreInGroupSharedMemory(group_thread_id + offset[j], radiance[j]);
    }
}

void FFX_DNSR_Reflections_ResolveTemporal(int2 dispatch_thread_id, int2 group_thread_id, uint2 screen_size, float2 inv_screen_size, float history_clip_weight) {
    FFX_DNSR_Reflections_InitializeGroupSharedMemory(dispatch_thread_id, group_thread_id, screen_size);
    GroupMemoryBarrierWithGroupSync();

    group_thread_id += 4; // Center threads in groupshared memory

    FFX_DNSR_Reflections_NeighborhoodSample center       = FFX_DNSR_Reflections_LoadFromGroupSharedMemory(group_thread_id);
    floatx                             new_signal   = center.radiance;
    float                              roughness    = FFX_DNSR_Reflections_LoadRoughness(dispatch_thread_id);
    float                              new_variance = FFX_DNSR_Reflections_LoadVariance(dispatch_thread_id);
    float flMask                    = center.radiance.a;
    if (FFX_DNSR_Reflections_IsGlossyReflection(roughness)) {
        float  num_samples  = FFX_DNSR_Reflections_LoadNumSamples(dispatch_thread_id);
        float2      uv8          = (float2(dispatch_thread_id.xy) + (0.5).xx) / FFX_DNSR_Reflections_RoundUp8(screen_size);
        floatx avg_radiance = FFX_DNSR_Reflections_SampleAverageRadiance(uv8);

        floatx                  old_signal         = FFX_DNSR_Reflections_LoadRadianceReprojected(dispatch_thread_id);
        FFX_DNSR_Reflections_Moments local_neighborhood = FFX_DNSR_Reflections_EstimateLocalNeighborhoodInGroup(group_thread_id);
        // Clip history based on the curren local statistics
        floatx                  color_std          = (sqrt(local_neighborhood.variance) + length(local_neighborhood.mean.xyz - avg_radiance.xyz)) * history_clip_weight * 1.4;
        local_neighborhood.mean.xyz = lerp(local_neighborhood.mean.xyz, avg_radiance.xyz, 0.2);

        floatx                  radiance_min       = local_neighborhood.mean - color_std;
        floatx                  radiance_max       = local_neighborhood.mean + color_std;
        floatx                  clipped_old_signal = FFX_DNSR_Reflections_ClipAABB(radiance_min, radiance_max, old_signal);
        float                   accumulation_speed = 1.0 / max(num_samples, 1.0);
        float                   weight             = saturate(1.0 - accumulation_speed);
        // Blend with average for small sample count
        new_signal                                  = lerp(new_signal, avg_radiance, 1.0 / max(num_samples + 1.0f, 1.0));
        // Clip outliers
        {
            floatx                  radiance_min       = avg_radiance - color_std * 1.0;
            floatx                  radiance_max       = avg_radiance + color_std * 1.0;
            new_signal                                  = FFX_DNSR_Reflections_ClipAABB(radiance_min, radiance_max, new_signal);
        }
        
        // Blend with history
        new_signal                                      = lerp(new_signal, clipped_old_signal, weight);
        new_variance                                    = lerp(FFX_DNSR_Reflections_ComputeTemporalVariance(new_signal.xyz, clipped_old_signal.xyz), new_variance, weight);
        
        
        // Ensure no negative values
        new_signal = clamp(new_signal, 0.0, 65504.0); // Clamp to valid range for float16
        new_variance = clamp(new_variance, 0.0, 65504.0); // Clamp to valid range for float16


    }
    FFX_DNSR_Reflections_StoreTemporalAccumulation(dispatch_thread_id, float4(new_signal ), new_variance);
}

#endif // FFX_DNSR_REFLECTIONS_RESOLVE_TEMPORAL
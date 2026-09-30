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

#ifndef FFX_DNSR_REFLECTIONS_REPROJECT
#define FFX_DNSR_REFLECTIONS_REPROJECT

#define FFX_DNSR_REFLECTIONS_ESTIMATES_LOCAL_NEIGHBORHOOD
#include "common/thirdparty/ffx-reflection-dnsr/ffx_denoiser_reflections_common.h"

groupshared float4  g_ffx_dnsr_shared_0[16][16];
groupshared float g_ffx_dnsr_shared_1[16][16];

struct FFX_DNSR_Reflections_NeighborhoodSample {
    floatx radiance;
};

FFX_DNSR_Reflections_NeighborhoodSample FFX_DNSR_Reflections_LoadFromGroupSharedMemory(int2 idx) {
    float4 unpacked_radiance        = g_ffx_dnsr_shared_0[idx.y][idx.x];

    FFX_DNSR_Reflections_NeighborhoodSample sample;
    sample.radiance = unpacked_radiance;
    return sample;
}


void FFX_DNSR_Reflections_StoreInGroupSharedMemory(int2 group_thread_id, float4 radiance_variance, float flWeight = 0) {
    g_ffx_dnsr_shared_0[group_thread_id.y][group_thread_id.x]     = radiance_variance.xyzw;
    g_ffx_dnsr_shared_1[group_thread_id.y][group_thread_id.x]     = flWeight;

}

void FFX_DNSR_Reflections_InitializeGroupSharedMemory(int2 dispatch_thread_id, int2 group_thread_id, int2 screen_size) {
    // Load 16x16 region into shared memory using 4 8x8 blocks.
    int2 offset[4] = {int2(0, 0), int2(8, 0), int2(0, 8), int2(8, 8)};

    // Intermediate storage registers to cache the result of all loads
    floatx radiance[4];

    // Start in the upper left corner of the 16x16 region.
    dispatch_thread_id -= DISPATCH_OFFSET;

    // First store all loads in registers
    for (int i = 0; i < 4; ++i) {
        radiance[i] = FFX_DNSR_Reflections_LoadRadiance(dispatch_thread_id + offset[i]);
    }

    // Then move all registers to groupshared memory
    for (int j = 0; j < 4; ++j) {
        FFX_DNSR_Reflections_StoreInGroupSharedMemory(group_thread_id + offset[j], radiance[j]); // X
    }
}

float4 FFX_DNSR_Reflections_LoadFromGroupSharedMemoryRaw(int2 idx) {
    return g_ffx_dnsr_shared_0[idx.y][idx.x];
}

float FFX_DNSR_Reflections_GetLuminanceWeight(float3 val) {
    float luma   = FFX_DNSR_Reflections_Luminance(val.xyz);
    float weight = max(exp(-luma * FFX_DNSR_REFLECTIONS_AVG_RADIANCE_LUMINANCE_WEIGHT), 1.0e-2);
    return weight;
}

float2 FFX_DNSR_Reflections_GetHitPositionReprojection(int2 dispatch_thread_id, float2 uv, float reflected_ray_length) 
{
    float  z              = FFX_DNSR_Reflections_LoadDepth(dispatch_thread_id);
    float3 view_space_ray = InvProjectPosition(float3(uv, z), g_matProjectionToWorld);

    // We start out with reconstructing the ray length in view space.
    // This includes the portion from the camera to the reflecting surface as well as the portion from the surface to the hit position.
    float surface_depth = length(view_space_ray);
    float ray_length    = surface_depth + reflected_ray_length;

    view_space_ray = normalize(view_space_ray);

    float3 vHitPositionWs = g_vCameraPositionWs.xyz + view_space_ray * ( ray_length );
    float2 vHitPositionSs = ProjectPosition( vHitPositionWs, g_matWorldToProjection ).xy * Dimensions;

    return Motion::Get( vHitPositionSs + 0.5f ).xy * InvDimensions;
}

float FFX_DNSR_Reflections_GetDisocclusionFactor(float3 normal, float3 history_normal, float linear_depth, float history_linear_depth) {
    // Improved disocclusion detection with adaptive thresholds based on depth
    float normalWeight = FFX_DNSR_REFLECTIONS_DISOCCLUSION_NORMAL_WEIGHT;
    float depthWeight = FFX_DNSR_REFLECTIONS_DISOCCLUSION_DEPTH_WEIGHT;
    
    // Scale weights based on depth - objects further away can tolerate more change
    depthWeight *= saturate(1.0 - linear_depth * 0.01);
    
    float factor = 1.0                                                           
                  * exp(-abs(1.0 - max(0.0, dot(normal, history_normal))) * normalWeight)
                  * exp(-abs(history_linear_depth - linear_depth) / linear_depth * depthWeight);
    return factor;
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
    estimate.mean     /= accumulated_weight;
    estimate.variance /= accumulated_weight;

    estimate.variance = abs(estimate.variance - estimate.mean * estimate.mean);
    return estimate;
}

floatx GetContactHardenedRadiance(int2 dispatch_thread_id)
{
    floatx radiance = FFX_DNSR_Reflections_LoadRadiance(dispatch_thread_id);
    float flRayLength = FFX_DNSR_Reflections_LoadRayLength(dispatch_thread_id);
    
    return radiance;
}

floatx GetContactHardenedRadianceHistory(float2 vUV)
{
    floatx radiance = FFX_DNSR_Reflections_SampleRadianceHistory(vUV);

    return radiance;
}

void FFX_DNSR_Reflections_PickReprojection(int2            dispatch_thread_id,  //
                                           int2            group_thread_id,     //
                                           uint2           screen_size,         //
                                           float      roughness,           //
                                           float      ray_length,          //
                                           out float  disocclusion_factor, //
                                           out float2      reprojection_uv,     //
                                           out floatx reprojection) {

    FFX_DNSR_Reflections_Moments local_neighborhood = FFX_DNSR_Reflections_EstimateLocalNeighborhoodInGroup(group_thread_id);

    float2      uv     = float2(dispatch_thread_id.x + 0.5, dispatch_thread_id.y + 0.5) / screen_size;
    float3 normal = FFX_DNSR_Reflections_LoadWorldSpaceNormal(dispatch_thread_id);
    float3 history_normal;
    float       history_linear_depth;

    {
        const float2      surface_reprojection_uv   = FFX_DNSR_Reflections_LoadMotionVector(dispatch_thread_id);
        const float2      hit_reprojection_uv       = FFX_DNSR_Reflections_GetHitPositionReprojection(dispatch_thread_id, uv, ray_length);
        const float3 surface_normal            = FFX_DNSR_Reflections_SampleWorldSpaceNormalHistory(surface_reprojection_uv);
        const float3 hit_normal                = FFX_DNSR_Reflections_SampleWorldSpaceNormalHistory(hit_reprojection_uv);
        const floatx surface_history           = GetContactHardenedRadianceHistory(surface_reprojection_uv);
        const floatx hit_history               = GetContactHardenedRadianceHistory(hit_reprojection_uv);
        const float       hit_normal_similarity     = dot(normalize((float3)hit_normal), normalize((float3)normal));
        const float       surface_normal_similarity = dot(normalize((float3)surface_normal), normalize((float3)normal));
        const float  hit_roughness             = FFX_DNSR_Reflections_SampleRoughnessHistory(hit_reprojection_uv);
        const float  surface_roughness         = FFX_DNSR_Reflections_SampleRoughnessHistory(surface_reprojection_uv);

        // Choose reprojection uv based on similarity to the local neighborhood.
        if (hit_normal_similarity > FFX_DNSR_REFLECTIONS_REPROJECTION_NORMAL_SIMILARITY_THRESHOLD  // Candidate for mirror reflection parallax
            && hit_normal_similarity + 1.0e-3 > surface_normal_similarity                          //
            //&& abs(hit_roughness - roughness) < abs(surface_roughness - roughness) + 1.0e-3        //
        ) {
            history_normal                 = hit_normal;
            float hit_history_depth        = FFX_DNSR_Reflections_SampleDepthHistory(hit_reprojection_uv);
            float hit_history_linear_depth = FFX_DNSR_Reflections_GetLinearDepth(hit_reprojection_uv, hit_history_depth);
            history_linear_depth           = hit_history_linear_depth;
            reprojection_uv                = hit_reprojection_uv;
            reprojection                   = hit_history;
        } else {
            // Reject surface reprojection based on simple distance
             {
                history_normal                     = surface_normal;
                float surface_history_depth        = FFX_DNSR_Reflections_SampleDepthHistory(surface_reprojection_uv);
                float surface_history_linear_depth = FFX_DNSR_Reflections_GetLinearDepth(surface_reprojection_uv, surface_history_depth);
                history_linear_depth               = surface_history_linear_depth;
                reprojection_uv                    = surface_reprojection_uv;
                reprojection                       = surface_history;
            }
        }
    }
    float depth        = FFX_DNSR_Reflections_LoadDepth(dispatch_thread_id);
    float linear_depth = FFX_DNSR_Reflections_GetLinearDepth(uv, depth);
    // Determine disocclusion factor based on history
    disocclusion_factor = FFX_DNSR_Reflections_GetDisocclusionFactor(normal, history_normal, linear_depth, history_linear_depth);

    if (disocclusion_factor > FFX_DNSR_REFLECTIONS_DISOCCLUSION_THRESHOLD) // Early out, good enough
        return;

    // Try to find the closest sample in the vicinity if we are not convinced of a disocclusion
    if (disocclusion_factor < FFX_DNSR_REFLECTIONS_DISOCCLUSION_THRESHOLD) {
        float2    closest_uv    = reprojection_uv;
        float2    dudv          = 1.0 / float2(screen_size);
        
        // Expand search radius for higher roughness surfaces
        const int base_search_radius = 1;
        const int rough_search_radius = roughness > 0.5 ? 2 : base_search_radius;
        
        // Spiral search pattern for better coverage
        const int2 spiral_offsets[8] = {
            int2(1, 0), int2(1, 1), int2(0, 1), int2(-1, 1),
            int2(-1, 0), int2(-1, -1), int2(0, -1), int2(1, -1)
        };
        
        for (int r = 1; r <= rough_search_radius; r++) {
            for (int s = 0; s < 8; s++) {
                float2 uv = reprojection_uv + float2(spiral_offsets[s] * r) * dudv;
                float3 history_normal       = FFX_DNSR_Reflections_SampleWorldSpaceNormalHistory(uv);
                float       history_depth        = FFX_DNSR_Reflections_SampleDepthHistory(uv);
                float       history_linear_depth = FFX_DNSR_Reflections_GetLinearDepth(uv, history_depth);
                float  weight               = FFX_DNSR_Reflections_GetDisocclusionFactor(normal, history_normal, linear_depth, history_linear_depth);
                if (weight > disocclusion_factor) {
                    disocclusion_factor = weight;
                    closest_uv          = uv;
                    reprojection_uv     = closest_uv;
                }
            }
        }
        reprojection = GetContactHardenedRadianceHistory(reprojection_uv);
    }

    // Rare slow path - triggered only on the edges.
    // Try to get rid of potential leaks at bilinear interpolation level.
    if (disocclusion_factor < FFX_DNSR_REFLECTIONS_DISOCCLUSION_THRESHOLD) {
        // If we've got a discarded history, try to construct a better sample out of 2x2 interpolation neighborhood
        // Helps quite a bit on the edges in movement
        float       uvx                    = frac(float(screen_size.x) * reprojection_uv.x + 0.5);
        float       uvy                    = frac(float(screen_size.y) * reprojection_uv.y + 0.5);
        int2        reproject_texel_coords = int2(screen_size * reprojection_uv - 0.5);
        floatx reprojection00         = GetContactHardenedRadiance(reproject_texel_coords + int2(0, 0));
        floatx reprojection10         = GetContactHardenedRadiance(reproject_texel_coords + int2(1, 0));
        floatx reprojection01         = GetContactHardenedRadiance(reproject_texel_coords + int2(0, 1));
        floatx reprojection11         = GetContactHardenedRadiance(reproject_texel_coords + int2(1, 1));
        float3 normal00               = FFX_DNSR_Reflections_LoadWorldSpaceNormalHistory(reproject_texel_coords + int2(0, 0));
        float3 normal10               = FFX_DNSR_Reflections_LoadWorldSpaceNormalHistory(reproject_texel_coords + int2(1, 0));
        float3 normal01               = FFX_DNSR_Reflections_LoadWorldSpaceNormalHistory(reproject_texel_coords + int2(0, 1));
        float3 normal11               = FFX_DNSR_Reflections_LoadWorldSpaceNormalHistory(reproject_texel_coords + int2(1, 1));
        float       depth00                = FFX_DNSR_Reflections_GetLinearDepth(reprojection_uv, FFX_DNSR_Reflections_LoadDepthHistory(reproject_texel_coords + int2(0, 0)));
        float       depth10                = FFX_DNSR_Reflections_GetLinearDepth(reprojection_uv, FFX_DNSR_Reflections_LoadDepthHistory(reproject_texel_coords + int2(1, 0)));
        float       depth01                = FFX_DNSR_Reflections_GetLinearDepth(reprojection_uv, FFX_DNSR_Reflections_LoadDepthHistory(reproject_texel_coords + int2(0, 1)));
        float       depth11                = FFX_DNSR_Reflections_GetLinearDepth(reprojection_uv, FFX_DNSR_Reflections_LoadDepthHistory(reproject_texel_coords + int2(1, 1)));
        float4 w                      = 1.0;
        // Initialize with occlusion weights
        w.x = FFX_DNSR_Reflections_GetDisocclusionFactor(normal, normal00, linear_depth, depth00) > FFX_DNSR_REFLECTIONS_DISOCCLUSION_THRESHOLD / 2.0 ? 1.0 : 0.0;
        w.y = FFX_DNSR_Reflections_GetDisocclusionFactor(normal, normal10, linear_depth, depth10) > FFX_DNSR_REFLECTIONS_DISOCCLUSION_THRESHOLD / 2.0 ? 1.0 : 0.0;
        w.z = FFX_DNSR_Reflections_GetDisocclusionFactor(normal, normal01, linear_depth, depth01) > FFX_DNSR_REFLECTIONS_DISOCCLUSION_THRESHOLD / 2.0 ? 1.0 : 0.0;
        w.w = FFX_DNSR_Reflections_GetDisocclusionFactor(normal, normal11, linear_depth, depth11) > FFX_DNSR_REFLECTIONS_DISOCCLUSION_THRESHOLD / 2.0 ? 1.0 : 0.0;
        // And then mix in bilinear weights
        w.x           = w.x * (1.0 - uvx) * (1.0 - uvy);
        w.y           = w.y * (uvx) * (1.0 - uvy);
        w.z           = w.z * (1.0 - uvx) * (uvy);
        w.w           = w.w * (uvx) * (uvy);
        float ws = max(w.x + w.y + w.z + w.w, 1.0e-3);
        // normalize
        w /= ws;

        float3 history_normal;
        float       history_linear_depth;
        reprojection         = reprojection00 * w.x + reprojection10 * w.y + reprojection01 * w.z + reprojection11 * w.w;
        history_linear_depth = depth00 * w.x + depth10 * w.y + depth01 * w.z + depth11 * w.w;
        history_normal       = normal00 * w.x + normal10 * w.y + normal01 * w.z + normal11 * w.w;
        disocclusion_factor  = FFX_DNSR_Reflections_GetDisocclusionFactor(normal, history_normal, linear_depth, history_linear_depth);
    }
    disocclusion_factor = disocclusion_factor < FFX_DNSR_REFLECTIONS_DISOCCLUSION_THRESHOLD ? 0.0 : disocclusion_factor;
}


void FFX_DNSR_Reflections_Reproject(int2 dispatch_thread_id, int2 group_thread_id, uint2 screen_size, float temporal_stability_factor, int max_samples) {
    FFX_DNSR_Reflections_InitializeGroupSharedMemory(dispatch_thread_id, group_thread_id, screen_size);
    GroupMemoryBarrierWithGroupSync();

    group_thread_id += 4; // Center threads in groupshared memory

    float       variance    = 1.0;
    float       num_samples = 0.0;
    float       roughness   = FFX_DNSR_Reflections_LoadRoughness(dispatch_thread_id);
    float3      normal      = FFX_DNSR_Reflections_LoadWorldSpaceNormal(dispatch_thread_id);
    floatx      radiance    = GetContactHardenedRadiance(dispatch_thread_id);
    const float ray_length  = FFX_DNSR_Reflections_LoadRayLength(dispatch_thread_id);

    if (FFX_DNSR_Reflections_IsGlossyReflection(roughness)) {
        float  disocclusion_factor;
        float2      reprojection_uv;
        floatx reprojection;
        FFX_DNSR_Reflections_PickReprojection(/*in*/ dispatch_thread_id,
                                              /* in */ group_thread_id,
                                              /* in */ screen_size,
                                              /* in */ roughness,
                                              /* in */ ray_length,
                                              /* out */ disocclusion_factor,
                                              /* out */ reprojection_uv,
                                              /* out */ reprojection);
        float prev_variance = FFX_DNSR_Reflections_SampleVarianceHistory(reprojection_uv);
        num_samples         = FFX_DNSR_Reflections_SampleNumSamplesHistory(reprojection_uv) * disocclusion_factor;
        float s_max_samples = max(8.0, max_samples * FFX_DNSR_REFLECTIONS_SAMPLES_FOR_ROUGHNESS(roughness));
        num_samples         = min(s_max_samples, num_samples + SampleCountIntersection);
        num_samples         = max(1.0, num_samples);
        float new_variance  = FFX_DNSR_Reflections_ComputeTemporalVariance(radiance.xyz, reprojection.xyz);
        if (disocclusion_factor < FFX_DNSR_REFLECTIONS_DISOCCLUSION_THRESHOLD) {
            FFX_DNSR_Reflections_StoreRadianceReprojected(dispatch_thread_id, (0.0).xxxx);
            FFX_DNSR_Reflections_StoreVariance(dispatch_thread_id, 1.0);
            FFX_DNSR_Reflections_StoreNumSamples(dispatch_thread_id, 1.0);
        } else {
            float variance_mix = lerp(new_variance, prev_variance, 1.0 / num_samples);
            FFX_DNSR_Reflections_StoreRadianceReprojected(dispatch_thread_id, reprojection);
            FFX_DNSR_Reflections_StoreVariance(dispatch_thread_id, variance_mix);
            FFX_DNSR_Reflections_StoreNumSamples(dispatch_thread_id, num_samples);
            // Mix in reprojection for radiance mip computation 
            radiance.xyz = lerp(radiance.xyz, reprojection.xyz, 0.3);
        }
    }
    
    // Downsample 8x8 -> 1 radiance using groupshared memory
    // Initialize groupshared array for downsampling
    float weight = FFX_DNSR_Reflections_GetLuminanceWeight(radiance.xyz);
    radiance.xyz *= weight;
    if (any(dispatch_thread_id >= screen_size) || weight > 1.0e3) {
        radiance = 0.0;
        weight   = 0.0;
    }

    group_thread_id -= 4; // Center threads in groupshared memory

    FFX_DNSR_Reflections_StoreInGroupSharedMemory(group_thread_id, radiance, weight);
    GroupMemoryBarrierWithGroupSync();

    for (int i = 2; i <= 8; i = i * 2) {
        int ox = group_thread_id.x * i;
        int oy = group_thread_id.y * i;
        int ix = group_thread_id.x * i + i / 2;
        int iy = group_thread_id.y * i + i / 2;
        if (ix < 8 && iy < 8) {
            float4 rad_weight00 = FFX_DNSR_Reflections_LoadFromGroupSharedMemoryRaw(int2(ox, oy));
            float4 rad_weight10 = FFX_DNSR_Reflections_LoadFromGroupSharedMemoryRaw(int2(ox, iy));
            float4 rad_weight01 = FFX_DNSR_Reflections_LoadFromGroupSharedMemoryRaw(int2(ix, oy));
            float4 rad_weight11 = FFX_DNSR_Reflections_LoadFromGroupSharedMemoryRaw(int2(ix, iy));
            float4 sumColor          = rad_weight00 + rad_weight01 + rad_weight10 + rad_weight11;
            float sumWeight     = g_ffx_dnsr_shared_1[oy][ox] + g_ffx_dnsr_shared_1[oy][ix] + g_ffx_dnsr_shared_1[iy][ox] + g_ffx_dnsr_shared_1[iy][ix];
            FFX_DNSR_Reflections_StoreInGroupSharedMemory(int2(ox, oy), sumColor, sumWeight );
        }
        GroupMemoryBarrierWithGroupSync();
    }

    if (all(group_thread_id == 0)) {
        float4 sumColor     = FFX_DNSR_Reflections_LoadFromGroupSharedMemoryRaw(int2(0, 0));
        float sumWeight     = g_ffx_dnsr_shared_1[0][0];
        float  weight_acc   = max(sumWeight, 1.0e-3);
        floatx      radiance_avg = sumColor / weight_acc;
        radiance_avg.a = saturate( radiance_avg.a ); 
        FFX_DNSR_Reflections_StoreAverageRadiance(dispatch_thread_id.xy / 8, radiance_avg);
    }
}

#endif // FFX_DNSR_REFLECTIONS_REPROJECT
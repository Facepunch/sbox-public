#ifndef LIGHTBINNER_HLSL
#define LIGHTBINNER_HLSL

#include "common/Bindless.hlsl"
#include "math_general.fxc"
#include "common_samplers.fxc"
#include "common/classes/BinnedLight.hlsl"

//-----------------------------------------------------------------------------
// Light Buffer
//-----------------------------------------------------------------------------

enum EnvMapFlags
{
    ProjectionBox,
    ProjectionSphere,
    ProjectionNoParallax
};

//-----------------------------------------------------------------------------

cbuffer ViewLightingConfigV2
{
    int4 NumLights;                     // x - num dynamic lights, y - num baked lights, z - num envmaps, w - num decals

    int4 BakedLightIndexMapping[256];   // Remaps baked light index to the light pool list for fast
                                        // query on the shader, we have a hard limit of 256 baked lights

    float4 AmbientLightColor;           // w = lerp between IBL and flat ambient light

    #define NumDynamicLights        NumLights.x
    #define NumBakedIndexedLights   NumLights.y
    #define NumEnvironmentMaps      NumLights.z
    #define NumDecals               NumLights.w
};

//
// matt: I could spend all day refactoring this struct/file
//       there's too much happening here
//       light cookies should be separate the same way as shadows are now
//       treating data like a class is wrong, there's a lot of redundant data
//       envmaps should be in their own file
//

struct BinnedEnvMap
{
    float4x3 WorldToLocal;
    float4 BoxMins;
    float4 BoxMaxs;
    float4 Color; // w - feathering
    float4 NormalizationSH; // Unused
    uint4   Attributes; // x = cubemap texture index, y = flags (future), z = unused, w = unused

    // ---------------------------------

    uint GetCubemapIndex() { return Attributes.x; }
};

//-----------------------------------------------------------------------------

StructuredBuffer<BinnedLight>    BinnedLightBufferV2    < Attribute( "BinnedLightBufferV2" );  > ;
StructuredBuffer<BinnedEnvMap>   BinnedEnvMapBuffer   < Attribute( "BinnedEnvMapBuffer" ); > ;

BinnedLight DynamicLightConstantByIndex( int index )
{
    return BinnedLightBufferV2[ index ];
}

BinnedLight BakedIndexedLightConstantByIndex( int index )
{
    return BinnedLightBufferV2[ BakedLightIndexMapping[index].x ];
}

BinnedEnvMap EnvironmentMapConstantByIndex( int index )
{
    return BinnedEnvMapBuffer[ index ];
}

//-----------------------------------------------------------------------------

#include "common/classes/ClusterCulling.hlsl"

//-----------------------------------------------------------------------------

#endif // LIGHTBINNER_HLSL

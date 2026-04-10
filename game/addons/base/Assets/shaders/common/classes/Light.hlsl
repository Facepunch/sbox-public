#ifndef LIGHT_HLSL
#define LIGHT_HLSL

#include "common/Shadow.hlsl"
#include "common/lightbinner.hlsl"
#include "light_probe_volume.fxc"
#include "baked_lighting_constants.fxc"

//-----------------------------------------------------------------------------
// Light structure
//-----------------------------------------------------------------------------
class Light
{
    // The color is an RGB value in the linear sRGB color space.
    float3 Color;

    // The normalized light vector, in world space (direction from the
    // current fragment's position to the light).
    float3 Direction;

    // The position of the light in world space. This value is the same as
    // Direction for directional lights.
    float3 Position;

    // Attenuation of the light based on the distance from the current
    // fragment to the light in world space. This value between 0.0 and 1.0
    // is computed differently for each type of light (it's always 1.0 for
    // directional lights).
    float Attenuation;

    // Visibility factor computed from shadow maps or other occlusion data
    // specific to the light being evaluated. This value is between 0.0 and
    // 1.0.
    float Visibility;

    BinnedLight LightData;

    void Init( float3 vPositionWs, BinnedLight lightData, float2 screenPos );
    static Light From( float3 vPositionWs, float4 vPositionSs, uint nLightIndex, float2 vLightMapUV = 0.0f );
    static uint Count( float4 vPositionSs );
    float3 GetLightCookie(float3 vPositionWs);
    float3 GetLightColor(float3 vPositionWs);
    float3 GetLightDirection(float3 vPositionWs);
    float3 GetLightPosition();
    float GetLightAttenuation(float3 vPositionWs);
    float Shadows(float3 vPositionWs, float2 screenPos);
};

//-----------------------------------------------------------------------------

void Light::Init( float3 vPositionWs, BinnedLight lightData, float2 screenPos )
{
    LightData = lightData;

    Color = GetLightColor( vPositionWs );
    Direction = GetLightDirection( vPositionWs );
    Position = GetLightPosition();
    Attenuation = GetLightAttenuation( vPositionWs );
    Visibility = Shadows( vPositionWs, screenPos );
}

// Light::From and Light::Count are implemented after StaticLight (forward reference)

//-----------------------------------------------------------------------------

float3 Light::GetLightCookie(float3 vPositionWs)
{
    if ( !LightData.HasLightCookie() )
        return 1.0f;

    float4 vSample = LightData.SampleLightCookie( LightData.GetCookieUV( vPositionWs ) );
    return vSample.rgb * vSample.a;
}

float3 Light::GetLightColor(float3 vPositionWs)
{
    return LightData.Color * GetLightCookie(vPositionWs);
}

float3 Light::GetLightDirection(float3 vPositionWs)
{
    float3 vLightDir = normalize(GetLightPosition() - vPositionWs);
    return vLightDir;
}

float3 Light::GetLightPosition()
{
    return LightData.GetPosition();
}

float Light::GetLightAttenuation(float3 vPositionWs)
{
    const float3 vPositionToLightRayWs = GetLightPosition() - vPositionWs.xyz; // "L"
    const float3 vPositionToLightDirWs = normalize(vPositionToLightRayWs.xyz);
    const float flDistToLightSq = dot(vPositionToLightRayWs.xyz, vPositionToLightRayWs.xyz);

    float flOuterConeCos = LightData.SpotLightInnerOuterConeCosines.y;
    float flConeToDirection = dot(vPositionToLightDirWs.xyz, -LightData.GetDirection()) - flOuterConeCos;
    if (flConeToDirection <= 0.0)
    {
        // Outside spotlight cone
        return 0.0f;
    }

    float flSpotAtten = flConeToDirection * LightData.SpotLightInnerOuterConeCosines.z;
    float flLightFalloff = CalculateDistanceFalloff(flDistToLightSq, LightData.LinearFalloff, LightData.QuadraticFalloff, LightData.FalloffBias, 1.0);

    float flLightMask = flLightFalloff * flSpotAtten;

    return flLightMask;
}

float Light::Shadows(float3 vPositionWs, float2 screenPos)
{
    float flShadowScalar = 1.0;

    if (LightData.Type == LightType::LightTypeDirectional)
        flShadowScalar = DirectionalLightShadow::GetVisibility(vPositionWs, screenPos);
    else if (LightData.Type == LightType::LightTypePoint)
        flShadowScalar = ProjectedShadowCube::GetVisibility(LightData.ShadowMapIndex, vPositionWs);
    else if (LightData.Type == LightType::LightTypeSpot)
        flShadowScalar = ProjectedShadow::GetVisibility(LightData.ShadowMapIndex, vPositionWs, screenPos);

    return flShadowScalar;
}

//-----------------------------------------------------------------------------
// Lightmapped Probe
//-----------------------------------------------------------------------------
bool UsesBakedLightingFromProbe < Attribute("UsesBakedLightingFromProbe"); > ;

class ProbeLight
{
    static bool UsesProbes()
    {
        return UsesBakedLightingFromProbe;
    }

    // Returns 4 baked light indices and their strengths from the probe volume
    static void Init( float3 vPositionWs, out int4 indices, out float4 strengths )
    {
        SampleLightProbeVolumeIndexedDirectLighting( indices, strengths, vPositionWs );
    }

    // Get a Light from a probe at the given sub-index (0-3)
    static Light From( float3 vPositionWs, uint subIndex, float2 screenPos )
    {
        Light light = (Light)0;

        int4 indices;
        float4 strengths;
        Init( vPositionWs, indices, strengths );

        int bakedIdx = indices[subIndex];
        float strength = strengths[subIndex];

        if ( bakedIdx < 0 || strength <= 0.0f )
            return light;

        BinnedLight bakedLight = BakedIndexedLightConstantByIndex( bakedIdx );
        light.Init( vPositionWs, bakedLight, screenPos );
        light.Attenuation = strength;

        return light;
    }
};

//-----------------------------------------------------------------------------
// 2D Lightmap
//-----------------------------------------------------------------------------
bool UsesBakedLightmaps < Attribute("UsesBakedLightmaps"); > ;

// Bless this
#define LightMap(a) Bindless::GetTexture2DArray(g_nLightmapTextureIndices[a])

#define DIRECTIONAL_LIGHTMAP_STRENGTH 1.0f
#define DIRECTIONAL_LIGHTMAP_MINZ 0.05

class LightmappedLight
{
    static bool UsesLightmaps()
    {
        return UsesBakedLightmaps;
    }

    // Reads baked light indices and strengths from a lightmap texture
    static void Init( float2 vLightMapUV, out int4 indices, out float4 strengths )
    {
        indices = (int4)LightMap(0).SampleLevel( g_sPointClamp, float3( vLightMapUV, 0.0f ), 0 );
        strengths = LightMap(1).SampleLevel( g_sTrilinearClamp, float3( vLightMapUV, 0.0f ), 0 );
    }

    static Light From( float3 vPositionWs, float2 vLightMapUV, uint subIndex, float2 screenPos )
    {
        Light light = (Light)0;

        int4 indices;
        float4 strengths;
        Init( vLightMapUV, indices, strengths );

        int bakedIdx = indices[subIndex];
        float strength = strengths[subIndex];

        if ( bakedIdx < 0 || strength <= 0.0f )
            return light;

        BinnedLight bakedLight = BakedIndexedLightConstantByIndex( bakedIdx );
        light.Init( vPositionWs, bakedLight, screenPos );
        light.Attenuation = strength;

        return light;
    }
};

//-----------------------------------------------------------------------------
// Static light — dispatches between probe and lightmap sources
//-----------------------------------------------------------------------------
class StaticLight
{
    // Static lights contribute up to 4 lights (one per XYZW channel of the index/strength textures)
    static uint Count()
    {
        if ( ProbeLight::UsesProbes() || LightmappedLight::UsesLightmaps() )
            return 4;

        return 0;
    }

    static Light From( float3 vPositionWs, float2 vLightMapUV, uint subIndex, float2 screenPos )
    {
        if ( ProbeLight::UsesProbes() )
            return ProbeLight::From( vPositionWs, subIndex, screenPos );

        if ( LightmappedLight::UsesLightmaps() )
            return LightmappedLight::From( vPositionWs, vLightMapUV, subIndex, screenPos );

        return (Light)0;
    }
};

//-----------------------------------------------------------------------------
// Light::From / Light::Count — defined here because they depend on StaticLight
//-----------------------------------------------------------------------------

static Light Light::From( float3 vPositionWs, float4 vPositionSs, uint nLightIndex, float2 vLightMapUV )
{
    uint dynamicCount = Cluster::Query( ClusterItemType_Light, vPositionSs ).Count;

    if ( nLightIndex < dynamicCount )
    {
        Light light = (Light)0;

        ClusterRange range = Cluster::Query( ClusterItemType_Light, vPositionSs );
        uint clusterLocalIndex = min( nLightIndex, range.Count - 1 );
        uint lightIndex = Cluster::LoadItem( range, clusterLocalIndex );

        light.Init( vPositionWs, DynamicLightConstantByIndex( lightIndex ), vPositionSs.xy );
        return light;
    }

    return StaticLight::From( vPositionWs, vLightMapUV, nLightIndex - dynamicCount, vPositionSs.xy );
}

static uint Light::Count( float4 vPositionSs )
{
    return Cluster::Query( ClusterItemType_Light, vPositionSs ).Count + StaticLight::Count();
}

#endif // LIGHT_HLSL

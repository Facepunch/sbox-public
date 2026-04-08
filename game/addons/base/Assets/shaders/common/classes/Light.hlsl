#ifndef LIGHT_HLSL
#define LIGHT_HLSL

#include "common/Shadow.hlsl"
#include "common/lightbinner.hlsl"
#include "light_probe_volume.fxc"
#include "baked_lighting_constants.fxc"

//-----------------------------------------------------------------------------
// Light::Query result
//-----------------------------------------------------------------------------
struct LightRange
{
    ClusterRange ClusterRange;
    uint DynamicCount;
    uint Count;
};

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

    // Initialize a dynamic light from BinnedLight data
    void Init( float3 vPositionWs, BinnedLight lightData, float2 vPositionSs );

    // Collect a range of lights to loop over. Use the result with Light::Fetch
    static LightRange Query( float4 vPositionSs );
    static Light Fetch( LightRange query, uint index, float3 vPositionWs, float2 vPositionSs, float2 vLightMapUV = 0.0f );
};

// Light::From and Light::Count are implemented after StaticLight (forward reference)

void Light::Init( float3 vPositionWs, BinnedLight lightData, float2 vPositionSs )
{
    Color = lightData.Color;
    if ( lightData.HasLightCookie() ) {
        float4 sample = lightData.SampleLightCookie( lightData.GetCookieUV( vPositionWs ) );
        Color *= sample.rgb * sample.a;
    }
    Position = lightData.GetPosition();
    float3 offset = Position - vPositionWs;
    Direction = normalize( offset );
    // Attenuation
    Attenuation = 0.0;
    float flConeToDirection = dot( Direction, -lightData.GetDirection() ) - lightData.SpotLightInnerOuterConeCosines.y;
    if ( flConeToDirection > 0.0 )
    {
        float flDistToLightSq = dot( offset, offset );
        float flLightFalloff = CalculateDistanceFalloff( flDistToLightSq, lightData.LinearFalloff, lightData.QuadraticFalloff, lightData.FalloffBias, 1.0 );
        Attenuation = flLightFalloff * flConeToDirection * lightData.SpotLightInnerOuterConeCosines.z;
    }
    // Visibility
    Visibility = 1.0;
    if ( lightData.Type == LightType::LightTypeDirectional )
        Visibility = DirectionalLightShadow::GetVisibility( vPositionWs, vPositionSs );
    else if ( lightData.Type == LightType::LightTypePoint )
        Visibility = ProjectedShadowCube::GetVisibility( lightData.ShadowMapIndex, vPositionWs );
    else if ( lightData.Type == LightType::LightTypeSpot )
        Visibility = ProjectedShadow::GetVisibility( lightData.ShadowMapIndex, vPositionWs, vPositionSs );
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
// Light::Query / Light::Fetch — defined here because they depend on StaticLight
//-----------------------------------------------------------------------------
static LightRange Light::Query( float4 vPositionSs )
{
    ClusterRange range = Cluster::Query( ClusterItemType_Light, vPositionSs );
    LightRange query;
    query.ClusterRange = range;
    query.Count = range.Count;
    if ( g_DirectionalLightEnabled )
        query.Count++;
    query.DynamicCount = query.Count;
    query.Count += StaticLight::Count();
    return query;
}

static Light Light::Fetch( LightRange query, uint index, float3 vPositionWs, float2 vPositionSs, float2 vLightMapUV )
{
    if ( index < query.ClusterRange.Count ) 
    {
        // Initialize dynamic light
        BinnedLight data = DynamicLightConstantByIndex( Cluster::LoadItem( query.ClusterRange, index ) );
        Light light = (Light)0;
        light.Init( vPositionWs, data, vPositionSs );
        return light;
    } 
    else if ( g_DirectionalLightEnabled && index == query.ClusterRange.Count )
    {
        // Initialize directional light
        Light light = (Light)0;
        light.Color = g_DirectionalLightColor.rgb;
        light.Position = 0.0;
        light.Direction = -g_DirectionalLightDirection.xyz;
        light.Attenuation = 1.0;
        light.Visibility = DirectionalLightShadow::GetVisibility( vPositionWs, vPositionSs );
        return light;
    }
    // static light
    return StaticLight::From( vPositionWs, vLightMapUV, index - query.DynamicCount, vPositionSs );
}

#endif // LIGHT_HLSL

#ifndef LIGHT_HLSL
#define LIGHT_HLSL

#include "common/Shadow.hlsl"
#include "common/lightbinner.hlsl"
#include "light_probe_volume.fxc"
#include "baked_lighting_constants.fxc"

//-----------------------------------------------------------------------------
// A baked light read out of a probe volume or a lightmap, before it becomes a Light.  Keeping this
// separate from Light is what lets the static light sources be declared ahead of it.
//-----------------------------------------------------------------------------
struct StaticLightSample
{
    int BakedIndex;         // index into the baked light constants, negative when there is no light
    float Strength;

    static StaticLightSample From( int bakedIndex, float strength )
    {
        StaticLightSample sample;
        sample.BakedIndex = ( strength > 0.0f ) ? bakedIndex : -1;
        sample.Strength = strength;
        return sample;
    }

    static StaticLightSample None()
    {
        return From( -1, 0.0f );
    }

    bool IsValid()
    {
        return BakedIndex >= 0;
    }

    BinnedLight GetLightData()
    {
        return BakedIndexedLightConstantByIndex( BakedIndex );
    }
};

//-----------------------------------------------------------------------------
// Lightmapped Probe
//-----------------------------------------------------------------------------
bool UsesBakedLightingFromProbe < Attribute("UsesBakedLightingFromProbe"); > ;

struct ProbeLight
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

    // Sample the probe at the given sub-index (0-3)
    static StaticLightSample Sample( float3 vPositionWs, uint subIndex )
    {
        int4 indices;
        float4 strengths;
        Init( vPositionWs, indices, strengths );

        return StaticLightSample::From( indices[subIndex], strengths[subIndex] );
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

struct LightmappedLight
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

    static StaticLightSample Sample( float2 vLightMapUV, uint subIndex )
    {
        int4 indices;
        float4 strengths;
        Init( vLightMapUV, indices, strengths );

        return StaticLightSample::From( indices[subIndex], strengths[subIndex] );
    }
};

//-----------------------------------------------------------------------------
// Static light — dispatches between probe and lightmap sources
//-----------------------------------------------------------------------------
struct StaticLight
{
    // Static lights contribute up to 4 lights (one per XYZW channel of the index/strength textures)
    static uint Count()
    {
        if ( ProbeLight::UsesProbes() || LightmappedLight::UsesLightmaps() )
            return 4;

        return 0;
    }

    static StaticLightSample Sample( float3 vPositionWs, float2 vLightMapUV, uint subIndex )
    {
        if ( ProbeLight::UsesProbes() )
            return ProbeLight::Sample( vPositionWs, subIndex );

        if ( LightmappedLight::UsesLightmaps() )
            return LightmappedLight::Sample( vLightMapUV, subIndex );

        return StaticLightSample::None();
    }
};

//-----------------------------------------------------------------------------
// Light structure
//-----------------------------------------------------------------------------
struct Light
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

    float3 GetLightCookie(float3 vPositionWs)
    {
        if ( !LightData.HasLightCookie() )
            return 1.0f;

        float4 vSample = LightData.SampleLightCookie( LightData.GetCookieUV( vPositionWs ) );
        return vSample.rgb * vSample.a;
    }

    float3 GetLightColor(float3 vPositionWs)
    {
        return LightData.Color * GetLightCookie(vPositionWs);
    }

    float3 GetLightPosition()
    {
        return LightData.GetPosition();
    }

    float3 GetLightDirection(float3 vPositionWs)
    {
        float3 vLightDir = normalize(GetLightPosition() - vPositionWs);
        return vLightDir;
    }

    float GetLightAttenuation(float3 vPositionWs)
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

    float Shadows(float3 vPositionWs, float4 vPositionSs)
    {
        float flShadowScalar = 1.0;

        if (LightData.Type == LightType::LightTypeDirectional)
            flShadowScalar = DirectionalLightShadow::GetVisibility(vPositionWs, vPositionSs);
        else if (LightData.Type == LightType::LightTypePoint)
            flShadowScalar = ProjectedShadowCube::GetVisibility(LightData.ShadowMapIndex, vPositionWs);
        else if (LightData.Type == LightType::LightTypeSpot)
            flShadowScalar = ProjectedShadow::GetVisibility(LightData.ShadowMapIndex, vPositionWs, vPositionSs.xy);

        return flShadowScalar;
    }

    // [mutating] is what lets a member write to its own fields under Slang; DXC ignores it
    [mutating]
    void Init( float3 vPositionWs, BinnedLight lightData, float4 vPositionSs )
    {
        LightData = lightData;

        Color = GetLightColor( vPositionWs );
        Direction = GetLightDirection( vPositionWs );
        Position = GetLightPosition();
        Attenuation = GetLightAttenuation( vPositionWs );
        Visibility = Shadows( vPositionWs, vPositionSs );
    }

    // Lights are addressed as one flat list per pixel: the cluster's dynamic lights first, then up
    // to StaticLight::Count() baked ones.
    static Light From( float3 vPositionWs, float4 vPositionSs, uint nLightIndex, float2 vLightMapUV = 0.0f )
    {
        Light light = {};

        ClusterRange range = Cluster::Query( ClusterItemType_Light, vPositionSs );
        if ( nLightIndex < range.Count )
        {
            uint clusterLocalIndex = min( nLightIndex, range.Count - 1 );
            uint lightIndex = Cluster::LoadItem( range, clusterLocalIndex );

            light.Init( vPositionWs, DynamicLightConstantByIndex( lightIndex ), vPositionSs );
            return light;
        }

        StaticLightSample sample = StaticLight::Sample( vPositionWs, vLightMapUV, nLightIndex - range.Count );
        if ( !sample.IsValid() )
            return light;

        light.Init( vPositionWs, sample.GetLightData(), vPositionSs );
        light.Attenuation = sample.Strength;

        return light;
    }

    static uint Count( float4 vPositionSs )
    {
        return Cluster::Query( ClusterItemType_Light, vPositionSs ).Count + StaticLight::Count();
    }
};

#endif // LIGHT_HLSL

#ifndef LIGHT_HLSL
#define LIGHT_HLSL

#include "common/Shadow.hlsl"
#include "common/lightbinner.hlsl"

#include "light_probe_volume.fxc"
#include "baked_lighting_constants.fxc"

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

    // Gets the light structure given the world position and the light index.
    static Light From( float3 vPositionWs, uint nLightIndex, float2 vLightMapUV = 0.0f );

    // Number of lights in the current fragment.
    static uint Count( float3 vPositionWs );
};

class DynamicLight : Light
{
    BinnedLight LightData;

    //
    // Main function that fills in the light data for the current light.
    //
    void Init( float3 vPositionWs, BinnedLight lightData )
    {
        LightData = lightData;
        
        // Get the light's color and intensity.
        Color = GetLightColor( vPositionWs );

        // Get the light's direction in world space.
        Direction = GetLightDirection( vPositionWs );

        // Get the position of the light in world space.
        Position = GetLightPosition();

        // Get the attenuation of the light based on the distance from the current
        // fragment to the light in world space.
        Attenuation = GetLightAttenuation( vPositionWs );

        // Get the visibility factor computed from shadow maps or other occlusion
        // data specific to the light being evaluated.
        Visibility = GetLightVisibility( vPositionWs );

    }

    //
    // Creates the structure of a dynamic light from the current pixel input.
    //
    static DynamicLight From( float3 vPositionWs, uint nLightIndex )
    {
        DynamicLight light = (DynamicLight)0;

        ClusterRange range = Cluster::Query( ClusterItemType_Light, vPositionWs );
        if ( range.Count == 0 )
        {
            return light;
        }

        uint clusterLocalIndex = min( nLightIndex, range.Count - 1 );
        uint lightIndex = Cluster::LoadItem( range, clusterLocalIndex );

        light.Init( vPositionWs, DynamicLightConstantByIndex( lightIndex ) );
        return light;
    }

    //
    // Number of lights in the current fragment.
    //
    static uint Count( float3 vPositionWs )
    {
        return Cluster::Query( ClusterItemType_Light, vPositionWs ).Count;
    }

    //
    // Helper functions
    //
    float3 GetLightCookie(float3 vPositionWs);
    float3 GetLightColor(float3 vPositionWs);
    float3 GetLightDirection(float3 vPositionWs);
    float3 GetLightPosition();
    float GetLightAttenuation(float3 vPositionWs);
    float DynamicShadows(float3 vPositionWs);
    float GetLightVisibility(float3 vPositionWs);
};

//-----------------------------------------------------------------------------
// Lightmapped Probe lights
//
// These are lightmapped surfaces that incide with stationary lighting.
// Can have dynamic shadows.
//-----------------------------------------------------------------------------
bool UsesBakedLightingFromProbe < Attribute( "UsesBakedLightingFromProbe" ); >;

class ProbeLight : DynamicLight
{
    //
    // Main function that fills in the light data for the current light.
    //
    void Init(float3 vPositionWs, uint nLightIndex, float lightStrength )
    {
        LightData = BakedIndexedLightConstantByIndex( nLightIndex );

        // Get the light's color and intensity.
        Color = GetLightColor( vPositionWs );

        // Get the light's direction in world space.
        Direction = GetLightDirection( vPositionWs );

        // Get the position of the light in world space.
        Position = GetLightPosition();

        // Get the attenuation of the light based on the distance from the current
        // fragment to the light in world space.
        Attenuation = lightStrength * lightStrength;

        // Get the visibility factor computed from shadow maps or other occlusion
        // data specific to the light being evaluated.
        Visibility = GetLightVisibility( vPositionWs );
    }

    //
    // Creates the structure of a static probe light from the current pixel input.
    //
    static Light From( float3 vPositionWs, uint nLightIndex )
    {
		int4 vLightIndices;
		float4 vLightStrengths;
        SampleLightProbeVolumeIndexedDirectLighting( vLightIndices, vLightStrengths, vPositionWs );

        ProbeLight light;
        light.Init( vPositionWs, vLightIndices[ nLightIndex ], vLightStrengths[nLightIndex] );
        return (Light)light;
    }

    static bool UsesProbes()
    {
        return UsesBakedLightingFromProbe;
    }
};

//-----------------------------------------------------------------------------
// Direct lightmapped light
//-----------------------------------------------------------------------------
bool UsesBakedLightmaps     < Attribute( "UsesBakedLightmaps" ); >;

// Bless this
#define LightMap( a ) Bindless::GetTexture2DArray( g_nLightmapTextureIndices[a] )

#define DIRECTIONAL_LIGHTMAP_STRENGTH 1.0f
#define DIRECTIONAL_LIGHTMAP_MINZ 0.05

class LightmappedLight : ProbeLight
{
    static int4 GetLightmappedLightIndices( float2 vLightmapUV )
    {
        float3 vLightmapUVW = float3( vLightmapUV.xy, 0 );
		float4 vLightIndexFloats = Tex2DArrayS( LightMap( 0 ), g_sPointClamp, vLightmapUVW ).rgba;

        int4 vLightIndices = int4( vLightIndexFloats.xyzw * 255 );
        return vLightIndices;
    }
    
    static float4 GetLightmappedLightStrengths( float2 vLightmapUV )
    {
        float3 vLightmapUVW = float3( vLightmapUV.xy, 0 );
		float4 vLightStrengths = Tex2DArrayS( LightMap( 1 ), g_sTrilinearClamp, vLightmapUVW ).rgba;
        return vLightStrengths;
    }
    
    //
    // Creates the structure of a lightmapped light from the current pixel input.
    //
    static Light From( float3 vPositionWs, float2 vLightmapUV, uint nLightIndex )
    {
        // Translated light index from the lightmap to the global light index.
        int nLightmappedIndex = GetLightmappedLightIndices( vLightmapUV )[nLightIndex];
        // Get the light strength from the lightmap.
        float fLightStrength = GetLightmappedLightStrengths( vLightmapUV )[nLightIndex];

        if (fLightStrength < 0.0001)
        {
            return (Light)0;
        }

        LightmappedLight light;
        light.Init( vPositionWs, nLightmappedIndex, fLightStrength );
        return (Light)light;
    }

    static bool UsesLightmaps()
    {
        return UsesBakedLightmaps;
    }
};

//-----------------------------------------------------------------------------
// Indexed static lights
// Could be just a single class?
//-----------------------------------------------------------------------------
class StaticLight : Light
{
    //
    // Creates the structure of a static light from the current pixel input.
    //
    static Light From( float3 vPositionWs, float2 vLightmapUV, uint nLightIndex )
    {
        if ( LightmappedLight::UsesLightmaps() )
            return LightmappedLight::From( vPositionWs, vLightmapUV, nLightIndex );
        else if ( ProbeLight::UsesProbes() )
            return ProbeLight::From( vPositionWs, nLightIndex );

        // No static lights. Send a dummy light.
        return (Light)0;
    }

    static bool UsesStaticLights()
    {
        return ( ProbeLight::UsesProbes() || LightmappedLight::UsesLightmaps() );
    }
   
    static uint Count()
    {
        if ( UsesStaticLights() )
        {
            return 4; // 4 indices in XYZW of the texture
        }

        // No static lights.
        return 0;
    }
};

//-----------------------------------------------------------------------------

static Light Light::From( float3 vPositionWs, uint nLightIndex, float2 vLightMapUV )
{
    uint dynamicCount = DynamicLight::Count( vPositionWs );

    if ( nLightIndex < dynamicCount )
    {
        return DynamicLight::From( vPositionWs, nLightIndex );
    }

    return StaticLight::From( vPositionWs, vLightMapUV, nLightIndex - dynamicCount );
}

static uint Light::Count( float3 vPositionWs )
{
    return DynamicLight::Count( vPositionWs ) + StaticLight::Count();
}

//-----------------------------------------------------------------------------

//
// Gets the light cookie
//
float3 DynamicLight::GetLightCookie(float3 vPositionWs)
{
    [branch]
    if (LightData.HasLightCookie())
    {
        // Light cookie
        float3 vPositionTextureSpace = Position3WsToShadowTextureSpace(vPositionWs.xyz, LightData.WorldToLightCookie);
        float4 vCookieSample = LightData.SampleLightCookie( vPositionTextureSpace.xy );

        return vCookieSample.rgb * vCookieSample.a;
    }

    return 1.0f;
}

//
// Get the light's color and intensity.
//
float3 DynamicLight::GetLightColor(float3 vPositionWs)
{
    return LightData.GetColor() * GetLightCookie(vPositionWs);
}

//
// Get the light's direction in world space.
//
float3 DynamicLight::GetLightDirection(float3 vPositionWs)
{
    float3 vLightDir = normalize(GetLightPosition() - vPositionWs);
    return vLightDir;
}

//
// Get the position of the light in world space.
//
float3 DynamicLight::GetLightPosition()
{
    return LightData.GetPosition();
}
// Get the attenuation of the light based on the distance from the current
// fragment to the light in world space.
float DynamicLight::GetLightAttenuation(float3 vPositionWs)
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
    float flLightFalloff = CalculateDistanceFalloff(flDistToLightSq, LightData.FalloffParams.xyzw, 1.0);

    float flLightMask = flLightFalloff * flSpotAtten;

    return flLightMask;
}

//
// Computes the shadow factor for the current light.
//
float DynamicLight::DynamicShadows(float3 vPositionWs)
{
    float flShadowScalar = 1.0;

    [branch]
    if (LightData.HasDynamicShadows())
    {
        [unroll(MAX_SHADOW_FRUSTA_PER_LIGHT)]
        for (uint i = 0; i < LightData.NumShadowFrusta(); i++)
        {
            const float3 vPositionTextureSpace = Position3WsToShadowTextureSpace(vPositionWs.xyz, LightData.WorldToShadow[i]);

            [branch]
            if (InsideShadowRegion(vPositionTextureSpace.xyz, LightData.ShadowBounds[i]))
            {
                flShadowScalar = ComputeShadow(vPositionTextureSpace.xyz);
                break;
            }
        }
    }

    return flShadowScalar;
}

//
// Get the visibility factor computed from shadow maps or other occlusion
// data specific to the light being evaluated.
//
float DynamicLight::GetLightVisibility(float3 vPositionWs)
{
    return DynamicShadows(vPositionWs);
}


#endif // LIGHT_HLSL
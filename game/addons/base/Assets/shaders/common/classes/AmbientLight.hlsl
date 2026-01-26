#ifndef AMBIENTLIGHT_HLSL
#define AMBIENTLIGHT_HLSL

#include "light_probe_volume.fxc"
#include "vr_environment_map.fxc"

enum AmbientLightKind
{
    EnvMapProbe,            // Image-based Lighting
    LightMapProbeVolume,    // Probe-based Lighting
    LightMap2D              // 2D Lightmaps for static geometry
};

/// <summary>
/// Ambient (Diffuse) light class.
/// </summary>
class AmbientLight
{
    static AmbientLightKind GetKind()
    {
        if ( ProbeLight::UsesProbes() )
        {
            return AmbientLightKind::LightMapProbeVolume;
        }
        else if ( LightmappedLight::UsesLightmaps() )
        {
            return AmbientLightKind::LightMap2D;
        }
        else
        {
            return AmbientLightKind::EnvMapProbe;
        }
    }

    static float3 From( float3 WorldPosition, float3 WorldNormal, float2 LightMapUV = 0.0f )
    {
        switch( GetKind() )
        {
            case AmbientLightKind::EnvMapProbe:
                return FromEnvMapProbe( WorldPosition, WorldNormal );
                break;
            case AmbientLightKind::LightMapProbeVolume:
                return FromLightMapProbeVolume( WorldPosition, WorldNormal );
                break;
            case AmbientLightKind::LightMap2D:
                return 0.0f;
        }
        return 0.0f;
    }

    static float3 FromEnvMapProbe(float3 WorldPosition, float3 WorldNormal);
    static float3 FromLightMapProbeVolume(float3 WorldPosition, float3 WorldNormal);
    static float3 FromLightMap(float3 WorldPosition, float2 LightMapUV);
};

float3 AmbientLight::FromEnvMapProbe(float3 WorldPosition, float3 WorldNormal)
{
    float accumulatedDistance = 0.0f;
    float3 ambientLightColor = float3(0.0, 0.0, 0.0);

    // Todo: all this shit could just use EnvMap::From( Roughness 1.0f ) just overgoing the parallax stuff
    
    ClusterRange range = Cluster::Query( ClusterItemType_EnvMap, WorldPosition );
    if ( range.Count == 0 )
    {
        return lerp( ambientLightColor, AmbientLightColor.rgb, AmbientLightColor.a );
    }

    // Iterate over environment maps in the tile
    for (uint i = 0; i < range.Count; i++)
    {
        const uint index = Cluster::LoadItem( range, i );

        // Transform world position to environment map local space
        const float3 localPos = mul(float4(WorldPosition, 1.0f), EnvMapWorldToLocal(index)).xyz;

        // Get edge feathering value
        const float edgeFeathering = EnvMapFeathering(index);

        // Environment map boundaries
        const float3 envMapMin = EnvMapBoxMins(index);
        const float3 envMapMax = EnvMapBoxMaxs(index);

        // Calculate intersection distances
        const float3 intersectA = min(localPos - envMapMin, envMapMax - localPos);
        const float3 intersectB = min(localPos - envMapMax, envMapMin - localPos);

        const float distance = min(
            min(intersectA.x, min(intersectA.y, intersectA.z)),
            min(-intersectB.x, -min(intersectB.y, intersectB.z))
        ) + 0.5f; // Offset to avoid edge artifacts

        // Skip if outside of influence range
        if (distance + max(edgeFeathering, 0.0f) < 0.0)
            continue;

        // Compute diffuse lighting from environment map
        const float3 localNormal = mul(float4(WorldNormal, 0.0f), EnvMapWorldToLocal(index)).xyz;

        float3 envMapColor = SampleEnvironmentMapLevel(WorldNormal, 1.0f, index);

        // Adjust for subsurface scattering if enabled
        /*#if (S_SUBSURFACE_SCATTERING == SUBSURFACE_SCATTERING_PREINTEGRATED)
        {
            float flSSSMask = 0;

            float3 normalR = normalize(lerp(WorldNormal, vNormalWs, g_vAmbientNormalSoftness.r * flSSSMask));
            float3 normalG = normalize(lerp(WorldNormal, vNormalWs, g_vAmbientNormalSoftness.g * flSSSMask));
            float3 normalB = normalize(lerp(WorldNormal, vNormalWs, g_vAmbientNormalSoftness.b * flSSSMask));
            
            envMapColor.r = SampleEnvironmentMapLevel(normalR, 1.0f, index).r;
            envMapColor.g = SampleEnvironmentMapLevel(normalG, 1.0f, index).g;
            envMapColor.b = SampleEnvironmentMapLevel(normalB, 1.0f, index).b;
        }
        #endif*/

        // Blend the ambient light color
        ambientLightColor = lerp(ambientLightColor, envMapColor, 1.0 - accumulatedDistance);

        // Update the accumulated distance
        accumulatedDistance += RemapValClamped(distance, min(-edgeFeathering, 0.0f), max(-edgeFeathering, 0.0f), 0.0, 1.0);

        // Break if we've accumulated enough light
        if (accumulatedDistance >= 1.0)
            break;
    }

    // Blend our IBL with the ambient light scalar defined in ViewLightingConfig
    ambientLightColor = lerp(ambientLightColor, AmbientLightColor.rgb, AmbientLightColor.a);

    return ambientLightColor;
}

float3 AmbientLight::FromLightMapProbeVolume(float3 WorldPosition, float3 WorldNormal)
{
    float3 vAmbientCube[6];
    SampleLightProbeVolume(vAmbientCube, WorldPosition);
    return SampleIrradiance(vAmbientCube, WorldNormal);
}

#endif // AMBIENTLIGHT_HLSL
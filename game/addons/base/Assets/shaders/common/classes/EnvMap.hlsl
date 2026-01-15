#ifndef ENVIRONMENTMAPPING_HLSL
#define ENVIRONMENTMAPPING_HLSL

#include "vr_environment_map.fxc"
#include "common/lightbinner.hlsl"

float3 CalcParallaxReflectionCubemapLocal(float3 vPositionWs, float3 vNormalWs, uint nEnvMap);

class EnvMap
{
    // Computes the environment map color contribution
    static float3 From(float3 WorldPosition, float3 WorldNormal, float2 Roughness = 0.0f)
    {
        float flDistAccumulated = 0.0f;
        float3 vColor = float3(0.0f, 0.0f, 0.0f);

        ClusterRange range = Cluster::Query( ClusterItemType_EnvMap, WorldPosition );
        if ( range.Count == 0 )
        {
            return vColor;
        }

        // Which mip level of the environment map to sample
        const float2 vLevel = sqrt(Roughness.xy);

        // Accumulate contributions from each environment map
        for (uint i = 0; i < range.Count; i++)
        {
            // Get the environment map index
            const uint index = Cluster::LoadItem( range, i );

            // Transform world position to local cubemap space
            const float3 vCubePos = mul(float4(WorldPosition.xyz, 1.0f), EnvMapWorldToLocal(index)).xyz;

            // Get feathering value for edge blending
            const float flEdgeFeathering = EnvMapFeathering(index);

            // Get environment map bounds
            const float3 vEnvMapMin = EnvMapBoxMins(index);
            const float3 vEnvMapMax = EnvMapBoxMaxs(index);

            // Calculate the distance to the environment map bounds
            const float3 vIntersectA = min((vCubePos - vEnvMapMin), (vEnvMapMax - vCubePos));
            const float3 vIntersectB = min((vCubePos - vEnvMapMax), (vEnvMapMin - vCubePos));

            const float flDistance = min(
                min(vIntersectA.x, min(vIntersectA.y, vIntersectA.z)),
                min(-vIntersectB.x, -min(vIntersectB.y, vIntersectB.z))
            ) + 0.5f; // Add half unit to avoid edge artifacts

            // Skip if outside of feathering range
            if (flDistance + max(flEdgeFeathering, 0.0f) < 0.0f)
                continue;

            // Calculate parallax reflection in local cubemap space
            float3 vParallaxReflectionCubemapLocal = CalcParallaxReflectionCubemapLocal(WorldPosition, WorldNormal, index);

            // Specular environment map sampling
            float3 vNormalWarpWs = normalize(lerp(vParallaxReflectionCubemapLocal, WorldNormal, vLevel.x)); // Used to reduce specular aliasing
            float3 vCubeSample = SampleEnvironmentMapLevel(vNormalWarpWs, vLevel.x, index);

            // Accumulate color contribution
            vColor = lerp(vColor, vCubeSample, 1.0f - flDistAccumulated);

            // Update accumulated distance
            flDistAccumulated += RemapValClamped(flDistance, min(-flEdgeFeathering, 0.0f), max(-flEdgeFeathering, 0.0f), 0.0f, 1.0f);

            // Break if enough contribution is accumulated
            if (flDistAccumulated >= 1.0f)
                break;
        }

        return vColor;
    }
};

// Helper Functions

//--------------------------------------------------------------------------------------------------
// Performs cube map box projection for parallax correction
float3 CubeMapBoxProjection(float3 vReflectionCubemapLocal, float3 vPositionCubemapLocal, float3 vBoxMins, float3 vBoxMaxs, float flClamp = 0.0f)
{
    // Clamp the position within the box extents
    vPositionCubemapLocal = clamp(vPositionCubemapLocal, vBoxMins + flClamp, vBoxMaxs - flClamp);

    // Calculate intersection distances along each axis
    float3 vIntersectA = (vBoxMaxs - vPositionCubemapLocal) / vReflectionCubemapLocal;
    float3 vIntersectB = (vBoxMins - vPositionCubemapLocal) / vReflectionCubemapLocal;

    float3 vIntersect = max(vIntersectA, vIntersectB);
    float flDistance = min(vIntersect.x, min(vIntersect.y, vIntersect.z));

    // Compute the reflected direction in world space
    float3 vReflectDirectionWs = vPositionCubemapLocal + vReflectionCubemapLocal * flDistance;

    return vReflectDirectionWs;
}

//--------------------------------------------------------------------------------------------------
// Calculates parallax-corrected reflection vector in local cubemap space
float3 CalcParallaxReflectionCubemapLocal( float3 vPositionWs, float3 vNormalWs, float2 vAnisotropy, float flRetroReflectivity, float3 vTangentUWs, float3 vTangentVWs, uint nEnvMap )
{
	//
	// Calculate local space cubemap reflections
	//
	float3 vPositionToCameraDirWs = CalculatePositionToCameraDirWs( vPositionWs.xyz );
	float3 vReflectionWs = CalculateCameraReflectionDirWs( vPositionWs.xyz, vNormalWs.xyz );
	float3 vPositionCubemapLocal;
	float3 vReflectionCubemapLocal;

	#if ( S_RETRO_REFLECTIVE )
	{
		vReflectionWs.xyz = lerp( vReflectionWs.xyz, vPositionToCameraDirWs.xyz, flRetroReflectivity );
	}
	#endif

	#if ( S_ANISOTROPIC_GLOSS )
	{
		// The geometric roughness term has facets because it's computed with derivatives.
		// This would show up in the anisotropic reflection, so we use the unadjusted roughness instead.
		float3 vAnisoDirection = lerp( vTangentVWs.xyz, vTangentUWs.xyz, vAnisotropy.x );

		float3 vAnisoTangentWs = cross( vPositionToCameraDirWs.xyz, vAnisoDirection.xyz );
		float3 vAnisoNormalWs = normalize( cross( vAnisoTangentWs.xyz, vAnisoDirection.xyz ) );
		float3 vReflectionNormal = normalize( lerp( vNormalWs.xyz, vAnisoNormalWs.xyz, vAnisotropy.y * 0.5 ) );
		vReflectionWs = CalculateCameraReflectionDirWs( vPositionWs.xyz, vReflectionNormal.xyz );
	}
	#endif

	vPositionCubemapLocal = mul( float4( vPositionWs.xyz, 1.0 ), EnvMapWorldToLocal( nEnvMap ) ).xyz;
	vReflectionCubemapLocal = mul( float4( vReflectionWs.xyz, 0.0 ), EnvMapWorldToLocal( nEnvMap ) ).xyz;

	return CubeMapBoxProjection( vReflectionCubemapLocal.xyz, vPositionCubemapLocal.xyz, EnvMapBoxMins( nEnvMap ).xyz, EnvMapBoxMaxs( nEnvMap ).xyz, EnvMapFeathering( nEnvMap ) );
}


//--------------------------------------------------------------------------------------------------
// Calculates parallax-corrected reflection vector in local cubemap space
float3 CalcParallaxReflectionCubemapLocal(float3 vPositionWs, float3 vNormalWs, uint nEnvMap)
{
    // Compute direction from position to camera
    float3 vPositionToCameraDirWs = CalculatePositionToCameraDirWs(vPositionWs);

    // Compute reflection direction in world space
    float3 vReflectionWs = CalculateCameraReflectionDirWs(vPositionWs, vNormalWs);

    // Transform position and reflection vector to cubemap local space
    float3 vPositionCubemapLocal = mul(float4(vPositionWs, 1.0f), EnvMapWorldToLocal(nEnvMap)).xyz;
    float3 vReflectionCubemapLocal = mul(float4(vReflectionWs, 0.0f), EnvMapWorldToLocal(nEnvMap)).xyz;

    // Perform box projection to correct for parallax
    return CubeMapBoxProjection(
        vReflectionCubemapLocal,
        vPositionCubemapLocal,
        EnvMapBoxMins(nEnvMap),
        EnvMapBoxMaxs(nEnvMap),
        EnvMapFeathering(nEnvMap)
    );
}

#endif
// Copyright (c) Facepunch. All Rights Reserved.

// ProjectedShadowCube.hlsl:
// Handles projected cube shadow maps, this is specialized to keep both normal and cube projected shadows
// as simple and as efficient as possible.

#ifndef SHADOWS_PROJECTEDCUBESHADOW_HLSL
#define SHADOWS_PROJECTEDCUBESHADOW_HLSL

#include "Shadows/ShadowFiltering.hlsl"

struct ProjectedShadowCubeStruct
{
	float4x4 ShadowViewProjectionMatrices[6];
	float3 LightPosition;
	uint ShadowMapTextureCubeIndex;
	float InvShadowMapRes;
	float ShadowHardness;
};

// Per view
StructuredBuffer<ProjectedShadowCubeStruct> ProjectedCubeShadows < Attribute( "ProjectedCubeShadows" ); > ;

//------------------------------------------------------------------------------
// GetCubemapFace
//
// Determines the cubemap face index that corresponds to the given light direction.
//------------------------------------------------------------------------------
int GetCubemapFace( float3 lightDirection )
{
	float3 absLightDirection = abs( lightDirection );
	float maxCoord = max( absLightDirection.x, max( absLightDirection.y, absLightDirection.z ) );

	if ( maxCoord == absLightDirection.x )
	{
		return absLightDirection.x == lightDirection.x ? 0 : 1;
	}
	else if ( maxCoord == absLightDirection.y )
	{
		return absLightDirection.y == lightDirection.y ? 2 : 3;
	}
	else
	{
		return absLightDirection.z == lightDirection.z ? 4 : 5;
	}
}

//------------------------------------------------------------------------------
// ProjectedShadowCube
//
// Represents a cube-style shadow projection system. Provides utilities
// to query shadow visibility at a given world position for a specific shadow index.
//------------------------------------------------------------------------------
struct ProjectedShadowCube
{
    //--------------------------------------------------------------------------
    // GetVisibility
    //
    // Computes the shadow visibility factor for a given position in the world.
	// Using hardware PCF
	// Computes shadowing for a given world position from a cubemap shadowmap used on a point light.
    //
    // Parameters:
    // - shadowIndex: Index identifying which shadow source to use.
    // - worldPosition: The 3D position in world space for which to compute visibility.
    //
    // Returns:
    // A float value between 0.0 (fully in shadow) and 1.0 (fully lit).
    //--------------------------------------------------------------------------	
	static float3 GetOccludedPosition( uint shadowCubeIndex, float3 fragPos, float lightRadius )
	{
		if ( shadowCubeIndex == 0xFFFFFFFF )
			return fragPos;

		ProjectedShadowCubeStruct shadow = ProjectedCubeShadows[shadowCubeIndex];
		float3 toLight = shadow.LightPosition - fragPos;
		float3 lightDir = normalize( toLight );

		// Determine cube face and project fragment into shadow clip space
		int cubeFaceIndex = GetCubemapFace( lightDir );
		float4 shadowPosition = mul( float4( fragPos, 1 ), shadow.ShadowViewProjectionMatrices[cubeFaceIndex] );

		float s = Bindless::GetTextureCube( shadow.ShadowMapTextureCubeIndex ).SampleLevel( g_sPointClamp, lightDir, 0 ).r;

		// Reversed-Z linearize: view_z = R / ((R-1)*ndc+1)
		float flOccluderViewZ = lightRadius / ( ( lightRadius - 1.0f ) * s + 1.0f );

		// Occluder lies on the ray from light through fragment; clamp so occluder can't be beyond fragment
		return shadow.LightPosition + ( fragPos - shadow.LightPosition ) * min( flOccluderViewZ / shadowPosition.w, 1.0f );
	}

	static float GetVisibility( uint shadowCubeIndex, float3 worldPosition, float3 normalWs )
	{
		const uint InvalidShadowIndex = 0xFFFFFFFF;

		if ( shadowCubeIndex == InvalidShadowIndex )
		{
			return 1.0f;
		}

		ProjectedShadowCubeStruct shadow = ProjectedCubeShadows[shadowCubeIndex];
		
		worldPosition = ApplyShadowNormalOffset( worldPosition, normalWs, 2.0f * length( shadow.LightPosition - worldPosition ) * shadow.InvShadowMapRes, shadow.ShadowHardness );

		float3 worldToLight = shadow.LightPosition - worldPosition;
		float distance = length( worldToLight );

		float3 lightDirectionN = worldToLight / distance;

		float3 upReference = ( abs( lightDirectionN.z ) < 0.999f ) ? float3( 0.0f, 0.0f, 1.0f ) : float3( 0.0f, 1.0f, 0.0f );
		float3 sideVector = normalize( cross( lightDirectionN, upReference ) );
		float3 upVector = cross( sideVector, lightDirectionN );
		float hardness = shadow.ShadowHardness > 0.0f ? shadow.ShadowHardness : 1.0f;
		float filterScale = rcp( hardness );

		// bigger radius gives more blur
		sideVector *= shadow.InvShadowMapRes * filterScale;
		upVector *= shadow.InvShadowMapRes * filterScale;

		int cubeFaceIndex = GetCubemapFace( lightDirectionN );
		float4x4 shadowViewProj = shadow.ShadowViewProjectionMatrices[cubeFaceIndex];

		// Transform the Light-relative position into shadow space (The light shadow view is pre-translated wrt the main view)
		float4 shadowPosition = mul( float4( worldPosition, 1 ), shadowViewProj );

		float compareDistance = shadowPosition.z / shadowPosition.w;

		TextureCube shadowMapTextureCube = Bindless::GetTextureCube( shadow.ShadowMapTextureCubeIndex );

		float shadowVisibility = 0.0f;

		if ( UserShadowFilterQuality == 0 )
		{
			shadowVisibility = shadowMapTextureCube.SampleCmpLevelZero( ShadowDepthPCFSampler, lightDirectionN, compareDistance );
		}
		else
		{
			// 4, 6 or 8 taps on one disk (Bevy filters cubes with 8). Every tap is a hardware 2x2 compare, so few
			// are needed. Radii keep the RMS radius of the 5, 12 and 29-tap tables these replaced (2.5, 2.14 and
			// 2.04), scaled by sqrt(2) for the disk's own RMS radius
			uint nTaps = UserShadowFilterQuality == 1 ? 4 : UserShadowFilterQuality == 2 ? 6 : 8;
			float flRadius = UserShadowFilterQuality == 1 ? 3.5355 : UserShadowFilterQuality == 2 ? 3.0209 : 2.8811;

			ShadowDisk disk = ShadowDisk::Start( 0.0, nTaps );
			[unroll]
			for ( uint i = 0; i < nTaps; ++i )
			{
				float2 offset = disk.Next( i ) * flRadius;
				float3 samplePos = lightDirectionN + sideVector * offset.x + upVector * offset.y;
				shadowVisibility += shadowMapTextureCube.SampleCmpLevelZero(
					ShadowDepthPCFSampler,
					samplePos,
					compareDistance );
			}
			shadowVisibility /= nTaps;
		}

		// PCF is overly blurry, squaring gets us a tighter shadow
		shadowVisibility = shadowVisibility * shadowVisibility;

		return saturate( shadowVisibility );
	}

	// For callers that have no receiver normal at hand. The normal comes from screen-space derivatives,
	// so this is only valid in uniform control flow - from inside a per-light loop, use the overload above.
	static float GetVisibility( uint shadowCubeIndex, float3 worldPosition )
	{
		return GetVisibility( shadowCubeIndex, worldPosition, ComputeShadowReceiverNormal( worldPosition ) );
	}
}

#endif

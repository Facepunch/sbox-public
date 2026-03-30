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

// Poisson disc samples for cube filtering

static const float2 PCFDiscSamples5[] =
{
	float2( 0.000000, 2.500000 ),
	float2( 2.377641, 0.772542 ),
	float2( 1.469463, -2.022543 ),
	float2( -1.469463, -2.022542 ),
	float2( -2.377641, 0.772543 ),
};

static const float2 PCFDiscSamples12[] =
{
	float2( 0.000000, 2.500000 ),
	float2( 1.767767, 1.767767 ),
	float2( 2.500000, -0.000000 ),
	float2( 1.767767, -1.767767 ),
	float2( -0.000000, -2.500000 ),
	float2( -1.767767, -1.767767 ),
	float2( -2.500000, 0.000000 ),
	float2( -1.767766, 1.767768 ),
	float2( -1.006119, -0.396207 ),
	float2( 1.000015, 0.427335 ),
	float2( 0.416807, -1.006577 ),
	float2( -0.408872, 1.024430 ),
};

static const float2 PCFDiscSamples29[]=
{
	float2( 0.000000, 2.500000 ),
	float2( 1.016842, 2.283864 ),
	float2( 1.857862, 1.672826 ),
	float2( 2.377641, 0.772542 ),
	float2( 2.486305, -0.261321 ),
	float2( 2.165063, -1.250000 ),
	float2( 1.469463, -2.022543 ),
	float2( 0.519779, -2.445369 ),
	float2( -0.519779, -2.445369 ),
	float2( -1.469463, -2.022542 ),
	float2( -2.165064, -1.250000 ),
	float2( -2.486305, -0.261321 ),
	float2( -2.377641, 0.772543 ),
	float2( -1.857862, 1.672827 ),
	float2( -1.016841, 2.283864 ),
	float2( 0.091021, -0.642186 ),
	float2( 0.698035, 0.100940 ),
	float2( 0.959731, -1.169393 ),
	float2( -1.053880, 1.180380 ),
	float2( -1.479156, -0.606937 ),
	float2( -0.839488, -1.320002 ),
	float2( 1.438566, 0.705359 ),
	float2( 0.067064, -1.605197 ),
	float2( 0.728706, 1.344722 ),
	float2( 1.521424, -0.380184 ),
	float2( -0.199515, 1.590091 ),
	float2( -1.524323, 0.364010 ),
	float2( -0.692694, -0.086749 ),
	float2( -0.082476, 0.654088 ),
};

//------------------------------------------------------------------------------
// ProjectedShadowCube
//
// Represents a cube-style shadow projection system. Provides utilities
// to query shadow visibility at a given world position for a specific shadow index.
//------------------------------------------------------------------------------
class ProjectedShadowCube
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

	static float GetVisibility( uint shadowCubeIndex, float3 worldPosition )
	{
		const uint InvalidShadowIndex = 0xFFFFFFFF;

		if ( shadowCubeIndex == InvalidShadowIndex )
		{
			return 1.0f;
		}

		ProjectedShadowCubeStruct shadow = ProjectedCubeShadows[shadowCubeIndex];

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
		else if ( UserShadowFilterQuality == 1 )
		{
			[unroll]
			for ( int i = 0; i < 5; ++i )
			{
				float2 offset = PCFDiscSamples5[i];
				float3 samplePos = lightDirectionN + sideVector * offset.x + upVector * offset.y;
				shadowVisibility += shadowMapTextureCube.SampleCmpLevelZero(
					ShadowDepthPCFSampler,
					samplePos,
					compareDistance );
			}
			shadowVisibility /= 5.0f;
		}
		else if ( UserShadowFilterQuality == 2 )
		{
			[unroll]
			for ( int i = 0; i < 12; ++i )
			{
				float2 offset = PCFDiscSamples12[i];
				float3 samplePos = lightDirectionN + sideVector * offset.x + upVector * offset.y;
				shadowVisibility += shadowMapTextureCube.SampleCmpLevelZero(
					ShadowDepthPCFSampler,
					samplePos,
					compareDistance );
			}
			shadowVisibility /= 12.0f;
		}
		else
		{
			[unroll]
			for ( int i = 0; i < 29; ++i )
			{
				float2 offset = PCFDiscSamples29[i];
				float3 samplePos = lightDirectionN + sideVector * offset.x + upVector * offset.y;
				shadowVisibility += shadowMapTextureCube.SampleCmpLevelZero(
					ShadowDepthPCFSampler,
					samplePos,
					compareDistance );
			}
			shadowVisibility /= 29.0f;
		}

		// PCF is overly blurry, squaring gets us a tighter shadow
		shadowVisibility = shadowVisibility * shadowVisibility;

		return saturate( shadowVisibility );
	}
}

#endif

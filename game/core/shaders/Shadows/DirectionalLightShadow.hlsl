#ifndef DIRECTIONAL_LIGHT_SHADOW_HLSL
#define DIRECTIONAL_LIGHT_SHADOW_HLSL

#include "common/Bindless.hlsl"
#include "Shadows/ShadowFiltering.hlsl"

// I don't know
;

// don't have more than this for fucks sake
#define MAX_CASCADE_COUNT 4

cbuffer DirectionalLightCB
{
    float4 g_DirectionalLightColor; // w fogstrength
    float4 g_DirectionalLightDirection; // w blank

    float4x4 g_DirectionalLightWorldToShadowViewMatrices[MAX_CASCADE_COUNT];
	uint4 g_DirectionalLightShadowMapTextureIndex;
	uint g_DirectionalLightCascadeCount;
    float g_DirectionalLightInverseShadowMapSize;
	float2 g_DirectionalLightPadding;
    float4 g_DirectionalLightCascadeHardness;
    float4 g_DirectionalLightCascadeSpheres[MAX_CASCADE_COUNT]; // xyz = world center, w = radius squared
    float4 g_DirectionalLightShadowBias; // per-cascade depth bias, scaled by cascade size
};

int DirectionalLightDebug < Attribute( "DirectionalLightDebug" ); >;

static const float3 DebugColors[4] = {
    float3( 1.0f, 0.0f, 0.0f ),
    float3( 0.0f, 1.0f, 0.0f ),
    float3( 0.0f, 0.0f, 1.0f ),
    float3( 1.0f, 1.0f, 0.0f )
};

int FindCascade( float3 worldPosition, out float3 posLs )
{
	posLs = 0;
	[unroll]
	for ( int i = 0; i < MAX_CASCADE_COUNT; i++ )
	{
		if ( i >= g_DirectionalLightCascadeCount )
			break;

		float3 toCenter = worldPosition - g_DirectionalLightCascadeSpheres[i].xyz;
		if ( dot( toCenter, toCenter ) < g_DirectionalLightCascadeSpheres[i].w )
		{
			posLs = mul( g_DirectionalLightWorldToShadowViewMatrices[i], float4( worldPosition, 1 ) ).xyz;
			return i;
		}
	}
	return -1;
}

class DirectionalLightShadow
{
	static float SampleCascade( int cascadeIndex, float3 worldPosition, float2 screenPos )
    {
		float4x4 worldToShadow = g_DirectionalLightWorldToShadowViewMatrices[cascadeIndex];
		float3 positionLs = mul( worldToShadow, float4( worldPosition, 1.0f ) ).xyz;

        ShadowPCFInput pcfInput;
        pcfInput.ShadowMap = g_bindless_Texture2D[ NonUniformResourceIndex( g_DirectionalLightShadowMapTextureIndex[cascadeIndex] ) ];
        pcfInput.ShadowPos = positionLs;
        pcfInput.InvShadowMapRes = g_DirectionalLightInverseShadowMapSize;
        pcfInput.Bias = g_DirectionalLightShadowBias[cascadeIndex];
		pcfInput.Hardness = g_DirectionalLightCascadeHardness[cascadeIndex];
        pcfInput.ScreenPos = screenPos;

        return SampleShadowPCF( pcfInput );
    }

	static float3 GetOccludedPosition( float3 fragPos )
    {
        if ( g_DirectionalLightCascadeCount == 0 )
            return fragPos;

		float3 posLs;
		int cascade = FindCascade( fragPos, posLs );

		if ( cascade < 0 )
		{
			cascade = (int)g_DirectionalLightCascadeCount - 1;
			posLs = mul( g_DirectionalLightWorldToShadowViewMatrices[cascade], float4( fragPos, 1 ) ).xyz;
		}

		float s = g_bindless_Texture2D[ NonUniformResourceIndex( g_DirectionalLightShadowMapTextureIndex[cascade] ) ].SampleLevel( g_sPointClamp, posLs.xy, 0 ).r;

		// zGrad is the gradient of shadow-Z w.r.t. world position; 1/|zGrad| converts shadow-Z delta to world units
		// Reversed-Z: s > posLs.z when occluder is closer to light than fragment
		float3 zGrad = g_DirectionalLightWorldToShadowViewMatrices[cascade][2].xyz;
        return fragPos + zGrad * max( s - posLs.z, 0.0f ) / dot( zGrad, zGrad );
    }

    static float GetVisibility( float3 worldPosition, float2 screenPos )
    {
        if ( g_DirectionalLightCascadeCount == 0 )
            return 1.0f;

		float3 posLs;
		int cascade = FindCascade( worldPosition, posLs );

		if ( cascade < 0 )
			return 1.0f;

		return SampleCascade( cascade, worldPosition, screenPos );
    }

    static float3 GetDebugColor( float3 worldPosition )
    {
		for ( int i = 0; i < (int)g_DirectionalLightCascadeCount; i++ )
		{
			float3 toCenter = worldPosition - g_DirectionalLightCascadeSpheres[i].xyz;
			if ( dot( toCenter, toCenter ) < g_DirectionalLightCascadeSpheres[i].w )
				return DebugColors[i];
		}

        return float3( 0.0f, 0.0f, 0.0f );
    }
};

#endif
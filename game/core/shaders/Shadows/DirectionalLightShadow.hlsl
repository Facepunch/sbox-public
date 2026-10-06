#ifndef DIRECTIONAL_LIGHT_SHADOW_HLSL
#define DIRECTIONAL_LIGHT_SHADOW_HLSL

#include "common/Bindless.hlsl"
#include "Shadows/ShadowFiltering.hlsl"

// I don't know
;

#include "common/utils/MSAAUtils.hlsl"

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
	// Bindless index of the screen-space (contact) shadow mask, 0 if none.
	uint g_DirectionalLightScreenSpaceShadowIndex;
	bool g_DirectionalLightEnabled;
    float4 g_DirectionalLightCascadeHardness;
    float4 g_DirectionalLightCascadeSpheres[MAX_CASCADE_COUNT]; // xyz = world center, w = radius squared
    float4 g_DirectionalLightShadowBias; // per-cascade depth bias, scaled by cascade size
};

int DirectionalLightDebug < Attribute("DirectionalLightDebug"); >;
bool DisableScreenSpaceShadows < Attribute("DisableScreenSpaceShadows" ); Default( 0 ); > ;

static const float3 DebugColors[4] = {
    float3( 1.0f, 0.0f, 0.0f ),
    float3( 0.0f, 1.0f, 0.0f ),
    float3( 0.0f, 0.0f, 1.0f ),
    float3( 1.0f, 1.0f, 0.0f )
};

// Camera-relative position to shadow space. The large translation and camera offset are folded first to keep precision.
float3 CascadeShadowPosition( int cascadeIndex, float3 positionWithOffsetWs )
{
	float4x4 worldToShadow = g_DirectionalLightWorldToShadowViewMatrices[cascadeIndex];
	float3 origin = g_vHighPrecisionLightingOffsetWs.xyz;
	float3 translation = float3(
		worldToShadow[0].w + dot( worldToShadow[0].xyz, origin ),
		worldToShadow[1].w + dot( worldToShadow[1].xyz, origin ),
		worldToShadow[2].w + dot( worldToShadow[2].xyz, origin ) );

	return mul( (float3x3)worldToShadow, positionWithOffsetWs ) + translation;
}

int FindCascadeWithOffset( float3 positionWithOffsetWs, out float3 posLs )
{
	posLs = 0;
	[unroll]
	for ( int i = 0; i < MAX_CASCADE_COUNT; i++ )
	{
		if ( i >= g_DirectionalLightCascadeCount )
			break;

		float3 toCenter = positionWithOffsetWs - ( g_DirectionalLightCascadeSpheres[i].xyz - g_vHighPrecisionLightingOffsetWs.xyz );
		if ( dot( toCenter, toCenter ) < g_DirectionalLightCascadeSpheres[i].w )
		{
			posLs = CascadeShadowPosition( i, positionWithOffsetWs );
			return i;
		}
	}
	return -1;
}

// Absolute-position version, kept for existing callers.
int FindCascade( float3 worldPosition, out float3 posLs )
{
	return FindCascadeWithOffset( worldPosition - g_vHighPrecisionLightingOffsetWs.xyz, posLs );
}

struct DirectionalLightShadow
{
	// Camera-relative position (vPositionWithOffsetWs).
	static float SampleCascadeWithOffset( int cascadeIndex, float3 positionWithOffsetWs, float3 normalWs, float2 screenPos )
    {
		float4x4 worldToShadow = g_DirectionalLightWorldToShadowViewMatrices[cascadeIndex];

		positionWithOffsetWs = ApplyShadowNormalOffset( positionWithOffsetWs, normalWs, g_DirectionalLightInverseShadowMapSize / length( worldToShadow[0].xyz ), g_DirectionalLightCascadeHardness[cascadeIndex] );

        ShadowPCFInput pcfInput;
        pcfInput.ShadowMap = Bindless::GetTexture2D( g_DirectionalLightShadowMapTextureIndex[cascadeIndex] );
        pcfInput.ShadowPos = CascadeShadowPosition( cascadeIndex, positionWithOffsetWs );
        pcfInput.InvShadowMapRes = g_DirectionalLightInverseShadowMapSize;
        pcfInput.Bias = g_DirectionalLightShadowBias[cascadeIndex];
		pcfInput.Hardness = g_DirectionalLightCascadeHardness[cascadeIndex];
        pcfInput.ScreenPos = screenPos;

#ifndef FORCE_BILINEAR_PCF_SHADOWS_ONLY
        if ( UserShadowFilterQuality >= 3 )
            return SampleDirectionalShadowTent16( pcfInput );
#endif
        return SampleShadowPCF( pcfInput );
    }

	// Absolute-position versions, kept for existing callers.
	static float SampleCascade( int cascadeIndex, float3 worldPosition, float3 normalWs, float2 screenPos )
    {
		return SampleCascadeWithOffset( cascadeIndex, worldPosition - g_vHighPrecisionLightingOffsetWs.xyz, normalWs, screenPos );
    }

    // For callers that have no receiver normal at hand. The normal comes from screen-space derivatives,
    // so this is only valid in uniform control flow - from inside a per-light loop, use the overload above.
    static float SampleCascade( int cascadeIndex, float3 worldPosition, float2 screenPos )
    {
        return SampleCascade( cascadeIndex, worldPosition, ComputeShadowReceiverNormal( worldPosition ), screenPos );
    }

	// Screen-space (contact) shadows precomputed into a full-screen mask by the ScreenSpaceShadows
	// component. 1 = lit, 0 = shadowed. Returns 1 (no occlusion) when no mask is bound (index 0).
	// The mask is a non-MSAA full-res texture, so composite it with MSAAUtils::GetSampleIndex to pick
	// the depth-matching gather lane - this keeps the mask pixel-perfect under MSAA.
	static float SampleScreenSpaceShadow( float4 vPositionSs )
	{
		#if ( S_TRANSLUCENT == 1 || PROGRAM != VFX_PROGRAM_PS )
        	return 1.0f;
		#endif

		if ( DisableScreenSpaceShadows )
			return 1.0f;
		
		if ( g_DirectionalLightScreenSpaceShadowIndex == 0 )
			return 1.0f;

        Texture2D tMask = Bindless::GetTexture2D(g_DirectionalLightScreenSpaceShadowIndex);
        vPositionSs.xy -= g_vViewportOffset.xy;
		return MSAAUtils::SampleRed( tMask, vPositionSs );
	}

	static float3 GetOccludedPosition( float3 fragPos )
    {
        if ( g_DirectionalLightCascadeCount == 0 )
            return fragPos;

		float3 posLs;
		int cascade = FindCascadeWithOffset( fragPos - g_vHighPrecisionLightingOffsetWs.xyz, posLs );

		if ( cascade < 0 )
		{
			cascade = (int)g_DirectionalLightCascadeCount - 1;
			posLs = CascadeShadowPosition( cascade, fragPos - g_vHighPrecisionLightingOffsetWs.xyz );
		}

		float s = Bindless::GetTexture2D( g_DirectionalLightShadowMapTextureIndex[cascade] ).SampleLevel( g_sPointClamp, posLs.xy, 0 ).r;

		// zGrad is the gradient of shadow-Z w.r.t. world position; 1/|zGrad| converts shadow-Z delta to world units
		// Reversed-Z: s > posLs.z when occluder is closer to light than fragment
		float3 zGrad = g_DirectionalLightWorldToShadowViewMatrices[cascade][2].xyz;
        return fragPos + zGrad * max( s - posLs.z, 0.0f ) / dot( zGrad, zGrad );
    }

    static float GetVisibilityWithOffset( float3 positionWithOffsetWs, float3 normalWs, float4 vPositionSs )
    {
        float ssShadow = SampleScreenSpaceShadow( vPositionSs );

        if ( g_DirectionalLightCascadeCount == 0 )
            return ssShadow;

		float3 posLs;
		int cascade = FindCascadeWithOffset( positionWithOffsetWs, posLs );

		if ( cascade < 0 )
			return ssShadow;

		return SampleCascadeWithOffset( cascade, positionWithOffsetWs, normalWs, vPositionSs.xy ) * ssShadow;
    }

    // For callers that have no receiver normal at hand. The normal comes from screen-space derivatives,
    // so this is only valid in uniform control flow - from inside a per-light loop, use the overload above.
    static float GetVisibilityWithOffset( float3 positionWithOffsetWs, float4 vPositionSs )
    {
        return GetVisibilityWithOffset( positionWithOffsetWs, ComputeShadowReceiverNormal( positionWithOffsetWs ), vPositionSs );
    }

    static float GetVisibility( float3 worldPosition, float3 normalWs, float4 vPositionSs )
    {
        return GetVisibilityWithOffset( worldPosition - g_vHighPrecisionLightingOffsetWs.xyz, normalWs, vPositionSs );
    }

    static float GetVisibility( float3 worldPosition, float4 vPositionSs )
    {
        return GetVisibility( worldPosition, ComputeShadowReceiverNormal( worldPosition ), vPositionSs );
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

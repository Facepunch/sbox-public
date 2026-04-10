// Copyright (c) Facepunch. All Rights Reserved.

// ProjectedShadow.hlsl:
// Handles projected shadow maps.

#ifndef SHADOWS_PROJECTEDSHADOW_HLSL
#define SHADOWS_PROJECTEDSHADOW_HLSL

#include "Shadows/ShadowFiltering.hlsl"
#include "common/Bindless.hlsl"

struct ProjectedShadowStruct
{
	float4x4 WorldToShadowMatrix;
	uint ShadowMapTextureIndex;
	float InvShadowMapRes;
	float ShadowHardness;
};

StructuredBuffer<ProjectedShadowStruct> ProjectedShadows < Attribute( "ProjectedShadows" ); >;

class ProjectedShadow
{
    static float3 GetOccludedPosition( uint shadowIndex, float3 fragPos, float3 lightPos, float lightRadius )
    {
        if ( shadowIndex == 0xFFFFFFFF )
            return fragPos;

        ProjectedShadowStruct shadow = ProjectedShadows[shadowIndex];
        float4 sp = mul( float4( fragPos, 1.0f ), shadow.WorldToShadowMatrix );
        float s = Bindless::GetTexture2D( shadow.ShadowMapTextureIndex ).SampleLevel( g_sPointClamp, sp.xy / sp.w, 0 ).r;
        float flOccluderViewZ = lightRadius / ( ( lightRadius - 1.0f ) * s + 1.0f ); // reversed-Z linearize: view_z = R / ((R-1)*ndc+1)
        // Occluder lies on the ray from light through fragment: t = occluderViewZ / fragViewZ
        return lightPos + ( fragPos - lightPos ) * min( flOccluderViewZ / sp.w, 1.0f );
    }

    static float GetVisibility( uint shadowIndex, float3 worldPosition, float2 screenPos )
    {
        if ( shadowIndex == 0xFFFFFFFF )
            return 1.0f;

        ProjectedShadowStruct shadow = ProjectedShadows[shadowIndex];
        Texture2D shadowmap = Bindless::GetTexture2D( shadow.ShadowMapTextureIndex );

        float4 shadowPosition = mul( float4( worldPosition, 1.0f ), shadow.WorldToShadowMatrix );

        float3 positionTextureSpace = shadowPosition.xyz / shadowPosition.w;

        ShadowPCFInput pcfInput;
        pcfInput.ShadowMap = shadowmap;
        pcfInput.ShadowPos = positionTextureSpace;
        pcfInput.InvShadowMapRes = shadow.InvShadowMapRes;
        pcfInput.Bias = 0;
        pcfInput.Hardness = shadow.ShadowHardness;
        pcfInput.ScreenPos = screenPos;

		float shadowVisibility = SampleShadowPCF( pcfInput );

        // Square the result for a softer falloff
        return shadowVisibility * shadowVisibility;
    }
};

#endif

#ifndef SHADOWS_OBSOLETE_HLSL
#define SHADOWS_OBSOLETE_HLSL

float CalculateDistanceFalloff( float flDistToLightSq, float linearFalloff, float quadraticFalloff, float truncation, float flMinDistance )
{
	flDistToLightSq = max( flDistToLightSq, flMinDistance );
	
	float2 vLightDistAndLightDistSq = float2( sqrt( flDistToLightSq ), flDistToLightSq );
	
	float flDot = dot( vLightDistAndLightDistSq, float2( linearFalloff, quadraticFalloff ) );
	
	return saturate( 1.0 / flDot - truncation );
}

float3 Position3WsToShadowTextureSpace(float3 vPositionWs, float4x4 matWorldToShadow)
{
    float4 vPositionTextureSpace = mul(float4(vPositionWs.xyz, 1.0), matWorldToShadow);
    return vPositionTextureSpace.xyz / vPositionTextureSpace.w;
}


// STUB SHIT
bool InsideShadowRegion(float3 vPositionTextureSpace, float4 vSpotLightShadowBounds)
{
    return true;
}

float ComputeShadow(float3 vPositionTextureSpace)
{
	return 0.0f;
}

#endif
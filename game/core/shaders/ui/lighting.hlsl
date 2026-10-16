#ifndef UI_LIGHTING_HLSL
#define UI_LIGHTING_HLSL

#include "vr_lighting.fxc"

bool g_bWorldPanelLighting < Attribute( "WorldPanelLighting" ); Default( 0 ); >;

float4 UI_ApplyLighting( float4 positionPs, float4 color )
{
	#if D_WORLDPANEL
	if ( g_bWorldPanelLighting )
	{
		// Reconstruct from raster depth so screen-space path quads use the same surface as regular UI geometry.
		float2 uv = ( positionPs.xy - g_vViewportOffset ) * g_vInvViewportSize;
		float depth = RemapValClamped( positionPs.z, g_flViewportMinZ, g_flViewportMaxZ, 0.0, 1.0 );
		float4 position = mul( g_matProjectionToWorld, float4( uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0, depth, 1.0 ) );
		float3 positionRelative = position.xyz / position.w;

		FinalCombinerInput_t input = {};
		input.vPositionWs = positionRelative - g_vWorldToCameraOffset.xyz;
		input.vPositionWithOffsetWs = positionRelative - ( g_vWorldToCameraOffset.xyz + g_vHighPrecisionLightingOffsetWs.xyz );
		input.vPositionSs = positionPs;
		input.vPositionSs.w = position.w;
		input.vNormalWs = normalize( cross( ddy( positionRelative ), ddx( positionRelative ) ) );
		input.vRoughness = 1.0;

		LightingTerms_t lighting = InitLightingTerms();
		ComputeDirectLighting( lighting, input );
		CalculateIndirectLighting( lighting, input );
		color.rgb *= lighting.vDiffuse + lighting.vIndirectDiffuse;
	}
	#endif

	return color;
}

#endif

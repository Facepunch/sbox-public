#ifndef FOG_HLSL
#define FOG_HLSL

#include "volumetric_fog.fxc"
#include "vr_gradient_fog.fxc"
#include "vr_cubemap_fog.fxc"

//
// Public Fog Api.
//
struct Fog
{
	static float3 Apply( float3 worldPos, float2 screenPos, float3 color )
	{
		#if PROGRAM != VFX_PROGRAM_CS
			const float3 vPositionToCameraWs = worldPos.xyz - g_vCameraPositionWs;

			color = ApplyGradientFog(color, worldPos.xyz, vPositionToCameraWs.xyz);
			color = ApplyCubemapFog(color, worldPos.xyz, vPositionToCameraWs.xyz);
			color = ApplyVolumetricFog(color, worldPos.xyz, screenPos.xy);
		#endif

		return color;
	}

};

#endif //FOG_HLSL
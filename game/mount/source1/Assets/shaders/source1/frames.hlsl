#ifndef SOURCE1_FRAMES_HLSL
#define SOURCE1_FRAMES_HLSL
#include "source1/proxies.hlsl"

StaticCombo( S_SOURCE_TEXTURE_ANIMATION, F_SOURCE_TEXTURE_ANIMATION, Sys( ALL ) );

#if S_SOURCE_TEXTURE_ANIMATION
	float4 g_vSourceTextureAnimation < Default4( 0, 1, 0, 0 ); >;

	float3 SourceFrameUv( float2 uv, float frame )
	{
		return float3( uv, frame );
	}
#else
	float2 SourceFrameUv( float2 uv, float frame )
	{
		return uv;
	}
#endif

float SourceFrame()
{
	#if S_SOURCE_TEXTURE_ANIMATION
		return SourceProxyScalar( fmod( floor( max( g_flTime, 0 ) * g_vSourceTextureAnimation.x ), g_vSourceTextureAnimation.y ), g_flSourceFrameProxy );
	#else
		return 0;
	#endif
}

#endif

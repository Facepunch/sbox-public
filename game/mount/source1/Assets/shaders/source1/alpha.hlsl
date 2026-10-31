#ifndef SOURCE1_ALPHA_HLSL
#define SOURCE1_ALPHA_HLSL
#include "source1/proxies.hlsl"

StaticCombo( S_SOURCE_ALPHA_SINE, F_SOURCE_ALPHA_SINE, Sys( ALL ) );

float g_flSourceAlpha < Default( 1 ); >;
#if S_SOURCE_ALPHA_SINE
	float4 g_vSourceAlphaSine < Default4( 1, 0, 0, 1 ); >;
#endif

float SourceAlpha()
{
	#if S_SOURCE_ALPHA_SINE
		float phase = fmod( g_flTime - g_vSourceAlphaSine.y, g_vSourceAlphaSine.x ) / g_vSourceAlphaSine.x;
		float wave = sin( phase * 6.283185307179586 ) * 0.5 + 0.5;
		return saturate( (g_vSourceAlphaSine.w - g_vSourceAlphaSine.z) * wave + g_vSourceAlphaSine.z );
	#else
		return saturate( SourceProxyScalar( g_flSourceAlpha, g_flSourceAlphaProxy ) );
	#endif
}

#endif

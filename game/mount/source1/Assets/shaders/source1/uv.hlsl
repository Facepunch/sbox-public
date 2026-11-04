#ifndef SOURCE1_UV_HLSL
#define SOURCE1_UV_HLSL
#include "source1/proxies.hlsl"

float2 SourceUv( float2 uv, float3 rowU, float3 rowV, float3 scroll, float3 proxyU = float3( -1, -1, -1 ), float3 proxyV = float3( -1, -1, -1 ) )
{
	rowU = SourceProxyVector( rowU, proxyU );
	rowV = SourceProxyVector( rowV, proxyV );
	float3 coordinate = float3( uv, 1 );
	float2 offset = scroll.xy * g_flTime;
	offset += offset < 0 ? 1 - trunc( offset ) : 0;
	return float2( dot( coordinate, rowU ), dot( coordinate, rowV ) ) + frac( offset ) * scroll.z;
}

#endif

#ifndef SOURCE1_PROXIES_HLSL
#define SOURCE1_PROXIES_HLSL

StaticCombo( S_SOURCE_PROXIES, F_SOURCE_PROXIES, Sys( ALL ) );

#if S_SOURCE_PROXIES
CreateInputTexture2D( SourceProxyProgram, Linear, 32, "None", "", "Source 1", Default4( 0, 0, 0, 0 ) );
Texture2D g_tSourceProxyProgram < Channel( RGBA, Box( SourceProxyProgram ), Linear ); OutputFormat( RGBA32323232F ); SrgbRead( false ); >;
float g_nSourceProxyCount < Default( 0 ); >;
static float g_SourceProxyValues[128];
static bool g_bSourceProxiesEvaluated = false;

void SourceEvaluateProxies()
{
	if ( g_bSourceProxiesEvaluated ) return;
	g_bSourceProxiesEvaluated = true;
	[loop]
	for ( int index = 0; index < (int)g_nSourceProxyCount; ++index )
	{
		float4 instruction = g_tSourceProxyProgram.Load( int3( 0, index, 0 ) );
		int operation = (int)instruction.x;
		float a = operation > 1 ? g_SourceProxyValues[(int)instruction.y] : 0;
		float b = operation > 1 ? g_SourceProxyValues[(int)instruction.z] : 0;
		float c = operation > 1 ? g_SourceProxyValues[(int)instruction.w] : 0;
		float value = 0;
		switch ( operation )
		{
			case 0: value = instruction.y; break;
			case 1: value = g_flTime; break;
			case 2: value = a + b; break;
			case 3: value = a - b; break;
			case 4: value = a * b; break;
			case 5: value = b == 0 ? a : a / b; break;
			case 6: value = abs( a ); break;
			case 7: value = a - trunc( a ); break;
			case 8: value = trunc( a ); break;
			case 9: value = sin( a ); break;
			case 10: value = cos( a ); break;
			case 11: value = min( a, b ); break;
			case 12: value = max( a, b ); break;
			case 13: value = a <= b ? 1 : 0; break;
			case 14: value = a != 0 ? b : c; break;
			case 15: value = exp( a ); break;
			case 16: value = pow( a, b ); break;
			case 17: value = a / b; break;
			case 18: value = fmod( a, b ); break;
		}
		g_SourceProxyValues[index] = value;
	}
}
#endif

float SourceProxyScalar( float value, float index )
{
	#if S_SOURCE_PROXIES
		if ( index >= 0 )
		{
			SourceEvaluateProxies();
			return g_SourceProxyValues[(int)index];
		}
	#endif
	return value;
}

float3 SourceProxyVector( float3 value, float3 indices )
{
	return float3( SourceProxyScalar( value.x, indices.x ), SourceProxyScalar( value.y, indices.y ), SourceProxyScalar( value.z, indices.z ) );
}

float g_flSourceAlphaProxy < Default( -1 ); >;
float g_flSourceFrameProxy < Default( -1 ); >;
float g_flSourceDetailBlendProxy < Default( -1 ); >;
float3 g_vSourceColorTintProxy < Default3( -1, -1, -1 ); >;
float3 g_vSourceEnvTintProxy < Default3( -1, -1, -1 ); >;
float3 g_vSourceSelfIllumTintProxy < Default3( -1, -1, -1 ); >;
float3 g_vSourceBaseProxyU < Default3( -1, -1, -1 ); >;
float3 g_vSourceBaseProxyV < Default3( -1, -1, -1 ); >;
float3 g_vSourceBase2ProxyU < Default3( -1, -1, -1 ); >;
float3 g_vSourceBase2ProxyV < Default3( -1, -1, -1 ); >;
float3 g_vSourceNormalProxyU < Default3( -1, -1, -1 ); >;
float3 g_vSourceNormalProxyV < Default3( -1, -1, -1 ); >;
float3 g_vSourceNormal2ProxyU < Default3( -1, -1, -1 ); >;
float3 g_vSourceNormal2ProxyV < Default3( -1, -1, -1 ); >;
float3 g_vSourceDetailProxyU < Default3( -1, -1, -1 ); >;
float3 g_vSourceDetailProxyV < Default3( -1, -1, -1 ); >;
float3 g_vSourceEnvMaskProxyU < Default3( -1, -1, -1 ); >;
float3 g_vSourceEnvMaskProxyV < Default3( -1, -1, -1 ); >;
float3 g_vSourceBlendProxyU < Default3( -1, -1, -1 ); >;
float3 g_vSourceBlendProxyV < Default3( -1, -1, -1 ); >;
float3 g_vSourceTexture2ProxyU < Default3( -1, -1, -1 ); >;
float3 g_vSourceTexture2ProxyV < Default3( -1, -1, -1 ); >;

#endif

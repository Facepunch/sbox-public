#include "common/vertex.hlsl"
#include "source1/alpha.hlsl"
#include "source1/frames.hlsl"
float g_flSourceWarpParam < Default( 0 ); >;
float3 g_vSourceWarpOrigin < Default3( 0, 0, 0 ); >;
float g_flSourceWarpEntityOrigin < Default( 0 ); >;

PixelInput MainVs( VertexInput v )
{
	PixelInput i = ProcessVertex( v );
	#if S_SOURCE_VORTWARP
	{
		float3 position = i.vPositionWs;
		float3 origin = lerp( g_vSourceWarpOrigin, mul( GetTransformMatrix( v.nInstanceTransformID ), float4( 0, 0, 0, 1 ) ).xyz, g_flSourceWarpEntityOrigin );
		float height = saturate( position.z / 72 );
		float spread = 3 * height * (1 - height) * (1 - height);
		float3 delta = (position - origin) * float3( spread, spread, 1 );
		delta.z += 5 + 5 * sin( position.z );
		float amount = pow( saturate( 1 - g_flSourceWarpParam ), 8 );
		i.vPositionWs = lerp( position, delta + origin, amount );
		i.vPositionPs = Position3WsToPs( i.vPositionWs );
	}
	#endif
	i.vSourceColor = v.vSourceColor;
	i.flSourceBlend = v.vSourceBlend.r;
	i.flSourceAlpha = SourceAlpha();
	i.flSourceFrame = SourceFrame();
	i.vSourcePropLighting = v.vSourcePropLighting;
	float3 normal = normalize( i.vNormalWs );
	i.vSourceSeamlessWeights = normal * normal;
	return FinalizeVertex( i );
}

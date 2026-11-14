// Compute writing a texture through the bindless storage table: moving rings.

#define COMPUTE
#include "bindless.hlsli"

struct Constants
{
	uint Target;
	float Time;
	uint2 Size;
};

[[vk::push_constant]] Constants g_Constants;

[numthreads( 8, 8, 1 )]
void MainCs( uint3 id : SV_DispatchThreadID )
{
	if ( any( id.xy >= g_Constants.Size ) ) return;

	float2 uv = ( id.xy + 0.5 ) / g_Constants.Size;
	float d = length( uv - 0.5 );
	float rings = 0.5 + 0.5 * sin( d * 40 - g_Constants.Time * 4 );
	float3 color = lerp( float3( 0.9, 0.5, 0.1 ), float3( 0.1, 0.3, 0.9 ), rings );
	color *= ( ( id.x / 16 + id.y / 16 ) & 1 ) ? 1 : 0.8;
	g_bindless_RWTexture2D[g_Constants.Target][id.xy] = float4( color, 1 );
}

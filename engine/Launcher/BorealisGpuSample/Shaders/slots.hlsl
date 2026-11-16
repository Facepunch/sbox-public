// Compute reading two bindless texture slots and writing their sum through the bindless storage table: what a draw does
// with a texture index it was never given (slot 0, which s&box's shaders read as none) or kept after the texture was freed.

#define COMPUTE
#include "bindless.hlsli"

struct Constants
{
	uint Target;
	uint First;
	uint Second;
	uint Pad;
};

[[vk::push_constant]] Constants g_Constants;

[numthreads( 1, 1, 1 )]
void MainCs()
{
	float4 first = g_bindless_Texture2D[NonUniformResourceIndex( g_Constants.First )].Load( int3( 0, 0, 0 ) );
	float4 second = g_bindless_Texture2D[NonUniformResourceIndex( g_Constants.Second )].Load( int3( 0, 0, 0 ) );
	g_bindless_RWTexture2D[g_Constants.Target][uint2( 0, 0 )] = first + second;
}

#define CUSTOM_MATERIAL_INPUTS
#ifndef S_SOURCE_MONITOR
	#define S_SOURCE_MONITOR 0
#endif
#ifndef S_SOURCE_WORLD_TWO_TEXTURE
	#define S_SOURCE_WORLD_TWO_TEXTURE 0
#endif
#define S_UV2 1
StaticCombo( S_RENDER_BACKFACES, F_RENDER_BACKFACES, Sys( ALL ) );
#include "common/shared.hlsl"

float g_flSourceSeamlessScale < Default( 0 ); >;

struct VertexInput
{
	#include "common/vertexinput.hlsl"
	float4 vSourceColor : COLOR0 < Semantic( Color ); >;
	float4 vSourceBlend : TEXCOORD4 < Semantic( VertexPaintBlendParams ); >;
	float3 vSourcePropLighting : TEXCOORD5 < Semantic( None ); >;
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float4 vSourceColor : COLOR0;
	float flSourceBlend : TEXCOORD14;
	nointerpolation float flSourceAlpha : TEXCOORD15;
	nointerpolation float flSourceFrame : TEXCOORD16;
	float3 vSourcePropLighting : TEXCOORD17;
	float3 vSourceSeamlessWeights : TEXCOORD18;
};

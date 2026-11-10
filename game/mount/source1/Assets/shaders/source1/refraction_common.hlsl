#define S_TRANSLUCENT 1
#define CUSTOM_MATERIAL_INPUTS
#include "common/shared.hlsl"

struct VertexInput
{
	#include "common/vertexinput.hlsl"
	float4 vSourceColor : COLOR0 < Semantic( Color ); >;
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float4 vSourceColor : COLOR0;
	nointerpolation float flSourceAlpha : TEXCOORD15;
	nointerpolation float flSourceFrame : TEXCOORD16;
	nointerpolation float3 vSourceOriginWs : TEXCOORD17;
};

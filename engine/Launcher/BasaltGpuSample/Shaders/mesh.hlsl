// A textured, lit mesh: position, normal and uv streams, its texture and sampler from the bindless table.

#include "bindless.hlsli"

// 128 bytes: all the push constants there are
struct Constants
{
	float4x4 ObjectToProjection;
	float4 NormalRows[3];
	uint Texture;
	uint Sampler;
	float2 Pad;
};

[[vk::push_constant]] Constants g_Constants;

struct VsInput
{
	[[vk::location( 0 )]] float3 Position : POSITION;
	[[vk::location( 1 )]] float3 Normal : NORMAL;
	[[vk::location( 2 )]] float2 Uv : TEXCOORD0;
};

struct VsOutput
{
	float4 Position : SV_Position;
	float3 Normal : NORMAL;
	float2 Uv : TEXCOORD0;
};

VsOutput MainVs( VsInput i )
{
	VsOutput o;
	// Row-vector matrices from C# read as their transpose here, so they multiply on the left
	o.Position = mul( g_Constants.ObjectToProjection, float4( i.Position, 1 ) );
	o.Normal = i.Normal.x * g_Constants.NormalRows[0].xyz + i.Normal.y * g_Constants.NormalRows[1].xyz + i.Normal.z * g_Constants.NormalRows[2].xyz;
	o.Uv = i.Uv;
	return o;
}

float4 MainPs( VsOutput i ) : SV_Target0
{
	float3 albedo = g_bindless_Texture2D[g_Constants.Texture].Sample( g_bindless_Sampler[g_Constants.Sampler], i.Uv ).rgb;
	float3 sun = normalize( float3( 0.4, 0.8, 0.5 ) );
	float light = saturate( dot( normalize( i.Normal ), sun ) ) * 0.85 + 0.15;
	return float4( albedo * light, 1 );
}

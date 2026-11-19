// scene_renderer_mesh: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: CompileTargets = ( IS_SM_50 && ( PC || VULKAN ) );
// HEADER: Description = "Managed scene renderer - default mesh shader. Fixed directional light, no material inputs.";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#include "sbox/pass.slangh"

Mode( Default );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "system.fxc" // This should always be the first include in COMMON
#include "common.fxc"

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct VS_INPUT
{
	float3 vPositionOs : POSITION < Semantic( PosXyz ); >;
	// Packed or not, depending on the model - DecompressNormal reads UncompressedTangentFrame
	float4 vNormalOs : NORMAL < Semantic( OptionallyCompressedTangentFrame ); >;

	// Instance i of a draw reads transform slot first + i - see TransformBuffer
	uint nInstanceTransformID : TEXCOORD13 < Semantic( InstanceTransformUv ); >;
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_INPUT
{
	float3 vNormalWs : TEXCOORD0;
	float4 vColor : COLOR0;

	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs : SV_Position;
	#endif
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_VS )
#include "instancing.fxc"
#include "vs_decompress.fxc"

[shader( "vertex" )] PS_INPUT MainVs( const VS_INPUT i )
{
	PS_INPUT o;

	float3x4 matObjectToWorld = GetTransformMatrix( i.nInstanceTransformID );
	float3 vPositionWs = mul( matObjectToWorld, float4( i.vPositionOs.xyz, 1.0 ) );
	o.vPositionPs = Position3WsToPs( vPositionWs );
	float3 vNormalOs;
	DecompressNormal( i.vNormalOs, vNormalOs );
	o.vNormalWs = mul( (float3x3)matObjectToWorld, vNormalOs );
	o.vColor = GetExtraPerInstanceShaderData( i.nInstanceTransformID ).vTint;

	return o;
}

#endif // VS

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_PS )
// Reverse depth - the near plane is 1
RenderState( DepthEnable, true );
RenderState( DepthWriteEnable, true );
RenderState( DepthFunc, GREATER_EQUAL );
RenderState( CullMode, NONE );

struct PS_OUTPUT
{
	float4 vColor : SV_Target0;
};

[shader( "pixel" )] PS_OUTPUT MainPs( PS_INPUT i )
{
	PS_OUTPUT o;

	float3 vNormalWs = normalize( i.vNormalWs );

	// Tint each face by its axis so a cube reads as a cube without any lighting
	float3 vAxisColor = abs( vNormalWs ) * 0.5 + 0.5;
	float flLight = saturate( dot( vNormalWs, normalize( float3( 0.4, 0.3, 0.85 ) ) ) ) * 0.75 + 0.25;

	o.vColor = float4( i.vColor.rgb * vAxisColor * flLight, 1.0 );
	return o;
}

#endif // PS


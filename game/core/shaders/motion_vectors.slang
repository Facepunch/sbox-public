// motion_vectors: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md). The .shader
// beside it is only Source 2's renderer's.
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "Reconstructs static motion vectors from depth buffer and previous frame matrices";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#include "sbox/pass.slangh"

Mode( Forward );
Mode( Default );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// We want absolute motion vectors even without viewmodel correction
#define ABSOLUTE_MOTION 1

#include "system.fxc"
#include "common.fxc"
#include "sbox_shared.fxc"

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_VS )
struct VS_INPUT
{
	float3 vPositionOs : POSITION < Semantic( PosXyz ); >;
	float2 vTexCoord : TEXCOORD0 < Semantic( LowPrecisionUv ); >;
};

struct VS_OUTPUT
{
	float4 vPositionPs : SV_Position;
};

[shader( "vertex" )] VS_OUTPUT MainVs( VS_INPUT i )
{
	VS_OUTPUT o;
	o.vPositionPs = float4( i.vPositionOs.xyz, 1.0 );
	return o;
}

#endif // VS

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_PS )
RenderState( DepthWriteEnable, false );
RenderState( DepthEnable, false );

#include "common/classes/Depth.hlsl"
#include "common/classes/Motion.hlsl"

[shader( "pixel" )] float4 MainPs( float4 vPositionSs : SV_Position ) : SV_Target0
{
	float3 vPrevScreenPos = Motion::Get( vPositionSs.xy );
	float2 vMotionVector = vPrevScreenPos.xy - vPositionSs.xy;
	return float4( vMotionVector, Depth::Get( vPositionSs.xy ), 0.0 );
}

#endif // PS


// scene_renderer_depth_chain: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "Managed scene renderer - the depth chain drawn as bands of linear depth, so a camera's command list can put it in a frame both renderers draw (SceneLab).";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#include "sbox/pass.slangh"

Mode( Forward );
Mode( Default );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "system.fxc" // This should always be the first include in COMMON

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_VS )
#include "common.fxc"

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
#include "common.fxc"
#include "sbox/math.slangh"
#include "common/classes/Depth.hlsl"

RenderState( DepthEnable, false );
RenderState( DepthWriteEnable, false );
RenderState( CullMode, NONE );

// World units per band
float BandSize < Attribute( "BandSize" ); Default( 40.0 ); >;

[shader( "pixel" )] float4 MainPs( float4 vPositionPs : SV_Position ) : SV_Target0
{
	// Bands of linear depth: a depth difference of a few units shows as a jump in colour
	float flDepth = Depth::GetLinear( vPositionPs.xy );
	return float4( frac( flDepth / BandSize ), frac( flDepth / ( BandSize * 5.0 ) ), 0.25, 1.0 );
}

#endif // PS


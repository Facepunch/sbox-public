// reactive_mask_debug: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "Visualize FSR3 reactive mask as red overlay";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#include "sbox/pass.slangh"

Mode( Forward );
Mode( Default );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "system.fxc"
#include "common.fxc"

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

Texture2D<float> g_tReactiveMask < Attribute( "ReactiveMask" ); SrgbRead( false ); >;

[shader( "pixel" )] float4 MainPs( float4 vPositionSs : SV_Position ) : SV_Target0
{
	float flReactive = g_tReactiveMask.Load( int3( int2( vPositionSs.xy ), 0 ) ).r;

	// Reactive pixels shown as red, non-reactive as dark grey scene silhouette
	float3 color = lerp( float3( 0.1, 0.1, 0.1 ), float3( 1.0, 0.0, 0.0 ), saturate( flReactive ) );
	return float4( color, 1.0 );
}

#endif // PS


// borealis_gpu_driven_figure: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "Borealis GPU-driven debug figure (borealislab -capture-visibility): replays a forward pass's multi-draws with pulled positions, writing a count per fragment, a flat colour per triangle, or edges.";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#include "sbox/pass.slangh"

Mode( Default );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "system.fxc" // This should always be the first include in COMMON
#include "vr_common.fxc"
#include "math_general.fxc"

cbuffer BorealisGpuDrivenFigure_t
{
	uint g_nFigureMode;	// 0: a count per fragment, 1: a flat colour per triangle, 2: dark edges, 3: faint additive edges
	uint g_nFigurePad0;
	uint g_nFigurePad1;
	uint g_nFigurePad2;
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_INPUT
{
	// The triangle, by its instance and first (provoking) vertex; flat
	nointerpolation uint nTriangle : TEXCOORD0;

	#if ( PROGRAM != VFX_PROGRAM_PS )
		noperspective float4 vPositionPs : SV_Position;
	#endif
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_VS )
#include "instancing.fxc"
#include "borealis_gpu_driven.fxc"

[shader( "vertex" )] PS_INPUT MainVs( uint nVertex : SV_VulkanVertexID, uint nInstance : SV_VulkanInstanceID )
{
	PS_INPUT o;
	uint2 vInstance = BorealisGpuDrivenInstance( nInstance );
	uint4 vDraw = BorealisGpuDrivenDraw( vInstance.y );
	float3 vPositionOs = asfloat( BorealisGpuDrivenFetch( vDraw.x, 0, nVertex, true ).xyz );

	float3x4 matObjectToWorld = GetTransformMatrix( vInstance.x, 0 );
	float3 vPositionWs = mul( matObjectToWorld, float4( vPositionOs.xyz, 1.0 ) );
	o.vPositionPs.xyzw = Position3WsToPs( vPositionWs.xyz );
	o.nTriangle = vInstance.x * 0x9E3779B1u ^ nVertex * 0x85EBCA77u;
	return o;
}

#endif // VS

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_PS )
RenderState( DepthEnable, false );
RenderState( DepthWriteEnable, false );

[shader( "pixel" )] float4 MainPs( PS_INPUT i ) : SV_Target0
{
	if ( g_nFigureMode == 0 )
		return 1.0;
	if ( g_nFigureMode == 2 )
		return float4( 0.02, 0.02, 0.03, 1.0 );
	if ( g_nFigureMode == 3 )
		return float4( 0.05, 0.16, 0.22, 1.0 );

	// A bright, saturated colour per triangle
	uint h = i.nTriangle;
	h ^= h >> 16;
	h *= 0x7feb352du;
	h ^= h >> 15;
	h *= 0x846ca68bu;
	h ^= h >> 16;
	float flHue = ( h & 0xffff ) / 65535.0;
	float3 vRgb = saturate( abs( frac( flHue + float3( 0.0, 2.0 / 3.0, 1.0 / 3.0 ) ) * 6.0 - 3.0 ) - 1.0 );
	float flValue = 0.55 + 0.45 * ( ( h >> 16 ) & 0xff ) / 255.0;
	return float4( lerp( float3( 1, 1, 1 ), vRgb, 0.75 ) * flValue, 1.0 );
}

#endif // PS


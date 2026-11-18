// reactive_mask_generate.shader as a pass module: Borealis.Gpu draws this; the .shader stays for the other routes
// and as the fallback (docs/managed/shader-format-replacement.md, "Pass modules").
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "Converts reactive depth buffer into R16F mask for FSR3";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// MODES: Forward();
// MODES: Default();

#include "sbox/pass.slangh"

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

Texture2D<float> g_tReactiveDepth < Attribute( "ReactiveDepth" ); SrgbRead( false ); >;

// Depths over this are reactive: 0.001, anything drawn into the reactive depth, or the overlays' range of a frame's own depth
float g_flReactiveDepthThreshold < Attribute( "ReactiveDepthThreshold" ); Default( 0.001 ); >;

[shader( "pixel" )] float4 MainPs( float4 vPositionSs : SV_Position ) : SV_Target0
{
	float flDepth = g_tReactiveDepth.Load( int3( int2( vPositionSs.xy ), 0 ) ).r;

	// Reverse-Z: clear = 0.0 (far). Any depth over the threshold means a reactive object was rendered here.
	float flReactive = flDepth > g_flReactiveDepthThreshold ? 1.0 : 0.0;
	return float4( flReactive, 0, 0, 0 );
}

#endif // PS


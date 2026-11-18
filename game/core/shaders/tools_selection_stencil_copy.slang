// tools_selection_stencil_copy: a standalone module, the whole shader for Borealis.Gpu (docs/managed/standalone-modules.md). The .shader
// beside it is only Source 2's renderer's.
//===================== Copyright (c) Valve Corporation. All Rights Reserved. ======================
//
//==================================================================================================

//--------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "Shader used to write solid colors based on stencil results";

//--------------------------------------------------------------------------------------------------

#include "sbox/pass.slangh"

Mode( Default );

//--------------------------------------------------------------------------------------------------
//Feature( F_COLOR_CHANNEL, 0..3 ( 0="Red", 1="Green", 2="Blue", 3="Alpha" ), "Color channel" );

//--------------------------------------------------------------------------------------------------
#include "system.fxc" // This should always be the first include in COMMON
#include "common.fxc"
#include "math_general.fxc"

//--------------------------------------------------------------------------------------------------
struct VS_INPUT
{
	float3 vPositionSs : POSITION < Semantic( PosXyz ); >;
};

//--------------------------------------------------------------------------------------------------
struct PS_INPUT
{
	// VS only
	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs			: SV_Position;
	#endif

	// PS only
	#if ( PROGRAM == VFX_PROGRAM_PS )
		float4 vPositionSs			: SV_Position;
	#endif
};

//--------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_VS )
// Main -------------------------------------------------------------------
[shader( "vertex" )] PS_INPUT MainVs( const VS_INPUT i )
{
	PS_INPUT o;

	o.vPositionPs.xyzw = float4( i.vPositionSs, 1.0 );

	return o;
}

#endif // VS

//--------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_PS )
//StaticCombo( S_COLOR_CHANNEL, F_COLOR_CHANNEL, Sys( ALL ) );

DynamicCombo( D_COLOR_CHANNEL, 0..3, Sys( ALL ) );

// Render State -----------------------------------------------------------

#if ( D_COLOR_CHANNEL == 0 )
	RenderState( ColorWriteEnable0, R );
#elif ( D_COLOR_CHANNEL == 1 )
	RenderState( ColorWriteEnable0, G );
#elif ( D_COLOR_CHANNEL == 2 )
	RenderState( ColorWriteEnable0, B );
#elif ( D_COLOR_CHANNEL == 3 )
	RenderState( ColorWriteEnable0, A );
#endif

RenderState( DepthEnable, false );
RenderState( DepthWriteEnable, false );
RenderState( DepthFunc, ALWAYS );
RenderState( BlendEnable, false );

// Main -------------------------------------------------------------------
struct PS_OUTPUT
{
	float4 vColor : SV_Target0;
};

[shader( "pixel" )] PS_OUTPUT MainPs( PS_INPUT i )
{
	PS_OUTPUT o;
	o.vColor = float4( 1.0, 1.0, 1.0, 1.0 );
	return o;
}

#endif // PS


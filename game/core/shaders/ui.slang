// ui.shader as a pass module: Borealis.Gpu draws this; the .shader stays for the other routes
// and as the fallback (docs/managed/shader-format-replacement.md, "Pass modules").
// Copyright (c) Valve Corporation, All rights reserved. ======================================================================================================

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: CompileTargets = ( IS_SM_50 && ( PC || VULKAN ) );
// HEADER: Description = "Used to draw UI elements";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// MODES: Default();

#include "sbox/pass.slangh"

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// !!! DO NOT INCLUDE ANY OTHER FILES IN THIS SHADER !!!
#include "system.fxc"
// !!! DO NOT INCLUDE ANY OTHER FILES IN THIS SHADER !!!

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct VS_INPUT
{
	float2 vPositionSs			: POSITION < Semantic( PosXyz ); >;
	float4 vColor				: COLOR0 < Semantic( Color ); >;
	float2 vTexCoord			: TEXCOORD0 < Semantic( LowPrecisionUv ); >;
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_INPUT
{
	float4 vColor				: COLOR0;
	float2 vTexCoord			: TEXCOORD0;

	// VS only
	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs	: SV_Position;
	#endif
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_VS )
	// Includes -----------------------------------------------------------------------------------------------------------------------------------------------
	#include "math_general.fxc"
	#include "vr_common.fxc"
	#define IS_SPRITECARD 1 // So GetViewSubviewAndAdjustInstanceId() doesn't cause compile error
	#include "vr_lighting.fxc"

	// Constants ----------------------------------------------------------------------------------------------------------------------------------------------
	float4 g_vViewport < Source( Viewport ); >;

	BoolAttribute( ui, true );
 	BoolAttribute( ScreenSpaceVertices, true );

	// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
	[shader( "vertex" )] PS_INPUT MainVs( VS_INPUT i )
	{
		PS_INPUT o;

		float4 vViewport = g_vViewport;
		float2 vPositionSs = i.vPositionSs;

		o.vPositionPs.xy = 2.0 * ( vPositionSs.xy - vViewport.xy ) / ( vViewport.zw ) - float2( 1.0, 1.0 );
		o.vPositionPs.y *= -1.0;
		o.vPositionPs.z = 1.0;
		o.vPositionPs.w = 1.0;

		o.vColor.rgb = SrgbGammaToLinear( i.vColor.rgb );
		o.vColor.a = i.vColor.a;

		o.vTexCoord.xy = i.vTexCoord.xy;

		return o;
	}

#endif // VS

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_PS )
Texture2D g_tColor < Attribute( "VguiTexture" ); SrgbRead( true ); >;

// Render State -------------------------------------------------------------------------------------------------------------------------------------------

// Always alpha blend
RenderState( BlendEnable, true );
RenderState( SrcBlend, SRC_ALPHA );
RenderState( DstBlend, INV_SRC_ALPHA );

// Always write rgba
RenderState( ColorWriteEnable0, RGBA );
RenderState( FillMode, SOLID );

// Never cull
RenderState( CullMode, NONE );

// No depth
RenderState( DepthEnable, false );
RenderState( DepthWriteEnable, false );

struct PS_OUTPUT
{
	float4 vColor : SV_Target0;
};

[shader( "pixel" )] PS_OUTPUT MainPs( PS_INPUT i )
{
	PS_OUTPUT o;

	float4 vColor = g_tColor.Sample( g_sPointClamp, i.vTexCoord.xy );

	o.vColor = i.vColor.rgba * vColor.rgba;
	return o;
}

#endif // PS


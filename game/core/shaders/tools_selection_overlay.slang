// tools_selection_overlay: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
// Copyright (c) Valve Corporation, All rights reserved. ======================================================================================================

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "Selection overlay";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#include "sbox/pass.slangh"

Mode( Default );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
Feature( F_NO_CULLING, 0..1 );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "system.fxc" // This should always be the first include in COMMON
#include "vr_common.fxc"
#include "common.fxc"
#include "sbox/math.slangh"

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct VS_INPUT
{
	float3 vPositionOs			: POSITION < Semantic( PosXyz ); >;
	float4 vColor				: COLOR0 < Semantic( Color ); >;
	
	// Instancing data
	uint nInstanceTransformID : TEXCOORD13 < Semantic( InstanceTransformUv ); >;
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_INPUT
{
	float4 vColor				: COLOR0;
	float4 vThisVertPosPs		: TEXCOORD3;

	// VS only
	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs		: SV_Position;
	#endif

	// PS only
	#if ( ( PROGRAM == VFX_PROGRAM_PS ) )
		float4 vPositionSs		: SV_Position;
	#endif
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_VS )
// Includes -----------------------------------------------------------------------------------------------------------------------------------------------
#include "sbox/instancing.slangh"

// Combos -------------------------------------------------------------------------------------------------------------------------------------------------

// Constants ----------------------------------------------------------------------------------------------------------------------------------------------

float4 g_vOverrideColor < UiType( Color ); Default4( 0.0, 0.0, 0.0, 0.0 ); UiGroup( "OverrideColor" ); Attribute( "g_vOverrideColor" ); >;
float g_OverrideColorFactor< Default( 0.0 ); Range( 0, 1 ); UiGroup( "OverrideColor" ); Attribute( "g_OverrideColorFactor" ); >;

// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
[shader( "vertex" )] PS_INPUT MainVs( const VS_INPUT i )
{
	PS_INPUT o;

	float3x4 matObjectToWorld = GetTransformMatrix( i.nInstanceTransformID );
	float3 vPositionWs = mul( matObjectToWorld, float4( i.vPositionOs.xyz, 1.0 ) );

	// Projected position
	o.vPositionPs.xyzw = Position3WsToPs( vPositionWs.xyz );

	// Setup info for the dotted line.
	o.vThisVertPosPs = o.vPositionPs;

	// Lerp between the input color and the override color
	o.vColor.rgb = lerp( SrgbGammaToLinear( i.vColor.rgb ), SrgbGammaToLinear( g_vOverrideColor.rgb ), g_OverrideColorFactor );
	o.vColor.a = lerp( i.vColor.a, g_vOverrideColor.a, g_OverrideColorFactor );

	return o;
}

#endif // VS

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_PS )
// Includes -----------------------------------------------------------------------------------------------------------------------------------------------

// Combos -------------------------------------------------------------------------------------------------------------------------------------------------

// Artist input textures ----------------------------------------------------------------------------------------------------------------------------------

// Textures -----------------------------------------------------------------------------------------------------------------------------------------------

// Render State -------------------------------------------------------------------------------------------------------------------------------------------

// Force stencil to mark pixels as rendered outside for now
RenderState( StencilEnable, true );
RenderState( StencilPassOp, REPLACE );
RenderState( StencilRef, 1 );

RenderState( DepthWriteEnable, false );
RenderState( BlendEnable, true );
RenderState( SrcBlend, SRC_ALPHA );
RenderState( DstBlend, INV_SRC_ALPHA );
		
RenderState( SlopeScaleDepthBias, 0.5 );
RenderState( DepthBiasClamp, 0.0005 );

RenderState( CullMode, F_NO_CULLING ? NONE : BACK );

// Globals ------------------------------------------------------------------------------------------------------------------------------------------------

// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_OUTPUT
{
	float4 vColor : SV_Target0;
};

bool RejectStipplePixel( PS_INPUT i )
{
	float xCoord = ( i.vPositionSs.x - 0.5 );
	float yCoord = ( i.vPositionSs.y - 0.5 );

	// This will do a much coarser pattern, which flickers less on Dell LCDs, but
	// it's hard to discern on bright surfaces.
	//if ( fmod( yCoord, 2.0 ) < 1.1 )
	//	return true;
	//yCoord /= 2.0;

	
	// Original two wide checker
	//int yOffset = (int)fmod( yCoord + 1.0, 2.0 );
	//float a = fmod( xCoord + yOffset * 2, 4.0 );
	//return ( a <= 1.0 );
	
	// Offset grid
	//int yOffset = (int)fmod( yCoord + 1.0, 2.0 );
	//float a = fmod( xCoord + yOffset * 2, 4.0 );
	//return ( a < 1.0 );
				
	// Fine grain checker board
	int yOffset = int( fmod( yCoord, 2.0 ) );
	float a = fmod( xCoord + yOffset, 2.0 );
	return ( a < 1.0 );
	
	// Dot grid
	//float a = fmod( xCoord, 2.0 );
	//float b = fmod( yCoord, 2.0 );
	//return ( ( a < 1.0 ) && ( b < 1.0 ) );
	
	// Cross hatch
	//int yOffset = (int)fmod( yCoord, 2.0 );
	//int yOffsetB = (int)fmod( yCoord, 4.0 );
	//float a = fmod( xCoord + yOffset, 2.0 );
	//float b = fmod( xCoord + yOffsetB, 4.0 );
	//float c = fmod( yOffset, 2 );
	//return ( a < 1.0 ) && !( ( b < 1.0 ) && ( c < 1.0 ) );
}

[shader( "pixel" )] PS_OUTPUT MainPs( PS_INPUT i )
{
	// Handle dotted lines.
	if ( !RejectStipplePixel( i ) )
	{
		clip( -1.0 );
	}

	//----------------//
	// Combine result //
	//----------------//
	PS_OUTPUT o;
	o.vColor = i.vColor;
	
	return o;
}

#endif // PS


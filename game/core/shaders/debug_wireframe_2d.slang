// debug_wireframe_2d.shader as a pass module: Borealis.Gpu draws this; the .shader stays for the other routes
// and as the fallback (docs/managed/shader-format-replacement.md, "Pass modules").
// Copyright (c) Valve Corporation, All rights reserved. ======================================================================================================

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: CompileTargets = ( IS_SM_50 && ( PC || VULKAN ) );
// HEADER: Description = "Simple, no frills screenspace wireframe with per-vertex colors";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// MODES: Default();

#include "sbox/pass.slangh"

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "system.fxc" // This should always be the first include in COMMON
#include "common.fxc"
#include "math_general.fxc"

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct VS_INPUT
{
	float4 vPositionPs : POSITION < Semantic( PosXyz ); >;
	float4 vColor : COLOR0 < Semantic( Color ); >;
	float4 vFirstLineVertPosPs : TEXCOORD1 < Semantic( uvw ); >;
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_INPUT
{
	float4 vColor				: COLOR0;
	
	float4 vFirstLineVertPosPs	: TANGENT0;
	float4 vThisVertPosPs		: TEXCOORD3;

	// VS only
	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs		: SV_Position;
	#endif
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_VS )
// Includes -----------------------------------------------------------------------------------------------------------------------------------------------
#include "vr_common.fxc"
#define IS_SPRITECARD 1 // So GetViewSubviewAndAdjustInstanceId() doesn't cause compile error
#include "vr_lighting.fxc"

// Constants ----------------------------------------------------------------------------------------------------------------------------------------------

// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
[shader( "vertex" )] PS_INPUT MainVs( const VS_INPUT i )
{
	PS_INPUT o;
	
	o.vPositionPs = i.vPositionPs;
	o.vColor = i.vColor;

	// Setup info for the dotted line.
	o.vFirstLineVertPosPs = i.vFirstLineVertPosPs;
	o.vThisVertPosPs = i.vPositionPs;
	
	return o;
}

#endif // VS

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_PS )
// Includes -----------------------------------------------------------------------------------------------------------------------------------------------

// Render State -------------------------------------------------------------------------------------------------------------------------------------------

RenderState( DepthWriteEnable, false );
RenderState( DepthEnable, false );

// Globals ------------------------------------------------------------------------------------------------------------------------------------------------

// Pattern to use
//	0 for solid line
//	1 for a dashed line
//	2 for a dotted line
float g_flPatternType < Attribute( "PatternType" );  Default( 0.0 ); >;

// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_OUTPUT
{
	float4 vColor : SV_Target0;
};

bool RejectDottedLinePixel( PS_INPUT i )
{
	float flGapDist = 0;
	if ( g_flPatternType == 1 ) // dashed
	{
		flGapDist = 16.0;
	}
	else if ( g_flPatternType == 2 ) // dotted
	{
		flGapDist = 3.0;
	}

	float4 v1 = i.vFirstLineVertPosPs;
	float4 v2 = i.vThisVertPosPs;

	// Get the screen positions.
	float2 vScreenPos1;
	vScreenPos1.xy = ( v1.xy / v1.w ) / g_vInvViewportSize.xy;
		
	float2 vScreenPos2;
	vScreenPos2.xy = ( v2.xy / v1.w ) / g_vInvViewportSize.xy;
		
	float dist = length( vScreenPos2 - vScreenPos1 );
	if ( flGapDist != 0 )
	{
		return fmod( dist, flGapDist * 2.0 ) > flGapDist;
	}
	return false;
}

[shader( "pixel" )] PS_OUTPUT MainPs( PS_INPUT i )
{
	PS_OUTPUT o;

	o.vColor = i.vColor;

	// Handle dotted lines.
	if ( RejectDottedLinePixel( i ) )
	{
		clip( -1.0 );
	}
	
	return o;
}

#endif // PS


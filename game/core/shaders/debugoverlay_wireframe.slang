// debugoverlay_wireframe: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
// Copyright (c) Valve Corporation, All rights reserved. ======================================================================================================

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "Wireframe for tools";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#include "sbox/pass.slangh"

Mode( Default );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
Feature( F_NO_ZTEST, 0..1 );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "sbox/system.slangh" // This should always be the first include in COMMON
#include "sbox/view.slangh"
#include "sbox/math.slangh"

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct VS_INPUT
{
	float3 vPositionOs			: POSITION < Semantic( PosXyz ); >;
	float3 vNormalOs			: NORMAL < Semantic( OptionallyCompressedTangentFrame ); >;
	float3 vFirstLineVertPosWs	: TEXCOORD0 < Semantic( uvw ); >;
	float4 vColor				: COLOR0 < Semantic( Color ); >;

	// Instancing data
	uint nInstanceTransformID : TEXCOORD13 < Semantic( InstanceTransformUv ); >;
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_INPUT
{
	float3 vNormalWs			: TEXCOORD0;
	float4 vColor				: COLOR0;

	// Used for dashed/dotted lines
	float4 vFirstLineVertPosPs	: TANGENT0;
	float4 vThisVertPosPs		: TEXCOORD3;

	float3 vPositionWs		: TEXCOORD2;
	
	// VS only
	#if ( ( PROGRAM == VFX_PROGRAM_VS ) || ( PROGRAM == VFX_PROGRAM_GS ) )
		float4 vPositionPs		: SV_Position;
	#endif

	// PS only
	#if ( PROGRAM == VFX_PROGRAM_PS )
		float4 vPositionSs		: SV_Position;
	#endif
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_VS )
// Includes -----------------------------------------------------------------------------------------------------------------------------------------------
#include "sbox/instancing.slangh"

// Combos -------------------------------------------------------------------------------------------------------------------------------------------------
DynamicCombo( D_DEPTH_BIAS, 0..1, Sys( ALL ) );
DynamicCombo( D_SNAP_TO_SCREEN_PIXELS, 0..1, Sys( ALL ) );

#define VS_OUTPUT PS_INPUT

// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
float4 SnapToScreenPixels( float4 vPositionPs )
{
	// Projection space->screen space
	float sx = (vPositionPs.x / vPositionPs.w) / g_vInvViewportSize.x;
	float sy = (vPositionPs.y / vPositionPs.w) / g_vInvViewportSize.y;

	// Snap to pixel centers.
	// The 0.01 here deals with rounding problems. Without this, one side of a line might get floored 
	// to the int(sx)-1 and the other side might get floored to int(sx).
	sx = floor( sx + 0.01 ) + g_flPixelCenter;
	sy = floor( sy + 0.01 ) + g_flPixelCenter;

	// Screen space->projection space
	vPositionPs.x = sx * g_vInvViewportSize.x * vPositionPs.w;
	vPositionPs.y = sy * g_vInvViewportSize.y * vPositionPs.w;

	return vPositionPs;
}

[shader( "vertex" )] VS_OUTPUT MainVs( VS_INPUT i )
{
	VS_OUTPUT o;
	
	float3x4 matObjectToWorld = GetTransformMatrix( i.nInstanceTransformID );
	float3 vPositionWs = mul( matObjectToWorld, float4( i.vPositionOs.xyz, 1.0 ) );

	// Worldspace normal
	float3 vNormalWs = normalize( mul( matObjectToWorld, float4( i.vNormalOs.xyz, 0.0 ) ) );

	// Projected position
	o.vPositionPs.xyzw = Position3WsToPs( vPositionWs.xyz );
	
	#if ( D_SNAP_TO_SCREEN_PIXELS )
		o.vPositionPs = SnapToScreenPixels( o.vPositionPs );
	#endif

	// Setup info for the dotted line if needed
	o.vFirstLineVertPosPs = Position3WsToPs( i.vFirstLineVertPosWs );		
	o.vThisVertPosPs = o.vPositionPs;

	o.vNormalWs.xyz = vNormalWs.xyz;

	#if ( D_DEPTH_BIAS )
	{
		// Just simple depth relative depth bias, for wireframe overlays sloped scale 
		// calculations were causing too much offset that would make the wireframe 
		// appear over things that should have been behind it.
		float flProjDepth = saturate( o.vPositionPs.z / o.vPositionPs.w );
		float flFacingBias = lerp( 0.005, 0.000001, flProjDepth );
		o.vPositionPs.z -= flFacingBias * o.vPositionPs.w;
	}
	#endif

	// Texture coordinate
	o.vColor = i.vColor;

	// World space position
	o.vPositionWs.xyz = vPositionWs.xyz;

	return o;
}

#endif // VS

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_PS )
// Includes -----------------------------------------------------------------------------------------------------------------------------------------------
#include "sbox/encoding.slangh"

#include "sbox/irradiance_probe.slangh"

// Combos -------------------------------------------------------------------------------------------------------------------------------------------------
DynamicCombo( D_NO_ZTEST, 0..1, Sys( ALL ) );

StaticCombo( S_NO_ZTEST, F_NO_ZTEST, Sys( ALL ) );

// Artist input textures ----------------------------------------------------------------------------------------------------------------------------------

// Textures -----------------------------------------------------------------------------------------------------------------------------------------------
// Constants ----------------------------------------------------------------------------------------------------------------------------------------------

// This is so wireframe shows up when there are no lights.
float3 g_vAlbedo < UiType( Color ); Default3( .5, .5, .5 ); UiGroup( "Albedo" ); >;

// Render State -------------------------------------------------------------------------------------------------------------------------------------------
RenderState( FillMode, WIREFRAME );

// Force stencil to mark pixels as rendered outside for now
RenderState( StencilEnable, true );
RenderState( StencilPassOp, REPLACE );
RenderState( StencilRef, 1 );

// Don't backface cull
RenderState( CullMode, NONE );

#if ( S_MODE_DEFERRED_GATHER )
	RenderState( DepthWriteEnable, false );
#endif

#if ( D_NO_ZTEST || S_NO_ZTEST )
	RenderState( DepthEnable, false );
#else
	RenderState( DepthEnable, true );
	#if ( S_MODE_DEFERRED_GATHER == 0 )
		RenderState( DepthWriteEnable, true );
	#endif
#endif

// Globals ------------------------------------------------------------------------------------------------------------------------------------------------
float g_flPhongExp < Default( 0.1 ); Range( 0.1, 256.0 ); >;
float g_flPhongBoost < Default( 0.1 ); Range( 0.0, 256.0 ); >;
float g_flAlphaTestReference < Default ( 0.3 ); Range( 0.01, 0.99 ); >;

// Pattern to use
//	0 for solid line
//	1 for a dashed line
//	2 for a dotted line
float g_flPatternType < Attribute( "PatternType" );  Default( 0.0 ); >;

// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_OUTPUT
{
	float4 vColor												: SV_Target0;
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
	vScreenPos1.x = v1.x / v1.w / g_vInvViewportSize.x;
	vScreenPos1.y = v1.y / v1.w / g_vInvViewportSize.y;

	float2 vScreenPos2;
	vScreenPos2.x = v2.x / v2.w / g_vInvViewportSize.x;
	vScreenPos2.y = v2.y / v2.w / g_vInvViewportSize.y;

	float dist = length( vScreenPos2 - vScreenPos1 );

	if ( flGapDist != 0.0 )
	{
		return fmod( dist, flGapDist * 2.0 ) > flGapDist;
	}
	return false;
}

[shader( "pixel" )] PS_OUTPUT MainPs( PS_INPUT i )
{
	// Figure out initial albedo.
	float4 vAlbedo;
	vAlbedo.rgba = i.vColor;

	// World-space normal.
	float3 vNormalWs = normalize( i.vNormalWs.xyz );

	// Pack specular exp into a texture-friendly format (0..1 range).  Solving the equation we use for specular
	// ( lSpecExp = 1.0 - flSpecTexel + ( 150.0 * flSpecTexel ) ) for flSpecTexel gives:
	float flSpecExp = g_flPhongExp;
	flSpecExp = ( flSpecExp - 1.0 ) / 149.0;

	// Handle dotted lines.
	if ( RejectDottedLinePixel( i ) )
	{
		clip( -1.0 );
	}

	//----------------//
	// Combine result //
	//----------------//
	PS_OUTPUT o;

	o.vColor = vAlbedo;

	return o;
}

#endif // PS


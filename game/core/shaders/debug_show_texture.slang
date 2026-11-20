// debug_show_texture: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
// Copyright (c) Valve Corporation, All rights reserved. ======================================================================================================

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: CompileTargets = ( IS_SM_50 && ( PC || VULKAN ) );
// HEADER: Description = "Debug Show Texture";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#include "sbox/pass.slangh"

Mode( Default );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "system.fxc" // This should always be the first include in COMMON
#include "vr_common.fxc"
#include "sbox/math.slangh"

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct VS_INPUT
{
	float4 vPositionPs : POSITION < Semantic( PosXyz ); >;
	float2 vTexCoord : TEXCOORD0 < Semantic( LowPrecisionUv ); >;
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_INPUT
{
	float2 vTexCoord		: TEXCOORD0;

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

// Combos -------------------------------------------------------------------------------------------------------------------------------------------------

// Constants ----------------------------------------------------------------------------------------------------------------------------------------------

// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
[shader( "vertex" )] PS_INPUT MainVs( const VS_INPUT i )
{
	PS_INPUT o;
	o.vPositionPs = float4( i.vPositionPs.xy, 0.5, 1 );
	o.vTexCoord = i.vTexCoord;

	return o;
}

#endif // VS

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_PS )
// Includes -----------------------------------------------------------------------------------------------------------------------------------------------

// Combos -------------------------------------------------------------------------------------------------------------------------------------------------

// Must match scenesystem/sceneutils.h
DynamicCombo( D_DEBUG_SHOW_TEXTURE_MODE, 0..4, Sys( ALL ) );
	#define DEBUG_SHOW_TEXTURE_RGB 0
	#define DEBUG_SHOW_TEXTURE_ALPHA 1
	#define DEBUG_SHOW_TEXTURE_RED 2
	#define DEBUG_SHOW_TEXTURE_RGBA 3
	#define DEBUG_SHOW_TEXTURE_BAKED_LIGHTING 4

DynamicCombo( D_DEBUG_SHOW_TEXTURE_SRGB, 0..1, Sys( ALL ) );

// Textures -----------------------------------------------------------------------------------------------------------------------------------------------
#if D_DEBUG_SHOW_TEXTURE_SRGB
	Texture2D g_tTex < Attribute( "DebugTexture" ); SrgbRead( true ); >;
#else
	Texture2D g_tTex < Attribute( "DebugTexture" ); SrgbRead( false ); >;
#endif

// Render State -------------------------------------------------------------------------------------------------------------------------------------------
RenderState( DepthEnable, false );
RenderState( DepthWriteEnable, false );
RenderState( CullMode, NONE );

#if ( D_DEBUG_SHOW_TEXTURE_MODE == DEBUG_SHOW_TEXTURE_RGBA )
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, INV_SRC_ALPHA );
#endif

// Globals ------------------------------------------------------------------------------------------------------------------------------------------------

// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_OUTPUT
{
	float4 vColor : SV_Target0;
};

[shader( "pixel" )] PS_OUTPUT MainPs( PS_INPUT i )
{
	PS_OUTPUT o;
	o.vColor = float4( 1.0, 1.0, 1.0, 1.0 );

	float4 vTexel = g_tTex.Sample( g_sPointClamp, i.vTexCoord.xy );

	#if ( DEBUG_SHOW_TEXTURE_RED == D_DEBUG_SHOW_TEXTURE_MODE )
	{
		o.vColor.xyz = vTexel.rrr;
	}
	#elif ( DEBUG_SHOW_TEXTURE_ALPHA == D_DEBUG_SHOW_TEXTURE_MODE )
	{
		o.vColor.xyz = vTexel.aaa;
	}
	#elif ( DEBUG_SHOW_TEXTURE_RGBA == D_DEBUG_SHOW_TEXTURE_MODE )
	{
		o.vColor.rgba = vTexel.rgba;
	}
	#elif ( DEBUG_SHOW_TEXTURE_BAKED_LIGHTING == D_DEBUG_SHOW_TEXTURE_MODE )
	{
		o.vColor.xyz = vTexel.xyz * g_flToneMapScalarLinear;
	}
	#else // DEBUG_SHOW_TEXTURE_RGB == D_DEBUG_SHOW_TEXTURE_MODE
	{
		o.vColor.xyz = vTexel.xyz;
	}
	#endif
	
	return o;
}

#endif // PS


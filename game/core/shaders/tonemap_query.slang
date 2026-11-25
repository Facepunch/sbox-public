// tonemap_query: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
// Copyright (c) Valve Corporation, All rights reserved. ======================================================================================================

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: CompileTargets = ( IS_SM_30 || IS_SM_40 || IS_SM_50 );
// HEADER: Description = "Compares the luminance against a constant value and returns all 1's when greater";
 

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#include "sbox/pass.slangh"

Mode( Default );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "sbox/system.slangh" // This should always be the first include in COMMON
#include "sbox/view.slangh"
#include "sbox/math.slangh"

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct VS_INPUT
{
	float2 vPositionPs	: POSITION < Semantic( PosXyz ); >;
	float2 vTexCoord	: TEXCOORD0 < Semantic( LowPrecisionUv ); >;
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_INPUT
{
	float2 vTexCoord : TEXCOORD0;

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

// Constants ----------------------------------------------------------------------------------------------------------------------------------------------

// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
[shader( "vertex" )] PS_INPUT MainVs( const VS_INPUT i )
{
	PS_INPUT o;

	o.vPositionPs.xyzw = float4( i.vPositionPs.xy, 0.0, 1.0 );
	o.vTexCoord.xy = i.vTexCoord.xy;
	return o;
}

#endif // VS

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_PS )
// Includes -----------------------------------------------------------------------------------------------------------------------------------------------

// Combos -------------------------------------------------------------------------------------------------------------------------------------------------
DynamicCombo( D_TONEMAP_MASK_COLOR, 0..1, Sys( ALL ) );
DynamicCombo( D_TONEMAP_LOG_LUM, 0..1, Sys( ALL ) );
DynamicCombo( D_MSAA_COLOR_BUFFER, 0..1, Sys( SM >= 50 ) );

// Textures -----------------------------------------------------------------------------------------------------------------------------------------------
#if ( D_MSAA_COLOR_BUFFER )
	Texture2DMS< float > g_tTex < Attribute( "ColorBuffer" ); SrgbRead( true ); > ;
#else
	Texture2D g_tTex < Attribute( "ColorBuffer" ); SrgbRead( true ); > ;
#endif

// Render State -------------------------------------------------------------------------------------------------------------------------------------------
RenderState( DepthEnable, false );
RenderState( DepthWriteEnable, false );
RenderState( CullMode, NONE );
RenderState( ColorWriteEnable0, false );

// Globals ------------------------------------------------------------------------------------------------------------------------------------------------
float g_flLuminanceMin < Attribute( "LuminanceMin" ); Default( 0.0 ); >;
float g_flLuminanceMax < Attribute( "LuminanceMax" ); Default( 0.0 ); >;
float g_flUseAlphaChannel < Attribute( "UseAlphaForAutoExposure" ); Default( 0.0 ); >;
// A multisampled frame's colour as native's query reads it: its Texture2DMS< float > loads red alone, into every channel.
// Borealis reads the resolved colour, so it meters red itself to expose as native does
float g_flMeterRed < Attribute( "MeterRedAsMultisampled" ); Default( 0.0 ); >;
float3 g_vTonemapMaskColor < Default3( -1.0, -1.0, -1.0 ); >;

// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_OUTPUT
{
	float4 vColor : SV_Target0;
};

[shader( "pixel" )] PS_OUTPUT MainPs( PS_INPUT i )
{
	PS_OUTPUT o;

	float4 cColor;
	#if ( D_MSAA_COLOR_BUFFER )
	{
		cColor = g_tTex.Load( i.vPositionSs.xy, 0 );
	}
	#else
	{
		cColor = g_tTex.SampleLevel( g_sPointWrap, i.vTexCoord.xy, 0.0 );
	}
	#endif
	// Kill any pixel that matches the mask color. This is used in the asset browser to ignore the clear color.
	#if ( D_TONEMAP_MASK_COLOR )
	{
		if ( dot( abs( cColor.rgb - SrgbGammaToLinear( g_vTonemapMaskColor.rgb ) ), float3( 1.0, 1.0, 1.0 ) ) < 0.001 )
		{
			// Kill the masked pixel
			clip( -1.0 );
		}
	}
	#endif

	// Formula for calculating luminance based on NTSC standard
	float3 tmpv = float3( 0.2125, 0.7154, 0.0721 );
	if ( g_flMeterRed > 0.0 ) cColor = cColor.rrrr;
	float flLuminance = dot( cColor.rgb, tmpv.rgb );
	flLuminance = lerp( flLuminance, cColor.a, g_flUseAlphaChannel );

	#if ( D_TONEMAP_LOG_LUM )
	{
		// Kill any (0,0,0) pixels. The tonemap scale won't affect them, so don't let them throw off the average.
		if ( flLuminance == 0.0f )
		{
			clip( -1.0 );
		}
		flLuminance = max( 0.0001, flLuminance );	// Clamp 0 and negative values to something larger than 0 so that log doesn't break
		flLuminance = log2( flLuminance );			// note log base 2 to match tonemapsystem.cpp
	}
	#endif

	// Use two step functions to generate 1 if the luminance is in the range, 0 if not
	float flInRange = step( g_flLuminanceMin, flLuminance ) * step( flLuminance, g_flLuminanceMax );

	// Kill any pixel not in the luminance range
	clip( flInRange - 1.0 );

	o.vColor = flInRange.xxxx;
	return o;
}

#endif // PS


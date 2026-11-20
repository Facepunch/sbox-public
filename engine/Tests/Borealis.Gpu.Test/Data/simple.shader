// Copyright (c) Valve Corporation, All rights reserved. ==================================================================
//
//=========================================================================================================================

HEADER
{
	DevShader = false;
	CompileTargets = ( IS_SM_50 && ( PC || VULKAN ) );
	Description = "Standard shader for surfaces that require normal maps";
	Version = 31;
}

//=========================================================================================================================

MODES
{
	Forward();

	// Note: depth_only.shader supports alpha test and translucent features, so if you change them here, you need to update depth_only.shader
	Depth( S_MODE_DEPTH ); 

	ToolsShadingComplexity( "tools_shading_complexity.shader" );
}

//=========================================================================================================================

FEATURES
{
	#include "vr_common_features.fxc"

	Feature( F_METALNESS_TEXTURE, 0..1, "PBR" );
	Feature( F_AMBIENT_OCCLUSION_TEXTURE, 0..1, "PBR" );
}

//=========================================================================================================================

COMMON
{
	#define ENABLE_NORMAL_MAPS
	#define S_SPECULAR 1
	#define S_SPECULAR_CUBE_MAP 1
	#define S_TRANSLUCENT 0
	#define S_ALPHA_TEST 0
	#define BLEND_MODE_ALREADY_SET
	#define S_UV2_DEFAULT_FOR_AO 0

	#include "system.fxc" // This should always be the first include in COMMON
	#include "vr_common.fxc"
}

//=========================================================================================================================

struct VS_INPUT
{
	#include "vr_shared_standard_vs_input.fxc"
};

//=========================================================================================================================

struct PS_INPUT
{
	#include "vr_shared_standard_ps_input.fxc"
};

//=========================================================================================================================

VS
{
	#include "vr_shared_standard_vs_code.fxc"

	//
	// Main
	//
	PS_INPUT MainVs( VS_INPUT i )
	{
		PS_INPUT o = VS_SharedStandardProcessing( i );

		return VS_CommonProcessing_Post( o );
	}
}

//=========================================================================================================================

PS
{
	DynamicCombo( D_OPAQUE_FADE, 0..1, Sys( ALL ) );

	StaticCombo( S_METALNESS_TEXTURE, F_METALNESS_TEXTURE, Sys( ALL ) );
	StaticCombo( S_AMBIENT_OCCLUSION_TEXTURE, F_AMBIENT_OCCLUSION_TEXTURE, Sys( ALL ) );
	StaticCombo( S_RENDER_BACKFACES, F_RENDER_BACKFACES, Sys( ALL ) );

	RenderState( BlendEnable,  false );
	RenderState( SrcBlend, ONE );
	RenderState( DstBlend, ZERO );
	

	//
	// Albedo & Metalness
	//

	#if ( S_METALNESS_TEXTURE )
		#define METALNESS_IN_COLOR
		Texture2D g_tColor < Channel( RGB, Box( TextureColor ), Srgb ); 
													Channel( A, Box( TextureMetalness ), Linear ); 
													OutputFormat( BC7 ); SrgbRead( true ); >;
	#elif ( S_AMBIENT_OCCLUSION_TEXTURE )
        #define AMBIENT_OCCLUSION_IN_COLOR
		Texture2D g_tColor < Channel( RGB, Box( TextureColor ), Srgb ); 
													Channel( A, Box( TextureAmbientOcclusion ), Linear ); 
													OutputFormat( BC7 ); SrgbRead( true ); >;
	#else
        #define AMBIENT_OCCLUSION_IN_COLOR // let it read a 1.0 from alpha.
		Texture2D g_tColor < Channel( RGB, Box( TextureColor ), Srgb ); 
													OutputFormat( DXT1 ); SrgbRead( true ); >;
	#endif

	BoolAttribute( SupportsLightmapping, true );
	BoolAttribute( PerVertexLighting, true );
	BoolAttribute( AllowPerDrawCulling, F_RENDER_BACKFACES ? false : true );

	#define COLOR_SAMPLER_DEFINED

	#include "vr_shared_standard_ps_code.fxc"

	//-----------------------------------------------------------------------------------------------------------------

	PS_OUTPUT MainPs( PS_INPUT i )
	{
		FinalCombinerInput_t finalCombinerInput = PS_SharedStandardProcessing( i );
		return PS_FinalCombiner( finalCombinerInput );
	}
}

//=========================================================================================================================


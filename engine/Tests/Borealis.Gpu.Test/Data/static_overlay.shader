// Copyright (c) Valve Corporation, All rights reserved. ==================================================================
//
//=========================================================================================================================

HEADER
{
	DevShader = false;
	CompileTargets = ( IS_SM_50 && ( PC || VULKAN ) );
	Description = "Shader for rendering static overlays placed in Hammer.";
	Version = 30;
}

//=========================================================================================================================

MODES
{
	Forward();

	// Note: depth_only.shader supports alpha test and translucent features, so if you change them here, you need to update depth_only.shader
	Depth( "depth_only.shader" ); 

	ToolsShadingComplexity( "tools_shading_complexity.shader" );
}

//=========================================================================================================================

FEATURES
{
	#include "vr_common_features.fxc"

	Feature( F_LIT, 0..1, "Lit" );
	Feature( F_BLEND_MODE, 0..6(0="Opaque", 1="Translucent", 2="Alpha Test", 3="Mod2x", 4="Additive", 5="Multiply", 6="ModThenAdd" ), "Blend Mode" );
}

//=========================================================================================================================

COMMON
{
	#define ENABLE_NORMAL_MAPS
	#define S_SPECULAR 0
	#define S_METALNESS_TEXTURE 0
	#define S_SPECULAR_CUBE_MAP 0
	#define S_RENDER_BACKFACES 0
	#define S_DO_NOT_CAST_SHADOWS 1

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
	StaticCombo( S_LIT, F_LIT, Sys( ALL ) );
	StaticCombo( S_BLEND_MODE, F_BLEND_MODE, Sys( ALL ) );

	DynamicCombo( D_OPAQUE_FADE, 0..1, Sys( ALL ) );
	DynamicComboRule( Requires1( D_OPAQUE_FADE, S_BLEND_MODE == 0 ) );

	BoolAttribute( SupportsLightmapping, true );
	BoolAttribute( PerVertexLighting, true );
	BoolAttribute( AllowPerDrawCulling, true );
	BoolAttribute( overlay, true );

	#define BLEND_MODE_ALREADY_SET
	#define DEPTH_STATE_ALREADY_SET

	RenderState( DepthEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );

	#define S_ALPHA_TEST ( S_BLEND_MODE == 2 ) // piggyback existing code

	RenderState( DepthWriteEnable, false );
	RenderState( DepthBias, 250 ); // Value is steps in depth buffer (i.e. out of 2^24 if the depth buffer is 24 bit)
	RenderState( SlopeScaleDepthBias, 0.5 );
	RenderState( DepthBiasClamp, 0.0005 );

	#if ( S_LIT == 0 )
		#define S_UNLIT 1
	#endif

	#if ( S_BLEND_MODE == 1 )
		#define S_TRANSLUCENT 1 // piggyback existing code
		RenderState( BlendEnable, true );
		RenderState( SrcBlend, SRC_ALPHA );
		RenderState( DstBlend, INV_SRC_ALPHA );
	#elif ( S_ALPHA_TEST )
		BoolAttribute( NoZPrepass, true );
		RenderState( AlphaToCoverageEnable, true );
	#elif( S_BLEND_MODE == 3 ) // mod2x
		#define S_TRANSLUCENT 1 // piggyback existing code
		RenderState( BlendEnable, true );
		RenderState( BlendOp, ADD );
		RenderState( SrcBlend, DEST_COLOR );
		RenderState( DstBlend, SRC_COLOR );
	#elif( S_BLEND_MODE == 4 ) // additive
		#define S_TRANSLUCENT 1 // piggyback existing code
		#define S_ADDITIVE_BLEND 1
		RenderState( BlendEnable, true );
		RenderState( SrcBlend, SRC_ALPHA );
		RenderState( DstBlend, ONE );
	#elif( S_BLEND_MODE == 5 ) // multiply
		#define S_TRANSLUCENT 1 // piggyback existing code
		RenderState( BlendEnable, true );
		RenderState( BlendOp, ADD );
		RenderState( SrcBlend, ZERO );
		RenderState( DstBlend, SRC_COLOR );
	#elif( S_BLEND_MODE == 6 ) // modthenadd
		#define S_TRANSLUCENT 1 // piggyback existing code
		#define S_ADDITIVE_BLEND 1
		#define S_SELF_ILLUM 1
		RenderState( BlendEnable, true );
		RenderState( BlendOp, ADD );
		RenderState( SrcBlend, DEST_COLOR );
		RenderState( DstBlend, ONE );
	#else
		RenderState( BlendEnable, false );
		RenderState( SrcBlend, SRC_ALPHA );
		RenderState( DstBlend, INV_SRC_ALPHA );
	#endif

	#if ( S_BLEND_MODE == 3 )
		#define COLOR_SAMPLER_DEFINED

		Texture2D g_tColor < Channel( RGB, Box( TextureColor ), Srgb );
															 Channel( A, Box( TextureTranslucency ), Linear );
															 OutputFormat( BC7 ); SrgbRead( false ); >;
	#endif
																
	#include "vr_shared_standard_ps_code.fxc"

	//-----------------------------------------------------------------------------------------------------------------

	PS_OUTPUT MainPs( PS_INPUT i )
	{
		FinalCombinerInput_t finalCombinerInput = PS_SharedStandardProcessing( i );
		LightingTerms_t lightingTerms = InitLightingTerms();
		
		PS_OUTPUT o = PS_FinalCombinerDoLighting( finalCombinerInput, lightingTerms );

		// Multiplicative overlays preserve their artistic fog blending per layer.
		if ( ( ( S_BLEND_MODE == FOG_OVERLAY_MOD2X ) || ( S_BLEND_MODE == FOG_OVERLAY_MULTIPLY ) || ( S_BLEND_MODE == FOG_OVERLAY_MOD_THEN_ADD ) ) && !ToolsVis::WantsToolsVis() )
		{
			[branch]
			if ( g_bFogEnabled )
			{
				o.vColor = Fog::ApplyOverlay( finalCombinerInput.vPositionWs, finalCombinerInput.vPositionSs.xy, o.vColor, S_BLEND_MODE );
			}

			#ifdef DISABLE_TONEMAPPING
			{
				o.vColor.rgb *= rcp( g_flToneMapScalarLinear );	// FIXME: precompute the reciprocal and store in cbuffer.
			}
			#endif
		}
		else
		{
			if( ToolsVis::WantsToolsVis() && S_BLEND_MODE == 6 )
				finalCombinerInput.vDiffuseColor.rgb = saturate( finalCombinerInput.vDiffuseColor.rgb + finalCombinerInput.vEmissive.rgb );
			
			o = PS_FinalCombinerDoPostProcessing( finalCombinerInput, lightingTerms, o );
		}

		#if ( S_BLEND_MODE == 3 )
		{
			o.vColor.rgb = lerp( float3( 0.5, 0.5, 0.5 ), o.vColor.rgb, o.vColor.a );
		}
		#elif ( S_BLEND_MODE == 5 )
		{
			o.vColor.rgb = lerp( float3( 1, 1, 1 ), o.vColor.rgb, o.vColor.a );
		}
		#elif( S_BLEND_MODE == 6 )
		{
			o.vColor.rgb = lerp( float3( 0, 0, 0 ), o.vColor.rgb, o.vColor.a );
		}
		#endif

		return o;
	}
}

//=========================================================================================================================


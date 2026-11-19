// tools_textured_unlit: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md). The .shader
// beside it is only Source 2's renderer's.
// Copyright (c) Valve Corporation, All rights reserved. ======================================================================================================

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "Unlit Textured";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#include "sbox/pass.slangh"

Mode( ToolsUtil );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
Feature( F_TRANSLUCENT, 0..1, "Translucent" );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "system.fxc" // This should always be the first include in COMMON
#include "vr_common.fxc"
#include "common.fxc"
#include "math_general.fxc"

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct VS_INPUT
{
	float3 vPositionOs			: POSITION < Semantic( PosXyz ); >;
	float3 vNormalOs			: NORMAL < Semantic( Normal ); >;
	float2 vTexCoord			: TEXCOORD0 < Semantic( LowPrecisionUv ); >;

	// Instancing data
	uint nInstanceTransformID	: TEXCOORD13 < Semantic( InstanceTransformUv ); >;
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_INPUT
{
	float3 vNormalWs			: TEXCOORD0;

	float2 vTextureCoords		: TEXCOORD1;

	// VS only
	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs		: SV_Position;
	#endif
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_VS )
// Includes -----------------------------------------------------------------------------------------------------------------------------------------------
#include "instancing.fxc"

// Combos -------------------------------------------------------------------------------------------------------------------------------------------------
StaticCombo( S_TRANSLUCENT, F_TRANSLUCENT, Sys( ALL ) );

// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
[shader( "vertex" )] PS_INPUT MainVs( VS_INPUT i )
{
	PS_INPUT o;

	//---------------------------------------------------//
	// Generate Object->World matrix and animation scale //
	//---------------------------------------------------//
	float3x4 matObjectToWorld = GetTransformMatrix( i.nInstanceTransformID );

	// Projected position
	float3 vPositionWs = mul( matObjectToWorld, float4( i.vPositionOs.xyz, 1.0 ) );
	o.vPositionPs.xyzw = Position3WsToPs( vPositionWs.xyz );

	// Worldspace normal
	o.vNormalWs.xyz = normalize( mul( matObjectToWorld, float4( i.vNormalOs.xyz, 0.0 ) ) );
	
	o.vTextureCoords.xy = i.vTexCoord.xy;

	return o;
}

#endif // VS

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_PS )
// Includes -----------------------------------------------------------------------------------------------------------------------------------------------

// Combos -------------------------------------------------------------------------------------------------------------------------------------------------
StaticCombo( S_TRANSLUCENT, F_TRANSLUCENT, Sys( ALL ) );

// Textures -----------------------------------------------------------------------------------------------------------------------------------------------
Texture2D g_tColor < Attribute( "ColorTexture" ); SrgbRead( false ); Filter( MIN_MAG_MIP_LINEAR ); >;

// Render State -------------------------------------------------------------------------------------------------------------------------------------------

// Force stencil to mark pixels as rendered outside for now
RenderState( StencilEnable, true );
RenderState( StencilPassOp, REPLACE );
RenderState( StencilRef, 1 );
RenderState( CullMode, NONE );

#if ( S_TRANSLUCENT )
	RenderState( DepthWriteEnable, false );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, INV_SRC_ALPHA );
#endif

//RenderState( DepthEnable, false );
//RenderState( ColorWriteEnable0, RGBA );
//RenderState( DepthWriteEnable, true );

// Attributes ---------------------------------------------------------------------------------------------------------------------------------------------
#if ( S_TRANSLUCENT )
	BoolAttribute( translucent, true );
#endif

// Globals ------------------------------------------------------------------------------------------------------------------------------------------------
#if ( S_TRANSLUCENT )
	float g_flOpacityScale < Default( 1.0 ); Range( 0.0, 1.0 ); UiGroup( "Translucent" ); Attribute( "g_flOpacityScale" ); >;
#endif

// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_OUTPUT
{
	float4 vColor : SV_Target0;
};

[shader( "pixel" )] PS_OUTPUT MainPs( PS_INPUT i )
{
	PS_OUTPUT o;

	float4 tex = g_tColor.Sample( g_sTrilinearWrap, i.vTextureCoords.xy );
	o.vColor.rgb = tex.rgb;

	#if ( S_TRANSLUCENT )
	{
		o.vColor.a = tex.a * g_flOpacityScale;
	}
	#else
	{
		o.vColor.a = 1.0;
	}
	#endif

	return o;
}

#endif // PS


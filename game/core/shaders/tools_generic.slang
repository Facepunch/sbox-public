// tools_generic: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
// Copyright (c) Valve Corporation, All rights reserved. ======================================================================================================

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "Tools Generic";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#include "sbox/pass.slangh"

Mode( ToolsUtil );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
Feature( F_TRANSLUCENT, 0..1, "Translucent" );
Feature( F_ADDITIVE_BLEND, 0..1, "Translucent" );
FeatureRule( Requires1( F_ADDITIVE_BLEND, F_TRANSLUCENT ), "Requires translucency" );

Feature( F_RENDER_BACKFACES, 0..1, "2-Sided Rendering" );

Feature( F_CODE_SUPPLIED_TEXTURE, 0..1, "Color Texture" );

Feature( F_DISABLE_DEPTH_TEST, 0..1, "Z-Buffering" );
Feature( F_VERTEX_COLOR_TINT, 0..1, "Enable tinting by vertex color stream" );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "system.fxc" // This should always be the first include in COMMON
#include "vr_common.fxc"
#include "common.fxc"
#include "sbox/math.slangh"

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct VS_INPUT
{
	float3 vPositionOs			: POSITION < Semantic( PosXyz ); >;
	float3 vNormalOs			: NORMAL < Semantic( Normal ); >;
	float2 vTexCoord			: TEXCOORD0 < Semantic( LowPrecisionUv ); >;
	#if ( S_VERTEX_COLOR_TINT )
		float4 vColor				: COLOR0 < Semantic( Color ); >;
	#endif

	// Instancing data
	uint nInstanceTransformID	: TEXCOORD13 < Semantic( InstanceTransformUv ); >;
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_INPUT
{
	float3 vNormalWs			: TEXCOORD0;
	float2 vTextureCoords		: TEXCOORD1;
	#if ( S_VERTEX_COLOR_TINT )
		float4 vColor				: COLOR0;
	#endif
	

	// VS only
	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs		: SV_Position;
	#endif

	// PS only
	#if ( PROGRAM == VFX_PROGRAM_PS )
		#if ( S_RENDER_BACKFACES )
			bool face : SV_IsFrontFace;
		#endif
	#endif
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_VS )
// Includes -----------------------------------------------------------------------------------------------------------------------------------------------
#include "instancing.fxc"

// Combos -------------------------------------------------------------------------------------------------------------------------------------------------
StaticCombo( S_TRANSLUCENT, F_TRANSLUCENT, Sys( ALL ) );
StaticCombo( S_VERTEX_COLOR_TINT, F_VERTEX_COLOR_TINT, Sys( ALL ) );
StaticCombo( S_ADDITIVE_BLEND, F_ADDITIVE_BLEND, Sys( ALL ) );

// Constants ----------------------------------------------------------------------------------------------------------------------------------------------
float2 g_vTexCoordScale < Default2( 1.0, 1.0 ); Range2( 0.0, 0.0, 8.0, 8.0 ); UiGroup( "Color/30" ); >;
float2 g_vTexCoordOffset < Default2( 0.0, 0.0 ); Range2( -1.0, -1.0, 1.0, 1.0 ); UiGroup( "Color/40" ); >;

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
	
	// Texture coords
	o.vTextureCoords.xy = i.vTexCoord.xy * g_vTexCoordScale.xy + g_vTexCoordOffset.xy;

	// Vertex color
	#if ( S_VERTEX_COLOR_TINT )
	{
		o.vColor.rgba = i.vColor.rgba;
	}
	#endif
	return o;
}

#endif // VS

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_PS )
// Includes -----------------------------------------------------------------------------------------------------------------------------------------------

// Combos -------------------------------------------------------------------------------------------------------------------------------------------------
StaticCombo( S_CODE_SUPPLIED_TEXTURE, F_CODE_SUPPLIED_TEXTURE, Sys( ALL ) );
StaticCombo( S_TRANSLUCENT, F_TRANSLUCENT, Sys( ALL ) );
StaticCombo( S_RENDER_BACKFACES, F_RENDER_BACKFACES, Sys( ALL ) );
StaticCombo( S_VERTEX_COLOR_TINT, F_VERTEX_COLOR_TINT, Sys( ALL ) );

// Textures -----------------------------------------------------------------------------------------------------------------------------------------------
float3 g_vColorTint < UiType( Color ); Default3( 1.0, 1.0, 1.0 ); UiGroup( "Color,10/20" ); >;
#if ( S_CODE_SUPPLIED_TEXTURE )
	Texture2D g_tCodeSuppliedColor < Attribute( "ColorTexture" ); SrgbRead( false ); Filter( MIN_MAG_MIP_LINEAR ); >;
#else
	CreateInputTexture2D( TextureColor, Srgb, 8, "", "_color", "Color/10", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( TextureTranslucency, Linear, 8, "", "_trans", "Translucent", Default3( 1.0, 1.0, 1.0 ) );
	#if ( S_TRANSLUCENT )
		Texture2D g_tColor < Channel( RGB, Box( TextureColor ), Srgb );
									  Channel( A, Box( TextureTranslucency ), Linear );
									  OutputFormat( DXT5 ); SrgbRead( true ); >;
		#define FetchTranslucency( uv ) ( g_tColor.Sample( g_sDefault, ( uv ).xy ).a )
	#else
		Texture2D g_tColor < Channel( RGB, Box( TextureColor ), Srgb );
									  OutputFormat( DXT1 ); SrgbRead( true ); >;
	#endif
#endif

// Render State -------------------------------------------------------------------------------------------------------------------------------------------
#if ( S_TRANSLUCENT )
	RenderState( DepthWriteEnable, false );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, F_ADDITIVE_BLEND ? ONE : INV_SRC_ALPHA );
#endif

RenderState( DepthFunc, F_DISABLE_DEPTH_TEST ? ALWAYS : GREATER );

// Disable backface culling
#if ( S_RENDER_BACKFACES )
	RenderState( CullMode, NONE );
#endif

// Attributes ---------------------------------------------------------------------------------------------------------------------------------------------
#if ( S_TRANSLUCENT )
	BoolAttribute( translucent, true );
#endif

// Globals ------------------------------------------------------------------------------------------------------------------------------------------------
#if ( S_TRANSLUCENT )
	float g_flOpacityScale < Default( 1.0 ); Range( 0.0, 1.0 ); UiGroup( "Translucent" ); >;
#endif

// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_OUTPUT
{
	float4 vColor : SV_Target0;
};

[shader( "pixel" )] PS_OUTPUT MainPs( PS_INPUT i )
{
	//-----------------------------------------------------------//
	// Negate the world normal if we are rendering the back face //
	//-----------------------------------------------------------//
	#if ( S_RENDER_BACKFACES )
	{
		i.vNormalWs.xyz *= ( i.face ? 1.0 : -1.0 );
	}
	#endif

	PS_OUTPUT o;

	float4 vTexture;
	#if ( S_CODE_SUPPLIED_TEXTURE )
	{
		vTexture.rgba = g_tCodeSuppliedColor.Sample( g_sTrilinearWrap, i.vTextureCoords.xy );
	}
	#else
	{
		vTexture.rgba = g_tColor.Sample( g_sDefault, i.vTextureCoords.xy );
	}
	#endif
	o.vColor.rgb = vTexture.rgb * g_vColorTint.rgb;
	
	#if ( S_VERTEX_COLOR_TINT )
	{
		o.vColor.rgb *= i.vColor.rgb;
	}
	#endif

	#if ( S_TRANSLUCENT )
	{
		o.vColor.a = vTexture.a * g_flOpacityScale;
		#if ( S_VERTEX_COLOR_TINT )
		{
			o.vColor.a *= i.vColor.a;
		}
		#endif
	}
	#else
	{
		o.vColor.a = 1.0;
	}
	#endif

	return o;
}

#endif // PS


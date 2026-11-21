// Copyright (c) Valve Corporation, All rights reserved. ======================================================================================================
//
// BEWARE
//   The purpose of this shader is NOT for material authoring. It is for 
//   displaying legacy materials, authored using the generic shader from
//   /core/, in the HLVR renderer. DO NOT change the way this shader processes
//   textures - otherwise errors will result.
//
//=============================================================================================================================================================

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
HEADER
{
	DevShader = true;
	CompileTargets = ( IS_SM_50 && ( PC || VULKAN ) );
	Description = "Used to display legacy content. Do not use to author new materials.";
	Version = 25
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
MODES
{
	Forward();

	Depth( S_MODE_DEPTH ); 

	ToolsShadingComplexity( "tools_shading_complexity.shader" );
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
FEATURES
{
	Feature( F_SPECULAR, 0..1, "Specular" );

	Feature( F_UNLIT, 0..1, "Lighting" );
	FeatureRule( Allow1( F_UNLIT, F_SPECULAR ), "Specular and Unlit are not compatible" );

	Feature( F_ALPHA_TEST, 0..1, "Translucent" );
	Feature( F_TRANSLUCENT, 0..1, "Translucent" );
	Feature( F_ADDITIVE_BLEND, 0..1, "Translucent" );
	FeatureRule( Allow1( F_TRANSLUCENT, F_ALPHA_TEST ), "Translucent and Alpha Test are not compatible" );
	FeatureRule( Requires1( F_ADDITIVE_BLEND, F_TRANSLUCENT ), "Requires translucency" );
		
	Feature( F_RENDER_BACKFACES, 0..1, "Rendering" );

	Feature( F_SELF_ILLUM, 0..1, "Self Illum" );
	FeatureRule( Allow1( F_SELF_ILLUM, F_UNLIT ), "Self Illum and Unlit are not compatible" );
	
	Feature( F_TINT_MASK, 0..1, "Per-Instance Tint Mask" );
	FeatureRule( Allow1( F_UNLIT, F_TINT_MASK ), "Tint Mask and Unlit are not compatible" );	
	Feature( F_PAINT_VERTEX_COLORS, 0..1, "Vertex Paint" );
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
COMMON
{
	#define MATERIAL_CONTROLLED_BACKFACES 1
	#include "system.fxc" // This should always be the first include in COMMON
	#include "vr_common.fxc"

	#define S_SPECULAR_CUBE_MAP S_SPECULAR

	StaticCombo( S_SPECULAR, F_SPECULAR, Sys( ALL ) );
	StaticCombo( S_UNLIT, F_UNLIT, Sys( ALL ) );
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct VS_INPUT
{
	//
	// Common Data Fields
	//
	#include "vr_shared_standard_vs_input.fxc"
	#if ( S_PAINT_VERTEX_COLORS )
		float4 vColorPaintValues : TEXCOORD5 < Semantic( VertexPaintTintColor ); >;
	#endif
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_INPUT
{
	//
	// Common Data Fields
	//
	#include "vr_common_ps_input.fxc"
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
VS
{
	StaticCombo( S_PAINT_VERTEX_COLORS, F_PAINT_VERTEX_COLORS, Sys( ALL ) );
	BoolAttribute( VertexPaintUIPickColor, F_PAINT_VERTEX_COLORS );

	// Includes -----------------------------------------------------------------------------------------------------------------------------------------------
	#include "system.fxc"
	#include "vr_common_vs_code.fxc"

	DynamicCombo( D_BAKED_LIGHTING_FROM_LIGHTMAP, 0..1, Sys( ALL ) );
	
	// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
	PS_INPUT MainVs( VS_INPUT i )
	{
		PS_INPUT o = VS_CommonProcessing( i );

		#if ( S_PAINT_VERTEX_COLORS )
			// A zero alpha denotes a missing paint stream, as in the blendable shader.
			if ( i.vColorPaintValues.a != 0 )
				o.vVertexColor.rgb *= i.vColorPaintValues.rgb;
		#endif

		return VS_CommonProcessing_Post( o );
	}
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
PS
{
	// Includes -----------------------------------------------------------------------------------------------------------------------------------------------
	#include "system.fxc"
	#include "bump_strength.fxc"

	#define DEPTH_STATE_ALREADY_SET
	#define COLOR_WRITE_ALREADY_SET
	#define BLEND_MODE_ALREADY_SET
	#include "vr_common_ps_code.fxc"

	// Combos -------------------------------------------------------------------------------------------------------------------------------------------------
	StaticCombo( S_UNLIT, F_UNLIT, Sys( ALL ) );
	StaticCombo( S_ALPHA_TEST, F_ALPHA_TEST, Sys( ALL ) );
	StaticCombo( S_TRANSLUCENT, F_TRANSLUCENT, Sys( ALL ) );
	StaticCombo( S_SELF_ILLUM, F_SELF_ILLUM, Sys( ALL ) );
	StaticCombo( S_ADDITIVE_BLEND, F_ADDITIVE_BLEND, Sys( ALL ) );
	StaticCombo( S_TINT_MASK, F_TINT_MASK, Sys( ALL ) );

	// Artist generated textures ------------------------------------------------------------------------------------------------------------------------------

	// Color and translucency
	float3 g_vColorTint < UiType( Color ); Default3( 1.0, 1.0, 1.0 ); UiGroup( "Color,10/20" ); >;
	CreateInputTexture2D( TextureColor, Srgb, 8, "", "_color", "Color/10", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( TextureTranslucency, Linear, 8, "", "_trans", "Translucent", Default3( 1.0, 1.0, 1.0 ) );
	#if ( S_ALPHA_TEST )
		Texture2D g_tColor < Channel( RGB, AlphaWeighted( TextureColor, TextureTranslucency ), Srgb );
									  Channel( A, PreserveCoverage( TextureTranslucency ), Linear );
									  OutputFormat( DXT5 ); SrgbRead( true ); >;
		#define FetchTranslucency( uv ) ( g_tColor.Sample( g_sDefault, ( uv ).xy ).a )
	#elif ( S_TRANSLUCENT )
		Texture2D g_tColor < Channel( RGB, AlphaWeighted( TextureColor, TextureTranslucency ), Srgb );
									  Channel( A, Box( TextureTranslucency ), Linear );
									  OutputFormat( DXT5 ); SrgbRead( true ); >;
		#define FetchTranslucency( uv ) ( g_tColor.Sample( g_sDefault, ( uv ).xy ).a )
	#else
		Texture2D g_tColor < Channel( RGB, Box( TextureColor ), Srgb );
									  OutputFormat( DXT1 ); SrgbRead( true ); >;
	#endif

	#if ( S_TINT_MASK )
		CreateInputTexture2D( TextureTintMask, Linear, 8, "", "_mask", "Color", Default3( 1.0, 1.0, 1.0 ) );
		Texture2D g_tTintMask < Channel( G, Box( TextureTintMask ), Linear ); OutputFormat( DXT1 ); SrgbRead( false ); >;
	#endif

	// Normal map
	CreateInputTexture2D( TextureNormal, Linear, 8, "NormalizeNormals", "_normal", "Normal,20", Default3( 0.5, 0.5, 1.0 ) );
	Texture2D g_tNormal < Channel( AG, MipNormal( TextureNormal ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;

	// Specular reflectance
	CreateInputTexture2D( TextureReflectance, Linear, 8, "", "_refl", "Lighting,30/Reflectance,10/10", Default3( 0.1, 0.1, 0.1 ) );
	float2 g_vReflectanceRange < Default2( 0.0, 1.0 ); Range2( 0.0, 0.0, 1.0, 1.0 ); UiGroup( "Lighting,30/Reflectance/20" ); >;

	// Aniso roughness (storing separately for now so we can see a before/after with additional runtime data)
	CreateInputTexture2D( TextureGlossiness, Linear, 8, "", "_gloss", "Lighting,30/Gloss,20/10", Default3( 0.5, 0.5, 0.5 ) );
	Texture2D g_tRoughness < Channel( RGBA, AnisoNormal( TextureNormal, TextureGlossiness ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >; // Filter( MIN_MAG_MIP_POINT ); >;
	float2 g_vGlossinessRange < Default2( 0.0, 1.0 ); Range2( 0.0, 0.0, 1.0, 1.0 ); UiGroup( "Lighting,30/Gloss/20" ); >;

	// Metalness
	CreateInputTexture2D( TextureMetalness, Linear, 8, "", "_metal", "Lighting,30/Metalness,30/10", Default3( 0.0, 0.0, 0.0 ) );
	float2 g_vMetalnessRange < Default2( 0.0, 1.0 ); Range2( 0.0, 0.0, 1.0, 1.0 ); UiGroup( "Lighting,30/Metalness/20" ); >;

	// Combine into a single runtime texture
	Texture2D g_tMetalnessReflectanceFresnel <
		Channel( R, Box( TextureMetalness ), Linear );
		Channel( G, Box( TextureReflectance ), Linear );
		OutputFormat( DXT1 );
		SrgbRead( false ); >;

	// Self Illum
	#if ( S_SELF_ILLUM )
		CreateInputTexture2D( TextureSelfIllumMask, Linear, 8, "", "_selfillum", "Self Illum,60", Default3( 1.0, 1.0, 1.0 ) );
		Texture2D g_tSelfIllumMask < Channel( RGB, Box( TextureSelfIllumMask ), Linear ); OutputFormat( DXT1 ); SrgbRead( false ); >;
	#endif

	// Attributes ---------------------------------------------------------------------------------------------------------------------------------------------

	BoolAttribute( SupportsLightmapping, F_UNLIT ? false : true );
	BoolAttribute( PerVertexLighting, F_UNLIT ? false : true );

	// Precomputed lighting info
	TextureAttribute( LightSim_DiffuseAlbedoTexture, g_tColor );

	#if ( S_SELF_ILLUM )
		TextureAttribute( LightSim_SelfIllumMaskTexture, g_tSelfIllumMask );
		Float3Attribute( LightSim_SelfIllumTint, g_vSelfIllumTint );
		FloatAttribute( LightSim_SelfIllumScale, g_flSelfIllumScale );
	#endif

	TextureAttribute( RepresentativeTexture, g_tColor );
	BoolAttribute( SupportsMappingDimensions, true );
	
	// System Textures ----------------------------------------------------------------------------------------------------------------------------------------

	// Render State -------------------------------------------------------------------------------------------------------------------------------------------

	#if ( S_TRANSLUCENT || S_ALPHA_TEST )
		RenderState( ColorWriteEnable0, RGB );
	#else
		RenderState( ColorWriteEnable0, RGBA );
	#endif

	RenderState( BlendEnable, F_TRANSLUCENT ? true : false );
	RenderState( SrcBlend, SRC_ALPHA );
	#if ( S_ADDITIVE_BLEND )
		RenderState( DstBlend, ONE );
	#else
		RenderState( DstBlend, INV_SRC_ALPHA );
	#endif

	RenderState( DepthEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );

	#if ( S_TRANSLUCENT )
		RenderState( DepthWriteEnable, false );
	#else
		RenderState( DepthWriteEnable, true );
	#endif

	// Attributes ---------------------------------------------------------------------------------------------------------------------------------------------
	BoolAttribute( translucent, ( F_TRANSLUCENT ) ? true : false );
	BoolAttribute( alphatest, ( F_ALPHA_TEST ) ? true : false );

	// Globals ------------------------------------------------------------------------------------------------------------------------------------------------
	#if ( S_ALPHA_TEST )
		float g_flAlphaTestReference < Default ( 0.5 ); Range( 0.01, 0.99 ); UiGroup( "Translucent" ); >;
	#endif

	float g_flOpacityScale < Default( 1.0 ); Range( 0.0, 1.0 ); UiGroup( "Translucent" ); >;

	float3 g_vSelfIllumTint < UiType( Color ); Default3( 1.0, 1.0, 1.0 ); UiGroup( "Self Illum" ); >;
	float g_flSelfIllumScale < Default( 1.0 ); Range( 0.0, 16.0 ); UiGroup( "Self Illum" ); >;

	// Helpers ------------------------------------------------------------------------------------------------------------------------------------------------
	float3 FetchReflectanceFresnelMetalness( float2 vTextureCoords )
	{
		#if ( S_SPECULAR )
		{
			// Fetch params from texture
			float3 vMetalnessReflectanceFresnel = g_tMetalnessReflectanceFresnel.Sample( g_sDefault, vTextureCoords.xy ).rgb;

			// Metalness
			float flMetalness = vMetalnessReflectanceFresnel.r;
			float2 vMetalnessScaleAndBias = float2( max( g_vMetalnessRange.x, g_vMetalnessRange.y ) - g_vMetalnessRange.x, g_vMetalnessRange.x );
			flMetalness = ( flMetalness * vMetalnessScaleAndBias.x ) + vMetalnessScaleAndBias.y;

			// Reflectance
			float flReflectance = vMetalnessReflectanceFresnel.g;

			float2 vReflectanceScaleAndBias = float2( max( g_vReflectanceRange.x, g_vReflectanceRange.y ) - g_vReflectanceRange.x, g_vReflectanceRange.x );
			flReflectance = ( flReflectance * vReflectanceScaleAndBias.x ) + vReflectanceScaleAndBias.y;

			// Fresnel Facing
			float flFresnelExponent = 5.0;

			return float3( flReflectance, flFresnelExponent, flMetalness );
		}
		#else
		{
			return float3( 0.0, 5.0, 0.0 );
		}
		#endif
	}

	//-------------------------------------------------------------------------------------------------------------------------------------------------------------
	float ComputeRoughnessFromTexel( float4 vRoughnessTexel, float2 vRoughnessScaleAndBias )
	{
		// NOTE: Currently computing isotropic roughness. Anisotropic roughness coming soon.
		float flRoughness = ( vRoughnessTexel.b + vRoughnessTexel.a ) * 0.5; // .ba is just the artist authored roughness without the normal ellipses
		flRoughness = ( flRoughness * vRoughnessScaleAndBias.x ) + vRoughnessScaleAndBias.y; // Put into range artists set in met

		// Add in isotropic roughness from mipped normals
		flRoughness += ( vRoughnessTexel.r + vRoughnessTexel.g ) * 0.5;
		flRoughness = min( 1.0, flRoughness ); // Clamp total roughness to 1.0

		return flRoughness;
	}

	// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
	PS_OUTPUT MainPs( PS_INPUT i )
	{
		FinalCombinerInput_t f = PS_CommonProcessing( i );

		// Albedo
		float3 vAlbedo = g_tColor.Sample( g_sDefault, i.vTextureCoords.xy ).rgb;

		// Apply artist color tint and per-vertex color tint, blend using tint mask.
		// Note: The per-vertex color tint includes the per-instance color tint.
		float flTintMask = 1.0;
		#if ( S_TINT_MASK )
		{
			flTintMask = g_tTintMask.Sample( g_sDefault, i.vTextureCoords.xy ).g;
		}
		#endif
		vAlbedo.rgb = lerp( vAlbedo.rgb, vAlbedo.rgb * g_vColorTint.rgb * i.vVertexColor.rgb, flTintMask );

		// Tangent & World Normal
		#if ( !S_UNLIT )
		{
			float4 vNormalTexel = g_tNormal.Sample( g_sDefault, i.vTextureCoords.xy );

			// Convert DXT5_NM format to a float3 tangent normal
			float3 vNormalTs = ComputeNormalFromDXT5Texture( vNormalTexel.rgba );

			// HACK: Tools still generate tangent space the inverted Source1 way where positive y is down. Flipping the normal here to compensate.
			vNormalTs.y = -vNormalTs.y;

			// Apply bump strength
			vNormalTs.xyz = ApplyBumpStrength( vNormalTs.xyz );

			PS_CommonTransformNormal( i, f, vNormalTs );
		}
		#endif

		#if ( S_SPECULAR )
		{
			// Roughness
			float4 vRoughnessTexel = g_tRoughness.Sample( g_sDefault, i.vTextureCoords.xy );
			float2 vRoughnessScaleAndBias = float2( max( 1.0 - g_vGlossinessRange.x, 1.0 - g_vGlossinessRange.y ) - ( 1.0 - g_vGlossinessRange.y ), 1.0 - g_vGlossinessRange.y );
			f.vRoughness.xy = ComputeRoughnessFromTexel( vRoughnessTexel.rgba, vRoughnessScaleAndBias.xy ).xx;

			// Metalness
			float flMetalness = FetchReflectanceFresnelMetalness( i.vTextureCoords.xy ).z;
			f = CalculateDiffuseAndSpecularFromAlbedoAndMetalness( f, vAlbedo, flMetalness );
		}
		#else
		{
			f.vDiffuseColor.rgb = vAlbedo.rgb;
		}
		#endif

		// Alpha blend
		#if ( S_TRANSLUCENT )
		{
			f.flOpacity = i.vVertexColor.a;
			f.flOpacity *= FetchTranslucency( i.vTextureCoords.xy );
			f.flOpacity *= g_flOpacityScale;
		}
		#endif

		// Alpha test
		#if ( S_ALPHA_TEST )
		{
			float flTranslucency = FetchTranslucency( i.vTextureCoords.xy );
			if ( flTranslucency < g_flAlphaTestReference )
			{
				clip( -1.0 );
			}
		}
		#endif

		// Self Illum
		#if ( S_SELF_ILLUM )
		{
			float flSelfIllumMask = g_tSelfIllumMask.Sample( g_sDefault, i.vTextureCoords.xy ).g;
			float3 vSelfIllumTintScaled = g_flSelfIllumScale * g_vSelfIllumTint.xyz;
			f.vEmissive.rgb = vSelfIllumTintScaled.rgb * vAlbedo.rgb * flSelfIllumMask;
			f.vDiffuseColor.rgb *= ( 1.0 - flSelfIllumMask );
			f.vSpecularColor.rgb *= ( 1.0 - flSelfIllumMask );
		}
		#endif

		return PS_FinalCombiner( f );
	}
}

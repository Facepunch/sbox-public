// sky: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md). The .shader
// beside it is only Source 2's renderer's.
// Copyright (c) Valve Corporation, All rights reserved. ======================================================================================================

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = false;
// HEADER: CompileTargets = ( IS_SM_50 && ( PC || VULKAN ) );
// HEADER: Description = "Used to draw the skybox";
// HEADER: Version = 8;

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#include "sbox/pass.slangh"

Mode( Forward );
Mode( ToolsShadingComplexity, S_MODE_TOOLS_SHADING_COMPLEXITY );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
Feature( F_LIGHTING_ONLY, 0..1, "Rendering" );
Feature( F_TEXTURE_FORMAT2, 0..6( 0 = "HDR (float16)", 1 = "RGB (8-bit uncompressed)", 2 = "YCoCg (dxt compressed)", 3 = "Dxt1 (LDR)", 4 = "RGBM (dxt compressed)", 5 = "RGBM (8-bit uncompressed)", 6 = "BC6H (HDR compressed - recommended)" ), "Format" );   // the .shader's enum misses its closing bracket, which vfx forgives

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "system.fxc" // This should always be the first include in COMMON
#include "vr_common.fxc"
#include "math_general.fxc"
#include "sky.fxc"

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct VS_INPUT
{
	float4 vPositionOs : POSITION < Semantic( PosXyz ); >;
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_INPUT
{
	float3 vSkyLookupInterpolant : TEXCOORD0;
	float3 vPositionWs : TEXCOORD1;

	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs	: SV_Position;
	#endif
	#if ( PROGRAM == VFX_PROGRAM_PS )
		float4 vPositionSs : SV_Position;
	#endif
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_VS )
	// Includes -----------------------------------------------------------------------------------------------------------------------------------------------
	#define IS_SPRITECARD 1// To avoid compile errors with missing VS_INPUT
	#include "vr_lighting.fxc"

	// Combos -------------------------------------------------------------------------------------------------------------------------------------------------
	StaticCombo( S_LIGHTING_ONLY, F_LIGHTING_ONLY, Sys( ALL ) );

	// Constants ----------------------------------------------------------------------------------------------------------------------------------------------

	// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
	[shader( "vertex" )] PS_INPUT MainVs( const VS_INPUT i )
	{
		PS_INPUT o;

		o.vPositionWs = i.vPositionOs.xyz;

		o.vSkyLookupInterpolant.xyz = ComputeSkyLookupInterpolant( i.vPositionOs.xyz, false );
		#if ( S_LIGHTING_ONLY )
		{
			o.vPositionPs.xyzw = float4( 0.0, 0.0, -1.0, 1.0 );		// Make sure the sky doesn't render on screen during the normal scene rendering pass
		}
		#else
        {
			float flSkyboxScale = g_flNearPlane + g_flFarPlane;
			float3 vPositionWs = g_vCameraPositionWs.xyz + i.vPositionOs.xyz * flSkyboxScale;
			
			o.vPositionPs.xyzw = Position3WsToPs( vPositionWs.xyz );
			o.vPositionWs.xyz = vPositionWs;
		}
		#endif

		return o;
	}

#endif // VS

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_PS )
// Includes -----------------------------------------------------------------------------------------------------------------------------------------------

#include "vr_lighting.fxc"
#include "volumetric_fog.fxc"
#include "common/classes/Fog.hlsl"

// Combos -------------------------------------------------------------------------------------------------------------------------------------------------
StaticCombo( S_MODE_TOOLS_SHADING_COMPLEXITY, 0..1, Sys( ALL ) );

StaticCombo( S_TEXTURE_FORMAT, F_TEXTURE_FORMAT2, Sys( ALL ) );
#define TEXTURE_FORMAT_HDR		0	// float16
#define TEXTURE_FORMAT_RGB		1	// int8
#define TEXTURE_FORMAT_YCOCG	2	// dxt5
#define TEXTURE_FORMAT_DXT1		3	// dxt1
#define TEXTURE_FORMAT_RGBM		4	// dxt5
#define TEXTURE_FORMAT_RGBMU	5	// int8
#define TEXTURE_FORMAT_BC6H		6	// BC6H

DynamicCombo( D_FOG_TYPE, 0..2, Sys( ALL ) ); // 0 = none, 1 = distance, 2 = angular
DynamicCombo( D_MIN_ANGULAR_FOG, 0..1, Sys( ALL ) );
DynamicComboRule( Requires1( D_MIN_ANGULAR_FOG, D_FOG_TYPE == 2 ) );
DynamicComboRule( Allow1( S_MODE_TOOLS_SHADING_COMPLEXITY, D_FOG_TYPE ) );

DynamicCombo( D_QUAD_OVERDRAW, 0..1, Sys( ALL ) );
DynamicComboRule( Requires1( D_QUAD_OVERDRAW, S_MODE_TOOLS_SHADING_COMPLEXITY ) );

// Attributes ---------------------------------------------------------------------------------------------------------------------------------------------
BoolAttribute( sky, true );

// Textures -----------------------------------------------------------------------------------------------------------------------------------------------

#if ( S_TEXTURE_FORMAT == TEXTURE_FORMAT_HDR )
	CreateInputTextureCube( SkyTexture, Srgb, 8, "", "", "Texture", Default3( 1.0, 1.0, 1.0 ) );
	CreateTextureCube( g_tSkyTexture ) < Channel( RGB, GGXCubeMapBlur( SkyTexture ), Linear ); OutputFormat( RGBA16161616F ); SrgbRead( false ); >;
#elif ( S_TEXTURE_FORMAT == TEXTURE_FORMAT_RGB )
	CreateInputTextureCube( SkyTexture, Srgb, 8, "", "", "Texture", Default3( 1.0, 1.0, 1.0 ) );
	CreateTextureCube( g_tSkyTexture ) < Channel( RGB, GGXCubeMapBlur( SkyTexture ), Srgb ); OutputFormat( RGBA8888 ); SrgbRead( true ); >;
#elif ( S_TEXTURE_FORMAT == TEXTURE_FORMAT_YCOCG )
	CreateInputTextureCube( SkyTexture, Linear, 8, "ConvertToYCoCg", "", "Texture", Default4( 0.0, 0.0, 0.0, 1.0 ) );
	CreateTextureCube( g_tSkyTexture ) < Channel( RGBA, GGXCubeMapBlur( SkyTexture ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
#elif ( S_TEXTURE_FORMAT == TEXTURE_FORMAT_DXT1 )
	CreateInputTextureCube( SkyTexture, Srgb, 8, "", "", "Texture", Default3( 1.0, 1.0, 1.0 ) );
	CreateTextureCube( g_tSkyTexture ) < Channel( RGB, GGXCubeMapBlur( SkyTexture ), Srgb ); OutputFormat( DXT1 ); SrgbRead( true ); >;
#elif ( S_TEXTURE_FORMAT == TEXTURE_FORMAT_RGBM )
	CreateInputTextureCube( SkyTexture, Srgb, 8, "", "", "Texture", Default3( 1.0, 1.0, 1.0 ) );
	CreateTextureCube( g_tSkyTexture ) < Channel( RGBA, GGXCubeMapBlur_RGBM( SkyTexture ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
#elif ( S_TEXTURE_FORMAT == TEXTURE_FORMAT_BC6H )
	CreateInputTextureCube( SkyTexture, Srgb, 8, "", "", "Texture", Default3( 1.0, 1.0, 1.0 ) );
	CreateTextureCube( g_tSkyTexture ) < Channel( RGB, GGXCubeMapBlur( SkyTexture ), Linear ); OutputFormat( BC6H ); SrgbRead( false ); >;
#elif ( S_TEXTURE_FORMAT == TEXTURE_FORMAT_RGBMU )
	CreateInputTextureCube( SkyTexture, Srgb, 8, "", "", "Texture", Default3( 1.0, 1.0, 1.0 ) );
	CreateTextureCube( g_tSkyTexture ) < Channel( RGBA, GGXCubeMapBlur_RGBM( SkyTexture ), Srgb ); OutputFormat( RGBA8888 ); SrgbRead( true ); >;
#endif

// Render State -------------------------------------------------------------------------------------------------------------------------------------------
#if ( S_MODE_TOOLS_SHADING_COMPLEXITY )
	RenderState( ColorWriteEnable0, RGBA );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, ONE );
#endif

RenderState( CullMode, NONE );
RenderState( DepthWriteEnable, false );
RenderState( DepthEnable, true );
RenderState( DepthFunc, GREATER_EQUAL );

// Globals ------------------------------------------------------------------------------------------------------------------------------------------------
float3 g_vTint < Attribute( "SkyTint" ); Default3( 1.0, 1.0, 1.0 ); >;

float g_flFogMinDotStart< Attribute( "FogMinStart" ); >;
float g_flFogMinDotEnd< Attribute( "FogMinEnd" ); >;
float g_flFogMaxDotStart< Attribute( "FogMaxStart" ); >;
float g_flFogMaxDotEnd< Attribute( "FogMaxEnd" ); >;

float4 g_vShadingComplexity < Source( ShadingComplexity ); >;

// Helpers ------------------------------------------------------------------------------------------------------------------------------------------------
float GetAngularFogAmount( float3 vEyeToSkyDirWs )
{
	float flSkyDot = vEyeToSkyDirWs.z;

	float flFogMaxSkyAtten = 1 - saturate( ( flSkyDot - g_flFogMaxDotStart ) / max( 0.001f, g_flFogMaxDotEnd - g_flFogMaxDotStart ) );

	#if ( D_MIN_ANGULAR_FOG )
		float flFogMinSkyAtten = saturate( ( flSkyDot - g_flFogMinDotEnd ) / max( 0.0001f, g_flFogMinDotStart - g_flFogMinDotEnd ) );			
		return min( flFogMinSkyAtten, flFogMaxSkyAtten );
	#else
		return flFogMaxSkyAtten;
	#endif
}

	
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
float _PsFog( float flDist )
{
	float flFog = saturate( flDist * g_flInvFogRange + g_flNegFogStartOverFogRange );
	flFog = pow( flFog, g_flFogExponent );
	return min( g_flFogMaxDensity, flFog );
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
float _PsRadialFog( float3 vPositionWs )
{
	float flDist = distance( vPositionWs.xyz, g_vCameraPositionWs.xyz );
	return _PsFog( flDist );
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
float CalculateFogFactor( float3 vPositionWs )
{
	return _PsRadialFog( vPositionWs.xyz );
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
float CalculateHeightFogFactor( float flPlaneDist )
{
	return _PsFog( flPlaneDist );
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
float3 ApplyFog( float3 vInputColor, float3 vFogColor, float flFogFactor )
{
	return lerp( vInputColor.rgb, vFogColor.rgb, saturate( flFogFactor ) );
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
float3 CalculateAndApplyFog( float3 vInputColor, float3 vPositionWs )
{
	float flFogFactor = CalculateFogFactor( vPositionWs.xyz );
	return ApplyFog( vInputColor.rgb, g_vFogColor.rgb, flFogFactor );
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
float3 CalculateAndApplyBlackFog( float3 vInputColor, float3 vPositionWs )
{
	float flFogFactor = CalculateFogFactor( vPositionWs.xyz );
	return ApplyFog( vInputColor.rgb, float3( 0, 0, 0 ), flFogFactor );
}

// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_OUTPUT
{
	float4 vColor : SV_Target0;
};

#if ( D_QUAD_OVERDRAW )
	#include "quad_overdraw_ps.fxc"
#else
	[shader( "pixel" )] PS_OUTPUT MainPs( PS_INPUT i )
	{
		PS_OUTPUT o;

		#if ( S_MODE_TOOLS_SHADING_COMPLEXITY )
		{
			o.vColor.rgba = g_vShadingComplexity.rgba;
		}
		#else
		{
			float3 vEyeToSkyDirWs = normalize( i.vSkyLookupInterpolant.xyz );

			#if ( S_TEXTURE_FORMAT == TEXTURE_FORMAT_YCOCG )
			{
				float4 vYCoCg = TexCube( g_tSkyTexture, vEyeToSkyDirWs.xyz );
				float flCoCgScale = ( vYCoCg.b * ( 255.0 / 8.0 ) ) + 1.0;
				float flCo = ( vYCoCg.r - ( 0.5 * 256.0 / 255.0 ) ) / flCoCgScale;
				float flCg = ( vYCoCg.g - ( 0.5 * 256.0 / 255.0 ) ) / flCoCgScale;
				float flY = vYCoCg.a;
				float4 vColor = float4( flY + flCo - flCg, flY + flCg, flY - flCo - flCg, 1.0 );
				vColor.rgb = SrgbGammaToLinear( vColor.rgb );	// Need this over a pow(2.2) to match uncompressed hardware srgb-decoded as closely as possible
				o.vColor = vColor;
			}
			#elif ( S_TEXTURE_FORMAT == TEXTURE_FORMAT_RGBM || S_TEXTURE_FORMAT == TEXTURE_FORMAT_RGBMU )
			{
				o.vColor.rgb = RGBMDecode( TexCube( g_tSkyTexture, vEyeToSkyDirWs.xyz ).rgba );
			}
			#else
			{
				o.vColor.rgba = TexCubeLevel( g_tSkyTexture, vEyeToSkyDirWs.xyz, 0 );
			}
			#endif

			o.vColor.rgb = max( o.vColor.rgb * g_vTint.rgb, float3( 0.0f, 0.0f, 0.0f ) );
			float3 unfoggedColor = o.vColor.rgb;

			#if ( D_FOG_TYPE == 2 )
			{
				o.vColor.rgb = lerp( o.vColor.rgb, g_vFogColor.rgb, GetAngularFogAmount( vEyeToSkyDirWs.xyz ) );
			}
			#elif ( D_FOG_TYPE == 1 )
			{
				o.vColor.rgb = CalculateAndApplyFog( o.vColor.rgb, g_vCameraPositionWs.xyz + g_flFarPlane * vEyeToSkyDirWs.xyz );
			}
			#endif

			o.vColor.rgb = Fog::ApplySky( i.vPositionWs, i.vPositionSs.xy, unfoggedColor, o.vColor.rgb );
			o.vColor.a = 1;
		}
		#endif

		return o;
	}
#endif

#endif // PS


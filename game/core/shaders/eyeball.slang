// eyeball.shader as a pass module: Borealis.Gpu draws this; the .shader stays for the other routes
// and as the fallback (docs/reference/borealis-slang-materials.md, "Pass modules").
// Copyright (c) Valve Corporation, All rights reserved. ======================================================================================================

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = false;
// HEADER: CompileTargets = ( IS_SM_50 && ( PC || VULKAN ) );
// HEADER: Description = "Eyeballs";
// HEADER: Version = 22;

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// MODES: Forward();
// MODES: Depth( S_MODE_DEPTH );
// MODES: ToolsShadingComplexity( "tools_shading_complexity.shader" );

#include "sbox/pass.slangh"

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
Feature( F_SPECULAR_CUBE_MAP, 0..2( 0 = "None", 1 = "In-game Cube Map", 2 = "Artist Cube Map" ), "Specular" );

Feature( F_SELF_ILLUM, 0..1, "Self Illum" );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#define S_SUBSURFACE_SCATTERING SUBSURFACE_SCATTERING_WRAP
#define S_SPECULAR 1

#include "system.fxc" // This should always be the first include in COMMON
#include "vr_common.fxc"

//=========================================================================================================================

struct VS_INPUT
{
	#include "vr_shared_standard_vs_input.fxc"
};

//=========================================================================================================================

struct PS_INPUT
{
	#include "vr_shared_standard_ps_input.fxc"
	
	float2 vOcclusionUV : TEXCOORD15;
};

//=========================================================================================================================

#if ( PROGRAM == VFX_PROGRAM_VS )
#include "vr_shared_standard_vs_code.fxc"

float4x4 g_vEyeOcclusionViewProj < Attribute("EyeOcclusionViewProj"); >;

//
// Main
//
[shader( "vertex" )] PS_INPUT MainVs( VS_INPUT i )
{
	PS_INPUT o = VS_SharedStandardProcessing( i );
	
	float3x4 matObjectToWorld = GetTransformMatrix( i.nInstanceTransformID );

	float3 vPositionWs = mul( matObjectToWorld, float4( i.vPositionOs.xyz, 1.0 ) );

	float4 vOcclusionCs = mul( g_vEyeOcclusionViewProj, float4( vPositionWs.xyz, 1 ) );
	o.vOcclusionUV.xy = ( vOcclusionCs.xy ) * float2( 0.5, -0.5 ) + float2( 0.5, 0.5 );

	return VS_CommonProcessing_Post( o );
}

#endif // VS

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_PS )
// Includes -----------------------------------------------------------------------------------------------------------------------------------------------
#include "vr_common_ps_code.fxc"
#include "common/classes/Fog.hlsl"

// Disable this warning since our tangent frame is computed with ddx/ddy instructions, and this is somehow tripping this warning when it shouldn't.
#pragma warning( disable : 4121 ) // warning X4121: gradient-based operations must be moved out of flow control to prevent divergence

// Combos -------------------------------------------------------------------------------------------------------------------------------------------------
StaticCombo( S_SPECULAR_CUBE_MAP, F_SPECULAR_CUBE_MAP, Sys( ALL ) );
StaticComboRule( Allow1( S_MODE_DEPTH, S_SPECULAR_CUBE_MAP ) );

StaticCombo( S_SELF_ILLUM, F_SELF_ILLUM, Sys( ALL ) );
StaticComboRule( Allow1( S_MODE_DEPTH, S_SELF_ILLUM ) );

DynamicCombo( D_OPAQUE_FADE, 0..1, Sys( ALL ) );

// Artist generated textures ------------------------------------------------------------------------------------------------------------------------------

// Specular reflectance
CreateInputTexture2D( TextureReflectance, Linear, 8, "", "_refl", "Lighting,30/Reflectance,10/10", Default3( 0.1, 0.1, 0.1 ) );
float2 g_vReflectanceRange < Default2( 0.0, 1.0 ); Range2( 0.0, 0.0, 1.0, 1.0 ); UiGroup( "Lighting,30/Reflectance/20" ); > ;

// Color and translucency
float3 g_vColorTint < UiType( Color ); Default3( 1.0, 1.0, 1.0 ); UiGroup( "Color,10/20" ); >;
CreateInputTexture2D( TextureColor, Srgb, 8, "", "_color", "Color/10", DefaultFile( "materials/default/default_eyes_color.psd" ) );
CreateInputTexture2D( TextureTranslucency, Linear, 8, "", "_trans", "Translucent,15", Default3( 1.0, 1.0, 1.0 ) );

Texture2D g_tColor < Channel( RGB, Box( TextureColor ), Srgb );
							  Channel( A, Box( TextureReflectance ), Linear );
							  OutputFormat( BC7 ); SrgbRead( true ); >;

// Outer eye, including cornea
CreateInputTexture2D( TextureNormal, Linear, 8, "NormalizeNormals", "_normal", "Normal,20", Default3AndFile( 0.5, 0.5, 1.0, "materials/default/default_eyes_normal.psd" ) );
Texture2D g_tNormal < Channel( RGBA, HemiOctNormal( TextureNormal ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;

// Height
//CreateInputTexture2D( TextureHeight, Linear, 8, "", "_height", "Height", Default3AndFile( 0.0, 0.0, 0.0, "materials/default/default_eyes_height.psd" ) );
//Texture2D g_tHeight < Channel( RGB, Box( TextureHeight ), Linear ); OutputFormat( DXT1 ); SrgbRead( false ); > ;

// Iris mask
CreateInputTexture2D( TextureIrisMask, Linear, 8, "", "_mask", "Iris,30/Mask,10", Default3AndFile( 0.0, 0.0, 0.0, "materials/default/default_eyes_height.psd" ) );
Texture2D g_tIrisMask < Channel( R, Box( TextureIrisMask ), Linear ); OutputFormat( ATI1N ); SrgbRead( false ); >;

// Iris
CreateInputTexture2D( IrisNormal, Linear, 8, "NormalizeNormals", "_normal", "Iris,30/Normal", Default3AndFile( 0.5, 0.5, 1.0, "materials/default/default_eyes_normal.psd" ) );
CreateInputTexture2D( IrisRoughness, Linear, 8, "Inverse", "_rough", "Iris,30/Roughness", Default3( 0.1, 0.1, 0.1 ) );
Texture2D g_tIris < Channel( RGBA, HemiOctAnisoRoughness( IrisNormal, IrisRoughness ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); > ;

float g_flCorneaScleraBumpStrength < Default( 1.0 ); Range( 0.0, 2.0 ); UiGroup( "Normal" ); >;
float g_flIrisBumpStrength < Default( 1.0 ); Range( 0.0, 2.0 ); UiGroup( "Iris,30" ); >;
float g_flParallaxStrength < Default( 0.05 ); Range( 0.0, 0.2 ); UiGroup( "Iris,30" ); >;
float2 g_vIrisAutoexposureRange < Default2( 0.0, 0.0 ); Range2( 0.0, 0.0, 1.0, 1.0 ); UiGroup( "Iris,30" ); >;

float g_flScleraDiffuseWrap < Default( 0.3 ); Range( 0.0, 1.0 ); UiGroup( "Normal/21" ); >;
float g_flScleraDiffuseExponent < Default( 1.5 ); Range( 1.0, 2.0 ); UiGroup( "Normal/22" ); >;

CreateInputTexture2D( TextureSelfIllumMask, Srgb, 8, "", "_selfillum", "Self Illum,60", Default3( 0.0, 0.0, 0.0 ) );
Texture2D g_tSelfIllumMask < Channel( RGB, Box( TextureSelfIllumMask ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;

CreateInputTexture2D( TextureOcclusion, Linear, 8, "", "_color", "Occlusion/10", Default3( 1.0, 1.0, 1.0 ) );
Texture2D g_tOcclusion < Channel( RGB, Box( TextureOcclusion ), Linear ); OutputFormat( DXT1 ); SrgbRead( false ); >;

float g_flOcclusionWidth < Default( 3.0 ); Range( 0.1, 5.0 ); UiGroup( "Occlusion/20" ); >;
float g_flOcclusionStrength < Default( 0.75 ); Range( 0, 1 ); UiGroup( "Occlusion/30" ); >;

SamplerState g_sAnisoClamp <Filter( ANISOTROPIC ); MaxAniso( 8 ); AddressU( CLAMP ); AddressV( CLAMP ); >;

TextureAttribute( RepresentativeTexture, g_tColor );
BoolAttribute( SupportsMappingDimensions, true );

// System Textures ----------------------------------------------------------------------------------------------------------------------------------------

// Render State -------------------------------------------------------------------------------------------------------------------------------------------
#if ( D_OPAQUE_FADE ) && ( !S_MODE_DEPTH )
	RenderState( AlphaToCoverageEnable, true );
#endif

// Globals ------------------------------------------------------------------------------------------------------------------------------------------------
#if ( S_SELF_ILLUM )
	float3 g_vSelfIllumTint < UiType( Color ); Default3( 1.0, 1.0, 1.0 ); UiGroup( "Self Illum" ); >;
	float g_flSelfIllumBrightness < Default( 0.0 ); Range( -10.0, 10.0 ); UiGroup( "Self Illum" ); >;
	float g_flSelfIllumAlbedoFactor < Default( 1.0 ); Range( 0.0, 1.0 ); UiGroup( "Self Illum" );  >;
#endif

// Helpers ------------------------------------------------------------------------------------------------------------------------------------------------
float FetchReflectance( float2 vTextureCoords, float4 vColorTexel )
{
	// Reflectance
	float flReflectance = vColorTexel.a;
	float2 vReflectanceScaleAndBias = float2( max( g_vReflectanceRange.x, g_vReflectanceRange.y ) - g_vReflectanceRange.x, g_vReflectanceRange.x );
	flReflectance = ( flReflectance * vReflectanceScaleAndBias.x ) + vReflectanceScaleAndBias.y;
	return flReflectance;
}

// Ray sphere intersect returns distance along ray to intersection ----------------------------------------------------------------------------------------
float IntersectRaySphere( float3 cameraPos, float3 ray, float3 sphereCenter, float sphereRadius )
{
	float3 dst = cameraPos.xyz - sphereCenter.xyz;
	float B = dot( dst, ray );
	float C = dot( dst, dst ) - ( sphereRadius * sphereRadius );
	float D = B*B - C;
	return ( D > 0.0 ) ? ( -B - sqrt( D ) ) : 0.0; // sqrt( dot( dst, dst ) );// 0.0;
}

float3x3 ComputeTangentFrame( float3 N, float3 P, float2 UV )
{
	float3 dp1 = ddx( P );
	float3 dp2 = ddy( P );
	float2 duv1 = ddx( UV );
	float2 duv2 = ddy( UV );

	float3x3 M = float3x3( dp1, dp2, cross( dp1, dp2 ) );
	float2x3 inverseM = float2x3( cross( M[ 1 ], M[ 2 ] ), cross( M[ 2 ], M[ 0 ] ) );
	float3 T = mul( float2( duv1.x, duv2.x ), inverseM );
	float3 B = mul( float2( duv1.y, duv2.y ), inverseM );

	return float3x3( normalize( T ), normalize( B ), N );
}

// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_OUTPUT_EYE
{
	float4 vColor : SV_Target0;
};

// The depth pass of an object that isn't fading has no pixel stage (the .shader renames MainPs away)
#if !( S_MODE_DEPTH && !D_OPAQUE_FADE )
[shader( "pixel" )] PS_OUTPUT_EYE MainPs( PS_INPUT i )
{
	float3 vPositionWithOffsetWs = i.vPositionWithOffsetWs.xyz;
	float3 vPositionWs = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;

	PS_OUTPUT_EYE o;
	
	float3 vCameraToPositionDirWs = CalculateCameraToPositionDirWs( vPositionWs.xyz );

	//--------//
	// Normal //
	//--------//
	float3 vNormalWs = normalize( i.vNormalWs.xyz );

	//---------------//
	// Tangent Space //
	//---------------//
	// Generate tangentV using cross product and sign
	float3 vTangentUWs = i.vTangentUWs.xyz;
	float3 vTangentVWs = i.vTangentVWs.xyz;

	float2 vTextureCoords = i.vTextureCoords.xy;

	//--------------------------//
	// Cornea                   //
	//--------------------------//
	float4 vCorneaScleraTexel = Tex2DS( g_tNormal, g_sAniso, vTextureCoords.xy );
	float3 vCorneaScleraNormalTs = DecodeHemiOctahedronNormal( vCorneaScleraTexel.ag );
	vCorneaScleraNormalTs.y = -vCorneaScleraNormalTs.y;
	vCorneaScleraNormalTs.xy *= g_flCorneaScleraBumpStrength; // Cornea bump strength
	vCorneaScleraNormalTs.z = 1.0;
	vCorneaScleraNormalTs.xyz = normalize( vCorneaScleraNormalTs.xyz );

	// Iris aperture
	float flIrisMask = Tex2DS( g_tIrisMask, g_sAniso, vTextureCoords.xy ).r;

	float2 vLowExposureUv = vTextureCoords.xy - ( vCorneaScleraNormalTs.xy * flIrisMask * g_vIrisAutoexposureRange.x );
	float2 vHighExposureUv = vTextureCoords.xy + ( vCorneaScleraNormalTs.xy * flIrisMask * g_vIrisAutoexposureRange.y );
	
	float2 vExposureUv = lerp( vHighExposureUv, vLowExposureUv, saturate( g_flToneMapScalarLinear * 0.4 ) );

	float flIrisParallaxMask = Tex2DS( g_tIrisMask, g_sAniso, vExposureUv.xy ).r;

	// Hacked parallax mapping on iris
	float3 vTangentViewVector = Vec3WsToTs( vCameraToPositionDirWs.xyz, vNormalWs.xyz, vTangentUWs.xyz, vTangentVWs.xyz );
	float2 vParallaxVector = ( ( vTangentViewVector.xy * flIrisParallaxMask * g_flParallaxStrength ));// / ( 1.0 - vTangentViewVector.z ) ); // Note: 0.25 is a magic number
	//vParallaxVector = 0.0; // Disable parallax for debugging
	float2 vShiftedUv = vExposureUv.xy + vParallaxVector.xy;

	// Cornea normal
	float3 vCorneaScleraNormalWs = Vec3TsToWsNormalized( vCorneaScleraNormalTs.xyz, vNormalWs.xyz, vTangentUWs.xyz, vTangentVWs.xyz );

	//-----------------//
	// Iris normal map //
	//-----------------//
	float3 vNormalTs = vCorneaScleraNormalTs;
	float4 vIrisNormalTs = Tex2DS( g_tIris, g_sAniso, vShiftedUv.xy );
	float3 vIrisNormalWs = float3( 0.0, 0.0, 1.0 );

	// Decode hemi-octahedron encoded normal
	vNormalTs.xyz = DecodeHemiOctahedronNormal( vIrisNormalTs.ag );

	// HACK: Tools still generate tangent space the inverted Source1 way where positive y is down. Flipping the normal here to compensate.
	vNormalTs.y = -vNormalTs.y;
	vNormalTs.xy *= g_flIrisBumpStrength;
	vNormalTs.z = 1.0;
	vNormalTs.xyz = normalize( vNormalTs.xyz );

	// Transform from tangent space into world space
	vIrisNormalWs = Vec3TsToWsNormalized( vNormalTs.xyz, vNormalWs.xyz, vTangentUWs.xyz, vTangentVWs.xyz );

	//-----------//
	// Roughness //
	//-----------//
	float2 vCorneaScleraRoughness = float2( 0.05, 0.05 );
	float2 vIrisRoughness = vIrisNormalTs.rb;
	vCorneaScleraRoughness = AdjustRoughnessByGeometricNormal( vCorneaScleraRoughness.xy, vNormalWs.xyz );
	vIrisRoughness = AdjustRoughnessByGeometricNormal( vIrisRoughness.xy, vNormalWs.xyz );

	//--------//
	// Albedo //
	//--------//
	float4 vColorTexel = Tex2DS( g_tColor, g_sAniso, vShiftedUv.xy );
	float3 vAlbedo = vColorTexel.rgb * SrgbGammaToLinear( g_vColorTint.rgb ) * i.vVertexColor.rgb;

	//----------------//
	// Combine result //
	//----------------//
	#if ( S_MODE_DEPTH )
	{
		#if ( D_OPAQUE_FADE )
		{
			OpaqueFadeDepth( i.vVertexColor.a, i.vPositionSs.xy );
		}
		#endif

		PS_OUTPUT_EYE emptyOutput = {};
		return emptyOutput;
	}
	#else // Forward, ToolsVis
	{
		o.vColor.a = 1.0;

		//
		// Indirect Diffuse, Transmissive Lighting
		//
		float3 vIndirectDiffuse = float3( 0.0, 0.0, 0.0 );
		float3 vDiffuseNormalWs = vNormalWs.xyz;

		if ( ProbeLight::UsesProbes() ) // Light probe volumes
		{
			float3 vIrradiance[6];
			SampleLightProbeVolume( vIrradiance, vPositionWs.xyz );
			vIndirectDiffuse.rgb = SampleIrradiance( vIrradiance, vDiffuseNormalWs.xyz );
		}

		float flCorneaScleraReflectance = 0.04;
		float flIrisReflectance = FetchReflectance( vTextureCoords.xy, vColorTexel.rgba );
		float flReflectance = lerp( flCorneaScleraReflectance, flIrisReflectance, flIrisMask );
		float flFresnelExponent = 5.0;

		FinalCombinerInput_t finalCombinerInput = PS_InitFinalCombiner();
		finalCombinerInput.vPositionWs =
			vPositionWs.xyz
			+ 0.1 * vNormalWs.xyz;
		finalCombinerInput.vPositionWithOffsetWs =
			vPositionWithOffsetWs.xyz
			+ 0.1 * vNormalWs.xyz;
		finalCombinerInput.vPositionSs = i.vPositionSs;
		finalCombinerInput.vNormalWs.xyz = vNormalWs.xyz;
		finalCombinerInput.vTangentUWs.xyz = vTangentUWs.xyz;
		finalCombinerInput.vTangentVWs.xyz = vTangentVWs.xyz;
		finalCombinerInput.vRoughness.xy = vCorneaScleraRoughness.xy;
		finalCombinerInput.flFresnelExponent = flFresnelExponent;

		// Finish sampling eye occlusion before lighting so its texture and sampler are no longer live.
		float flEyeOcclusion = Tex2DS( g_tOcclusion, g_sAnisoClamp, i.vOcclusionUV.xy ).r;
		flEyeOcclusion = pow( flEyeOcclusion, g_flOcclusionWidth ) * g_flOcclusionStrength + ( 1.0f - g_flOcclusionStrength );

		//----------//
		// Lighting //
		//----------//
		LightingTerms_t lightingTerms = InitLightingTerms();

		// Cornea/sclera lighting
		float4 vScleraDiffuseWrapParameters = float4( g_flScleraDiffuseWrap, g_flScleraDiffuseExponent, 1.0 / ( 1.0 + g_flScleraDiffuseWrap ), ( 1.0 + g_flScleraDiffuseExponent ) / ( 2.0 + 2.0 * g_flScleraDiffuseWrap ) );
		finalCombinerInput.vSSSWrapParameters = vScleraDiffuseWrapParameters.xyzw;
		finalCombinerInput.vNormalWs.xyz = vCorneaScleraNormalWs.xyz;
		finalCombinerInput.vSSSNormalWs.xyz = vNormalWs.xyz;
		finalCombinerInput.vRoughness.xy = vCorneaScleraRoughness.xy;
		finalCombinerInput.vSpecularColor = flCorneaScleraReflectance.xxx;
		ComputeDirectLighting( lightingTerms, finalCombinerInput );
		CalculateIndirectLighting( lightingTerms, finalCombinerInput );

		[branch] if ( flIrisMask > 0.0 )
		{
			// Iris lighting
			LightingTerms_t lightingTermsIris = InitLightingTerms();
			finalCombinerInput.vSSSWrapParameters = SUBSURFACE_SCATTERING_DEFAULT_WRAP_PARAMETERS;
			finalCombinerInput.vNormalWs.xyz = vIrisNormalWs.xyz;
			finalCombinerInput.vRoughness.xy = vIrisRoughness.xy;
			finalCombinerInput.vSpecularColor = flIrisReflectance.xxx;
			ComputeDirectLighting( lightingTermsIris, finalCombinerInput );
			CalculateIndirectLighting( lightingTermsIris, finalCombinerInput );

			lightingTerms.vDiffuse.rgb = lerp( lightingTerms.vDiffuse.rgb, lightingTermsIris.vDiffuse.rgb, flIrisMask );
			lightingTerms.vIndirectDiffuse.rgb = lerp( lightingTerms.vIndirectDiffuse.rgb, lightingTermsIris.vIndirectDiffuse.rgb, flIrisMask );
			lightingTerms.vSpecular.rgb += lightingTermsIris.vSpecular.rgb * flIrisMask;
			lightingTerms.vIndirectSpecular.rgb += lightingTermsIris.vIndirectSpecular.rgb * flIrisMask;
		}

		// Diffuse
		o.vColor.rgb = ( lightingTerms.vDiffuse.rgb + lightingTerms.vIndirectDiffuse.rgb ) * vAlbedo.rgb;

		// Self Illum
		#if ( S_SELF_ILLUM )
		{
			float3 vSelfIllumTintScaled = pow( 2, g_flSelfIllumBrightness ).xxx * SrgbGammaToLinear( g_vSelfIllumTint.xyz );
			o.vColor.rgb += Tex2DS( g_tSelfIllumMask, g_sAniso, vTextureCoords.xy ).rgb * vSelfIllumTintScaled.rgb * lerp( ( 1.0 ).xxx, vAlbedo.rgb, g_flSelfIllumAlbedoFactor );
		}
		#endif

		// Specular
		o.vColor.rgb += lightingTerms.vSpecular.rgb;
		o.vColor.rgb += lightingTerms.vIndirectSpecular.rgb; // Indirect specular applies its own fresnel in the forward lighting header file
		
		o.vColor.rgb *= flEyeOcclusion;

		// Fog
		if( g_bFogEnabled )
		{
			o.vColor.rgb = Fog::Apply( vPositionWs.xyz, o.vColor.rgb );
		}

		o.vColor.rgb = ApplyVolumetricFog( o.vColor.rgb, vPositionWs.xyz, i.vPositionSs.xy );

		//
		// Opaque fade
		//
		#if ( D_OPAQUE_FADE )
		{
			o.vColor.a = OpaqueFade( i.vVertexColor.a, i.vPositionSs.xyzw );
		}
		#endif

		if( ToolsVis::WantsToolsVis() )
		{
			ToolsVis toolVis = ToolsVis::Init( o.vColor.rgba, lightingTerms.vDiffuse.rgb, lightingTerms.vSpecular.rgb, lightingTerms.vIndirectDiffuse.rgb, lightingTerms.vIndirectSpecular.rgb, lightingTerms.vTransmissive.rgb );

			toolVis.HandleFullbright( o.vColor.rgba, vAlbedo.rgb, vPositionWs.xyz, vNormalWs.xyz );
			toolVis.HandleDiffuseLighting( o.vColor.rgba );
			toolVis.HandleSpecularLighting( o.vColor.rgba );
			toolVis.HandleTransmissiveLighting( o.vColor.rgba );
			toolVis.HandleLightingComplexity( o.vColor.rgba, finalCombinerInput.vPositionWs.xyz, finalCombinerInput.vPositionSs, finalCombinerInput.vNormalWs.xyz );

			toolVis.HandleAlbedo( o.vColor.rgba, vAlbedo.rgb );
			toolVis.HandleReflectivity( o.vColor.rgba, vAlbedo.rgb );
			toolVis.HandleRoughness( o.vColor.rgba, vCorneaScleraRoughness.xy );
			toolVis.HandleReflectance( o.vColor.rgba, flReflectance.xxx );

			toolVis.HandleNormalTs( o.vColor.rgba, vNormalTs.xyz );
			toolVis.HandleNormalWs( o.vColor.rgba, vNormalWs.xyz );
			toolVis.HandleTangentUWs( o.vColor.rgba, vTangentUWs.xyz );
			toolVis.HandleTangentVWs( o.vColor.rgba, vTangentVWs.xyz );

			toolVis.HandleGeometricRoughness( o.vColor.rgba, vNormalWs.xyz );
			toolVis.HandleEyeAndMouthMask( o.vColor.rgba, 1.0 );
		}
	}
	#endif
	return o;
}
#endif // has a pixel stage

#endif // PS


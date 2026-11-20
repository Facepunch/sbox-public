// tools_light_probe: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
// Copyright (c) Valve Corporation, All rights reserved. ======================================================================================================

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "Shader used in tools to render the reflection from an environment map. Meant to look like Paul Debevec's light probes. See www.debevec.org/probes";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#include "sbox/pass.slangh"

Mode( Default );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "system.fxc" // This should always be the first include in COMMON
#include "common.fxc"
#include "math_general.fxc"

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct VS_INPUT
{
	float4 vPositionWs : POSITION < Semantic( PosXyz ); >;
	float4 vNormalWs : NORMAL < Semantic( Normal ); >;
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_INPUT
{
	float3 vPositionWs : TEXCOORD0;
	float3 vNormalWs : TEXCOORD1;

	// VS only
	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs	: SV_Position;
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

	o.vPositionPs = Position3WsToPs( i.vPositionWs.xyz );
	o.vPositionWs = i.vPositionWs.xyz;
	o.vNormalWs = i.vNormalWs.xyz;

	return o;
}

#endif // VS

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_PS )
// Includes -----------------------------------------------------------------------------------------------------------------------------------------------

// Combos -------------------------------------------------------------------------------------------------------------------------------------------------

// Render State -------------------------------------------------------------------------------------------------------------------------------------------

// System Textures ----------------------------------------------------------------------------------------------------------------------------------------
SamplerState g_sSkyRadianceSampler < Filter( MIN_MAG_MIP_LINEAR ); AddressU( WRAP ); AddressV( CLAMP ); >;
Texture2D g_tSkyRadiance < Attribute( "CustomTexture0" ); SrgbRead( false ); >;

// Disable depth
RenderState( DepthEnable, true );
RenderState( DepthWriteEnable, true );
RenderState( DepthFunc, GREATER_EQUAL );

// Globals ------------------------------------------------------------------------------------------------------------------------------------------------
//float g_flRadianceScale < Attribute( "RadianceScale" ); >;

// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_OUTPUT
{
	float4 vColor : SV_Target0;
};

[shader( "pixel" )] PS_OUTPUT MainPs( PS_INPUT i )
{
	PS_OUTPUT o;

	float3 vNormalWs = normalize( i.vNormalWs );
	float3 vCameraToPositionRayWs = CalculateCameraToPositionRayWs( i.vPositionWs );
	float3 vReflectPositionToSkyDirWs = normalize( reflect( vCameraToPositionRayWs, vNormalWs ) );

	o.vColor = TexLatLong( g_sSkyRadianceSampler, g_tSkyRadiance, vReflectPositionToSkyDirWs );

	
	//o.vColor.rgb = SrgbLinearToGamma( o.vColor.rgb );

	return o;
}

#endif // PS


// error: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
// Copyright (c) Valve Corporation, All rights reserved. ======================================================================================================
//
// ########     ###    ##    ##  ######   ######## ########  ####          ########     ###    ##    ##  ######   ######## ########  ####
// ##     ##   ## ##   ###   ## ##    ##  ##       ##     ## ####          ##     ##   ## ##   ###   ## ##    ##  ##       ##     ## ####
// ##     ##  ##   ##  ####  ## ##        ##       ##     ## ####          ##     ##  ##   ##  ####  ## ##        ##       ##     ## ####
// ##     ## ##     ## ## ## ## ##   #### ######   ########   ##           ##     ## ##     ## ## ## ## ##   #### ######   ########   ##
// ##     ## ######### ##  #### ##    ##  ##       ##   ##                 ##     ## ######### ##  #### ##    ##  ##       ##   ##
// ##     ## ##     ## ##   ### ##    ##  ##       ##    ##  ####          ##     ## ##     ## ##   ### ##    ##  ##       ##    ##  ####
// ########  ##     ## ##    ##  ######   ######## ##     ## ####          ########  ##     ## ##    ##  ######   ######## ##     ## ####
//
// ___  ____    _  _ ____ ___    _ _  _ ____ _    _  _ ___  ____    ____ _  _ _   _    ____ _ _    ____ ____    _ _  _    ___ _  _ _ ____
// |  \ |  |    |\ | |  |  |     | |\ | |    |    |  | |  \ |___    |__| |\ |  \_/     |___ | |    |___ [__     | |\ |     |  |__| | [__
// |__/ |__|    | \| |__|  |     | | \| |___ |___ |__| |__/ |___    |  | | \|   |      |    | |___ |___ ___]    | | \|     |  |  | | ___]
//
//            ____ _  _ ____ ___  ____ ____    ____ _  _ ____ ____ ___  ___    ____ _   _ ____ ___ ____ _  _  ____ _  _ ____   /
//            [__  |__| |__| |  \ |___ |__/    |___  \/  |    |___ |__]  |     [__   \_/  [__   |  |___ |\/|  |___  \/  |     /
//            ___] |  | |  | |__/ |___ |  \    |___ _/\_ |___ |___ |     |     ___]   |   ___]  |  |___ |  | .|    _/\_ |___ .
//
//=============================================================================================================================================================

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: CompileTargets = ( IS_SM_50 && ( PC || VULKAN ) );
// HEADER: Description = "Error shader (that supports multiview instancing)";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#include "sbox/pass.slangh"

Mode( Default );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// !!! DO NOT INCLUDE ANY OTHER FILES IN THIS SHADER !!!
#include "sbox/system.slangh"
// !!! DO NOT INCLUDE ANY OTHER FILES IN THIS SHADER !!!

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct VS_INPUT
{
	float3 vPositionOs		: POSITION < Semantic( PosXyz ); >;

	// Instancing data
	uint nInstanceTransformID : TEXCOORD13 < Semantic( InstanceTransformUv ); >;
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_INPUT
{
	float3 vPosition				: POSITION;

	// VS only
	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vOutputPositionPs	: SV_Position;
	#endif
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_VS )
// !!! DO NOT INCLUDE ANY OTHER FILES IN THIS SHADER !!!
#include "sbox/instancing.slangh" // I really don't want to do this, but we have to until we can transform instanced objects differently when the shader breaks

// !!! DO NOT INCLUDE ANY OTHER FILES IN THIS SHADER !!!

// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
[shader( "vertex" )] PS_INPUT MainVs( VS_INPUT i )
{
	PS_INPUT o;

	//---------------------------------------------------//
	// Generate Object->World matrix and animation scale //
	//---------------------------------------------------//
	float3x4 matObjectToWorld = GetTransformMatrix( i.nInstanceTransformID );

	//----------------//
	// World position //
	//----------------//
	float3 vPositionWs = mul( matObjectToWorld, float4( i.vPositionOs.xyz, 1.0 ) );

	//--------------------//
	// Projected position //
	//--------------------//
	o.vOutputPositionPs.xyzw = Position3WsToPs( vPositionWs.xyz );
	o.vPosition = vPositionWs;

	return o;
}

#endif // VS

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_PS )
// DO NOT INCLUDE ANY FILES IN THIS SHADER!

// MUST MATCH DESCRIPTOR SETS IN COMMON.FXC
ExternalDescriptorSet g_globalLateBoundBindlessSet Slot 1;
ExternalDescriptorSet g_viewSet Slot 2;

// Constants ----------------------------------------------------------------------------------------------------------------------------------------------
float g_flTime < Attribute( "Time" ); Default( 0.0 ); >;

// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_OUTPUT
{
	float4 vColor0 : SV_Target0;
	float4 vColor1 : SV_Target1;
	float4 vColor2 : SV_Target2;
	float4 vColor3 : SV_Target3;
};

[shader( "pixel" )] PS_OUTPUT MainPs( PS_INPUT i )
{
	PS_OUTPUT o;
	
	const float checkerPattern = frac( 0.5 * ( floor( i.vPosition.x * 0.5 ) + floor( i.vPosition.y * 0.5 ) + floor( i.vPosition.z * 0.5 ) ) );
	const float flRed = lerp(0.1, ( 0.5 * sin( g_flTime * 6.0 ) + 0.5 ), 2.0 * checkerPattern );
	
	o.vColor0 = float4( flRed, 0.0, 0.0, 1.0 );
	o.vColor1 = o.vColor2 = o.vColor3 = float4( 0.0, 0.0, 0.0, 0.0 );

	return o;
}

#endif // PS


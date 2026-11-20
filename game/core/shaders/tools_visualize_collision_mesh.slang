// tools_visualize_collision_mesh: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
// Copyright (c) Valve Corporation, All rights reserved. ======================================================================================================

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: CompileTargets = ( IS_SM_50 && ( PC || VULKAN ) );
// HEADER: Description = "Debug shader to visualize collision meshes";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#include "sbox/pass.slangh"

Mode( Default );
Mode( Forward );
Mode( Depth );
Mode( ToolsUtil );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
Feature( F_DEPTH_TEST, 0..1 );
Feature( F_WIREFRAME, 0..1 );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "system.fxc" // This should always be the first include in COMMON
#include "common.fxc"
#include "math_general.fxc"
#include "vr_common.fxc"

#define SUPPORTS_COMPRESSED_NORMAL_AND_TANGENT 1

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct VS_INPUT
{
	float3 vPositionOs		: POSITION < Semantic( PosXyz ); >;
	float4 vNormalOs		: NORMAL < Semantic( OptionallyCompressedTangentFrame ); >;
	float4 vColor			: COLOR0 < Semantic( Color ); >;

	// Instancing data
	uint nInstanceTransformID : TEXCOORD13 < Semantic( InstanceTransformUv ); >;
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_INPUT
{
	float4 vVertexColor : COLOR0;
	float3 vNormalWs	: TEXCOORD0;

	// GS only
	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs		: SV_Position;
	#endif
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_VS )
// Includes -----------------------------------------------------------------------------------------------------------------------------------------------
#include "instancing.fxc"
#include "vs_decompress.fxc"
#include "vr_lighting.fxc"

// Combos -------------------------------------------------------------------------------------------------------------------------------------------------
StaticCombo( S_WIREFRAME, F_WIREFRAME, Sys( ALL ) );

// System Textures ----------------------------------------------------------------------------------------------------------------------------------------

// Constants ----------------------------------------------------------------------------------------------------------------------------------------------
float4 g_vColor < Default4( 1.0, 1.0, 1.0, 1.0 ); Attribute( "Color" ); >;
float4 g_vWireframeTransparencyModulation < Default4( 1.0, 0.0, 1.0, 0.0 ); Attribute( "WireframeTransparencyModulation" ); >;

#define VS_OUTPUT PS_INPUT

// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
[shader( "vertex" )] VS_OUTPUT MainVs( VS_INPUT i )
{
	VS_OUTPUT o;

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
	o.vPositionPs.xyzw = Position3WsToPs(  vPositionWs.xyz );

	//--------------//
	// World normal //
	//--------------//
	float4 vInputTangentUOs_flTangentVSign = float4( 0.0, 0.0, 1.0, 1.0 );

	//-------------------//
	// World normal only //
	//-------------------//
	float3 vNormalWs = normalize( mul( matObjectToWorld, float4( i.vNormalOs.xyz, 0.0 ) ) );

	o.vNormalWs.xyz = vNormalWs.xyz;

	#if ( S_WIREFRAME )
	// No lighting for the wireframe, as it doesn't work in game, and we use this shader in game now
	o.vVertexColor = i.vColor * g_vColor;
	#else
	// tetrahedron lighting
	o.vVertexColor.rgb = i.vColor.rgb * g_vColor.rgb * 
		( saturate( float3(
			saturate( dot( vNormalWs, float3( 1.0,  1.0,  1.0) ) ) + saturate( dot( vNormalWs, float3(-1.0, 1.0, -1.0) ) ),
			saturate( dot( vNormalWs, float3(-1.0, -1.0,  1.0) ) ) + saturate( dot( vNormalWs, float3(-1.0, 1.0, -1.0) ) ),
			saturate( dot( vNormalWs, float3( 1.0, -1.0, -1.0) ) )
		) * 0.015 ) + 0.9 );

	float rayHeadOn = dot( CalculatePositionToCameraDirWs( vPositionWs.xyz ), vNormalWs.xyz );
	float flFresnel = saturate( ( ( rayHeadOn < 0.0 ) ? 0.25 : 1.0 ) * pow( 1 - abs( rayHeadOn ), ( rayHeadOn < 0.0 ) ? 4.0 : 2.0 ) + pow( saturate( rayHeadOn ), 8.0 ) ) ;//CalculateFresnel( 1.0, 1.0, vPositionWs, vNormalWs );
	o.vVertexColor.a = i.vColor.a * flFresnel * g_vColor.a;
	#endif

	return o;
}

#endif // VS

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_PS )
// Combos -------------------------------------------------------------------------------------------------------------------------------------------------
StaticCombo( S_DEPTH_TEST, F_DEPTH_TEST, Sys( ALL ) );
StaticCombo( S_WIREFRAME, F_WIREFRAME, Sys( ALL ) );

// Render State -------------------------------------------------------------------------------------------------------------------------------------------
RenderState( CullMode, NONE );

#if ( S_DEPTH_TEST )
	RenderState( DepthEnable, true );
	RenderState( DepthWriteEnable, true );
#else
	RenderState( DepthEnable, false );
	RenderState( DepthWriteEnable, false );
#endif

RenderState( DepthFunc, GREATER_EQUAL );
RenderState( ColorWriteEnable0, RGBA );
#if ( S_WIREFRAME )
RenderState( FillMode, WIREFRAME );
#endif
RenderState( BlendEnable, true );
RenderState( SrcBlend, SRC_ALPHA );
RenderState( DstBlend, INV_SRC_ALPHA );
BoolAttribute( translucent, true );

// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_OUTPUT
{
	float4 vColor : SV_Target0;
};

[shader( "pixel" )] PS_OUTPUT MainPs( PS_INPUT i )
{
	PS_OUTPUT o;
	o.vColor = float4( i.vVertexColor.rgba );
	return o;
}

#endif // PS


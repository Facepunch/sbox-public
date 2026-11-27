// morph_composite: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
// Copyright (c) Valve Corporation, All rights reserved. ======================================================================================================

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "Composite morph texture";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#include "sbox/pass.slangh"

Mode( Default );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "sbox/system.slangh" // This should always be the first include in COMMON
#include "sbox/view.slangh"
#include "sbox/math.slangh"
#include "sbox/classes/bindless.slangh"

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct VS_INPUT
{
	float4 vPositionWeights			: POSITION < Semantic( None ); >;
	float4 vTexCoords				: TEXCOORD0 < Semantic( None ); >;
	float4 vOffsetsPositionSpeed	: TEXCOORD1 < Semantic( None ); >;
	float4 vRangesPositionSpeed		: TEXCOORD2 < Semantic( None ); >;
	uint nAtlasIndex				: TEXCOORD3 < Semantic( None ); >;
	#if ( D_COMPOSITETYPE == 1 )
		float4 vOffsetsNormalWrinkle : TEXCOORD4 < Semantic( None ); >;
		float4 vRangesNormalWrinkle	: TEXCOORD5 < Semantic( None ); >;
	#endif
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_INPUT
{
	float4 vOffsetsPositionSpeed	: TEXCOORD1;
	float4 vRangesPositionSpeed		: TEXCOORD2;
	float2 vWeights					: TEXCOORD3;
	nointerpolation uint nAtlasIndex : TEXCOORD6;

	#if ( D_COMPOSITETYPE == 0 )
		float2 vTexCoords			: TEXCOORD0;
	#else
		float4 vTexCoords			: TEXCOORD0;
		float4 vOffsetsNormalWrinkle : TEXCOORD4;
		float4 vRangesNormalWrinkle	: TEXCOORD5;
	#endif

	// VS only
	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs			: SV_Position;
	#endif

	// PS only
	#if ( PROGRAM == VFX_PROGRAM_PS )
		float4 vPositionSs			: SV_Position;
	#endif
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_VS )
// Includes -----------------------------------------------------------------------------------------------------------------------------------------------

// Combos -------------------------------------------------------------------------------------------------------------------------------------------------
DynamicCombo( D_COMPOSITETYPE, 0..1, Sys( ALL ) );

// Constants ----------------------------------------------------------------------------------------------------------------------------------------------

// Main ---------------------------------------------------------------------------------------------------------------------------------------------------

#define VS_INPUT_POSITION_X	(i.vPositionWeights.x)
#define VS_INPUT_POSITION_Y	(i.vPositionWeights.y)
#define VS_WEIGHT			(i.vPositionWeights.z)
#define VS_DELAYED_WEIGHT	(i.vPositionWeights.w)

[shader( "vertex" )] PS_INPUT MainVs( const VS_INPUT i )
{
	PS_INPUT o;

	o.vPositionPs.xyzw = float4( VS_INPUT_POSITION_X, VS_INPUT_POSITION_Y, 1.0, 1.0 );
	o.vWeights.xy = float2( VS_WEIGHT, VS_DELAYED_WEIGHT );
	o.vOffsetsPositionSpeed.xyzw = i.vOffsetsPositionSpeed.xyzw;
	o.vRangesPositionSpeed.xyzw = i.vRangesPositionSpeed.xyzw;
	o.nAtlasIndex = i.nAtlasIndex;

	#if ( D_COMPOSITETYPE == 0 )
	{
		o.vTexCoords.xy = i.vTexCoords.xy;
	}
	#else
	{
		o.vTexCoords.xyzw = i.vTexCoords.xyzw;
		o.vOffsetsNormalWrinkle.xyzw = i.vOffsetsNormalWrinkle.xyzw;
		o.vRangesNormalWrinkle.xyzw = i.vRangesNormalWrinkle.xyzw;
	}
	#endif

	return o;
}

#endif // VS

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_PS )
// Includes -----------------------------------------------------------------------------------------------------------------------------------------------

// Combos -------------------------------------------------------------------------------------------------------------------------------------------------
DynamicCombo( D_COMPOSITETYPE, 0..1, Sys( ALL ) );

// Textures -----------------------------------------------------------------------------------------------------------------------------------------------
// SourceMorphAtlas is sampled bindless via per-vertex atlas index so we can batch composites across all morph sets.

// Render State -------------------------------------------------------------------------------------------------------------------------------------------
RenderState( ColorWriteEnable0, RGBA );
RenderState( DepthEnable, false );
RenderState( DepthWriteEnable, false );
RenderState( BlendEnable, true );
RenderState( SrcBlend, ONE );
RenderState( DstBlend, ONE );
RenderState( SrcBlendAlpha, ONE );
RenderState( DstBlendAlpha, ONE );
RenderState( CullMode, NONE );

// Globals ------------------------------------------------------------------------------------------------------------------------------------------------

// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_OUTPUT
{
	float4 vColor : SV_Target0;
};

#define PS_TEXCOORD_POSITION_SPEED	i.vTexCoords.xy
#define PS_TEXCOORD_NORMAL_WRINKLE	i.vTexCoords.zw	
#define	PS_WEIGHT					i.vWeights.x
#define	PS_DELAYED_WEIGHT			i.vWeights.y
#define PS_POSITION					vPositionSpeed.xyz
#define PS_SPEED					vPositionSpeed.w

[shader( "pixel" )] PS_OUTPUT MainPs( PS_INPUT i )
{
	PS_OUTPUT o;

	Texture2D tSourceMorphAtlas = Bindless::GetTexture2D( i.nAtlasIndex );
	float4 vPositionSpeed = tSourceMorphAtlas.Sample( g_sPointWrap, PS_TEXCOORD_POSITION_SPEED );
	// Scale position and speed back to full range.
	vPositionSpeed.xyzw = ( vPositionSpeed.xyzw * i.vRangesPositionSpeed.xyzw ) + i.vOffsetsPositionSpeed.xyzw;
	// Calculate the speed-adjusted weight.
	float flFinalWeight = ( ( 1.0 - PS_SPEED ) * PS_WEIGHT ) + ( PS_SPEED * PS_DELAYED_WEIGHT );

	#if ( D_COMPOSITETYPE == 0 )
	{
		float3 vPosition = PS_POSITION;
		// Weight-adjust position delta and blend with target.
		o.vColor.rgba = float4( PS_POSITION * flFinalWeight, 0.0 );
	}
	#else
	{
		float4 vNormalWrinkle = tSourceMorphAtlas.Sample( g_sPointWrap, PS_TEXCOORD_NORMAL_WRINKLE );
		// Scale normal and wrinkle back to full range.
		vNormalWrinkle.xyzw = ( vNormalWrinkle.xyzw * i.vRangesNormalWrinkle.xyzw ) + i.vOffsetsNormalWrinkle.xyzw;
		// Weight-adjust normal delta and wrinkle and blend with target.
		o.vColor.rgba = vNormalWrinkle.xyzw * flFinalWeight;
	}
	#endif

	return o;
}

#endif // PS


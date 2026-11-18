// visualize_quad_overdraw: a standalone module, the whole shader for Borealis.Gpu (docs/managed/standalone-modules.md).
// Copyright (c) Valve Corporation, All rights reserved. ======================================================================================================

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: CompileTargets = IS_SM_50 && ( PC || VULKAN );
// HEADER: Description = "Visualize quad overshading";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#include "sbox/pass.slangh"

Mode( Default );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "system.fxc" // This should always be the first include in COMMON

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct VS_INPUT
{
	float2 vPositionPs : POSITION < Semantic( position ); >;
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_INPUT
{
	// VS only
	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs : SV_Position;
	#else // PS only
		float4 vPositionSs : SV_Position;
	#endif
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_VS )
// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
[shader( "vertex" )] PS_INPUT MainVs( const VS_INPUT i )
{
	PS_INPUT o;
	o.vPositionPs.xyzw = float4( i.vPositionPs.xy, 0.0, 1.0 );
	return o;
}

#endif // VS

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_PS )
// Combos -------------------------------------------------------------------------------------------------------------------------------------------------

// Textures -----------------------------------------------------------------------------------------------------------------------------------------------
Texture2D<uint> g_tQuadOverdrawUAV < Attribute( "QuadOverdrawUAV" ); >;

// Render State -------------------------------------------------------------------------------------------------------------------------------------------
RenderState( DepthEnable, false );
RenderState( DepthWriteEnable, false );
RenderState( CullMode, NONE );

// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_OUTPUT
{
	float4 vColor : SV_Target0;
};

[shader( "pixel" )] PS_OUTPUT MainPs( PS_INPUT i )
{
	PS_OUTPUT o;

	uint2 vQuad = i.vPositionSs.xy * float2( 0.5, 0.5 );

	uint nOverdraw = g_tQuadOverdrawUAV[vQuad];
	switch ( nOverdraw )
	{
	case 0:
		o.vColor.rgba = float4( 0.0, 0.0, 0.0, 1.0 );
		break;
	case 1:
		o.vColor.rgba = float4( 0.0, 0.0, 0.5, 1.0 );
		break;
	case 2:
		o.vColor.rgba = float4( 0.0, 0.3, 1.0, 1.0 );
		break;
	case 3:
		o.vColor.rgba = float4( 0.0, 0.9, 1.0, 1.0 );
		break;
	case 4:
		o.vColor.rgba = float4( 0.3, 1.0, 0.0, 1.0 );
		break;
	case 5:
		o.vColor.rgba = float4( 0.9, 1.0, 0.0, 1.0 );
		break;
	case 6:
		o.vColor.rgba = float4( 1.0, 0.3, 0.0, 1.0 );
		break;
	case 7:
		o.vColor.rgba = float4( 1.0, 0.0, 0.3, 1.0 );
		break;
	case 8:
		o.vColor.rgba = float4( 1.0, 0.0, 0.9, 1.0 );
		break;
	case 9:
		o.vColor.rgba = float4( 1.0, 0.3, 0.9, 1.0 );
		break;
	default:
		o.vColor.rgba = float4( 1.0, 1.0, 1.0, 1.0 );
		break;
	}

	return o;
}

#endif // PS


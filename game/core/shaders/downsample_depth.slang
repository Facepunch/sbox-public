// downsample_depth: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
// Copyright (c) Valve Corporation, All rights reserved. ======================================================================================================

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: CompileTargets = ( IS_SM_50 );
// HEADER: Description = "Downsample/resolve depth buffer";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#include "sbox/pass.slangh"

Mode( Default );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "sbox/system.slangh" // This should always be the first include in COMMON
#include "sbox/view.slangh"
#include "sbox/math.slangh"

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct VS_INPUT
{
	float3 vPositionSs : POSITION < Semantic( PosXyz ); >;
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_INPUT
{
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
// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
[shader( "vertex" )] PS_INPUT MainVs( const VS_INPUT i )
{
	PS_INPUT o;
	o.vPositionPs.xyzw = float4( i.vPositionSs, 1.0 );
	return o;
}

#endif // VS

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_PS )
// Attributes ---------------------------------------------------------------------------------------------------------------------------------------------
int	g_nDownsampleRatio < Attribute( "g_nDownsampleRatio" ); >; // 1 = resolve, 2 = half-res, 4 = quarter res, etc
float4 SrcViewportOffset < Attribute( "SrcViewportOffset" ); >; // Offset from the source viewport

// Combos -------------------------------------------------------------------------------------------------------------------------------------------------
DynamicCombo( D_MINMAX_CHECKER_2X2, 0..1, Sys( ALL ) );
DynamicCombo( D_NON_MSAA_DEPTH, 0..1, Sys( ALL ) );

// Textures -----------------------------------------------------------------------------------------------------------------------------------------------
#if ( D_NON_MSAA_DEPTH == 0 )
	Texture2DMS< float > g_tDepth < Attribute( "DepthBuffer" ); SrgbRead( false ); >;
#else
	Texture2D g_tDepth < Attribute( "DepthBuffer" ); SrgbRead( false ); >;
#endif

// Render State -------------------------------------------------------------------------------------------------------------------------------------------
RenderState( ColorWriteEnable0, false );
RenderState( DepthEnable, true );
RenderState( DepthWriteEnable, true );
RenderState( DepthFunc, ALWAYS );
RenderState( BlendEnable, false );

// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_OUTPUT
{
	float flDepth : SV_Depth;
};

float FetchDepth( int2 vTexCoord )
{
	#if ( D_NON_MSAA_DEPTH == 0 )
		return Tex2DMS( g_tDepth, vTexCoord, 0+0 ).r;
	#else
		return g_tDepth.Load( int3( vTexCoord, 0 ) ).r;
	#endif
}

[shader( "pixel" )] PS_OUTPUT MainPs( PS_INPUT i )
{
	PS_OUTPUT o;
	int2 vDestPixel = int2( i.vPositionSs.xy );
	int2 vSrcPixelUpperLeft = ( vDestPixel.xy * g_nDownsampleRatio.xx ) + int2( SrcViewportOffset.xy );

	#if ( D_MINMAX_CHECKER_2X2 )
	{
		float4 v4Depths;

		// NOTE: could read more MSAA samples to further reduce silhouette artifacts
		v4Depths.w = FetchDepth( vSrcPixelUpperLeft.xy + int2( 0, 0 ) );
		v4Depths.z = FetchDepth( vSrcPixelUpperLeft.xy + int2( 1, 0 ) );
		v4Depths.y = FetchDepth( vSrcPixelUpperLeft.xy + int2( 1, 1 ) );
		v4Depths.x = FetchDepth( vSrcPixelUpperLeft.xy + int2( 0, 1 ) );

		//
		// Alternate between using the minimum depth and the maximum depth in a checkerboard
		// pattern. This is to maximize the variety of depth information within each 2x2
		// region of the result, so that when we upsample, we'll most likely find a depth
		// value near our own.
		//
		float flMinDepth = min( min( v4Depths.x, v4Depths.y ), min( v4Depths.z, v4Depths.w ) );
		float flMaxDepth = max( max( v4Depths.x, v4Depths.y ), min( v4Depths.z, v4Depths.w ) );
		o.flDepth = ( ( ( vDestPixel.x + vDestPixel.y ) & 1 ) == 1 ) ? flMinDepth : flMaxDepth;
	}
	#else
	{
		// Default to just 1 tap - fast and crappy!
		o.flDepth = FetchDepth( vSrcPixelUpperLeft.xy );
	}
	#endif

	return o;
}

#endif // PS


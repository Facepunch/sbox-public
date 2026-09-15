//=================================================================================================
// FSR1 EASU (Edge-Adaptive Spatial Upsampling) compute pass.
// Reads the low-res post-tonemap scene color and writes the upscaled result.
// Constants are computed in the shader from input/output sizes — no CPU-side cbuffer setup.
//=================================================================================================
HEADER
{
	DevShader = true;
	Description = "FSR1 EASU upscale pass";
}

MODES
{
	Default();
}

FEATURES
{
}

COMMON
{
	#include "system.fxc"
}

CS
{
	float2 g_vFsrInputViewportSize	< Attribute( "FsrInputViewportSize" ); >;
	float2 g_vFsrInputSize			< Attribute( "FsrInputSize" ); >;
	float2 g_vFsrOutputSize			< Attribute( "FsrOutputSize" ); >;

	Texture2D<float4>	g_tFsrInput		< Attribute( "FsrInput" ); SrgbRead( false ); >;
	RWTexture2D<float4>	g_tFsrOutput	< Attribute( "FsrOutput" ); >;

	SamplerState g_sFsrLinearClamp < Filter( BILINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); >;

	#define A_GPU 1
	#define A_HLSL 1
	#include "common/ffx/ffx_a.h"

	#define FSR_EASU_F 1
	AF4 FsrEasuRF( AF2 p ) { return g_tFsrInput.GatherRed  ( g_sFsrLinearClamp, p ); }
	AF4 FsrEasuGF( AF2 p ) { return g_tFsrInput.GatherGreen( g_sFsrLinearClamp, p ); }
	AF4 FsrEasuBF( AF2 p ) { return g_tFsrInput.GatherBlue ( g_sFsrLinearClamp, p ); }

	#include "common/ffx/ffx_fsr1.h"

	void FsrEasuFilter( int2 pos, AU4 con0, AU4 con1, AU4 con2, AU4 con3 )
	{
		AF3 c;
		FsrEasuF( c, pos, con0, con1, con2, con3 );
		g_tFsrOutput[ pos ] = float4( c, 1.0 );
	}

	[numthreads( 64, 1, 1 )]
	void MainCs( uint3 vGroupThreadId : SV_GroupThreadID, uint3 vGroupId : SV_GroupID )
	{
		AU4 con0, con1, con2, con3;
		FsrEasuCon( con0, con1, con2, con3,
			g_vFsrInputViewportSize.x, g_vFsrInputViewportSize.y,
			g_vFsrInputSize.x,         g_vFsrInputSize.y,
			g_vFsrOutputSize.x,        g_vFsrOutputSize.y );

		// 64-thread workgroup processes a 16x16 output tile via the AMD 8x8 quad remap.
		AU2 gxy = ARmp8x8( vGroupThreadId.x ) + AU2( vGroupId.x << 4u, vGroupId.y << 4u );

		FsrEasuFilter( gxy,                       con0, con1, con2, con3 );
		FsrEasuFilter( gxy + AU2( 8u, 0u ),       con0, con1, con2, con3 );
		FsrEasuFilter( gxy + AU2( 8u, 8u ),       con0, con1, con2, con3 );
		FsrEasuFilter( gxy + AU2( 0u, 8u ),       con0, con1, con2, con3 );
	}
}

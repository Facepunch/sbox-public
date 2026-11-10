//=================================================================================================
// FSR1 RCAS (Robust Contrast Adaptive Sharpening) compute pass.
// Reads the EASU-upscaled image and writes a sharpened final image at the same resolution.
//=================================================================================================
HEADER
{
	DevShader = true;
	Description = "FSR1 RCAS sharpening pass";
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
	// Sharpness reduction: 0 = sharpest, 2 = least sharp (FFX RCAS convention).
	// We expose user-facing sharpness 0..1 and remap CPU-side to FFX 2..0.
	float g_flFsrRcasAttenuation < Attribute( "FsrRcasAttenuation" ); Default( 0.2f ); >;

	Texture2D<float4>	g_tFsrInput		< Attribute( "FsrInput" ); SrgbRead( false ); >;
	RWTexture2D<float4>	g_tFsrOutput	< Attribute( "FsrOutput" ); >;

	#define A_GPU 1
	#define A_HLSL 1
	#include "common/ffx/ffx_a.h"

	#define FSR_RCAS_F 1
	AF4 FsrRcasLoadF( ASU2 p ) { return g_tFsrInput.Load( int3( p, 0 ) ); }
	void FsrRcasInputF( inout AF1 r, inout AF1 g, inout AF1 b ) {}

	#include "common/ffx/ffx_fsr1.h"

	void FsrRcasFilter( int2 pos, AU4 con )
	{
		AF3 c;
		FsrRcasF( c.r, c.g, c.b, pos, con );
		g_tFsrOutput[ pos ] = float4( c, 1.0 );
	}

	[numthreads( 64, 1, 1 )]
	void MainCs( uint3 vGroupThreadId : SV_GroupThreadID, uint3 vGroupId : SV_GroupID )
	{
		AU4 con;
		FsrRcasCon( con, g_flFsrRcasAttenuation );

		AU2 gxy = ARmp8x8( vGroupThreadId.x ) + AU2( vGroupId.x << 4u, vGroupId.y << 4u );

		FsrRcasFilter( gxy,                 con );
		FsrRcasFilter( gxy + AU2( 8u, 0u ), con );
		FsrRcasFilter( gxy + AU2( 8u, 8u ), con );
		FsrRcasFilter( gxy + AU2( 0u, 8u ), con );
	}
}

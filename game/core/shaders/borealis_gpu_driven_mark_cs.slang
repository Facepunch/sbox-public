// borealis_gpu_driven_mark_cs.shader as a pass module: Borealis.Gpu draws this; the .shader stays for the other routes
// and as the fallback (docs/managed/shader-format-replacement.md, "Pass modules").
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "Borealis GPU-driven visibility: marks the first vertex of every triangle that owns a sample of the prepass's ids.";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// MODES: Default();

#include "sbox/pass.slangh"

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "system.fxc" // This should always be the first include in COMMON

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_CS )
#include "common.fxc"

DynamicCombo( D_MSAA, 0..1, Sys( ALL ) );

#if D_MSAA
	Texture2DMS<uint> g_tIds < Attribute( "BorealisIds" ); >;
#else
	Texture2D<uint> g_tIds < Attribute( "BorealisIds" ); >;
#endif

RWByteAddressBuffer g_Mask < Attribute( "BorealisCullMask" ); >;

cbuffer BorealisGpuDrivenMark_t
{
	uint g_nMarkWidth;
	uint g_nMarkHeight;
	uint g_nMarkSamples;
	uint g_nMarkBits;	// vertex bits in use
	uint g_nMarkPad0;
	uint g_nMarkPad1;
	uint g_nMarkPad2;
	uint g_nMarkPad3;
};

// Sets the lanes' bits. Neighbouring pixels mostly name the same triangle, and an atomic each serialises them on its
// mask word, so the wave's lowest words go first with one atomic each; lanes with a word beyond those take their own.
// Called by the whole wave: the wave operations stay in uniform control flow.
void MarkBits( bool bMark, uint nBit )
{
	uint nWord = bMark ? nBit >> 5 : 0xffffffffu;
	uint nMine = 1u << ( nBit & 31 );
	[unroll] for ( uint nRound = 0; nRound < 4; nRound++ )
	{
		uint nLowest = WaveActiveMin( nWord );
		if ( nLowest == 0xffffffffu )
			break;   // uniform: the wave has nothing left

		bool bLowest = nWord == nLowest;
		uint nBits = WaveActiveBitOr( bLowest ? nMine : 0u );
		if ( WaveIsFirstLane() )
			g_Mask.InterlockedOr( nLowest * 4, nBits );
		if ( bLowest )
			nWord = 0xffffffffu;
	}

	if ( nWord != 0xffffffffu )
		g_Mask.InterlockedOr( nWord * 4, nMine );
}

// Every distinct id of a pixel's samples sets its bit: the first vertex's bit + 1, 0 for nothing
[numthreads( 8, 8, 1 )]
[shader( "compute" )] void MainCs( uint3 vThreadId : SV_DispatchThreadID )
{
	bool bInside = vThreadId.x < g_nMarkWidth && vThreadId.y < g_nMarkHeight;
	int2 vPixel = int2( min( vThreadId.xy, uint2( g_nMarkWidth, g_nMarkHeight ) - 1 ) );

	uint ids[8];
	uint nSamples = min( g_nMarkSamples, 8u );
	for ( uint s = 0; s < nSamples; s++ )
	{
		#if D_MSAA
			uint v = g_tIds.Load( vPixel, s );
		#else
			uint v = g_tIds.Load( int3( vPixel, 0 ) );
		#endif

		ids[s] = v;
		bool bSeen = v == 0;
		for ( uint j = 0; j < s; j++ )
			bSeen = bSeen || ids[j] == v;

		uint nBit = v - 1;
		MarkBits( bInside && !bSeen && nBit < g_nMarkBits, nBit );
	}
}

#endif // CS


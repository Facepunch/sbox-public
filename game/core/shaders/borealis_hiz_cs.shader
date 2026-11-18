//-------------------------------------------------------------------------------------------------------------------------------------------------------------
HEADER
{
	DevShader = true;
	Description = "Borealis two-phase Hi-Z occlusion: builds a prepass's Hi-Z from the depth its first phase drew and culls the second phase's fragments against it.";
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
MODES
{
	Default();
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
FEATURES
{
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
COMMON
{
	#include "system.fxc" // This should always be the first include in COMMON
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// The work is in borealis_hiz_cs.slang, which Borealis.Gpu draws: the backend only culls with a Hi-Z where pass modules
// run. This declares the constants, whose reflection the backend's struct is checked against, and the buffers.
CS
{
	#include "common.fxc"

	DynamicCombo( D_HIZ_DEPTH, 0..1, Sys( ALL ) );
	DynamicCombo( D_MSAA, 0..1, Sys( ALL ) );

	#if D_MSAA
		Texture2DMS<float> g_tDepth < Attribute( "BorealisHiZDepth" ); >;
	#else
		Texture2D<float> g_tDepth < Attribute( "BorealisHiZDepth" ); >;
	#endif

	RWByteAddressBuffer g_HiZ < Attribute( "BorealisHiZ" ); >;
	RWByteAddressBuffer g_Frame < Attribute( "BorealisCullFrame" ); >;
	RWByteAddressBuffer g_Stamps < Attribute( "BorealisCullStamps" ); >;
	RWByteAddressBuffer g_MeshletStamps < Attribute( "BorealisMeshletStamps" ); >;

	cbuffer BorealisHiZ_t
	{
		uint g_nHiZPass;
		uint g_nHiZWidth;
		uint g_nHiZHeight;
		uint g_nHiZLevels;
		uint g_nHiZSource;
		uint g_nHiZDepthWidth;
		uint g_nHiZDepthHeight;
		uint g_nHiZSamples;
		uint g_nHiZCandidates;
		uint g_nHiZRuns;
		uint g_nHiZListed;
		uint g_nHiZListed2;
		uint g_nHiZInstances;
		uint g_nHiZCommands;
		uint g_nHiZFirstInstance;
		uint g_nHiZFragmentWords;
		uint g_nHiZFrame;
		uint g_nHiZFlags;
		uint g_nHiZPad0;
		uint g_nHiZPad1;
		float4 g_nHiZViewport;
		float4 g_nHiZClipX;
		float4 g_nHiZClipY;
		float4 g_nHiZClipZ;
		float4 g_nHiZClipW;
	};

	[numthreads( 64, 1, 1 )]
	void MainCs( uint3 vGroupId : SV_GroupID )
	{
		// Keep the constants and buffers: a sum written nowhere a draw reads
		#if D_MSAA
			float flDepth = g_tDepth.Load( int2( 0, 0 ), 0 );
		#else
			float flDepth = g_tDepth.Load( int3( 0, 0, 0 ) );
		#endif
		if ( g_nHiZPass == 0xffffffffu )
			g_Frame.Store( 0, asuint( flDepth ) + D_HIZ_DEPTH + g_HiZ.Load( 0 ) + g_Stamps.Load( 0 ) + g_MeshletStamps.Load( 0 ) + g_nHiZWidth + g_nHiZHeight + g_nHiZLevels + g_nHiZSource + g_nHiZDepthWidth + g_nHiZDepthHeight + g_nHiZSamples
				+ g_nHiZCandidates + g_nHiZRuns + g_nHiZListed + g_nHiZListed2 + g_nHiZInstances + g_nHiZCommands + g_nHiZFirstInstance + g_nHiZFragmentWords + g_nHiZFrame + g_nHiZFlags
				+ asuint( g_nHiZViewport.x + g_nHiZClipX.x + g_nHiZClipY.x + g_nHiZClipZ.x + g_nHiZClipW.x ) );
	}
}

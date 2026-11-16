//-------------------------------------------------------------------------------------------------------------------------------------------------------------
HEADER
{
	DevShader = true;
	Description = "Borealis meshlet culling: a group per chunk of each fragment a depth prepass listed tests its meshlets against the view and draws those that can reach a sample from compacted indices.";
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
// The work is in borealis_meshlet_cull_cs.slang, which Borealis.Gpu draws: the backend only culls meshlets where pass
// modules run. This declares the constants, whose reflection the backend's struct is checked against.
CS
{
	#include "common.fxc"

	RWByteAddressBuffer g_Frame < Attribute( "BorealisCullFrame" ); >;

	cbuffer BorealisMeshletCull_t
	{
		uint g_nMeshletListed;
		uint g_nMeshletInstances;
		uint g_nMeshletCommands;
		uint g_nMeshletFirstInstance;
		uint g_nMeshletRuns;
		uint g_nMeshletDraws;
		uint g_nMeshletRunTable;
		uint g_nMeshletCapacity;
		uint g_nMeshletFragmentWords;
		uint g_nMeshletSamples;
		uint g_nMeshletPass;
		uint g_nMeshletFrame;
		float4 g_nMeshletViewport;
		float4 g_nMeshletClipX;
		float4 g_nMeshletClipY;
		float4 g_nMeshletClipZ;
		float4 g_nMeshletClipW;
	};

	[numthreads( 64, 1, 1 )]
	void MainCs( uint3 vGroupId : SV_GroupID )
	{
		// Keep the constants: a count of what it would cull, written nowhere a draw reads
		if ( g_nMeshletListed == 0xffffffffu )
			g_Frame.Store( 0, g_nMeshletInstances + g_nMeshletCommands + g_nMeshletFirstInstance + g_nMeshletRuns + g_nMeshletDraws + g_nMeshletRunTable + g_nMeshletCapacity
				+ g_nMeshletFragmentWords + g_nMeshletSamples + g_nMeshletPass + g_nMeshletFrame + asuint( g_nMeshletViewport.x + g_nMeshletClipX.x + g_nMeshletClipY.x + g_nMeshletClipZ.x + g_nMeshletClipW.x ) );
	}
}

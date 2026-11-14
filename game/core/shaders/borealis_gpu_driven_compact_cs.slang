// borealis_gpu_driven_compact_cs.shader as a pass module: Borealis.Gpu draws this; the .shader stays for the other routes
// and as the fallback (docs/managed/shader-format-replacement.md, "Pass modules").
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "Borealis GPU-driven visibility: a group per listed fragment draws it whole, compacts its triangles that own a sample, or drops it.";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// MODES: Default();

#include "sbox/pass.slangh"

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "system.fxc" // This should always be the first include in COMMON

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_CS )
#include "common.fxc"

#ifndef BINDLESS_VERTEX_BUFFERS_DECLARED
#define BINDLESS_VERTEX_BUFFERS_DECLARED
ByteAddressBuffer g_bindless_VertexBuffers[] EXTERNAL_DESC_SET( t, g_globalLateBoundBindlessSet, 17 );
#endif

// As borealis_gpu_driven_cull_cs: the frame's region, the view's vertex mask and compacted indices
RWByteAddressBuffer g_Frame < Attribute( "BorealisCullFrame" ); >;
RWByteAddressBuffer g_Mask < Attribute( "BorealisCullMask" ); >;
RWByteAddressBuffer g_Compacted < Attribute( "BorealisCullCompacted" ); >;

cbuffer BorealisGpuDrivenCull_t
{
	uint g_nCullInput;
	uint g_nCullBatches;
	uint g_nCullInstances;		// byte offset of the instance records
	uint g_nCullCommands;		// byte offset of the commands
	uint g_nCullFirstInstance;	// the first instance record's index in g_Frame
	uint g_nCullFragments;
	uint g_nCullDraws;			// bindless slot of the draw table
	uint g_nCullRuns;			// byte offset of the multi-draws: first command slot and count each
	uint g_nCullFrame;
	uint g_nCullVisibility;
	uint g_nCullPad0;			// the compacted indices' cursor is g_Compacted's first word, shared by the view's calls
	uint g_nCullCapacity;		// indices g_Compacted holds
	uint g_nCullFragmentWords;
	uint g_nCullListed;			// byte offset of the listed fragments: dispatch arguments, then 32 bytes each
	uint g_nCullPad1;
	uint g_nCullPad2;
};

static const uint None = 0xffffffffu;

uint LoadIndex( ByteAddressBuffer page, uint nIndex, bool b32 )
{
	if ( b32 )
		return page.Load( nIndex * 4 );

	uint nWord = page.Load( ( nIndex * 2 ) & ~3u );
	return ( nIndex & 1 ) != 0 ? nWord >> 16 : nWord & 0xffff;
}

void WriteDraw( uint nSlot, uint nTransform, uint nDraw, uint nIndexCount, uint nFirstIndex, uint nBaseVertex )
{
	g_Frame.Store( g_nCullInstances + nSlot * 8, nTransform );
	g_Frame.Store( g_nCullInstances + nSlot * 8 + 4, nDraw );

	uint nCommand = g_nCullCommands + nSlot * 20;
	g_Frame.Store( nCommand, nIndexCount );
	g_Frame.Store( nCommand + 4, 1 );
	g_Frame.Store( nCommand + 8, nFirstIndex );
	g_Frame.Store( nCommand + 12, nBaseVertex );
	g_Frame.Store( nCommand + 16, g_nCullFirstInstance + nSlot );
}

uint TakeSlot( uint nRun )
{
	uint nIndex;
	g_Frame.InterlockedAdd( g_nCullRuns + nRun * 8 + 4, 1, nIndex );
	return g_Frame.Load( g_nCullRuns + nRun * 8 ) + nIndex;
}

bool Owned( ByteAddressBuffer page, uint nFirst, bool b32, uint nVertexBase, uint t, out uint i0 )
{
	i0 = LoadIndex( page, nFirst + t * 3, b32 );
	uint nBit = nVertexBase + i0;
	return ( ( g_Mask.Load( ( nBit >> 5 ) * 4 ) >> ( nBit & 31 ) ) & 1 ) != 0;
}

groupshared uint gs_Owned;
groupshared uint gs_Start;
groupshared uint gs_Cursor;

// A listed fragment: transform, draw record, its vertices' first bit (with the base vertex), its multi-draw and its
// bucket's compacted multi-draw. A triangle owns a sample when its first vertex's bit is set (what the ids name).
[numthreads( 256, 1, 1 )]
[shader( "compute" )] void MainCs( uint3 vGroupId : SV_GroupID, uint nThread : SV_GroupIndex )
{
	uint nEntry = g_nCullListed + 16 + vGroupId.x * 32;
	uint nTransform = g_Frame.Load( nEntry );
	uint nDrawRecord = g_Frame.Load( nEntry + 4 );
	uint nVertexBase = g_Frame.Load( nEntry + 8 );
	uint nRun = g_Frame.Load( nEntry + 12 );
	uint nCompactRun = g_Frame.Load( nEntry + 16 );

	ByteAddressBuffer draws = g_bindless_VertexBuffers[ g_nCullDraws ];
	uint4 vDrawHead = draws.Load4( nDrawRecord * 32 );
	uint4 vDraw = draws.Load4( nDrawRecord * 32 + 16 );
	uint nTriangles = vDraw.y / 3;
	ByteAddressBuffer page = g_bindless_VertexBuffers[ NonUniformResourceIndex( vDraw.w ) ];
	bool b32 = ( vDrawHead.w & 2 ) != 0;

	if ( nThread == 0 )
	{
		gs_Owned = 0;
		gs_Cursor = 0;
	}

	GroupMemoryBarrierWithGroupSync();

	uint nMine = 0;
	for ( uint t = nThread; t < nTriangles; t += 256 )
	{
		uint i0;
		nMine += Owned( page, vDraw.x, b32, nVertexBase, t, i0 ) ? 1 : 0;
	}

	InterlockedAdd( gs_Owned, nMine );
	GroupMemoryBarrierWithGroupSync();
	uint nOwned = gs_Owned;
	if ( nOwned == 0 )
		return;

	// Copied where few own a sample and there's room; else the whole draw
	if ( nThread == 0 )
	{
		uint nStart = None;
		if ( nOwned * 2 < nTriangles )
		{
			g_Compacted.InterlockedAdd( 0, nOwned * 3, nStart );
			if ( nStart + nOwned * 3 > g_nCullCapacity )
				nStart = None;
		}

		gs_Start = nStart;
		if ( nStart == None )
			WriteDraw( TakeSlot( nRun ), nTransform, nDrawRecord, vDraw.y, vDraw.x, vDraw.z );
		else
			WriteDraw( TakeSlot( nCompactRun ), nTransform, nDrawRecord, nOwned * 3, nStart, vDraw.z );
	}

	GroupMemoryBarrierWithGroupSync();
	uint nStart = gs_Start;
	if ( nStart == None )
		return;

	// Each owned triangle takes the next three indices (order within the fragment doesn't matter)
	for ( uint t = nThread; t < nTriangles; t += 256 )
	{
		uint i0;
		if ( !Owned( page, vDraw.x, b32, nVertexBase, t, i0 ) )
			continue;

		uint nAt;
		InterlockedAdd( gs_Cursor, 3, nAt );
		nAt += nStart;
		g_Compacted.Store( ( nAt + 0 ) * 4, i0 );
		g_Compacted.Store( ( nAt + 1 ) * 4, LoadIndex( page, vDraw.x + t * 3 + 1, b32 ) );
		g_Compacted.Store( ( nAt + 2 ) * 4, LoadIndex( page, vDraw.x + t * 3 + 2, b32 ) );
	}
}

#endif // CS


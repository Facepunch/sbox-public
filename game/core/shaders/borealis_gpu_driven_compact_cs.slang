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

// Where the view's prepass culled meshlets (borealis_meshlet_cull_cs): per visibility fragment, the frame, where its kept
// triangles' indices start in the prepass's compacted buffer (None: drawn whole), how many and whether that's its 16-bit
// buffer (0 or 1), or, where task shaders culled them (borealis_meshlet_ids), 2: the word its kept triangles' numbers start
// at in this buffer and how many; a fragment stamped this frame scans only those
RWByteAddressBuffer g_MeshletStamps < Attribute( "BorealisMeshletStamps" ); >;
RWByteAddressBuffer g_PrepassCompacted < Attribute( "BorealisMeshletCompacted" ); >;
RWByteAddressBuffer g_PrepassCompacted16 < Attribute( "BorealisMeshletCompacted16" ); >;

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
	uint g_nCullLodTable;		// the culls' (the compacted indices' cursor is g_Compacted's first word, shared by the view's calls)
	uint g_nCullCapacity;		// indices g_Compacted holds
	uint g_nCullFragmentWords;
	uint g_nCullListed;			// byte offset of the listed fragments: dispatch arguments, then 32 bytes each
	uint g_nCullMeshletRuns;		// bindless slot of the meshlet runs (16 bytes per draw record) where the view's prepass culls meshlets; else None
	uint g_nCullLodCount;
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

groupshared uint gs_Owned;
groupshared uint gs_Start;
groupshared uint gs_Cursor;

// A triangle's index k (0-2): from the page (the kept triangle t's number from word nListed, where task shaders wrote
// them), or from the prepass's compacted buffer (32 or 16-bit, by b32)
uint TriangleIndex( ByteAddressBuffer page, uint nFirst, bool b32, bool bPrepass, uint t, uint k, uint nListed )
{
	if ( nListed != None )
		t = g_MeshletStamps.Load( ( nListed + t ) * 4 );
	uint i = nFirst + t * 3 + k;
	if ( !bPrepass )
		return LoadIndex( page, i, b32 );
	if ( b32 )
		return g_PrepassCompacted.Load( i * 4 );

	uint nWord = g_PrepassCompacted16.Load( ( i * 2 ) & ~3u );
	return ( i & 1 ) != 0 ? nWord >> 16 : nWord & 0xffff;
}

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

	// Where its prepass culled meshlets this frame: only the triangles it kept, from its compacted indices (or, where it
	// drew all of them, the page)
	uint nScan = nTriangles;
	uint nFirst = vDraw.x;
	bool bPrepass = false;
	uint nListed = None;
	if ( g_nCullMeshletRuns != None )
	{
		uint nFragment = g_Frame.Load( nEntry + 28 );
		uint4 vStamp = nFragment != None ? g_MeshletStamps.Load4( nFragment * 16 ) : 0;
		if ( nFragment != None && vStamp.x == g_nCullFrame )
		{
			if ( vStamp.z == 0 )
				return;
			if ( vStamp.y != None && vStamp.w == 2 )
			{
				// Kept triangles' numbers, which the task shaders' mesh groups wrote
				nListed = vStamp.y;
				nScan = vStamp.z;
			}
			else if ( vStamp.y != None && vStamp.w != 2 )
			{
				bPrepass = true;
				nFirst = vStamp.y;
				nScan = vStamp.z;
				b32 = vStamp.w == 0;
			}
		}
	}

	// Four triangles a lane at a time, their index loads and then their mask loads issued together: the count waits on
	// memory, not arithmetic
	uint nMine = 0;
	for ( uint t0 = nThread; t0 < nScan; t0 += 1024 )
	{
		uint4 vT = t0 + uint4( 0, 256, 512, 768 );
		uint4 vIn = uint4( vT < nScan );
		vT = min( vT, nScan - 1 );
		uint4 vBit = nVertexBase + uint4( TriangleIndex( page, nFirst, b32, bPrepass, vT.x, 0, nListed ), TriangleIndex( page, nFirst, b32, bPrepass, vT.y, 0, nListed ),
			TriangleIndex( page, nFirst, b32, bPrepass, vT.z, 0, nListed ), TriangleIndex( page, nFirst, b32, bPrepass, vT.w, 0, nListed ) );
		uint4 vWords = uint4( g_Mask.Load( ( vBit.x >> 5 ) * 4 ), g_Mask.Load( ( vBit.y >> 5 ) * 4 ), g_Mask.Load( ( vBit.z >> 5 ) * 4 ), g_Mask.Load( ( vBit.w >> 5 ) * 4 ) );
		uint4 vOwned = ( ( vWords >> ( vBit & 31 ) ) & 1 ) * vIn;
		nMine += vOwned.x + vOwned.y + vOwned.z + vOwned.w;
	}

	nMine = WaveActiveSum( nMine );
	if ( WaveIsFirstLane() )
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
			if ( nStart < 4 || nStart >= g_nCullCapacity || nOwned * 3 > g_nCullCapacity - nStart )
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
	for ( uint t = nThread; t < nScan; t += 256 )
	{
		uint i0 = TriangleIndex( page, nFirst, b32, bPrepass, t, 0, nListed );
		uint nBit = nVertexBase + i0;
		if ( ( ( g_Mask.Load( ( nBit >> 5 ) * 4 ) >> ( nBit & 31 ) ) & 1 ) == 0 )
			continue;

		uint nAt;
		InterlockedAdd( gs_Cursor, 3, nAt );
		nAt += nStart;
		g_Compacted.Store( ( nAt + 0 ) * 4, i0 );
		g_Compacted.Store( ( nAt + 1 ) * 4, TriangleIndex( page, nFirst, b32, bPrepass, t, 1, nListed ) );
		g_Compacted.Store( ( nAt + 2 ) * 4, TriangleIndex( page, nFirst, b32, bPrepass, t, 2, nListed ) );
	}
}

#endif // CS


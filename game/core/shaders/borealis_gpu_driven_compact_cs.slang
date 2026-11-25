// borealis_gpu_driven_compact_cs: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "Borealis GPU-driven visibility: a group per listed fragment draws it whole, compacts its triangles that own a sample, or drops it.";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#include "sbox/pass.slangh"

Mode( Default );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "sbox/system.slangh" // This should always be the first include in COMMON

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_CS )
#include "sbox/view.slangh"
#include "borealis_records.fxc"

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

// Two-phase Hi-Z occlusion: a bit per visibility fragment, set where one has a triangle that owns a sample, which the view's
// next prepass draws first (written where g_nCullVisibility isn't 0)
RWByteAddressBuffer g_HiZVisible < Attribute( "BorealisHiZVisible" ); >;

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
	uint g_nCullVisibility;		// not 0: set each listed fragment's bit in g_HiZVisible where it has a triangle that owns a sample
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
	g_Frame.Store( g_nCullInstances + nSlot * InstanceRecord_Size + InstanceRecord_Transform, nTransform );
	g_Frame.Store( g_nCullInstances + nSlot * InstanceRecord_Size + InstanceRecord_Draw, nDraw );

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
	g_Frame.InterlockedAdd( g_nCullRuns + nRun * MultiDraw_Size + MultiDraw_Count, 1, nIndex );
	return g_Frame.Load( g_nCullRuns + nRun * MultiDraw_Size + MultiDraw_FirstSlot ) + nIndex;
}

// Triangles a lane takes at once in each scan
static const uint Batch = 8;

// Threads a listed fragment gets: most fragments are a few hundred triangles
static const uint GroupSize = 1024;

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
[numthreads( GroupSize, 1, 1 )]
[shader( "compute" )] void MainCs( uint3 vGroupId : SV_GroupID, uint nThread : SV_GroupIndex )
{
	uint nEntry = g_nCullListed + ListedArguments_Size + vGroupId.x * ListedFragment_Size;
	uint nTransform = g_Frame.Load( nEntry + ListedFragment_Transform );
	uint nDrawRecord = g_Frame.Load( nEntry + ListedFragment_Draw );
	uint nVertexBase = g_Frame.Load( nEntry + ListedFragment_VertexBase );
	uint nRun = g_Frame.Load( nEntry + ListedFragment_Run );
	uint nCompactRun = g_Frame.Load( nEntry + ListedFragment_CompactRun );

	ByteAddressBuffer draws = g_bindless_VertexBuffers[ g_nCullDraws ];
	uint nDrawAt = nDrawRecord * DrawRecord_Size;
	uint4 vDraw = uint4( draws.Load( nDrawAt + DrawRecord_FirstIndex ), draws.Load( nDrawAt + DrawRecord_IndexCount ), draws.Load( nDrawAt + DrawRecord_BaseVertex ), draws.Load( nDrawAt + DrawRecord_IndexPage ) );
	uint nTriangles = vDraw.y / 3;
	ByteAddressBuffer page = g_bindless_VertexBuffers[ NonUniformResourceIndex( vDraw.w ) ];
	bool b32 = ( draws.Load( nDrawAt + DrawRecord_Flags ) & 2 ) != 0;

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
		uint nFragment = g_Frame.Load( nEntry + ListedFragment_Fragment );
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

	// Eight triangles a lane at a time, their index loads and then their mask loads issued together: the count waits on
	// memory, not arithmetic, and a large fragment's one group would otherwise wait on its loads one batch after another
	uint nMine = 0;
	for ( uint t0 = nThread; t0 < nScan; t0 += GroupSize * Batch )
	{
		uint vBit[Batch];
		[unroll] for ( uint k = 0; k < Batch; k++ )
			vBit[k] = nVertexBase + TriangleIndex( page, nFirst, b32, bPrepass, min( t0 + GroupSize * k, nScan - 1 ), 0, nListed );
		uint vWord[Batch];
		[unroll] for ( uint k = 0; k < Batch; k++ )
			vWord[k] = g_Mask.Load( ( vBit[k] >> 5 ) * 4 );
		[unroll] for ( uint k = 0; k < Batch; k++ )
			nMine += t0 + GroupSize * k < nScan ? ( vWord[k] >> ( vBit[k] & 31 ) ) & 1 : 0;
	}

	nMine = WaveActiveSum( nMine );
	if ( WaveIsFirstLane() )
		InterlockedAdd( gs_Owned, nMine );
	GroupMemoryBarrierWithGroupSync();
	uint nOwned = gs_Owned;
	if ( nOwned == 0 )
		return;

	if ( nThread == 0 && g_nCullVisibility != 0 )
	{
		uint nVisible = g_Frame.Load( nEntry + ListedFragment_Fragment );
		if ( nVisible != None )
			g_HiZVisible.InterlockedOr( ( nVisible >> 5 ) * 4, 1u << ( nVisible & 31 ) );
	}

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

	// Each owned triangle takes the next three indices (order within the fragment doesn't matter): eight a lane at a time,
	// loads issued together as in the count, each wave taking its owned triangles' room with one atomic
	for ( uint t0 = nThread; t0 < nScan; t0 += GroupSize * Batch )
	{
		uint vFirst[Batch];
		[unroll] for ( uint k = 0; k < Batch; k++ )
			vFirst[k] = TriangleIndex( page, nFirst, b32, bPrepass, min( t0 + GroupSize * k, nScan - 1 ), 0, nListed );
		uint vWord[Batch];
		[unroll] for ( uint k = 0; k < Batch; k++ )
			vWord[k] = g_Mask.Load( ( ( nVertexBase + vFirst[k] ) >> 5 ) * 4 );

		[unroll] for ( uint k = 0; k < Batch; k++ )
		{
			uint t = t0 + GroupSize * k;
			bool bOwned = t < nScan && ( ( vWord[k] >> ( ( nVertexBase + vFirst[k] ) & 31 ) ) & 1 ) != 0;
			uint nWave = WaveActiveCountBits( bOwned );
			if ( nWave == 0 )
				continue;

			uint nBase = 0;
			if ( WaveIsFirstLane() )
				InterlockedAdd( gs_Cursor, nWave * 3, nBase );
			nBase = WaveReadLaneFirst( nBase );
			if ( !bOwned )
				continue;

			uint nAt = nStart + nBase + WavePrefixCountBits( bOwned ) * 3;
			g_Compacted.Store( ( nAt + 0 ) * 4, vFirst[k] );
			g_Compacted.Store( ( nAt + 1 ) * 4, TriangleIndex( page, nFirst, b32, bPrepass, t, 1, nListed ) );
			g_Compacted.Store( ( nAt + 2 ) * 4, TriangleIndex( page, nFirst, b32, bPrepass, t, 2, nListed ) );
		}
	}
}

#endif // CS


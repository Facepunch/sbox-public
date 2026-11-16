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

// The view's prepass meshlet bits and their stamps (borealis_meshlet_cull_cs): a fragment stamped this frame scans only
// the triangles of the meshlets its prepass kept
RWByteAddressBuffer g_MeshletBits < Attribute( "BorealisMeshletBits" ); >;
RWByteAddressBuffer g_MeshletStamps < Attribute( "BorealisMeshletStamps" ); >;

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
	uint g_nCullMeshletRuns;		// bindless slot of the meshlet runs (16 bytes per draw record) where the view's prepass culls meshlets; else None
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

// A fragment whose prepass culled meshlets: its kept ones (first triangle << 8 | triangles), where each one's triangles
// start among the kept ones', and how many
static const uint MaxKept = 1024;
groupshared uint gs_Kept[MaxKept];
groupshared uint gs_KeptStart[MaxKept];
groupshared uint gs_KeptCount;
groupshared uint gs_KeptTriangles;
groupshared uint gs_Scan[256];

// Kept triangle t's triangle in the fragment
uint KeptTriangle( uint t, uint nKept )
{
	uint lo = 0, hi = nKept - 1;
	while ( lo < hi )
	{
		uint mid = ( lo + hi + 1 ) / 2;
		if ( gs_KeptStart[mid] <= t )
			lo = mid;
		else
			hi = mid - 1;
	}

	return ( gs_Kept[lo] >> 8 ) + t - gs_KeptStart[lo];
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
		gs_KeptCount = 0;
		gs_KeptTriangles = 0;
	}

	GroupMemoryBarrierWithGroupSync();

	// Where its prepass culled meshlets this frame: only their kept meshlets' triangles, nScan of them
	uint nScan = nTriangles;
	uint nKeptMeshlets = 0;
	if ( g_nCullMeshletRuns != None )
	{
		uint nFragment = g_Frame.Load( nEntry + 28 );
		uint4 vRun = g_bindless_VertexBuffers[ g_nCullMeshletRuns ].Load4( nDrawRecord * 16 );
		if ( nFragment != None && vRun.z - 1 < 4096u && g_MeshletStamps.Load( nFragment * 4 ) == g_nCullFrame )
		{
			ByteAddressBuffer meshlets = g_bindless_VertexBuffers[ vRun.x ];
			for ( uint m = nThread; m < vRun.z; m += 256 )
			{
				uint nBit = vRun.w + m;
				if ( ( ( g_MeshletBits.Load( ( nBit >> 5 ) * 4 ) >> ( nBit & 31 ) ) & 1 ) == 0 )
					continue;

				uint nCount = meshlets.Load( ( vRun.y + m ) * 32 + 28 );
				uint nSlot;
				InterlockedAdd( gs_KeptCount, 1, nSlot );
				if ( nSlot < MaxKept )
					gs_Kept[nSlot] = meshlets.Load( ( vRun.y + m ) * 32 + 12 ) << 8 | ( nCount & 0xff );
				InterlockedAdd( gs_KeptTriangles, nCount );
			}

			GroupMemoryBarrierWithGroupSync();
			nKeptMeshlets = gs_KeptCount;
			if ( nKeptMeshlets == 0 )
				return;

			// Too many kept to list: scan every triangle as without meshlets
			if ( nKeptMeshlets > MaxKept )
			{
				nKeptMeshlets = 0;
			}
			else
			{
				nScan = gs_KeptTriangles;
				uint nRunning = 0;
				for ( uint c = 0; c < nKeptMeshlets; c += 256 )
				{
					uint nValue = c + nThread < nKeptMeshlets ? gs_Kept[c + nThread] & 0xff : 0;
					gs_Scan[nThread] = nValue;
					GroupMemoryBarrierWithGroupSync();
					[unroll]
					for ( uint d = 1; d < 256; d <<= 1 )
					{
						uint nAdd = nThread >= d ? gs_Scan[nThread - d] : 0;
						GroupMemoryBarrierWithGroupSync();
						gs_Scan[nThread] += nAdd;
						GroupMemoryBarrierWithGroupSync();
					}

					if ( c + nThread < nKeptMeshlets )
						gs_KeptStart[c + nThread] = nRunning + gs_Scan[nThread] - nValue;
					nRunning += gs_Scan[255];
					GroupMemoryBarrierWithGroupSync();
				}
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
		if ( nKeptMeshlets != 0 )
			vT = uint4( KeptTriangle( vT.x, nKeptMeshlets ), KeptTriangle( vT.y, nKeptMeshlets ), KeptTriangle( vT.z, nKeptMeshlets ), KeptTriangle( vT.w, nKeptMeshlets ) );
		vT = vT * 3 + vDraw.x;
		uint4 vBit = nVertexBase + uint4( LoadIndex( page, vT.x, b32 ), LoadIndex( page, vT.y, b32 ), LoadIndex( page, vT.z, b32 ), LoadIndex( page, vT.w, b32 ) );
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
	for ( uint s = nThread; s < nScan; s += 256 )
	{
		uint t = nKeptMeshlets != 0 ? KeptTriangle( s, nKeptMeshlets ) : s;
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


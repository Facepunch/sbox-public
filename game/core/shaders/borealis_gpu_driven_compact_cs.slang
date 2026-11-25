// borealis_gpu_driven_compact_cs: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "Borealis GPU-driven visibility: a group per listed fragment draws it whole, compacts its triangles that own a sample, or drops it; large fragments in parts.";

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

// D_PARTS 0: a group per listed fragment (the cull's dispatch), each taking its whole fragment; 1, the same, but one of more
// triangles than a group scans at once is put in the view's list of large fragments instead, which 2 counts in parts (a
// group a part), 3 decides on (a thread a fragment) and 4 copies in parts. The same triangles drawn, the same way, as one
// group taking the whole fragment
DynamicCombo( D_PARTS, 0..4, Sys( ALL ) );
#ifndef D_PARTS
	#define D_PARTS 0
#endif

#ifndef BINDLESS_VERTEX_BUFFERS_DECLARED
#define BINDLESS_VERTEX_BUFFERS_DECLARED
ByteAddressBuffer g_bindless_VertexBuffers[] EXTERNAL_DESC_SET( t, g_globalLateBoundBindlessSet, 17 );
#endif

// As borealis_gpu_driven_cull_cs: the frame's region, the view's vertex mask and compacted indices
RWByteAddressBuffer g_Frame < Attribute( "BorealisCullFrame" ); >;
RWByteAddressBuffer g_Mask < Attribute( "BorealisCullMask" ); >;
RWByteAddressBuffer g_Compacted < Attribute( "BorealisCullCompacted" ); >;

// The view's list of large fragments (D_PARTS 1 to 4), made the first time its forward pass compacts, sized to the view
RWByteAddressBuffer g_Large < Attribute( "BorealisCompactLarge" ); >;

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

uint LoadIndex( uint nPage, uint nIndex, bool b32 )
{
	ByteAddressBuffer page = g_bindless_VertexBuffers[ NonUniformResourceIndex( nPage ) ];
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

// Threads a group has: most fragments are a few hundred triangles, and a large one is split over groups
static const uint GroupSize = 256;

// Triangles a group scans at once: a fragment of more goes in the list of large fragments
static const uint Chunk = GroupSize * Batch;

// The list of large fragments (g_Large): the parts' dispatch arguments (a group a part: the parts listed, 1, 1) and the
// parts it has room for; the decision's (a thread a fragment: its groups, 1, 1) and the fragments listed; the fragments it
// has room for and three words. Then each part (its fragment's number times MaxParts, plus the part's); then per fragment
// its entry (None: taken by its own group after all), owned triangles, where its copy starts (None: drawn whole or not at
// all), a word, and each part's owned triangles. Sized to the view (BackendDevice.CompactLargeFor): where it's full a
// fragment takes its own group
static const uint MaxParts = 64;
static const uint LargeWords = 4 + MaxParts;
uint PartCapacity() { return g_Large.Load( 12 ); }
uint LargeCapacity() { return g_Large.Load( 32 ); }
uint PartAt( uint nPart ) { return 48 + nPart * 4; }
uint LargeRecord( uint nLarge ) { return 48 + PartCapacity() * 4 + nLarge * LargeWords * 4; }

groupshared uint gs_Owned;
groupshared uint gs_Start;
groupshared uint gs_Cursor;

// A listed fragment, from its entry: transform, draw record, its vertices' first bit (with the base vertex), its multi-draw
// and its bucket's compacted multi-draw, its draw's first index, index count, base vertex and index page, and the triangles
// a scan takes (its prepass's kept ones where it culled meshlets this frame)
struct Fragment
{
	uint nTransform, nDrawRecord, nVertexBase, nRun, nCompactRun;
	uint4 vDraw;
	uint nTriangles;
	bool b32;
	uint nScan, nFirst;
	bool bPrepass;
	uint nListed;
	bool bNone;
};

Fragment LoadFragment( uint nEntry )
{
	Fragment f;
	f.nTransform = g_Frame.Load( nEntry + ListedFragment_Transform );
	f.nDrawRecord = g_Frame.Load( nEntry + ListedFragment_Draw );
	f.nVertexBase = g_Frame.Load( nEntry + ListedFragment_VertexBase );
	f.nRun = g_Frame.Load( nEntry + ListedFragment_Run );
	f.nCompactRun = g_Frame.Load( nEntry + ListedFragment_CompactRun );
	ByteAddressBuffer draws = g_bindless_VertexBuffers[ g_nCullDraws ];
	uint nDrawAt = f.nDrawRecord * DrawRecord_Size;
	f.vDraw = uint4( draws.Load( nDrawAt + DrawRecord_FirstIndex ), draws.Load( nDrawAt + DrawRecord_IndexCount ), draws.Load( nDrawAt + DrawRecord_BaseVertex ), draws.Load( nDrawAt + DrawRecord_IndexPage ) );
	f.nTriangles = f.vDraw.y / 3;
	f.b32 = ( draws.Load( nDrawAt + DrawRecord_Flags ) & 2 ) != 0;

	// Where its prepass culled meshlets this frame: only the triangles it kept, from its compacted indices (or, where it
	// drew all of them, the page); none kept, nothing to do
	f.nScan = f.nTriangles;
	f.nFirst = f.vDraw.x;
	f.bPrepass = false;
	f.nListed = None;
	f.bNone = false;
	if ( g_nCullMeshletRuns != None )
	{
		uint nFragment = g_Frame.Load( nEntry + ListedFragment_Fragment );
		uint4 vStamp = nFragment != None ? g_MeshletStamps.Load4( nFragment * 16 ) : 0;
		if ( nFragment != None && vStamp.x == g_nCullFrame )
		{
			if ( vStamp.z == 0 )
				f.bNone = true;
			else if ( vStamp.y != None && vStamp.w == 2 )
			{
				// Kept triangles' numbers, which the task shaders' mesh groups wrote
				f.nListed = vStamp.y;
				f.nScan = vStamp.z;
			}
			else if ( vStamp.y != None && vStamp.w != 2 )
			{
				f.bPrepass = true;
				f.nFirst = vStamp.y;
				f.nScan = vStamp.z;
				f.b32 = vStamp.w == 0;
			}
		}
	}
	return f;
}

// A triangle's index k (0-2): from the page (the kept triangle t's number from word nListed, where task shaders wrote
// them), or from the prepass's compacted buffer (32 or 16-bit, by b32)
uint TriangleIndex( Fragment f, uint t, uint k )
{
	if ( f.nListed != None )
		t = g_MeshletStamps.Load( ( f.nListed + t ) * 4 );
	uint i = f.nFirst + t * 3 + k;
	if ( !f.bPrepass )
		return LoadIndex( f.vDraw.w, i, f.b32 );
	if ( f.b32 )
		return g_PrepassCompacted.Load( i * 4 );
	uint nWord = g_PrepassCompacted16.Load( ( i * 2 ) & ~3u );
	return ( i & 1 ) != 0 ? nWord >> 16 : nWord & 0xffff;
}

// The triangles of the scan's nLo to nHi that own a sample (their first vertex's bit is set: what the ids name), counted by
// the whole group. Eight a lane at a time, their index loads and then their mask loads issued together: the count waits on
// memory, not arithmetic
uint CountOwned( Fragment f, uint nLo, uint nHi, uint nThread )
{
	if ( nThread == 0 )
		gs_Owned = 0;
	GroupMemoryBarrierWithGroupSync();

	uint nMine = 0;
	for ( uint t0 = nLo + nThread; t0 < nHi; t0 += Chunk )
	{
		uint vBit[Batch];
		[unroll] for ( uint k = 0; k < Batch; k++ )
			vBit[k] = f.nVertexBase + TriangleIndex( f, min( t0 + GroupSize * k, nHi - 1 ), 0 );
		uint vWord[Batch];
		[unroll] for ( uint k = 0; k < Batch; k++ )
			vWord[k] = g_Mask.Load( ( vBit[k] >> 5 ) * 4 );
		[unroll] for ( uint k = 0; k < Batch; k++ )
			nMine += t0 + GroupSize * k < nHi ? ( vWord[k] >> ( vBit[k] & 31 ) ) & 1 : 0;
	}

	nMine = WaveActiveSum( nMine );
	if ( WaveIsFirstLane() )
		InterlockedAdd( gs_Owned, nMine );
	GroupMemoryBarrierWithGroupSync();
	return gs_Owned;
}

// Copied where few own a sample and there's room; else the whole draw. Where its copy starts, or None
uint DrawFragment( Fragment f, uint nOwned )
{
	uint nStart = None;
	if ( nOwned * 2 < f.nTriangles )
	{
		g_Compacted.InterlockedAdd( 0, nOwned * 3, nStart );
		if ( nStart < 4 || nStart >= g_nCullCapacity || nOwned * 3 > g_nCullCapacity - nStart )
			nStart = None;
	}

	if ( nStart == None )
		WriteDraw( TakeSlot( f.nRun ), f.nTransform, f.nDrawRecord, f.vDraw.y, f.vDraw.x, f.vDraw.z );
	else
		WriteDraw( TakeSlot( f.nCompactRun ), f.nTransform, f.nDrawRecord, nOwned * 3, nStart, f.vDraw.z );
	return nStart;
}

// Each owned triangle of the scan's nLo to nHi takes the next three indices from nStart (order within the fragment doesn't
// matter): eight a lane at a time, loads issued together as in the count, each wave taking its owned triangles' room with
// one atomic
void CopyOwned( Fragment f, uint nLo, uint nHi, uint nStart, uint nThread )
{
	if ( nThread == 0 )
		gs_Cursor = 0;
	GroupMemoryBarrierWithGroupSync();

	for ( uint t0 = nLo + nThread; t0 < nHi; t0 += Chunk )
	{
		uint vFirst[Batch];
		[unroll] for ( uint k = 0; k < Batch; k++ )
			vFirst[k] = TriangleIndex( f, min( t0 + GroupSize * k, nHi - 1 ), 0 );
		uint vWord[Batch];
		[unroll] for ( uint k = 0; k < Batch; k++ )
			vWord[k] = g_Mask.Load( ( ( f.nVertexBase + vFirst[k] ) >> 5 ) * 4 );
		[unroll] for ( uint k = 0; k < Batch; k++ )
		{
			uint t = t0 + GroupSize * k;
			bool bOwned = t < nHi && ( ( vWord[k] >> ( ( f.nVertexBase + vFirst[k] ) & 31 ) ) & 1 ) != 0;
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
			g_Compacted.Store( ( nAt + 1 ) * 4, TriangleIndex( f, t, 1 ) );
			g_Compacted.Store( ( nAt + 2 ) * 4, TriangleIndex( f, t, 2 ) );
		}
	}
}

// Its listed fragment's bit in the next prepass's visible fragments (two-phase Hi-Z occlusion)
void SetVisible( uint nEntry )
{
	if ( g_nCullVisibility == 0 )
		return;
	uint nVisible = g_Frame.Load( nEntry + ListedFragment_Fragment );
	if ( nVisible != None )
		g_HiZVisible.InterlockedOr( ( nVisible >> 5 ) * 4, 1u << ( nVisible & 31 ) );
}

// A large fragment's parts, at most MaxParts, each at least a scan's chunk, and part nPart's triangles: nLo to nHi
uint Parts( Fragment f ) { return clamp( ( f.nScan + Chunk - 1 ) / Chunk, 1u, MaxParts ); }
void PartRange( Fragment f, uint nParts, uint nPart, out uint nLo, out uint nHi )
{
	uint nEach = f.nScan / nParts, nRest = f.nScan % nParts;
	nLo = nEach * nPart + min( nPart, nRest );
	nHi = nLo + nEach + ( nPart < nRest ? 1u : 0u );
}

[numthreads( GroupSize, 1, 1 )]
[shader( "compute" )] void MainCs( uint3 vGroupId : SV_GroupID, uint nThread : SV_GroupIndex )
{
#if ( D_PARTS <= 1 )
	// A group per listed fragment
	uint nEntry = g_nCullListed + ListedArguments_Size + vGroupId.x * ListedFragment_Size;
	Fragment f = LoadFragment( nEntry );
	if ( f.bNone )
		return;

	// A large one goes in the list, its parts after the parts listed, where there's room for both (a fragment listed whose
	// parts don't fit is left out by its entry)
#if ( D_PARTS == 1 )
	if ( f.nScan > Chunk )
	{
		uint nParts = Parts( f );
		if ( nThread == 0 )
		{
			uint nPartCapacity = PartCapacity();
			uint nLarge, nFirst = nPartCapacity;
			g_Large.InterlockedAdd( 28, 1, nLarge );
			if ( nLarge % GroupSize == 0 )
				g_Large.InterlockedAdd( 16, 1 );
			if ( nLarge < LargeCapacity() )
			{
				g_Large.InterlockedAdd( 0, nParts, nFirst );
				bool bRoom = nFirst <= nPartCapacity && nParts <= nPartCapacity - nFirst;
				g_Large.Store( LargeRecord( nLarge ), bRoom ? nEntry : None );
				g_Large.Store( LargeRecord( nLarge ) + 4, 0 );
				if ( !bRoom )
					nFirst = nPartCapacity;
			}
			gs_Start = nFirst;
			gs_Owned = nLarge;
		}
		GroupMemoryBarrierWithGroupSync();
		uint nFirst = gs_Start;
		if ( nFirst != PartCapacity() )
		{
			if ( nThread < nParts )
				g_Large.Store( PartAt( nFirst + nThread ), gs_Owned * MaxParts + nThread );
			return;
		}
	}
#endif

	uint nOwned = CountOwned( f, 0, f.nScan, nThread );
	if ( nOwned == 0 )
		return;
	if ( nThread == 0 )
	{
		SetVisible( nEntry );
		gs_Start = DrawFragment( f, nOwned );
	}
	GroupMemoryBarrierWithGroupSync();
	uint nStart = gs_Start;
	if ( nStart != None )
		CopyOwned( f, 0, f.nScan, nStart, nThread );
#elif ( D_PARTS == 3 )
	// A thread a listed fragment: drawn as one group would draw it, once
	uint nLarge = vGroupId.x * GroupSize + nThread;
	if ( nLarge >= min( g_Large.Load( 28 ), LargeCapacity() ) )
		return;
	uint nRecord = LargeRecord( nLarge );
	uint nEntry = g_Large.Load( nRecord );
	if ( nEntry == None )
		return;
	Fragment f = LoadFragment( nEntry );
	uint nOwned = g_Large.Load( nRecord + 4 );
	g_Large.Store( nRecord + 8, nOwned != 0 ? DrawFragment( f, nOwned ) : None );
#else
	// A group a listed part
	if ( vGroupId.x >= PartCapacity() )
		return;
	uint nWord = g_Large.Load( PartAt( vGroupId.x ) );
	uint nRecord = LargeRecord( nWord / MaxParts );
	uint nPart = nWord % MaxParts;
	uint nEntry = g_Large.Load( nRecord );
	Fragment f = LoadFragment( nEntry );
	uint nParts = Parts( f );
	uint nLo, nHi;
	PartRange( f, nParts, nPart, nLo, nHi );

	#if ( D_PARTS == 2 )
		// Each part's owned triangles, and the fragment's
		uint nOwned = CountOwned( f, nLo, nHi, nThread );
		if ( nThread == 0 )
		{
			g_Large.Store( nRecord + ( 4 + nPart ) * 4, nOwned );
			if ( nOwned != 0 )
			{
				g_Large.InterlockedAdd( nRecord + 4, nOwned );
				SetVisible( nEntry );
			}
		}
	#else
		// Copied where the parts before it end
		uint nStart = g_Large.Load( nRecord + 8 );
		if ( nStart == None )
			return;
		if ( nThread == 0 )
			gs_Start = 0;
		GroupMemoryBarrierWithGroupSync();
		if ( nThread < nPart )
			InterlockedAdd( gs_Start, g_Large.Load( nRecord + ( 4 + nThread ) * 4 ) );
		GroupMemoryBarrierWithGroupSync();
		CopyOwned( f, nLo, nHi, nStart + gs_Start * 3, nThread );
	#endif
#endif
}

#endif // CS

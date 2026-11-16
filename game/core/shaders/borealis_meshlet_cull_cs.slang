// borealis_meshlet_cull_cs.shader as a pass module: Borealis.Gpu draws this. The .shader only declares its constants (its
// reflection is what the backend's struct is checked against): meshlets draw only where pass modules do.
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "Borealis meshlet culling: the fragments a depth prepass listed, their meshlets tested against the view in chunks, then those that can reach a sample drawn from compacted indices.";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// MODES: Default();

#include "sbox/pass.slangh"

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "system.fxc" // This should always be the first include in COMMON

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_CS )
#include "common.fxc"
#include "transform_buffer.fxc"

#ifndef BINDLESS_VERTEX_BUFFERS_DECLARED
#define BINDLESS_VERTEX_BUFFERS_DECLARED
ByteAddressBuffer g_bindless_VertexBuffers[] EXTERNAL_DESC_SET( t, g_globalLateBoundBindlessSet, 17 );
#endif

// The call's region (listed fragments in; instance records, commands, multi-draw counts and fragment words out), the
// view's prepass compacted indices (the cursor is the first word), its meshlet bits (one per meshlet, by its index among
// every meshlet) and a stamp per visibility fragment whose bits this frame's prepass wrote, for its forward compaction
RWByteAddressBuffer g_Frame < Attribute( "BorealisCullFrame" ); >;
RWByteAddressBuffer g_Compacted < Attribute( "BorealisMeshletCompacted" ); >;
RWByteAddressBuffer g_MeshletBits < Attribute( "BorealisMeshletBits" ); >;
RWByteAddressBuffer g_MeshletStamps < Attribute( "BorealisMeshletStamps" ); >;

// Constants: the float members are g_n-named too, as the backend's checker reads one prefix
cbuffer BorealisMeshletCull_t
{
	uint g_nMeshletListed;			// byte offset of the listed fragments: dispatch arguments, then 32 bytes each
	uint g_nMeshletInstances;		// byte offset of the instance records
	uint g_nMeshletCommands;		// byte offset of the commands
	uint g_nMeshletFirstInstance;	// the first instance record's index in g_Frame
	uint g_nMeshletRuns;			// byte offset of the multi-draws: first command slot and count each
	uint g_nMeshletDraws;			// bindless slot of the draw table
	uint g_nMeshletRunTable;		// bindless slot of the meshlet runs, 16 bytes per draw record
	uint g_nMeshletCapacity;		// indices g_Compacted holds
	uint g_nMeshletFragmentWords;	// g_Frame byte offset of instance 0's fragment word for the ids (wraps), or None
	uint g_nMeshletSamples;			// the depth target's samples: 1 or 4 cull meshlets between samples, others don't
	uint g_nMeshletPass;			// 0: test and count, 1: place, 2: copy
	uint g_nMeshletFrame;			// this frame's stamp
	float4 g_nMeshletViewport;		// width and height in pixels, and the meshlet bits g_MeshletBits holds
	float4 g_nMeshletClipX;			// world to clip: x, y, z, w = dot( row, float4( world, 1 ) ), y before the vertex shaders negate it
	float4 g_nMeshletClipY;
	float4 g_nMeshletClipZ;
	float4 g_nMeshletClipW;
};

static const uint None = 0xffffffffu;

// Meshlets a group takes of a fragment: a fragment's chunks are the dispatch's second dimension (ResidentRecords' 4)
static const uint ChunkMeshlets = 1024;

uint LoadIndex( ByteAddressBuffer page, uint nIndex, bool b32 )
{
	if ( b32 )
		return page.Load( nIndex * 4 );

	uint nWord = page.Load( ( nIndex * 2 ) & ~3u );
	return ( nIndex & 1 ) != 0 ? nWord >> 16 : nWord & 0xffff;
}

uint TakeSlot( uint nRun )
{
	uint nIndex;
	g_Frame.InterlockedAdd( g_nMeshletRuns + nRun * 8 + 4, 1, nIndex );
	return g_Frame.Load( g_nMeshletRuns + nRun * 8 ) + nIndex;
}

// An instance record and its command, and the ids' fragment word
void WriteDraw( uint nSlot, uint nTransform, uint nDraw, uint nVertexBase, uint nIndexCount, uint nFirstIndex, uint nBaseVertex )
{
	g_Frame.Store( g_nMeshletInstances + nSlot * 8, nTransform );
	g_Frame.Store( g_nMeshletInstances + nSlot * 8 + 4, nDraw );

	uint nCommand = g_nMeshletCommands + nSlot * 20;
	g_Frame.Store( nCommand, nIndexCount );
	g_Frame.Store( nCommand + 4, 1 );
	g_Frame.Store( nCommand + 8, nFirstIndex );
	g_Frame.Store( nCommand + 12, nBaseVertex );
	g_Frame.Store( nCommand + 16, g_nMeshletFirstInstance + nSlot );

	if ( g_nMeshletFragmentWords != None && nVertexBase != None )
		g_Frame.Store( g_nMeshletFragmentWords + ( g_nMeshletFirstInstance + nSlot ) * 4, nVertexBase );
}

float4 Clip( float3 vPosition, float flW )
{
	float4 v = float4( vPosition, flW );
	return float4( dot( g_nMeshletClipX, v ), dot( g_nMeshletClipY, v ), dot( g_nMeshletClipZ, v ), dot( g_nMeshletClipW, v ) );
}

// Whether a sample of the frame's grid lies in [vMin, vMax] (pixels): pixel centres, or the standard 4x positions
bool HoldsSample( float2 vMin, float2 vMax )
{
	if ( g_nMeshletSamples == 1 )
		return all( ceil( vMin - 0.5 ) <= floor( vMax - 0.5 ) );

	const float2 vSamples[4] = { float2( 0.375, 0.125 ), float2( 0.875, 0.375 ), float2( 0.125, 0.625 ), float2( 0.625, 0.875 ) };
	[unroll]
	for ( uint s = 0; s < 4; s++ )
	{
		if ( all( ceil( vMin - vSamples[s] ) <= floor( vMax - vSamples[s] ) ) )
			return true;
	}

	return false;
}

// A meshlet's box (centre and half extents in the mesh's space) under its instance's transform: outside one clip plane,
// or wholly in front of the near plane with screen bounds that hold no sample
bool Culled( float3 vCenter, float3 vExtents, float3x4 mTransform )
{
	float4 vC = Clip( mul( mTransform, float4( vCenter, 1 ) ), 1 );
	float4 vA0 = Clip( float3( mTransform[0][0], mTransform[1][0], mTransform[2][0] ) * vExtents.x, 0 );
	float4 vA1 = Clip( float3( mTransform[0][1], mTransform[1][1], mTransform[2][1] ) * vExtents.y, 0 );
	float4 vA2 = Clip( float3( mTransform[0][2], mTransform[1][2], mTransform[2][2] ) * vExtents.z, 0 );

	uint nOutLeft = 0, nOutRight = 0, nOutBottom = 0, nOutTop = 0, nOutFar = 0, nOutNear = 0;
	bool bFront = true;
	float2 vMin = 1e30;
	float2 vMax = -1e30;
	[unroll]
	for ( uint k = 0; k < 8; k++ )
	{
		float4 v = vC + ( ( k & 1 ) != 0 ? vA0 : -vA0 ) + ( ( k & 2 ) != 0 ? vA1 : -vA1 ) + ( ( k & 4 ) != 0 ? vA2 : -vA2 );
		nOutLeft += v.x < -v.w ? 1 : 0;
		nOutRight += v.x > v.w ? 1 : 0;
		nOutBottom += v.y < -v.w ? 1 : 0;
		nOutTop += v.y > v.w ? 1 : 0;
		nOutFar += v.z < 0 ? 1 : 0;
		nOutNear += v.z > v.w ? 1 : 0;

		// Reverse depth: the near plane is z = w
		bFront = bFront && v.w > 1e-4 && v.z <= v.w;
		float2 vPixel = float2( v.x / v.w * 0.5 + 0.5, 0.5 - v.y / v.w * 0.5 ) * g_nMeshletViewport.xy;
		vMin = min( vMin, vPixel );
		vMax = max( vMax, vPixel );
	}

	if ( nOutLeft == 8 || nOutRight == 8 || nOutBottom == 8 || nOutTop == 8 || nOutFar == 8 || nOutNear == 8 )
		return true;

	return bFront && ( g_nMeshletSamples == 1 || g_nMeshletSamples == 4 ) && !HoldsSample( vMin, vMax );
}

// A chunk's meshlet bits: a bit per meshlet of the chunk, in order
groupshared uint gs_Bits[ChunkMeshlets / 32];

// The kept meshlets of a chunk in the copy pass: first triangle << 8 | triangles, and where each one's triangles start
groupshared uint gs_Kept[ChunkMeshlets];
groupshared uint gs_Start[ChunkMeshlets];
groupshared uint gs_Count;
groupshared uint gs_Triangles;
groupshared uint gs_Base;
groupshared uint gs_Scan[256];

// The kept meshlet whose triangles hold kept triangle t: the last whose start is at or before it
uint KeptAt( uint t, uint nKept )
{
	uint lo = 0, hi = nKept - 1;
	while ( lo < hi )
	{
		uint mid = ( lo + hi + 1 ) / 2;
		if ( gs_Start[mid] <= t )
			lo = mid;
		else
			hi = mid - 1;
	}

	return lo;
}

// Write a chunk's bits into the view's meshlet bits from meshlet nGlobal on: set where kept, clear where culled, only the
// chunk's own meshlets (neighbours share the words)
void WriteBits( uint nGlobal, uint nMeshlets, uint nThread )
{
	if ( nThread >= ChunkMeshlets / 32 || nThread * 32 >= nMeshlets )
		return;

	uint nValid = nMeshlets - nThread * 32 >= 32 ? 0xffffffffu : ( 1u << ( nMeshlets - nThread * 32 ) ) - 1;
	uint nBits = gs_Bits[nThread] & nValid;
	uint nAt = nGlobal + nThread * 32;
	uint nShift = nAt & 31;
	uint nWord = ( nAt >> 5 ) * 4;
	g_MeshletBits.InterlockedOr( nWord, nBits << nShift );
	g_MeshletBits.InterlockedAnd( nWord, ~( ( nValid & ~nBits ) << nShift ) );
	if ( nShift != 0 )
	{
		g_MeshletBits.InterlockedOr( nWord + 4, nBits >> ( 32 - nShift ) );
		g_MeshletBits.InterlockedAnd( nWord + 4, ~( ( nValid & ~nBits ) >> ( 32 - nShift ) ) );
	}
}

// Group ( fragment, chunk ) of the listed fragments, three passes with barriers between:
// 0: test the chunk's meshlets, write their bits and add its kept triangles to the fragment's (listed entry word 5)
// 1: the fragment's first chunk places it: nothing, its kept triangles' room in the compacted indices (word 6), or whole
// 2: each chunk copies its kept meshlets' indices into the fragment's room, taking its own from word 6 as a cursor
// A listed entry: transform, draw record, the vertices' first bit for the ids, its multi-draw, its bucket's compacted
// multi-draw, then those two words (zeroed by the culling) and its visibility fragment (or None)
[numthreads( 256, 1, 1 )]
[shader( "compute" )] void MainCs( uint3 vGroupId : SV_GroupID, uint nThread : SV_GroupIndex )
{
	uint nEntry = g_nMeshletListed + 16 + vGroupId.x * 32;
	uint nDrawRecord = g_Frame.Load( nEntry + 4 );
	uint4 vRun = g_bindless_VertexBuffers[ g_nMeshletRunTable ].Load4( nDrawRecord * 16 );
	uint nMeshlets = min( vRun.z, 4 * ChunkMeshlets );
	uint nChunk = vGroupId.y * ChunkMeshlets;
	bool bBits = vRun.w + nMeshlets <= uint( g_nMeshletViewport.z );
	if ( nChunk >= nMeshlets )
		return;

	uint nChunkMeshlets = min( nMeshlets - nChunk, ChunkMeshlets );
	ByteAddressBuffer meshlets = g_bindless_VertexBuffers[ vRun.x ];

	if ( g_nMeshletPass == 0 )
	{
		uint nTransform = g_Frame.Load( nEntry );
		float3x4 mTransform = g_TransformData[ nTransform ].transform;
		if ( nThread < ChunkMeshlets / 32 )
			gs_Bits[nThread] = 0;
		if ( nThread == 0 )
			gs_Triangles = 0;
		GroupMemoryBarrierWithGroupSync();

		uint nMine = 0;
		for ( uint m = nThread; m < nChunkMeshlets; m += 256 )
		{
			uint nAt = ( vRun.y + nChunk + m ) * 32;
			uint4 r0 = meshlets.Load4( nAt );
			uint4 r1 = meshlets.Load4( nAt + 16 );
			if ( !Culled( asfloat( r0.xyz ), asfloat( r1.xyz ), mTransform ) )
			{
				InterlockedOr( gs_Bits[m >> 5], 1u << ( m & 31 ) );
				nMine += r1.w;
			}
		}

		nMine = WaveActiveSum( nMine );
		if ( WaveIsFirstLane() )
			InterlockedAdd( gs_Triangles, nMine );
		GroupMemoryBarrierWithGroupSync();

		if ( bBits )
			WriteBits( vRun.w + nChunk, nChunkMeshlets, nThread );
		if ( nThread == 0 && gs_Triangles != 0 )
			g_Frame.InterlockedAdd( nEntry + 20, gs_Triangles );
		return;
	}

	if ( g_nMeshletPass == 1 )
	{
		if ( vGroupId.y != 0 || nThread != 0 )
			return;

		uint nTransform = g_Frame.Load( nEntry );
		uint nVertexBase = g_Frame.Load( nEntry + 8 );
		uint nRun = g_Frame.Load( nEntry + 12 );
		uint nCompactRun = g_Frame.Load( nEntry + 16 );
		uint nKept = g_Frame.Load( nEntry + 20 );
		uint nFragment = g_Frame.Load( nEntry + 28 );
		uint4 vDraw = g_bindless_VertexBuffers[ g_nMeshletDraws ].Load4( nDrawRecord * 32 + 16 );
		uint nTriangles = vDraw.y / 3;

		uint nStart = None;
		if ( nKept != 0 && nKept < nTriangles && bBits )
		{
			// A buffer made this frame starts its cursor wherever: whole draws until its first clear
			g_Compacted.InterlockedAdd( 0, nKept * 3, nStart );
			if ( nStart < 4 || nStart >= g_nMeshletCapacity || nKept * 3 > g_nMeshletCapacity - nStart )
				nStart = None;
		}

		g_Frame.Store( nEntry + 24, nStart );
		if ( nStart != None )
			WriteDraw( TakeSlot( nCompactRun ), nTransform, nDrawRecord, nVertexBase, nKept * 3, nStart, vDraw.z );
		else if ( nKept != 0 )
			WriteDraw( TakeSlot( nRun ), nTransform, nDrawRecord, nVertexBase, vDraw.y, vDraw.x, vDraw.z );

		// Forward reads the bits only where they say what was drawn: not where kept meshlets drew whole for want of room
		if ( nFragment != None && bBits && vRun.z <= 4 * ChunkMeshlets && ( nStart != None || nKept == 0 || nKept == nTriangles ) )
			g_MeshletStamps.Store( nFragment * 4, g_nMeshletFrame );
		return;
	}

	// Pass 2: the chunk's kept meshlets, from its bits, into the fragment's room
	uint nStart = g_Frame.Load( nEntry + 24 );
	if ( nStart == None || !bBits )
		return;

	if ( nThread == 0 )
	{
		gs_Count = 0;
		gs_Triangles = 0;
	}
	GroupMemoryBarrierWithGroupSync();

	for ( uint m = nThread; m < nChunkMeshlets; m += 256 )
	{
		uint nBit = vRun.w + nChunk + m;
		if ( ( ( g_MeshletBits.Load( ( nBit >> 5 ) * 4 ) >> ( nBit & 31 ) ) & 1 ) == 0 )
			continue;

		uint nAt = ( vRun.y + nChunk + m ) * 32;
		uint nFirst = meshlets.Load( nAt + 12 );
		uint nCount = meshlets.Load( nAt + 28 );
		uint nSlot;
		InterlockedAdd( gs_Count, 1, nSlot );
		gs_Kept[nSlot] = nFirst << 8 | ( nCount & 0xff );
		InterlockedAdd( gs_Triangles, nCount );
	}

	GroupMemoryBarrierWithGroupSync();
	uint nKeptMeshlets = gs_Count;
	if ( nKeptMeshlets == 0 )
		return;

	// The chunk's room in the fragment's: word 6 is a cursor now, its command written in pass 1
	if ( nThread == 0 )
	{
		uint nBase;
		g_Frame.InterlockedAdd( nEntry + 24, gs_Triangles * 3, nBase );
		gs_Base = nBase;
	}

	// Where each kept meshlet's triangles start: an exclusive scan of their counts, 256 at a time
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
			gs_Start[c + nThread] = nRunning + gs_Scan[nThread] - nValue;
		nRunning += gs_Scan[255];
		GroupMemoryBarrierWithGroupSync();
	}

	uint4 vDrawHead = g_bindless_VertexBuffers[ g_nMeshletDraws ].Load4( nDrawRecord * 32 );
	uint4 vDraw = g_bindless_VertexBuffers[ g_nMeshletDraws ].Load4( nDrawRecord * 32 + 16 );
	ByteAddressBuffer page = g_bindless_VertexBuffers[ vDraw.w ];
	bool b32 = ( vDrawHead.w & 2 ) != 0;
	uint nAt = gs_Base;

	// Every kept index, four a lane at a time: their loads issued together, then their stores
	uint nIndices = nRunning * 3;
	for ( uint i0 = nThread; i0 < nIndices; i0 += 1024 )
	{
		uint4 vI = i0 + uint4( 0, 256, 512, 768 );
		uint4 vSource;
		[unroll]
		for ( uint u = 0; u < 4; u++ )
		{
			uint i = min( vI[u], nIndices - 1 );
			uint t = i / 3;
			uint k = KeptAt( t, nKeptMeshlets );
			vSource[u] = vDraw.x + ( ( gs_Kept[k] >> 8 ) + t - gs_Start[k] ) * 3 + i % 3;
		}

		uint4 vIndex = uint4( LoadIndex( page, vSource.x, b32 ), LoadIndex( page, vSource.y, b32 ), LoadIndex( page, vSource.z, b32 ), LoadIndex( page, vSource.w, b32 ) );
		[unroll]
		for ( uint u = 0; u < 4; u++ )
		{
			if ( vI[u] < nIndices )
				g_Compacted.Store( ( nAt + vI[u] ) * 4, vIndex[u] );
		}
	}
}

#endif // CS

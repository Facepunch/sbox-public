// borealis_meshlet_cull_cs: a standalone module, the whole shader for Borealis.Gpu (docs/managed/standalone-modules.md). Its
// reflection is what the backend's constants struct is checked against.
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "Borealis meshlet culling: a group per fragment a depth prepass listed tests its meshlets against the view and draws those that can reach a sample from compacted indices.";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#include "sbox/pass.slangh"

Mode( Default );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "system.fxc" // This should always be the first include in COMMON

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_CS )
#include "common.fxc"
#include "transform_buffer.fxc"
#include "borealis_meshlet.fxc"

#ifndef BINDLESS_VERTEX_BUFFERS_DECLARED
#define BINDLESS_VERTEX_BUFFERS_DECLARED
ByteAddressBuffer g_bindless_VertexBuffers[] EXTERNAL_DESC_SET( t, g_globalLateBoundBindlessSet, 17 );
#endif

// The call's region (listed fragments in; instance records, commands, multi-draw counts and fragment words out), the
// view's prepass compacted indices, 32-bit and 16-bit (each one's cursor is its first word) and, per visibility fragment, the frame, where its kept
// triangles start in the compacted indices (None: drawn whole) and how many, for its forward compaction; and the copy
// jobs the test pass leaves for the copy pass
RWByteAddressBuffer g_Frame < Attribute( "BorealisCullFrame" ); >;
RWByteAddressBuffer g_Compacted < Attribute( "BorealisMeshletCompacted" ); >;
RWByteAddressBuffer g_Compacted16 < Attribute( "BorealisMeshletCompacted16" ); >;
RWByteAddressBuffer g_MeshletStamps < Attribute( "BorealisMeshletStamps" ); >;
RWByteAddressBuffer g_MeshletJobs < Attribute( "BorealisMeshletJobs" ); >;

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
	uint g_nMeshletCapacity;		// indices g_Compacted and g_Compacted16 hold
	uint g_nMeshletFragmentWords;	// g_Frame byte offset of instance 0's fragment word for the ids (wraps), or None
	uint g_nMeshletSamples;			// the depth target's samples: 1 or 4 cull meshlets between samples, others don't
	uint g_nMeshletPass;			// 0: test and place (a group per listed fragment), 1: copy (a group per job), 2: test and place for a shadow view
	uint g_nMeshletFrame;			// this frame's stamp
	float4 g_nMeshletViewport;		// width and height in pixels, and the copy jobs g_MeshletJobs holds
	float4 g_nMeshletClipX;			// world to clip: x, y, z, w = dot( row, float4( world, 1 ) ), y before the vertex shaders negate it
	float4 g_nMeshletClipY;
	float4 g_nMeshletClipZ;
	float4 g_nMeshletClipW;
};

static const uint None = 0xffffffffu;

// Meshlets a group takes of a fragment at a time: at most four chunks a fragment (BackendContext.MeshletChunks)
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

// The view the meshlets are culled against
MeshletView View()
{
	MeshletView view;
	view.vClipX = g_nMeshletClipX;
	view.vClipY = g_nMeshletClipY;
	view.vClipZ = g_nMeshletClipZ;
	view.vClipW = g_nMeshletClipW;
	view.vViewport = g_nMeshletViewport.xy;
	view.nSamples = g_nMeshletSamples;
	return view;
}

// The kept meshlets of the chunk a group is on: first triangle << 8 | triangles
groupshared uint gs_Kept[ChunkMeshlets];
groupshared uint gs_Count;
groupshared uint gs_Triangles;
groupshared uint gs_Padded;
groupshared uint gs_At;
groupshared uint gs_Jobs;
groupshared uint gs_Hole;
groupshared uint gs_Scan[64];

// Below this share of a fragment's triangles culled, copying the rest costs more than drawing them: it draws whole
static const float WholeKept = 0.7;

// The copy pass: a group per job, each job one kept meshlet's indices (destination, source, indices, page slot with bit 31
// for 32-bit indices), after the header (job count, which is also the dispatch's group count, then 1, 1). A 16-bit page's
// meshlets go to the 16-bit buffer two indices a word, an odd triangle count padded with a triangle of its last index
void Copy( uint nJob, uint nThread )
{
	if ( nJob >= uint( g_nMeshletViewport.z ) )
		return;

	uint4 vJob = g_MeshletJobs.Load4( 16 + nJob * 16 );

	// A meshlet's triangles at most (255, padded), within the compacted buffer: anything else isn't a job this call wrote
	if ( vJob.z > 768 || vJob.x >= g_nMeshletCapacity || vJob.z + 3 > g_nMeshletCapacity - vJob.x )
		return;
	ByteAddressBuffer page = g_bindless_VertexBuffers[ vJob.w & 0x7fffffffu ];
	if ( ( vJob.w & 0x80000000u ) != 0 )
	{
		for ( uint i = nThread; i < vJob.z; i += 64 )
			g_Compacted.Store( ( vJob.x + i ) * 4, LoadIndex( page, vJob.y + i, true ) );
		return;
	}

	uint nPadded = vJob.z + ( ( vJob.z / 3 ) & 1 ) * 3;
	for ( uint w = nThread; w * 2 < nPadded; w += 64 )
	{
		uint i0 = min( w * 2, vJob.z - 1 );
		uint i1 = min( w * 2 + 1, vJob.z - 1 );
		g_Compacted16.Store( ( vJob.x / 2 + w ) * 4, LoadIndex( page, vJob.y + i0, false ) | LoadIndex( page, vJob.y + i1, false ) << 16 );
	}
}

// A group per listed fragment, its meshlets in chunks of ChunkMeshlets: tests each chunk's meshlets and draws those that
// can reach a sample from room of the chunk's own in the compacted indices, from the bucket's compacted multi-draw
// (MeshletChunks slots a fragment), leaving a copy job per kept meshlet for the copy pass. A fragment of one chunk draws
// whole from its page when most of its triangles are kept, or when there's no room, and stamps what it drew for its
// forward compaction; of several chunks, the first without room draws the fragment whole, once.
// A listed entry: transform, draw record, the vertices' first bit for the ids, its multi-draw, its bucket's compacted
// multi-draw, two words zeroed by the culling, its visibility fragment or None
[numthreads( 64, 1, 1 )]
[shader( "compute" )] void MainCs( uint3 vGroupId : SV_GroupID, uint nThread : SV_GroupIndex )
{
	if ( g_nMeshletPass == 1 )
	{
		Copy( vGroupId.x, nThread );
		return;
	}

	uint nEntry = g_nMeshletListed + 16 + vGroupId.x * 32;
	uint nDrawRecord = g_Frame.Load( nEntry + 4 );
	uint4 vRun = g_bindless_VertexBuffers[ g_nMeshletRunTable ].Load4( nDrawRecord * 16 );
	uint nMeshlets = min( vRun.z, 4 * ChunkMeshlets );
	bool bOneChunk = nMeshlets <= ChunkMeshlets;
	ByteAddressBuffer meshlets = g_bindless_VertexBuffers[ vRun.x ];
	uint nTransform = g_Frame.Load( nEntry );
	float3x4 mTransform = g_TransformData[ nTransform ].transform;
	uint nVertexBase = g_Frame.Load( nEntry + 8 );
	uint nFragment = g_Frame.Load( nEntry + 28 );
	uint4 vDraw = g_bindless_VertexBuffers[ g_nMeshletDraws ].Load4( nDrawRecord * 32 + 16 );
	uint4 vDrawHead = g_bindless_VertexBuffers[ g_nMeshletDraws ].Load4( nDrawRecord * 32 );
	bool b32 = ( vDrawHead.w & 2 ) != 0;
	uint nPage = vDraw.w | ( b32 ? 0x80000000u : 0 );
	bool bWhole = false;
	MeshletView view = View();
	bool bShadow = g_nMeshletPass == 2;

	for ( uint nChunk = 0; nChunk < nMeshlets && !bWhole; nChunk += ChunkMeshlets )
	{
		uint nChunkMeshlets = min( nMeshlets - nChunk, ChunkMeshlets );
		if ( nThread == 0 )
		{
			gs_Count = 0;
			gs_Triangles = 0;
			gs_Padded = 0;
			gs_Hole = None;
		}
		GroupMemoryBarrierWithGroupSync();

		// Each meshlet tested once; a kept one joins the list (a 16-bit one's triangles padded to an even count)
		for ( uint m = nThread; m < nChunkMeshlets; m += 64 )
		{
			uint nAt = ( vRun.y + nChunk + m ) * 32;
			uint4 r0 = meshlets.Load4( nAt );
			uint4 r1 = meshlets.Load4( nAt + 16 );
			if ( !MeshletOutside( view, asfloat( r0.xyz ), asfloat( r1.xyz ), mTransform, bShadow ) )
			{
				uint nSlot;
				InterlockedAdd( gs_Count, 1, nSlot );
				gs_Kept[nSlot] = r0.w << 8 | ( r1.w & 0xff );
				InterlockedAdd( gs_Triangles, r1.w );
				InterlockedAdd( gs_Padded, b32 ? r1.w : r1.w + ( r1.w & 1 ) );
			}
		}

		GroupMemoryBarrierWithGroupSync();
		uint nKeptMeshlets = gs_Count;
		uint nKept = gs_Triangles;
		uint nPadded = gs_Padded;
		if ( nKept == 0 )
		{
			if ( bOneChunk && nThread == 0 && nFragment != None )
				g_MeshletStamps.Store4( nFragment * 16, uint4( g_nMeshletFrame, None, 0, 0 ) );
			GroupMemoryBarrierWithGroupSync();
			continue;
		}

		if ( nThread == 0 )
		{
			// Room for its kept triangles and a copy job per kept meshlet; else whole
			uint nStart = None;
			uint nJobs = None;
			if ( !bOneChunk || float( nKept ) < WholeKept * float( vDraw.y / 3 ) )
			{
				g_MeshletJobs.InterlockedAdd( 0, nKeptMeshlets, nJobs );
				if ( nJobs + nKeptMeshlets > uint( g_nMeshletViewport.z ) )
				{
					// No room: the slots it took below the capacity would keep whatever an earlier call left there, and
					// the copy pass runs every slot up to the count, so they're zeroed (a job of no indices)
					gs_Hole = nJobs;
					nJobs = None;
				}
				else
				{
					// A buffer made this frame starts its cursor wherever: whole draws until its first clear
					if ( b32 )
						g_Compacted.InterlockedAdd( 0, nPadded * 3, nStart );
					else
						g_Compacted16.InterlockedAdd( 0, nPadded * 3, nStart );
					if ( nStart < 4 || nStart >= g_nMeshletCapacity || nPadded * 3 > g_nMeshletCapacity - nStart )
						nStart = None;
				}
			}

			gs_At = nStart;
			gs_Jobs = nJobs;
			if ( nStart != None )
			{
				// The bucket's compacted multi-draws: 32-bit, then 16-bit
				WriteDraw( TakeSlot( g_Frame.Load( nEntry + 16 ) + ( b32 ? 0 : 1 ) ), nTransform, nDrawRecord, nVertexBase, nPadded * 3, nStart, vDraw.z );
				if ( bOneChunk && nFragment != None )
					g_MeshletStamps.Store4( nFragment * 16, uint4( g_nMeshletFrame, nStart, nPadded, b32 ? 0 : 1 ) );
			}
			else
			{
				// Whole, once a fragment: a chunk without room ends its fragment's chunks
				WriteDraw( TakeSlot( g_Frame.Load( nEntry + 12 ) ), nTransform, nDrawRecord, nVertexBase, vDraw.y, vDraw.x, vDraw.z );
				if ( bOneChunk && nFragment != None )
					g_MeshletStamps.Store4( nFragment * 16, uint4( g_nMeshletFrame, None, vDraw.y / 3, 0 ) );
			}
		}

		GroupMemoryBarrierWithGroupSync();
		uint nAt = gs_At;
		uint nJobs = gs_Jobs;
		uint nHole = gs_Hole;
		for ( uint k = nThread; nHole != None && k < nKeptMeshlets && nHole + k < uint( g_nMeshletViewport.z ); k += 64 )
			g_MeshletJobs.Store4( 16 + ( nHole + k ) * 16, uint4( 0, 0, 0, 0 ) );
		if ( nAt == None )
		{
			// Jobs it took but has no room for copy nothing; its earlier chunks' kept meshlets are drawn twice, which the
			// depth test makes harmless
			for ( uint k = nThread; nJobs != None && k < nKeptMeshlets; k += 64 )
				g_MeshletJobs.Store4( 16 + ( nJobs + k ) * 16, uint4( 0, 0, 0, 0 ) );
			bWhole = true;
			GroupMemoryBarrierWithGroupSync();
			continue;
		}

		// Each kept meshlet's job: where its triangles go among the kept ones' (an exclusive scan of their padded counts)
		uint nRunning = 0;
		for ( uint c = 0; c < nKeptMeshlets; c += 64 )
		{
			uint nKeptAt = c + nThread < nKeptMeshlets ? gs_Kept[c + nThread] : 0;
			uint nTriangles = nKeptAt & 0xff;
			uint nValue = b32 ? nTriangles : nTriangles + ( nTriangles & 1 );
			gs_Scan[nThread] = nValue;
			GroupMemoryBarrierWithGroupSync();
			[unroll]
			for ( uint d = 1; d < 64; d <<= 1 )
			{
				uint nAdd = nThread >= d ? gs_Scan[nThread - d] : 0;
				GroupMemoryBarrierWithGroupSync();
				gs_Scan[nThread] += nAdd;
				GroupMemoryBarrierWithGroupSync();
			}

			if ( c + nThread < nKeptMeshlets )
			{
				uint nStartAt = nRunning + gs_Scan[nThread] - nValue;
				g_MeshletJobs.Store4( 16 + ( nJobs + c + nThread ) * 16, uint4( nAt + nStartAt * 3, vDraw.x + ( nKeptAt >> 8 ) * 3, nTriangles * 3, nPage ) );
			}

			nRunning += gs_Scan[63];
			GroupMemoryBarrierWithGroupSync();
		}
	}
}

#endif // CS

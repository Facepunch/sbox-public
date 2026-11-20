// borealis_resident_cull_cs: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "Borealis resident cull: culls every view's resident batches in one dispatch, a thread per entry looping over the views, and writes their instance records and indirect commands.";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#include "sbox/pass.slangh"

Mode( Default );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "system.fxc" // This should always be the first include in COMMON

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_CS )
#include "common.fxc"
#include "borealis_records.fxc"

// The raw buffer array in the bindless set (binding 63): the fragment and draw tables
#ifndef BINDLESS_VERTEX_BUFFERS_DECLARED
#define BINDLESS_VERTEX_BUFFERS_DECLARED
ByteAddressBuffer g_bindless_VertexBuffers[] EXTERNAL_DESC_SET( t, g_globalLateBoundBindlessSet, 17 );
#endif

// The frame's region: the items and views in; each view's instance records, commands, multi-draw counts and listed
// fragments out. Each plan's batch records and entry map are in a buffer of its own, in the bindless array (Borealis.Backend/ResidentRecords.cs, docs/reference/borealis-gpu-driven.md)
RWByteAddressBuffer g_Frame < Attribute( "BorealisCullFrame" ); >;

// The visibility-driven forward pass: each fragment's frame stamp, written where a prepass view draws ids and read by its
// forward view in the next dispatch
RWByteAddressBuffer g_Stamps < Attribute( "BorealisCullStamps" ); >;

// Generated LODs: the counters of level changes, then the levels views keep, a byte per visibility fragment
RWByteAddressBuffer g_Levels < Attribute( "BorealisCullLevels" ); >;

cbuffer BorealisResidentCull_t
{
	uint g_nResidentItems;		// byte offset of the items, 32 bytes each (ResidentCullItem)
	uint g_nResidentItemCount;
	uint g_nResidentViews;		// byte offset of the views, 32 bytes each (ResidentCullView)
	uint g_nResidentEntries;	// threads: every item's entries
	uint g_nResidentFragments;	// bindless slot of the fragment table
	uint g_nResidentDraws;		// bindless slot of the draw table
	uint g_nResidentFrame;		// this frame's stamp
	uint g_nResidentFrameSlot;	// bindless slot of g_Frame, read there where a whole wave reads the same words
	uint g_nResidentExpand;		// not 0: expand one plan's entries (g_Frame is its records), writing its entry records here
	uint g_nResidentExpandMap;	// its entry map
	uint g_nResidentExpandHeaders;	// its batches' headers
	uint g_nResidentMeshletRuns;	// bindless slot of the meshlet runs (16 bytes per draw record), or None
	uint g_nResidentLodTable;		// bindless slot of the generated LOD table (128 bytes per draw record), or None
	uint g_nResidentLodCount;		// draw records the LOD table covers
	uint g_nResidentPad0;
	uint g_nResidentPad1;
};

static const uint None = 0xffffffffu;

bool Inside( float4 vPlane, float3 vCenter, float3 vExtents )
{
	return dot( vPlane.xyz, vCenter ) + vPlane.w + dot( vExtents, abs( vPlane.xyz ) ) >= 0;
}

// A resident mesh group's header (ResidentMeshHeader) as MeshLod reads it: its LOD word and switch distances 1 to 3, then 4 to 7
void LoadMeshHeader( ByteAddressBuffer records, uint nAt, out uint4 vHeader0, out uint4 vHeader1 )
{
	uint nSwitches = nAt + ResidentMeshHeader_SwitchDistances;
	vHeader0 = uint4( records.Load( nAt + ResidentMeshHeader_Lods ), records.Load( nSwitches ), records.Load( nSwitches + 4 ), records.Load( nSwitches + 8 ) );
	vHeader1 = uint4( records.Load( nSwitches + 12 ), records.Load( nSwitches + 16 ), records.Load( nSwitches + 20 ), records.Load( nSwitches + 24 ) );
}

// A batch record (BatchRecord) as the culls read it: first, count, transform and run; then first visibility fragment,
// moved words or compacted run, flags and first vertex bit
void LoadBatch( ByteAddressBuffer records, uint nAt, out uint4 vBatch, out uint4 vVisibility )
{
	vBatch = uint4( records.Load( nAt + BatchRecord_First ), records.Load( nAt + BatchRecord_Count ), records.Load( nAt + BatchRecord_Transform ), records.Load( nAt + BatchRecord_Run ) );
	vVisibility = uint4( records.Load( nAt + BatchRecord_FirstVisibility ), records.Load( nAt + BatchRecord_Moved ), records.Load( nAt + BatchRecord_Flags ), records.Load( nAt + BatchRecord_FirstVertexBit ) );
}

void LoadBatch( RWByteAddressBuffer records, uint nAt, out uint4 vBatch, out uint4 vVisibility )
{
	vBatch = uint4( records.Load( nAt + BatchRecord_First ), records.Load( nAt + BatchRecord_Count ), records.Load( nAt + BatchRecord_Transform ), records.Load( nAt + BatchRecord_Run ) );
	vVisibility = uint4( records.Load( nAt + BatchRecord_FirstVisibility ), records.Load( nAt + BatchRecord_Moved ), records.Load( nAt + BatchRecord_Flags ), records.Load( nAt + BatchRecord_FirstVertexBit ) );
}

// A resident filter's test (GpuContext.ResidentFilter): whether a view keeps a caster by its moved word (its frame, bit 31
// restless; None for a static one) against the stamps the far cascade's cache was drawn at, as SettledCasters.SettledBy does.
// 1: static and settled ones (the cache), 2: settled ones that aren't static (a baked sun's cache), 3: the rest that aren't
// static (drawn over the cache)
bool TakesMoved( uint nFilter, uint nMoved, uint nSettled, uint nRestless )
{
	bool bStatic = nMoved == None;
	bool bSettled = bStatic || ( ( nMoved & 0x80000000u ) != 0 ? ( nMoved & 0x7fffffffu ) <= nRestless : nMoved <= nSettled );
	if ( nFilter == 1 )
		return bSettled;
	if ( nFilter == 2 )
		return !bStatic && bSettled;
	return !bStatic && !bSettled;
}

// A resident mesh's LOD, as the CPU picks it (MeshRenderFeature.ChooseLod, RenderMesh.LodForScreenSize): its model's LOD count
// and switch distances (1 to 7) in its group's header record, its LOD scale (largest scale; -1 - LOD for a fixed one), and
// the view's LOD camera and tangent of half its field of view, and screen width, or texels per unit for a texel view
uint MeshLod( uint4 vHeader0, uint4 vHeader1, float3 vCenter, float flScale, float4 vLodCamera, float flLodWidth, bool bTexels )
{
	uint nLods = vHeader0.x & 0xff;
	if ( nLods <= 1 )
		return 0;
	if ( flScale < 0 )
		return min( uint( -flScale ) - 1, nLods - 1 );

	float flPixels = flLodWidth;
	if ( !bTexels )
	{
		float flDistance = distance( vCenter, vLodCamera.xyz );
		float flScreen = flDistance < 0.5 ? 1.0 : clamp( 0.5 / ( flDistance * vLodCamera.w ), 0.0, 1.0 );
		flPixels = flScreen * flLodWidth;
	}

	float flMetric = flPixels > 0 ? 50.0 / flPixels : 0;
	float flSwitch[8] = { 0, asfloat( vHeader0.y ), asfloat( vHeader0.z ), asfloat( vHeader0.w ), asfloat( vHeader1.x ), asfloat( vHeader1.y ), asfloat( vHeader1.z ), asfloat( vHeader1.w ) };
	uint nSwitches = ( vHeader0.x >> 8 ) & 0xff;
	uint nLod = nSwitches > 0 ? nSwitches - 1 : 0;
	for ( ; nLod > 0; nLod-- )
	{
		float flSwitchDistance = flSwitch[nLod] * flScale;
		if ( flSwitchDistance > 0 && flSwitchDistance < flMetric )
			break;
	}

	return min( nLod, nLods - 1 );
}

// Generated LODs (Borealis.Backend/Lods/LodStore.cs, docs/reference/borealis-gpu-driven.md "Generated LODs"). A draw record's LOD
// record, 128 bytes in the LOD table: its level count, a word unused and each level's error (a fraction of the fragment's
// bounds' half diagonal); then 16 bytes per level: first index, index count, draw record and a word unused
static const float LodHysteresis = 0.75;
static const uint LodStateHeader = 64;
static const uint LodSliceFragments = 524288;

// The coarsest level whose error, in the view's units, is at most flLimit (level 0 has none)
uint LodUnder( float flLimit, float flUnits, uint nLevels, float4 vErrors, float2 vMore )
{
	uint nLevel = 0;
	nLevel = nLevels > 0 && vErrors.x * flUnits <= flLimit ? 1 : nLevel;
	nLevel = nLevels > 1 && vErrors.y * flUnits <= flLimit ? 2 : nLevel;
	nLevel = nLevels > 2 && vErrors.z * flUnits <= flLimit ? 3 : nLevel;
	nLevel = nLevels > 3 && vErrors.w * flUnits <= flLimit ? 4 : nLevel;
	nLevel = nLevels > 4 && vMore.x * flUnits <= flLimit ? 5 : nLevel;
	nLevel = nLevels > 5 && vMore.y * flUnits <= flLimit ? 6 : nLevel;
	return nLevel;
}

// A fragment's level in a view, by the view's LOD mode (CullViewRecord.LodMode: 0 none, 1 by distance, 2 by texels, 3 by
// distance with the level kept per visibility fragment, its view's slice in bits 8-15) and units (LodUnits: pixels per unit at
// distance 1, or texels per unit, over the limit). Mode 1 takes the coarsest level under LodHysteresis of the limit. Mode 3
// keeps a fragment's level until its error goes over the limit, and goes coarser only to a level under LodHysteresis of it,
// so a still camera changes none and a slowly moving one changes each once; the prepass and forward of a view read and
// write the same byte, and every call picks the same (picking again from what one wrote changes nothing)
uint PickLod( uint nMode, float flScale, float3 vEye, float3 vCenter, float3 vExtents, uint nLevels, float4 vErrors, float2 vMore, uint nFragment )
{
	uint nKind = nMode & 0xff;
	if ( nKind == 0 || nLevels == 0 )
		return 0;

	// The error's units: per unit of error, pixels at the bounds' nearest point (never inside them), or texels
	float flUnits = length( vExtents ) * flScale;
	if ( nKind == 2 )
		return LodUnder( 1.0, flUnits, nLevels, vErrors, vMore );

	float flDistance = length( max( abs( vCenter - vEye ) - vExtents, 0.0 ) );
	if ( !( flDistance > 0 ) )
		return 0;
	flUnits /= flDistance;
	uint nFresh = LodUnder( LodHysteresis, flUnits, nLevels, vErrors, vMore );
	if ( nKind != 3 || nFragment >= LodSliceFragments )
		return nFresh;

	// The level kept: a byte per fragment (bits 0-3 the level, 4 its last change was to a coarser one, 5 it has changed)
	uint nAddress = LodStateHeader + ( nMode >> 8 ) * LodSliceFragments + nFragment;
	uint nWordAt = nAddress & ~3u;
	uint nShift = ( nAddress & 3u ) * 8;
	uint nWord = g_Levels.Load( nWordAt );
	uint nState = ( nWord >> nShift ) & 0xff;
	uint nKept = min( nState & 15, nLevels );
	float flKept = 0;
	flKept = nKept == 1 ? vErrors.x : flKept;
	flKept = nKept == 2 ? vErrors.y : flKept;
	flKept = nKept == 3 ? vErrors.z : flKept;
	flKept = nKept == 4 ? vErrors.w : flKept;
	flKept = nKept == 5 ? vMore.x : flKept;
	flKept = nKept == 6 ? vMore.y : flKept;
	uint nLevel = flKept * flUnits > 1.0 ? LodUnder( 1.0, flUnits, nLevels, vErrors, vMore ) : max( nKept, nFresh );
	if ( nLevel == ( nState & 15 ) )
		return nLevel;

	// Changed: written with a compare-exchange (the word's other bytes are other fragments'), and counted for the shimmer check
	bool bCoarser = nLevel > ( nState & 15 );
	uint nNew = nLevel | ( bCoarser ? 16u : 0u ) | 32u;
	bool bReversal = ( nState & 32 ) != 0 && ( ( nState & 16 ) != 0 ) != bCoarser;
	for ( uint nTry = 0; nTry < 16; nTry++ )
	{
		uint nOriginal;
		g_Levels.InterlockedCompareExchange( nWordAt, nWord, ( nWord & ~( 0xffu << nShift ) ) | ( nNew << nShift ), nOriginal );
		if ( nOriginal == nWord )
		{
			uint nCount;
			g_Levels.InterlockedAdd( 0, 1, nCount );
			if ( bReversal )
				g_Levels.InterlockedAdd( 4, 1, nCount );
			g_Levels.InterlockedAdd( bCoarser ? 8 : 12, 1, nCount );
			break;
		}

		// Another fragment's byte changed under it, or another call wrote this one's level first
		nWord = nOriginal;
		if ( ( ( nWord >> nShift ) & 15 ) == nLevel )
			break;
	}

	return nLevel;
}

// A lane's place in what a counter at nCounter (the same for the whole wave) counts: one atomic a wave instead of one a
// lane. Every lane of the wave calls it
uint WaveAppend( bool bTake, uint nCounter )
{
	uint nCount = WaveActiveCountBits( bTake );
	uint nBase = 0;
	if ( nCount > 0 && WaveIsFirstLane() )
		g_Frame.InterlockedAdd( nCounter, nCount, nBase );
	return WaveReadLaneFirst( nBase ) + WavePrefixCountBits( bTake );
}

// A batch record's sixth word with this bit: its bucket's meshlets are drawn with task and mesh shaders, from the task run
// the rest names (its first slot and count, then a run of 1 and 1, which make the count a task draw's arguments)
static const uint TaskRun = 0x80000000u;

// The next slot of multi-draw nRun (multi-draws at nRuns: first slot and count each) for each lane that takes one: lanes of
// the same multi-draw share one atomic. A wave's entries are a few batches', so it meets few multi-draws. Every lane calls it
uint WaveTakeSlot( bool bTake, uint nRuns, uint nRun, ByteAddressBuffer frame )
{
	uint nSlot = 0;
	bool bPending = bTake;
	while ( WaveActiveAnyTrue( bPending ) )
	{
		uint nLeader = WaveActiveMin( bPending ? nRun : None );
		bool bMine = bPending && nRun == nLeader;
		uint nCount = WaveActiveCountBits( bMine );
		uint nPrefix = WavePrefixCountBits( bMine );
		uint nBase = 0;
		if ( WaveIsFirstLane() )
		{
			g_Frame.InterlockedAdd( nRuns + nLeader * MultiDraw_Size + MultiDraw_Count, nCount, nBase );
			nBase += frame.Load( nRuns + nLeader * MultiDraw_Size + MultiDraw_FirstSlot );
		}
		nBase = WaveReadLaneFirst( nBase );
		if ( bMine )
		{
			nSlot = nBase + nPrefix;
			bPending = false;
		}
	}
	return nSlot;
}

// Two-phase Hi-Z occlusion (CullViewRecord.Flags bit 3; docs/reference/borealis-gpu-driven.md, "Hi-Z occlusion"), as
// borealis_gpu_driven_cull_cs defers: an ids fragment (not a forward one) forward didn't see last frame (its bit clear in the
// bits at bindless slot vViewFlags.z) is left for the second phase, as a 64-byte candidate at byte offset vViewFlags.w (group
// count, 1, 1, count, occluded; then from byte 32 the candidates, 256 a group). bMeshlets: the view's prepass would list it for the meshlet pass
bool HiZDeferred( uint4 vViewFlags, uint4 vVisibility, float3 vCenter, float3 vExtents, uint nTransform, uint nDrawRecord, uint3 vDraw, uint nVertexBase, uint nFragment, uint nRun, bool bMeshletView )
{
	if ( ( vViewFlags.x & 8 ) == 0 || ( vVisibility.z & 3 ) != 1 || nFragment == None || nVertexBase == None )
		return false;
	if ( ( ( g_bindless_VertexBuffers[ vViewFlags.z ].Load( ( nFragment >> 5 ) * 4 ) >> ( nFragment & 31 ) ) & 1 ) != 0 )
		return false;

	bool bMeshlets = false;
	if ( bMeshletView && ( vVisibility.z & 12 ) == 0 && g_nResidentMeshletRuns != None && ( vVisibility.y & 0x80000000u ) == 0 )
		bMeshlets = g_bindless_VertexBuffers[ g_nResidentMeshletRuns ].Load( nDrawRecord * MeshletRun_Size + MeshletRun_Count ) - 1 < 4096u;

	uint nAt;
	g_Frame.InterlockedAdd( vViewFlags.w + HiZCandidates_Count, 1, nAt );
	if ( ( nAt & 255 ) == 0 )
	{
		uint nGroups;
		g_Frame.InterlockedAdd( vViewFlags.w + HiZCandidates_GroupsX, 1, nGroups );
	}

	uint nEntry = vViewFlags.w + HiZCandidates_Size + nAt * HiZCandidate_Size;
	g_Frame.Store3( nEntry + HiZCandidate_Center, asuint( vCenter ) );
	g_Frame.Store( nEntry + HiZCandidate_Transform, nTransform );
	g_Frame.Store3( nEntry + HiZCandidate_Extents, asuint( vExtents ) );
	g_Frame.Store( nEntry + HiZCandidate_Draw, nDrawRecord );
	g_Frame.Store( nEntry + HiZCandidate_FirstIndex, vDraw.x );
	g_Frame.Store( nEntry + HiZCandidate_IndexCount, vDraw.y );
	g_Frame.Store( nEntry + HiZCandidate_BaseVertex, vDraw.z );
	g_Frame.Store( nEntry + HiZCandidate_VertexBase, nVertexBase );
	g_Frame.Store( nEntry + HiZCandidate_Fragment, nFragment );
	g_Frame.Store( nEntry + HiZCandidate_Run, nRun );
	g_Frame.Store( nEntry + HiZCandidate_CompactRun, vVisibility.y );
	g_Frame.Store( nEntry + HiZCandidate_Meshlets, bMeshlets ? 1u : 0u );
	return true;
}

// An expand dispatch: entry n of a new plan, whose records are g_Frame. What the cull reads of its fragment or resident
// mesh and draw, from the fragment and draw tables in host memory, into its entry record (ResidentCullEntryRecord, 48
// bytes) in video memory: centre and transform slot; extents and draw record; the draw's first index, index count and base
// vertex, and a resident mesh's LOD scale or a fragment's no-shadow flag. A resident mesh batch's first entry writes its
// group's header record at the batch's index
void Expand( uint n )
{
	if ( n >= g_nResidentEntries )
		return;

	uint nMapAt = g_nResidentExpandMap + n * ResidentCullEntry_Size;
	uint2 vMap = uint2( g_Frame.Load( nMapAt + ResidentCullEntry_Batch ), g_Frame.Load( nMapAt + ResidentCullEntry_Element ) );
	uint nBatch = vMap.x & 0x7fffffffu;
	uint i = vMap.y;
	uint4 vBatch;
	uint4 vVisibility;
	LoadBatch( g_Frame, nBatch * BatchRecord_Size, vBatch, vVisibility );
	ByteAddressBuffer fragments = g_bindless_VertexBuffers[ g_nResidentFragments ];
	ByteAddressBuffer draws = g_bindless_VertexBuffers[ g_nResidentDraws ];

	uint3 vCenter;
	uint3 vExtents;
	uint nTransform;
	uint nDrawRecord;
	uint nLast;
	if ( ( vVisibility.z & 8 ) != 0 )
	{
		// Resident mesh record (ResidentMeshRecord)
		uint nMeshAt = ( vBatch.x + 1 + i ) * ResidentMeshRecord_Size;
		vCenter = fragments.Load3( nMeshAt + ResidentMeshRecord_Center );
		vExtents = fragments.Load3( nMeshAt + ResidentMeshRecord_Extents );
		nTransform = fragments.Load( nMeshAt + ResidentMeshRecord_Transform );
		nDrawRecord = ( vBatch.z & 0x80000000u ) != 0 ? fragments.Load( ( vBatch.z & 0x7fffffffu ) * FragmentRecord_Size + i * 4 ) : vBatch.z;
		nLast = fragments.Load( nMeshAt + ResidentMeshRecord_LodScale );
		if ( i == 0 )
		{
			// Its group's header (ResidentMeshHeader), copied whole
			[unroll]
			for ( uint w = 0; w < ResidentMeshHeader_Size; w += 4 )
				g_Frame.Store( g_nResidentExpandHeaders + nBatch * ResidentMeshHeader_Size + w, fragments.Load( vBatch.x * ResidentMeshHeader_Size + w ) );
		}
	}
	else
	{
		// Fragment record (FragmentRecord): bit 31 of its instance word: it casts no shadow
		uint nFragmentAt = ( vBatch.x + i ) * FragmentRecord_Size;
		uint nInstance = fragments.Load( nFragmentAt + FragmentRecord_Instance );
		vCenter = fragments.Load3( nFragmentAt + FragmentRecord_Center );
		vExtents = fragments.Load3( nFragmentAt + FragmentRecord_Extents );
		nTransform = vBatch.z + ( nInstance & 0x7fffffffu );
		nDrawRecord = fragments.Load( nFragmentAt + FragmentRecord_Draw );
		nLast = nInstance >> 31;
	}

	// The draw record's first index, index count and base vertex
	uint nDrawAt = nDrawRecord * DrawRecord_Size;
	uint nAt = g_nResidentExpand + n * ResidentCullEntryRecord_Size;
	g_Frame.Store3( nAt + ResidentCullEntryRecord_Center, vCenter );
	g_Frame.Store( nAt + ResidentCullEntryRecord_Transform, nTransform );
	g_Frame.Store3( nAt + ResidentCullEntryRecord_Extents, vExtents );
	g_Frame.Store( nAt + ResidentCullEntryRecord_Draw, nDrawRecord );
	g_Frame.Store( nAt + ResidentCullEntryRecord_FirstIndex, draws.Load( nDrawAt + DrawRecord_FirstIndex ) );
	g_Frame.Store( nAt + ResidentCullEntryRecord_IndexCount, draws.Load( nDrawAt + DrawRecord_IndexCount ) );
	g_Frame.Store( nAt + ResidentCullEntryRecord_BaseVertex, draws.Load( nDrawAt + DrawRecord_BaseVertex ) );
	g_Frame.Store( nAt + ResidentCullEntryRecord_Last, nLast );
}

[numthreads( 64, 1, 1 )]
[shader( "compute" )] void MainCs( uint3 vGroupId : SV_GroupID, uint nThread : SV_GroupIndex )
{
	if ( g_nResidentExpand != 0 )
	{
		Expand( vGroupId.x * 64 + nThread );
		return;
	}

	// What the CPU wrote (items, views, view records, multi-draws' first slots) is read through the frame's bindless slot,
	// which the shader never writes: the same words for the whole wave
	ByteAddressBuffer frame = g_bindless_VertexBuffers[ g_nResidentFrameSlot ];

	// Its item: the last whose first entry is at or before the group's. An item is one plan's batches and the views that
	// cull them; items start on a multiple of 64 threads, so a group reads one item
	uint nGroupEntry = vGroupId.x * 64;
	uint nItem = 0;
	uint nHigh = g_nResidentItemCount > 0 ? g_nResidentItemCount - 1 : 0;
	while ( nItem < nHigh )
	{
		uint nMid = ( nItem + nHigh + 1 ) / 2;
		if ( frame.Load( g_nResidentItems + nMid * ResidentCullItem_Size + ResidentCullItem_FirstEntry ) <= nGroupEntry )
			nItem = nMid;
		else
			nHigh = nMid - 1;
	}

	// Item: first entry, entries, the bindless slot of its plan's records and its entry map's offset in them; its first view
	// and views, and its entry records' and batch headers' offsets in its records
	uint nItemAt = g_nResidentItems + nItem * ResidentCullItem_Size;
	uint4 vItem = uint4( frame.Load( nItemAt + ResidentCullItem_FirstEntry ), frame.Load( nItemAt + ResidentCullItem_Entries ), frame.Load( nItemAt + ResidentCullItem_Records ), frame.Load( nItemAt + ResidentCullItem_EntryMap ) );
	uint4 vItemViews = uint4( frame.Load( nItemAt + ResidentCullItem_FirstView ), frame.Load( nItemAt + ResidentCullItem_Views ), frame.Load( nItemAt + ResidentCullItem_EntryRecords ), frame.Load( nItemAt + ResidentCullItem_Headers ) );
	uint nLocal = nGroupEntry + nThread - vItem.x;
	bool bActive = g_nResidentItemCount > 0 && nGroupEntry + nThread < g_nResidentEntries && nLocal < vItem.y;

	// The entry: its batch and its place in it
	ByteAddressBuffer records = g_bindless_VertexBuffers[ vItem.z ];
	uint nMapAt = vItem.w + nLocal * ResidentCullEntry_Size;
	uint2 vMap = bActive ? uint2( records.Load( nMapAt + ResidentCullEntry_Batch ), records.Load( nMapAt + ResidentCullEntry_Element ) ) : 0;
	uint i = vMap.y;

	// Forward: whether the view's prepass draws ids for it (ResidentCullEntry.PrepassIds)
	bool bPrepassIds = ( vMap.x & 0x80000000u ) != 0;
	vMap.x &= 0x7fffffffu;

	// Batch record, as borealis_gpu_driven_cull_cs reads it: first fragment record (resident meshes: their group's header),
	// fragments, the first fragment's transform slot (resident meshes: the draw record, bit 31 a list of one per mesh), its
	// multi-draw; then its first visibility fragment, its multi-draw of compacted triangles, its flags (1: ids, 2: lists,
	// 8: resident meshes, bits 4-7 their LOD, the top 24 bits the vertex stride) and its first vertex bit
	uint4 vBatch = 0;
	uint4 vVisibility = 0;
	if ( bActive )
		LoadBatch( records, vMap.x * BatchRecord_Size, vBatch, vVisibility );
	bool bMeshes = ( vVisibility.z & 8 ) != 0;
	uint nBatchLod = ( vVisibility.z >> 4 ) & 15;
	uint nStride = vVisibility.z >> 8;

	// The entry's bounds, transform slot and draw, read once for every view from its entry record (written by the expand
	// dispatch), and a resident mesh batch's header
	float3 vCenter = 0;
	float3 vExtents = 0;
	float flLodScale = 0;
	uint4 vHeader0 = 0;
	uint4 vHeader1 = 0;
	bool bNoShadow = false;
	uint nTransform = 0;
	uint nDrawRecord = 0;
	uint4 vDraw = 0;
	if ( bActive )
	{
		uint nAt = vItemViews.z + nLocal * ResidentCullEntryRecord_Size;
		vCenter = asfloat( records.Load3( nAt + ResidentCullEntryRecord_Center ) );
		nTransform = records.Load( nAt + ResidentCullEntryRecord_Transform );
		vExtents = asfloat( records.Load3( nAt + ResidentCullEntryRecord_Extents ) );
		nDrawRecord = records.Load( nAt + ResidentCullEntryRecord_Draw );
		vDraw = uint4( records.Load( nAt + ResidentCullEntryRecord_FirstIndex ), records.Load( nAt + ResidentCullEntryRecord_IndexCount ), records.Load( nAt + ResidentCullEntryRecord_BaseVertex ), 0 );
		uint nLast = records.Load( nAt + ResidentCullEntryRecord_Last );
		if ( bMeshes )
		{
			flLodScale = asfloat( nLast );
			LoadMeshHeader( records, vItemViews.w + vMap.x * ResidentMeshHeader_Size, vHeader0, vHeader1 );
		}
		else
		{
			bNoShadow = nLast != 0;
		}
	}

	uint nFragment = vVisibility.x != None ? vVisibility.x + i : None;
	uint nVertexBase = vVisibility.w != None ? vVisibility.w + i * nStride : None;
	float flRadiusSquared = dot( vExtents, vExtents );

	// A fragment's generated levels (resident meshes have their own LODs), read once for every view
	uint nLevels = 0;
	float4 vLodErrors = 0;
	float2 vLodMore = 0;
	uint nLodAt = nDrawRecord * LodRecord_Size;
	if ( bActive && !bMeshes && g_nResidentLodTable != None && nDrawRecord < g_nResidentLodCount )
	{
		ByteAddressBuffer lods = g_bindless_VertexBuffers[ g_nResidentLodTable ];
		nLevels = min( lods.Load( nLodAt + LodRecord_Levels ), 6u );
		vLodErrors = asfloat( lods.Load4( nLodAt + LodRecord_Errors ) );
		vLodMore = asfloat( lods.Load2( nLodAt + LodRecord_Errors + 4 * LodRecord_ErrorsStride ) );
	}

	// Each view in turn, the same for the whole group: its 32-byte record (view record, instance records, commands,
	// multi-draws; first instance, fragment words, listed fragments, flags), then its CullViewRecord (six planes, the
	// camera and squared size threshold, whether it's a shadow view (bit 0) and its resident filter (bits 1-2), whether
	// resident meshes skip the size test, the filter's settled and restless stamps, the LOD camera, and the LOD width and
	// whether it's texels per unit). Every lane runs every view to the wave operations at its end, kept or not
	for ( uint v = 0; v < vItemViews.y; v++ )
	{
		uint nViewAt = g_nResidentViews + ( vItemViews.x + v ) * ResidentCullView_Size;
		uint4 vView = uint4( frame.Load( nViewAt + ResidentCullView_View ), frame.Load( nViewAt + ResidentCullView_Instances ), frame.Load( nViewAt + ResidentCullView_Commands ), frame.Load( nViewAt + ResidentCullView_Runs ) );
		uint4 vViewOut = uint4( frame.Load( nViewAt + ResidentCullView_FirstInstance ), frame.Load( nViewAt + ResidentCullView_FragmentWords ), frame.Load( nViewAt + ResidentCullView_Listed ), frame.Load( nViewAt + ResidentCullView_Flags ) );
		uint4 vViewFlags = uint4( frame.Load( vView.x + CullViewRecord_Flags ), frame.Load( vView.x + CullViewRecord_KeepsSmallMeshes ), frame.Load( vView.x + CullViewRecord_SettledStamp ), frame.Load( vView.x + CullViewRecord_RestlessStamp ) );
		bool bKept = bActive && !( ( vViewFlags.x & 1 ) != 0 && bNoShadow );

		// A resident filter: the casters it keeps by when each last moved, whose words the batch record's sixth word finds
		uint nFilter = ( vViewFlags.x >> 1 ) & 3;
		if ( bKept && nFilter != 0 && vVisibility.y != None )
			bKept = TakesMoved( nFilter, records.Load( vVisibility.y + i * 4 ), vViewFlags.z, vViewFlags.w );

		[unroll]
		for ( uint p = 0; p < 6; p++ )
			bKept = bKept && Inside( asfloat( frame.Load4( vView.x + CullViewRecord_Planes + p * CullViewRecord_PlanesStride ) ), vCenter, vExtents );

		// Borealis's size cull: too small for the threshold unless the camera is inside the bounding sphere. A negative
		// threshold is a sun cascade's, by its texel size: the squared radius below which it culls, wherever it is
		float4 vCamera = asfloat( frame.Load4( vView.x + CullViewRecord_CameraAndThreshold ) );
		bool bSize = !bMeshes || ( vViewFlags.y & 1 ) == 0;
		float3 vToCamera = vCenter - vCamera.xyz;
		float flDistanceSquared = dot( vToCamera, vToCamera );
		if ( bSize && vCamera.w > 0 && flDistanceSquared > flRadiusSquared && flRadiusSquared < vCamera.w * flDistanceSquared )
			bKept = false;
		if ( bSize && vCamera.w < 0 && flRadiusSquared < -vCamera.w )
			bKept = false;

		// A resident mesh is kept only in the batch of its LOD in this view
		if ( bKept && bMeshes )
		{
			float4 vLodCamera = asfloat( frame.Load4( vView.x + CullViewRecord_LodCamera ) );
			bKept = MeshLod( vHeader0, vHeader1, vCenter, flLodScale, vLodCamera, asfloat( frame.Load( vView.x + CullViewRecord_LodWidth ) ), frame.Load( vView.x + CullViewRecord_LodTexels ) != 0 ) == nBatchLod;
		}

		// A fragment's generated level in this view (LodMode and LodUnits): its draw record and range from here on
		uint nViewDraw = nDrawRecord;
		uint2 vViewRange = vDraw.xy;
		if ( bKept && nLevels > 0 )
		{
			uint nLevel = PickLod( frame.Load( vView.x + CullViewRecord_LodMode ), asfloat( frame.Load( vView.x + CullViewRecord_LodUnits ) ), asfloat( frame.Load3( vView.x + CullViewRecord_LodCamera ) ), vCenter, vExtents, nLevels, vLodErrors, vLodMore, nFragment );
			if ( nLevel > 0 )
			{
				ByteAddressBuffer lods = g_bindless_VertexBuffers[ g_nResidentLodTable ];
				uint nLevelAt = nLodAt + LodRecord_Level + ( nLevel - 1 ) * LodRecord_LevelStride;
				nViewDraw = lods.Load( nLevelAt + LodLevel_Draw );
				vViewRange = uint2( lods.Load( nLevelAt + LodLevel_FirstIndex ), lods.Load( nLevelAt + LodLevel_IndexCount ) );
			}
		}

		// A prepass's fragment forward didn't see last frame waits for the second phase (two-phase Hi-Z occlusion)
		if ( bKept )
		{
			if ( HiZDeferred( vViewFlags, vVisibility, vCenter, vExtents, nTransform, nViewDraw, uint3( vViewRange, vDraw.z ), nVertexBase, nFragment, vBatch.w, ( vViewOut.w & 4 ) != 0 ) )
				bKept = false;
		}

		// Forward over a fragment its prepass view draws ids for this frame: listed for the compaction pass. The prepass
		// keeps it as this view does; a view whose flags say so knows from the entry that it draws ids for it, and the rest
		// read the stamps the prepass's culling wrote in an earlier dispatch
		bool bListed = bKept && ( vVisibility.z & 2 ) != 0 && nFragment != None && nVertexBase != None;
		if ( bListed && ( vViewOut.w & 1 ) != 0 )
			bListed = bPrepassIds;
		else if ( bListed )
			bListed = g_Stamps.Load( nFragment * 4 ) == g_nResidentFrame;

		// A prepass that culls meshlets (view flag 4): a fragment of an ids bucket whose draw has meshlets is listed for the
		// meshlet pass, which draws those that can reach a sample (borealis_meshlet_cull_cs); its stamp is written here
		// (A depth prepass lists only aggregates' fragments: static meshes' draws have meshlets for shadow views.)
		bool bMeshlets = bKept && !bListed && ( vViewOut.w & 4 ) != 0 && ( vVisibility.z & 1 ) != 0 && ( ( vViewFlags.x & 1 ) != 0 || ( vVisibility.z & 12 ) == 0 ) && g_nResidentMeshletRuns != None
			&& g_bindless_VertexBuffers[ g_nResidentMeshletRuns ].Load( nViewDraw * MeshletRun_Size + MeshletRun_Count ) - 1 < 4096u;

		// In a bucket drawn with task and mesh shaders (the sixth word's top bit), it takes a slot of the bucket's task run
		// instead, a task group of borealis_meshlet_ids each
		bool bTask = bMeshlets && ( vVisibility.y & TaskRun ) != 0;
		bMeshlets = bMeshlets && !bTask;

		uint nAt = WaveAppend( bListed || bMeshlets, vViewOut.z );
		if ( bListed || bMeshlets )
		{
			uint nListed = vViewOut.z + ListedArguments_Size + nAt * ListedFragment_Size;
			g_Frame.Store( nListed + ListedFragment_Transform, nTransform );
			g_Frame.Store( nListed + ListedFragment_Draw, nViewDraw );
			g_Frame.Store( nListed + ListedFragment_VertexBase, bMeshlets ? ( nFragment != None ? nVertexBase : None ) : nVertexBase + vDraw.z );
			g_Frame.Store( nListed + ListedFragment_Run, vBatch.w );
			g_Frame.Store( nListed + ListedFragment_CompactRun, vVisibility.y );

			// Zeroed, and the visibility fragment forward's compaction checks
			g_Frame.Store( nListed + ListedFragment_Reserved0, 0 );
			g_Frame.Store( nListed + ListedFragment_Reserved1, 0 );
			g_Frame.Store( nListed + ListedFragment_Fragment, nFragment );
		}

		if ( ( bMeshlets || bTask ) && nFragment != None && ( vViewOut.w & 2 ) != 0 )
			g_Stamps.Store( nFragment * 4, g_nResidentFrame );

		// The next slot of its multi-draw (or task run) in this view
		bool bDrawn = bKept && !bListed && !bMeshlets && !bTask;
		uint nSlot = WaveTakeSlot( bDrawn || bTask, vView.w, bTask ? vVisibility.y & ~TaskRun : vBatch.w, frame );
		if ( bTask )
		{
			g_Frame.Store( vView.y + nSlot * InstanceRecord_Size + InstanceRecord_Transform, nTransform );
			g_Frame.Store( vView.y + nSlot * InstanceRecord_Size + InstanceRecord_Draw, nViewDraw );
			g_Frame.Store( vViewOut.y + ( vViewOut.x + nSlot ) * 4, nFragment != None ? nVertexBase : None );
			g_Frame.Store( vView.z + nSlot * 20, nFragment );
		}

		if ( bDrawn )
		{
			g_Frame.Store( vView.y + nSlot * InstanceRecord_Size + InstanceRecord_Transform, nTransform );
			g_Frame.Store( vView.y + nSlot * InstanceRecord_Size + InstanceRecord_Draw, nViewDraw );

			uint nCommand = vView.z + nSlot * 20;
			g_Frame.Store( nCommand, vViewRange.y );
			g_Frame.Store( nCommand + 4, 1 );
			g_Frame.Store( nCommand + 8, vViewRange.x );
			g_Frame.Store( nCommand + 12, vDraw.z );
			g_Frame.Store( nCommand + 16, vViewOut.x + nSlot );

			// The prepass's ids: this instance's fragment, and its stamp for forward this frame where forward reads it
			if ( ( vVisibility.z & 1 ) != 0 && nFragment != None )
			{
				g_Frame.Store( vViewOut.y + ( vViewOut.x + nSlot ) * 4, nVertexBase );
				if ( ( vViewOut.w & 2 ) != 0 )
					g_Stamps.Store( nFragment * 4, g_nResidentFrame );
			}
		}
	}
}

#endif // CS

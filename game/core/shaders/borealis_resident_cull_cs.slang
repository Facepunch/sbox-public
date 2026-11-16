// borealis_resident_cull_cs.shader as a pass module: Borealis.Gpu draws this; the .shader stays for the other routes
// and as the fallback (docs/managed/shader-format-replacement.md, "Pass modules").
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "Borealis resident cull: culls every view's resident batches in one dispatch, a thread per entry looping over the views, and writes their instance records and indirect commands.";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// MODES: Default();

#include "sbox/pass.slangh"

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "system.fxc" // This should always be the first include in COMMON

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_CS )
#include "common.fxc"

// The raw buffer array in the bindless set (binding 63): the fragment and draw tables
#ifndef BINDLESS_VERTEX_BUFFERS_DECLARED
#define BINDLESS_VERTEX_BUFFERS_DECLARED
ByteAddressBuffer g_bindless_VertexBuffers[] EXTERNAL_DESC_SET( t, g_globalLateBoundBindlessSet, 17 );
#endif

// The frame's region: the items and views in; each view's instance records, commands, multi-draw counts and listed
// fragments out. Each plan's batch records and entry map are in a buffer of its own, in the bindless array (Borealis.Backend/ResidentRecords.cs, docs/managed/borealis-gpu-driven.md)
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

// Generated LODs (Borealis.Backend/Lods/LodStore.cs, docs/managed/borealis-gpu-driven.md "Generated LODs"). A draw record's LOD
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

// A fragment's level in a view, by the view's LOD mode (view record word 38: 0 none, 1 by distance, 2 by texels, 3 by
// distance with the level kept per visibility fragment, its view's slice in bits 8-15) and units (word 39: pixels per unit at
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
			g_Frame.InterlockedAdd( nRuns + nLeader * 8 + 4, nCount, nBase );
			nBase += frame.Load( nRuns + nLeader * 8 );
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

// An expand dispatch: entry n of a new plan, whose records are g_Frame. What the cull reads of its fragment or resident
// mesh and draw, from the fragment and draw tables in host memory, into its entry record (ResidentCullEntryRecord, 48
// bytes) in video memory: centre and transform slot; extents and draw record; the draw's first index, index count and base
// vertex, and a resident mesh's LOD scale or a fragment's no-shadow flag. A resident mesh batch's first entry writes its
// group's header record at the batch's index
void Expand( uint n )
{
	if ( n >= g_nResidentEntries )
		return;

	uint2 vMap = g_Frame.Load2( g_nResidentExpandMap + n * 8 );
	uint nBatch = vMap.x & 0x7fffffffu;
	uint i = vMap.y;
	uint4 vBatch = g_Frame.Load4( nBatch * 32 );
	uint4 vVisibility = g_Frame.Load4( nBatch * 32 + 16 );
	ByteAddressBuffer fragments = g_bindless_VertexBuffers[ g_nResidentFragments ];
	ByteAddressBuffer draws = g_bindless_VertexBuffers[ g_nResidentDraws ];

	uint4 a;
	uint4 b;
	uint nTransform;
	uint nDrawRecord;
	uint nLast;
	if ( ( vVisibility.z & 8 ) != 0 )
	{
		// Resident mesh record: centre and LOD scale, extents and transform slot
		a = fragments.Load4( ( vBatch.x + 1 + i ) * 32 );
		b = fragments.Load4( ( vBatch.x + 1 + i ) * 32 + 16 );
		nTransform = b.w;
		nDrawRecord = ( vBatch.z & 0x80000000u ) != 0 ? fragments.Load( ( vBatch.z & 0x7fffffffu ) * 32 + i * 4 ) : vBatch.z;
		nLast = a.w;
		if ( i == 0 )
		{
			g_Frame.Store4( g_nResidentExpandHeaders + nBatch * 32, fragments.Load4( vBatch.x * 32 ) );
			g_Frame.Store4( g_nResidentExpandHeaders + nBatch * 32 + 16, fragments.Load4( vBatch.x * 32 + 16 ) );
		}
	}
	else
	{
		// Fragment record: centre and draw record, extents and fragment index (bit 31: casts no shadow)
		a = fragments.Load4( ( vBatch.x + i ) * 32 );
		b = fragments.Load4( ( vBatch.x + i ) * 32 + 16 );
		nTransform = vBatch.z + ( b.w & 0x7fffffffu );
		nDrawRecord = a.w;
		nLast = b.w >> 31;
	}

	// Draw record's second half: first index, index count, base vertex, index page
	uint4 vDraw = draws.Load4( nDrawRecord * 32 + 16 );
	uint nAt = g_nResidentExpand + n * 48;
	g_Frame.Store4( nAt, uint4( a.xyz, nTransform ) );
	g_Frame.Store4( nAt + 16, uint4( b.xyz, nDrawRecord ) );
	g_Frame.Store4( nAt + 32, uint4( vDraw.xyz, nLast ) );
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
		if ( frame.Load( g_nResidentItems + nMid * 32 ) <= nGroupEntry )
			nItem = nMid;
		else
			nHigh = nMid - 1;
	}

	// Item: first entry, entries, the bindless slot of its plan's records and its entry map's offset in them; its first view
	// and views, and its entry records' and batch headers' offsets in its records
	uint4 vItem = frame.Load4( g_nResidentItems + nItem * 32 );
	uint4 vItemViews = frame.Load4( g_nResidentItems + nItem * 32 + 16 );
	uint nLocal = nGroupEntry + nThread - vItem.x;
	bool bActive = g_nResidentItemCount > 0 && nGroupEntry + nThread < g_nResidentEntries && nLocal < vItem.y;

	// The entry: its batch and its place in it
	ByteAddressBuffer records = g_bindless_VertexBuffers[ vItem.z ];
	uint2 vMap = bActive ? records.Load2( vItem.w + nLocal * 8 ) : 0;
	uint i = vMap.y;

	// Forward: whether the view's prepass draws ids for it (ResidentCullEntry.PrepassIds)
	bool bPrepassIds = ( vMap.x & 0x80000000u ) != 0;
	vMap.x &= 0x7fffffffu;

	// Batch record, as borealis_gpu_driven_cull_cs reads it: first fragment record (resident meshes: their group's header),
	// fragments, the first fragment's transform slot (resident meshes: the draw record, bit 31 a list of one per mesh), its
	// multi-draw; then its first visibility fragment, its multi-draw of compacted triangles, its flags (1: ids, 2: lists,
	// 8: resident meshes, bits 4-7 their LOD, the top 24 bits the vertex stride) and its first vertex bit
	uint4 vBatch = bActive ? records.Load4( vMap.x * 32 ) : 0;
	uint4 vVisibility = bActive ? records.Load4( vMap.x * 32 + 16 ) : 0;
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
		uint nAt = vItemViews.z + nLocal * 48;
		uint4 e0 = records.Load4( nAt );
		uint4 e1 = records.Load4( nAt + 16 );
		uint4 e2 = records.Load4( nAt + 32 );
		vCenter = asfloat( e0.xyz );
		nTransform = e0.w;
		vExtents = asfloat( e1.xyz );
		nDrawRecord = e1.w;
		vDraw = uint4( e2.xyz, 0 );
		if ( bMeshes )
		{
			flLodScale = asfloat( e2.w );
			vHeader0 = records.Load4( vItemViews.w + vMap.x * 32 );
			vHeader1 = records.Load4( vItemViews.w + vMap.x * 32 + 16 );
		}
		else
		{
			bNoShadow = e2.w != 0;
		}
	}

	uint nFragment = vVisibility.x != None ? vVisibility.x + i : None;
	uint nVertexBase = vVisibility.w != None ? vVisibility.w + i * nStride : None;
	float flRadiusSquared = dot( vExtents, vExtents );

	// A fragment's generated levels (resident meshes have their own LODs), read once for every view
	uint nLevels = 0;
	float4 vLodErrors = 0;
	float2 vLodMore = 0;
	uint nLodAt = nDrawRecord * 128;
	if ( bActive && !bMeshes && g_nResidentLodTable != None && nDrawRecord < g_nResidentLodCount )
	{
		ByteAddressBuffer lods = g_bindless_VertexBuffers[ g_nResidentLodTable ];
		uint4 h0 = lods.Load4( nLodAt );
		uint4 h1 = lods.Load4( nLodAt + 16 );
		nLevels = min( h0.x, 6u );
		vLodErrors = asfloat( uint4( h0.zw, h1.xy ) );
		vLodMore = asfloat( h1.zw );
	}

	// Each view in turn, the same for the whole group: its 32-byte record (view record, instance records, commands,
	// multi-draws; first instance, fragment words, listed fragments, flags), then the 160-byte view record (six planes, the
	// camera and squared size threshold, whether it's a shadow view (bit 0) and its resident filter (bits 1-2), whether
	// resident meshes skip the size test, the filter's settled and restless stamps, the LOD camera, and the LOD width and
	// whether it's texels per unit). Every lane runs every view to the wave operations at its end, kept or not
	for ( uint v = 0; v < vItemViews.y; v++ )
	{
		uint nViewAt = g_nResidentViews + ( vItemViews.x + v ) * 32;
		uint4 vView = frame.Load4( nViewAt );
		uint4 vViewOut = frame.Load4( nViewAt + 16 );
		uint4 vViewFlags = frame.Load4( vView.x + 112 );
		bool bKept = bActive && !( ( vViewFlags.x & 1 ) != 0 && bNoShadow );

		// A resident filter: the casters it keeps by when each last moved, whose words the batch record's sixth word finds
		uint nFilter = ( vViewFlags.x >> 1 ) & 3;
		if ( bKept && nFilter != 0 && vVisibility.y != None )
			bKept = TakesMoved( nFilter, records.Load( vVisibility.y + i * 4 ), vViewFlags.z, vViewFlags.w );

		[unroll]
		for ( uint p = 0; p < 6; p++ )
			bKept = bKept && Inside( asfloat( frame.Load4( vView.x + p * 16 ) ), vCenter, vExtents );

		// Borealis's size cull: too small for the threshold unless the camera is inside the bounding sphere. A negative
		// threshold is a sun cascade's, by its texel size: the squared radius below which it culls, wherever it is
		float4 vCamera = asfloat( frame.Load4( vView.x + 96 ) );
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
			float4 vLodCamera = asfloat( frame.Load4( vView.x + 128 ) );
			uint4 vLod = frame.Load4( vView.x + 144 );
			bKept = MeshLod( vHeader0, vHeader1, vCenter, flLodScale, vLodCamera, asfloat( vLod.x ), vLod.y != 0 ) == nBatchLod;
		}

		// A fragment's generated level in this view (view words 38 and 39): its draw record and range from here on
		uint nViewDraw = nDrawRecord;
		uint2 vViewRange = vDraw.xy;
		if ( bKept && nLevels > 0 )
		{
			uint4 vLodView = frame.Load4( vView.x + 144 );
			uint nLevel = PickLod( vLodView.z, asfloat( vLodView.w ), asfloat( frame.Load4( vView.x + 128 ) ).xyz, vCenter, vExtents, nLevels, vLodErrors, vLodMore, nFragment );
			if ( nLevel > 0 )
			{
				uint4 vLevel = g_bindless_VertexBuffers[ g_nResidentLodTable ].Load4( nLodAt + 32 + ( nLevel - 1 ) * 16 );
				nViewDraw = vLevel.z;
				vViewRange = vLevel.xy;
			}
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
		bool bMeshlets = bKept && !bListed && ( vViewOut.w & 4 ) != 0 && ( vVisibility.z & 1 ) != 0 && g_nResidentMeshletRuns != None
			&& g_bindless_VertexBuffers[ g_nResidentMeshletRuns ].Load( nViewDraw * 16 + 8 ) - 1 < 4096u;

		// In a bucket drawn with task and mesh shaders (the sixth word's top bit), it takes a slot of the bucket's task run
		// instead, a task group of borealis_meshlet_ids each
		bool bTask = bMeshlets && ( vVisibility.y & TaskRun ) != 0;
		bMeshlets = bMeshlets && !bTask;

		uint nAt = WaveAppend( bListed || bMeshlets, vViewOut.z );
		if ( bListed || bMeshlets )
		{
			uint nListed = vViewOut.z + 16 + nAt * 32;
			g_Frame.Store( nListed, nTransform );
			g_Frame.Store( nListed + 4, nViewDraw );
			g_Frame.Store( nListed + 8, bMeshlets ? ( nFragment != None ? nVertexBase : None ) : nVertexBase + vDraw.z );
			g_Frame.Store( nListed + 12, vBatch.w );
			g_Frame.Store( nListed + 16, vVisibility.y );

			// The meshlet pass's kept triangles and room (zeroed), and the visibility fragment forward's compaction checks
			g_Frame.Store( nListed + 20, 0 );
			g_Frame.Store( nListed + 24, 0 );
			g_Frame.Store( nListed + 28, nFragment );
		}

		if ( ( bMeshlets || bTask ) && nFragment != None && ( vViewOut.w & 2 ) != 0 )
			g_Stamps.Store( nFragment * 4, g_nResidentFrame );

		// The next slot of its multi-draw (or task run) in this view
		bool bDrawn = bKept && !bListed && !bMeshlets && !bTask;
		uint nSlot = WaveTakeSlot( bDrawn || bTask, vView.w, bTask ? vVisibility.y & ~TaskRun : vBatch.w, frame );
		if ( bTask )
		{
			g_Frame.Store2( vView.y + nSlot * 8, uint2( nTransform, nViewDraw ) );
			g_Frame.Store( vViewOut.y + ( vViewOut.x + nSlot ) * 4, nFragment != None ? nVertexBase : None );
			g_Frame.Store( vView.z + nSlot * 20, nFragment );
		}

		if ( bDrawn )
		{
			g_Frame.Store( vView.y + nSlot * 8, nTransform );
			g_Frame.Store( vView.y + nSlot * 8 + 4, nViewDraw );

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

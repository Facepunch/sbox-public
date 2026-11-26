// borealis_gpu_driven_cull_cs: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "Borealis GPU-driven aggregates: culls a layer's fragments by frustum and size and writes their instance records and indirect commands.";

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

// The raw buffer array in the bindless set (binding 63): the fragment and draw tables
#ifndef BINDLESS_VERTEX_BUFFERS_DECLARED
#define BINDLESS_VERTEX_BUFFERS_DECLARED
ByteAddressBuffer g_bindless_VertexBuffers[] EXTERNAL_DESC_SET( t, g_globalLateBoundBindlessSet, 17 );
#endif

// The frame's region: the view and batch records in, instance records and commands out (docs/reference/borealis-gpu-driven.md)
RWByteAddressBuffer g_Frame < Attribute( "BorealisCullFrame" ); >;

// The visibility-driven forward pass: each fragment's frame stamp (written where the prepass draws ids, read in
// forward), the triangles that own a sample (marked after the prepass), and the index buffer of compacted triangles
RWByteAddressBuffer g_Stamps < Attribute( "BorealisCullStamps" ); >;
RWByteAddressBuffer g_Mask < Attribute( "BorealisCullMask" ); >;
RWByteAddressBuffer g_Compacted < Attribute( "BorealisCullCompacted" ); >;

// Generated LODs: the counters of level changes, then the levels views keep, a byte per visibility fragment
RWByteAddressBuffer g_Levels < Attribute( "BorealisCullLevels" ); >;

// What the culls let through, for the draw counters (Borealis.Backend/CullStats.cs)
RWByteAddressBuffer g_Stats < Attribute( "BorealisCullStats" ); >;

cbuffer BorealisGpuDrivenCull_t
{
	uint g_nCullInput;			// byte offset of the view record, then a batch record each
	uint g_nCullBatches;
	uint g_nCullInstances;		// byte offset of the instance records
	uint g_nCullCommands;		// byte offset of the commands
	uint g_nCullFirstInstance;	// the first instance record's index in g_Frame
	uint g_nCullFragments;		// bindless slot of the fragment table
	uint g_nCullDraws;			// bindless slot of the draw table
	uint g_nCullRuns;			// byte offset of the multi-draws: first command slot and visible count each, or 0 to keep every slot
	uint g_nCullFrame;			// this frame's stamp
	uint g_nCullVisibility;		// bindless slot of each visibility fragment's first triangle bit
	uint g_nCullLodTable;		// bindless slot of the generated LOD table (128 bytes per draw record), or None
	uint g_nCullCapacity;		// indices g_Compacted holds
	uint g_nCullFragmentWords;	// g_Frame byte offset of instance 0's fragment word, for the id prepass (wraps)
	uint g_nCullListed;			// byte offset of the fragments forward compacts: dispatch arguments, then 32 bytes each
	uint g_nCullMeshletRuns;		// a prepass that culls meshlets: bindless slot of the meshlet runs (16 bytes per draw record); else None
	uint g_nCullLodCount;		// draw records the LOD table covers
	uint g_nCullCorners;		// compaction's (borealis_gpu_driven_compact_cs)
	uint g_nCullPad0;
	uint g_nCullPad1;
	uint g_nCullPad2;
};

static const uint None = 0xffffffffu;

// What the culls let through, counted for the draw counters (Borealis.Backend/CullStats.cs): cumulative words in g_Stats, a
// slot a render system (view flags bits 8-15) and a kind of pass each (shadow; else depth, view flag 32; else forward): batches
// with a survivor, survivors, then triangles' low and high words
static const uint StatsSlotBytes = 64;

void CountSurvivors( uint nViewFlags, uint nBatches, uint nSurvivors, uint nTriangles )
{
	uint nKind = ( nViewFlags & 1 ) != 0 ? 2 : ( ( nViewFlags >> 5 ) & 1 );
	uint nAt = ( ( nViewFlags >> 8 ) & 0xff ) * StatsSlotBytes + nKind * 16;
	uint nOld;
	g_Stats.InterlockedAdd( nAt, nBatches, nOld );
	g_Stats.InterlockedAdd( nAt + 4, nSurvivors, nOld );
	g_Stats.InterlockedAdd( nAt + 8, nTriangles, nOld );
	if ( nOld + nTriangles < nOld )
		g_Stats.InterlockedAdd( nAt + 12, 1, nOld );
}

groupshared uint g_nGroupSurvivors;
groupshared uint g_nGroupTriangles;

bool Inside( float4 vPlane, float3 vCenter, float3 vExtents )
{
	return dot( vPlane.xyz, vCenter ) + vPlane.w + dot( vExtents, abs( vPlane.xyz ) ) >= 0;
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

// A resident mesh group's header (ResidentMeshHeader) as MeshLod reads it: its LOD word and switch distances 1 to 3, then 4 to 7
void LoadMeshHeader( ByteAddressBuffer records, uint nAt, out uint4 vHeader0, out uint4 vHeader1 )
{
	uint nSwitches = nAt + ResidentMeshHeader_SwitchDistances;
	vHeader0 = uint4( records.Load( nAt + ResidentMeshHeader_Lods ), records.Load( nSwitches ), records.Load( nSwitches + 4 ), records.Load( nSwitches + 8 ) );
	vHeader1 = uint4( records.Load( nSwitches + 12 ), records.Load( nSwitches + 16 ), records.Load( nSwitches + 20 ), records.Load( nSwitches + 24 ) );
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

// The draw record of a draw's generated level in this view (view words 38 and 39), or the draw's own: a fragment's or a
// resident mesh's of a model without LODs, by its bounds and visibility fragment (whose byte keeps its level)
uint LevelDraw( uint nDrawRecord, uint4 vLod, float3 vEye, float3 vCenter, float3 vExtents, uint nFragment )
{
	if ( g_nCullLodTable == None || nDrawRecord >= g_nCullLodCount )
		return nDrawRecord;

	ByteAddressBuffer lods = g_bindless_VertexBuffers[ g_nCullLodTable ];
	uint nLodAt = nDrawRecord * LodRecord_Size;
	uint nLevels = min( lods.Load( nLodAt + LodRecord_Levels ), 6u );
	float4 vErrors = asfloat( lods.Load4( nLodAt + LodRecord_Errors ) );
	float2 vMore = asfloat( lods.Load2( nLodAt + LodRecord_Errors + 4 * LodRecord_ErrorsStride ) );
	uint nLevel = PickLod( vLod.z, asfloat( vLod.w ), vEye, vCenter, vExtents, nLevels, vErrors, vMore, nFragment );
	return nLevel > 0 ? lods.Load( nLodAt + LodRecord_Level + ( nLevel - 1 ) * LodRecord_LevelStride + LodLevel_Draw ) : nDrawRecord;
}

// One index of a draw, from its index page (16 or 32 bits)
uint LoadIndex( ByteAddressBuffer page, uint nIndex, bool b32 )
{
	if ( b32 )
		return page.Load( nIndex * 4 );

	uint nWord = page.Load( ( nIndex * 2 ) & ~3u );
	return ( nIndex & 1 ) != 0 ? nWord >> 16 : nWord & 0xffff;
}

void WriteDraw( uint nSlot, uint nTransform, uint nDraw, uint nIndexCount, uint nInstanceCount, uint nFirstIndex, uint nBaseVertex )
{
	g_Frame.Store( g_nCullInstances + nSlot * InstanceRecord_Size + InstanceRecord_Transform, nTransform );
	g_Frame.Store( g_nCullInstances + nSlot * InstanceRecord_Size + InstanceRecord_Draw, nDraw );

	uint nCommand = g_nCullCommands + nSlot * 20;
	g_Frame.Store( nCommand, nIndexCount );
	g_Frame.Store( nCommand + 4, nInstanceCount );
	g_Frame.Store( nCommand + 8, nFirstIndex );
	g_Frame.Store( nCommand + 12, nBaseVertex );
	g_Frame.Store( nCommand + 16, g_nCullFirstInstance + nSlot );
}

// A batch record's sixth word with this bit: its bucket's meshlets are drawn with task and mesh shaders, from the task run
// the rest names (its first slot and count, then a run of 1 and 1, which make the count a task draw's arguments)
static const uint TaskRun = 0x80000000u;

// Two-phase Hi-Z occlusion (CullViewRecord.Flags bit 3; docs/reference/borealis-gpu-driven.md, "Hi-Z occlusion"): an ids fragment
// forward didn't see last frame (its bit clear in the bits at bindless slot vViewFlags.z) is left for the second phase, as
// a 64-byte candidate at byte offset vViewFlags.w (group count, 1, 1, count, occluded; then from byte 32 the candidates, 256 a group). Whether it was
// (not a mesh draw's instances, whose fragments are the frame's). bMeshlets: the first phase would list it for the meshlet pass
bool HiZDeferred( uint4 vViewFlags, uint4 vVisibility, float3 vCenter, float3 vExtents, uint nTransform, uint nDrawRecord, uint4 vDraw, uint nVertexBase, uint nFragment, uint nRun )
{
	if ( ( vViewFlags.x & 8 ) == 0 || ( vVisibility.z & 7 ) != 1 || nFragment == None || nVertexBase == None )
		return false;
	if ( ( ( g_bindless_VertexBuffers[ vViewFlags.z ].Load( ( nFragment >> 5 ) * 4 ) >> ( nFragment & 31 ) ) & 1 ) != 0 )
		return false;

	bool bMeshlets = false;
	if ( ( vVisibility.z & 12 ) == 0 && g_nCullMeshletRuns != None && ( vVisibility.y & TaskRun ) == 0 )
		bMeshlets = g_bindless_VertexBuffers[ g_nCullMeshletRuns ].Load( nDrawRecord * MeshletRun_Size + MeshletRun_Count ) - 1 < 4096u;
	uint nCompactRun = vVisibility.y;

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
	g_Frame.Store( nEntry + HiZCandidate_CompactRun, nCompactRun );
	g_Frame.Store( nEntry + HiZCandidate_Meshlets, bMeshlets ? 1u : 0u );
	return true;
}

// The next slot of multi-draw nRun
uint TakeSlot( uint nRun )
{
	uint nIndex;
	g_Frame.InterlockedAdd( g_nCullRuns + nRun * MultiDraw_Size + MultiDraw_Count, 1, nIndex );
	return g_Frame.Load( g_nCullRuns + nRun * MultiDraw_Size + MultiDraw_FirstSlot ) + nIndex;
}

[numthreads( 64, 1, 1 )]
[shader( "compute" )] void MainCs( uint3 vGroupId : SV_GroupID, uint nThread : SV_GroupIndex )
{
	uint nBatch = vGroupId.x;
	if ( nBatch >= g_nCullBatches )
		return;

	// View record (CullViewRecord): six planes, the camera and the squared size threshold, then whether it's a shadow view
	float4 vPlanes[6];
	[unroll]
	for ( uint p = 0; p < 6; p++ )
		vPlanes[p] = asfloat( g_Frame.Load4( g_nCullInput + CullViewRecord_Planes + p * CullViewRecord_PlanesStride ) );
	float4 vCamera = asfloat( g_Frame.Load4( g_nCullInput + CullViewRecord_CameraAndThreshold ) );
	uint4 vViewFlags = uint4( g_Frame.Load( g_nCullInput + CullViewRecord_Flags ), g_Frame.Load( g_nCullInput + CullViewRecord_KeepsSmallMeshes ), g_Frame.Load( g_nCullInput + CullViewRecord_SettledStamp ), g_Frame.Load( g_nCullInput + CullViewRecord_RestlessStamp ) );
	bool bShadow = ( vViewFlags.x & 1 ) != 0;

	// A resident filter (bits 1-2) and its settled and restless stamps: a batch's casters are kept by when each last moved,
	// their words found by its record's sixth word (none: every one is kept)
	uint nFilter = ( vViewFlags.x >> 1 ) & 3;

	// Resident meshes: whether their size cull is off (a static shadow cache, kept while the camera moves), and their LOD's
	// camera and tangent of half its field of view, and screen width or (a texel view) texels per unit
	bool bMeshSize = ( vViewFlags.y & 1 ) == 0;
	float4 vLodCamera = asfloat( g_Frame.Load4( g_nCullInput + CullViewRecord_LodCamera ) );
	uint4 vLod = uint4( g_Frame.Load( g_nCullInput + CullViewRecord_LodWidth ), g_Frame.Load( g_nCullInput + CullViewRecord_LodTexels ), g_Frame.Load( g_nCullInput + CullViewRecord_LodMode ), g_Frame.Load( g_nCullInput + CullViewRecord_LodUnits ) );
	float flLodWidth = asfloat( vLod.x );
	bool bLodTexels = vLod.y != 0;

	// Batch record: first fragment record, fragments, the first fragment's transform slot, its first command slot (or,
	// compacting, its multi-draw); then its first visibility fragment, its multi-draw of compacted triangles, its flags
	// (1: the prepass draws its ids, 2: forward draws its triangles that own a sample, 4: a mesh draw's instances, 8:
	// resident meshes, bits 4-7 their LOD) and its first vertex bit. A mesh draw's instances have its draw record first,
	// and the g_Frame byte offset of their transform slots third; the CPU culled them; each has its own vertex bits, the
	// flags' top 24 bits apart. Resident meshes have their group's header record first, then a record each, and their
	// draw record third (bit 31: a list of a draw record per mesh in the fragment table instead, for meshes lit
	// differently); each has its own vertex bits too
	uint nBatchAt = g_nCullInput + CullViewRecord_Size + nBatch * BatchRecord_Size;
	uint4 vBatch = uint4( g_Frame.Load( nBatchAt + BatchRecord_First ), g_Frame.Load( nBatchAt + BatchRecord_Count ), g_Frame.Load( nBatchAt + BatchRecord_Transform ), g_Frame.Load( nBatchAt + BatchRecord_Run ) );
	uint4 vVisibility = uint4( g_Frame.Load( nBatchAt + BatchRecord_FirstVisibility ), g_Frame.Load( nBatchAt + BatchRecord_Moved ), g_Frame.Load( nBatchAt + BatchRecord_Flags ), g_Frame.Load( nBatchAt + BatchRecord_FirstVertexBit ) );
	bool bInstances = ( vVisibility.z & 4 ) != 0;
	bool bMeshes = ( vVisibility.z & 8 ) != 0;
	uint nBatchLod = ( vVisibility.z >> 4 ) & 15;
	uint nStride = vVisibility.z >> 8;

	ByteAddressBuffer fragments = g_bindless_VertexBuffers[ g_nCullFragments ];
	ByteAddressBuffer draws = g_bindless_VertexBuffers[ g_nCullDraws ];

	uint4 vHeader0 = 0;
	uint4 vHeader1 = 0;
	if ( bMeshes )
	{
		LoadMeshHeader( fragments, vBatch.x * ResidentMeshHeader_Size, vHeader0, vHeader1 );
	}

	// Counted for the draw counters (view flag 16), but not a mesh draw's instances, which the CPU culled and counts
	bool bCount = ( vViewFlags.x & 16 ) != 0 && !bInstances;
	uint nSurvivors = 0;
	uint nTriangles = 0;
	if ( nThread == 0 )
	{
		g_nGroupSurvivors = 0;
		g_nGroupTriangles = 0;
	}
	GroupMemoryBarrierWithGroupSync();

	for ( uint nBase = 0; nBase < vBatch.y; nBase += 64 )
	{

		uint i = nBase + nThread;
		if ( i < vBatch.y )
		{
			bool bVisible = true;
			float3 vBoxCenter = 0;
			float3 vBoxExtents = 0;
			uint nTransform;
			uint nDrawRecord;
			if ( bInstances )
			{
				nTransform = g_Frame.Load( vBatch.z + i * 4 );
				nDrawRecord = vBatch.x;
			}
			else if ( bMeshes )
			{
				// Resident mesh record: centre and LOD scale, extents and transform slot. Culled as the CPU culls meshes, then
				// kept only where its LOD is the batch's
				uint nMeshAt = ( vBatch.x + 1 + i ) * ResidentMeshRecord_Size;
				float3 vCenter = asfloat( fragments.Load3( nMeshAt + ResidentMeshRecord_Center ) );
				float3 vExtents = asfloat( fragments.Load3( nMeshAt + ResidentMeshRecord_Extents ) );
				vBoxCenter = vCenter;
				vBoxExtents = vExtents;

				[unroll]
				for ( uint p = 0; p < 6; p++ )
					bVisible = bVisible && Inside( vPlanes[p], vCenter, vExtents );

				float flRadiusSquared = dot( vExtents, vExtents );
				float3 vToCamera = vCenter - vCamera.xyz;
				float flDistanceSquared = dot( vToCamera, vToCamera );
				if ( bMeshSize && vCamera.w > 0 && flDistanceSquared > flRadiusSquared && flRadiusSquared < vCamera.w * flDistanceSquared )
					bVisible = false;
				if ( bMeshSize && vCamera.w < 0 && flRadiusSquared < -vCamera.w )
					bVisible = false;

				bVisible = bVisible && MeshLod( vHeader0, vHeader1, vCenter, asfloat( fragments.Load( nMeshAt + ResidentMeshRecord_LodScale ) ), vLodCamera, flLodWidth, bLodTexels ) == nBatchLod;
				nTransform = fragments.Load( nMeshAt + ResidentMeshRecord_Transform );
				nDrawRecord = ( vBatch.z & 0x80000000u ) != 0 ? fragments.Load( ( vBatch.z & 0x7fffffffu ) * FragmentRecord_Size + i * 4 ) : vBatch.z;

				// A model without LODs of its own: its draw's generated level, as a fragment's
				if ( bVisible && ( vHeader0.x & 0xff ) <= 1 )
					nDrawRecord = LevelDraw( nDrawRecord, vLod, vLodCamera.xyz, vCenter, vExtents, vVisibility.x != None ? vVisibility.x + i : None );
			}
			else
			{
				// Fragment record: centre and draw record, extents and fragment index (bit 31: casts no shadow)
				uint nFragmentAt = ( vBatch.x + i ) * FragmentRecord_Size;
				float3 vCenter = asfloat( fragments.Load3( nFragmentAt + FragmentRecord_Center ) );
				float3 vExtents = asfloat( fragments.Load3( nFragmentAt + FragmentRecord_Extents ) );
				uint nInstance = fragments.Load( nFragmentAt + FragmentRecord_Instance );
				vBoxCenter = vCenter;
				vBoxExtents = vExtents;

				bVisible = !( bShadow && ( nInstance & 0x80000000u ) != 0 );
				[unroll]
				for ( uint p = 0; p < 6; p++ )
					bVisible = bVisible && Inside( vPlanes[p], vCenter, vExtents );

				// Borealis's size cull: too small for the threshold unless the camera is inside the bounding sphere. A negative
				// threshold is a sun cascade's, by its texel size: the squared radius below which it culls, wherever it is
				float flRadiusSquared = dot( vExtents, vExtents );
				float3 vToCamera = vCenter - vCamera.xyz;
				float flDistanceSquared = dot( vToCamera, vToCamera );
				if ( vCamera.w > 0 && flDistanceSquared > flRadiusSquared && flRadiusSquared < vCamera.w * flDistanceSquared )
					bVisible = false;
				if ( vCamera.w < 0 && flRadiusSquared < -vCamera.w )
					bVisible = false;

				nTransform = vBatch.z + ( nInstance & 0x7fffffffu );
				nDrawRecord = fragments.Load( nFragmentAt + FragmentRecord_Draw );

				// Its generated level in this view (LodMode and LodUnits): that level's draw record from here on
				if ( bVisible )
					nDrawRecord = LevelDraw( nDrawRecord, vLod, vLodCamera.xyz, vCenter, vExtents, vVisibility.x != None ? vVisibility.x + i : None );
			}

			if ( nFilter != 0 && vVisibility.y != None && !TakesMoved( nFilter, g_Frame.Load( vVisibility.y + i * 4 ), vViewFlags.z, vViewFlags.w ) )
				bVisible = false;

			// The draw record's first index, index count, base vertex and index page
			uint nDrawAt = nDrawRecord * DrawRecord_Size;
			uint4 vDraw = uint4( draws.Load( nDrawAt + DrawRecord_FirstIndex ), draws.Load( nDrawAt + DrawRecord_IndexCount ), draws.Load( nDrawAt + DrawRecord_BaseVertex ), draws.Load( nDrawAt + DrawRecord_IndexPage ) );
			uint nFragment = vVisibility.x != None ? vVisibility.x + i : None;
			uint nVertexBase = vVisibility.w != None ? vVisibility.w + i * nStride : None;

			// A fragment the view's tests and filter keep, not one left for the Hi-Z's second phase
			bool bDeferred = g_nCullRuns != 0 && bVisible && HiZDeferred( vViewFlags, vVisibility, vBoxCenter, vBoxExtents, nTransform, nDrawRecord, vDraw, nVertexBase, nFragment, vBatch.w );
			if ( bCount && bVisible && !bDeferred )
			{
				nSurvivors++;
				nTriangles += vDraw.y / 3;
			}

			if ( g_nCullRuns == 0 )
			{
				// Without compacting every fragment keeps its slot, with no instance where it's culled
				WriteDraw( vBatch.w + i, nTransform, nDrawRecord, vDraw.y, bVisible ? 1u : 0u, vDraw.x, vDraw.z );
			}
			else if ( bVisible && !bDeferred )
			{
				// Forward over a fragment the prepass drew ids for this frame: listed for the compaction pass
				bool bListed = false;
				if ( ( vVisibility.z & 2 ) != 0 && nFragment != None && nVertexBase != None && g_Stamps.Load( nFragment * 4 ) == g_nCullFrame )
				{
					uint nAt;
					g_Frame.InterlockedAdd( g_nCullListed, 1, nAt );
					uint nEntry = g_nCullListed + ListedArguments_Size + nAt * ListedFragment_Size;
					g_Frame.Store( nEntry + ListedFragment_Transform, nTransform );
					g_Frame.Store( nEntry + ListedFragment_Draw, nDrawRecord );
					g_Frame.Store( nEntry + ListedFragment_VertexBase, nVertexBase + vDraw.z );
					g_Frame.Store( nEntry + ListedFragment_Run, vBatch.w );
					g_Frame.Store( nEntry + ListedFragment_CompactRun, vVisibility.y );
					g_Frame.Store( nEntry + ListedFragment_Fragment, nFragment );
					bListed = true;
				}

				// A prepass that culls meshlets: an ids fragment whose draw has meshlets is listed for the meshlet pass
				// (borealis_meshlet_cull_cs), which draws those that can reach a sample; or, in a bucket drawn with task and
				// mesh shaders (its sixth word's top bit), takes a slot of the bucket's task run, a task group of
				// borealis_meshlet_ids each
				// (A depth prepass lists only aggregates' fragments: static meshes' draws have meshlets for shadow views.)
				bool bMeshlets = !bListed && ( vVisibility.z & 1 ) != 0 && ( bShadow || ( vVisibility.z & 12 ) == 0 ) && g_nCullMeshletRuns != None && g_bindless_VertexBuffers[ g_nCullMeshletRuns ].Load( nDrawRecord * MeshletRun_Size + MeshletRun_Count ) - 1 < 4096u;
				if ( bMeshlets && ( vVisibility.y & TaskRun ) != 0 )
				{
					uint nSlot = TakeSlot( vVisibility.y & ~TaskRun );
					g_Frame.Store( g_nCullInstances + nSlot * InstanceRecord_Size + InstanceRecord_Transform, nTransform );
					g_Frame.Store( g_nCullInstances + nSlot * InstanceRecord_Size + InstanceRecord_Draw, nDrawRecord );
					g_Frame.Store( g_nCullFragmentWords + ( g_nCullFirstInstance + nSlot ) * 4, nFragment != None ? nVertexBase : None );
					g_Frame.Store( g_nCullCommands + nSlot * 20, nFragment );
					if ( nFragment != None )
						g_Stamps.Store( nFragment * 4, g_nCullFrame );
					bListed = true;
				}
				else if ( bMeshlets )
				{
					uint nAt;
					g_Frame.InterlockedAdd( g_nCullListed, 1, nAt );
					uint nEntry = g_nCullListed + ListedArguments_Size + nAt * ListedFragment_Size;
					g_Frame.Store( nEntry + ListedFragment_Transform, nTransform );
					g_Frame.Store( nEntry + ListedFragment_Draw, nDrawRecord );
					g_Frame.Store( nEntry + ListedFragment_VertexBase, nFragment != None ? nVertexBase : None );
					g_Frame.Store( nEntry + ListedFragment_Run, vBatch.w );
					g_Frame.Store( nEntry + ListedFragment_CompactRun, vVisibility.y );
					g_Frame.Store( nEntry + ListedFragment_Reserved0, 0 );
					g_Frame.Store( nEntry + ListedFragment_Reserved1, 0 );
					g_Frame.Store( nEntry + ListedFragment_Fragment, nFragment );
					if ( nFragment != None )
						g_Stamps.Store( nFragment * 4, g_nCullFrame );
					bListed = true;
				}

				if ( !bListed )
				{
					uint nSlot = TakeSlot( vBatch.w );
					WriteDraw( nSlot, nTransform, nDrawRecord, vDraw.y, 1, vDraw.x, vDraw.z );

					// The prepass's ids: this instance's fragment, and its stamp for forward this frame
					if ( ( vVisibility.z & 1 ) != 0 && nFragment != None )
					{
						g_Frame.Store( g_nCullFragmentWords + ( g_nCullFirstInstance + nSlot ) * 4, nVertexBase );
						g_Stamps.Store( nFragment * 4, g_nCullFrame );
					}
				}
			}
		}

	}

	// The group's survivors: one atomic a batch, in the pass kind and slot the view says
	if ( nSurvivors > 0 )
	{
		InterlockedAdd( g_nGroupSurvivors, nSurvivors );
		InterlockedAdd( g_nGroupTriangles, nTriangles );
	}
	GroupMemoryBarrierWithGroupSync();
	if ( nThread == 0 && g_nGroupSurvivors > 0 )
		CountSurvivors( vViewFlags.x, 1, g_nGroupSurvivors, g_nGroupTriangles );
}

#endif // CS

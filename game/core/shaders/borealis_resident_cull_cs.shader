//-------------------------------------------------------------------------------------------------------------------------------------------------------------
HEADER
{
	DevShader = true;
	Description = "Borealis resident cull: culls every view's resident batches in one dispatch, a thread per entry looping over the views, and writes their instance records and indirect commands.";
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
MODES
{
	Default();
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
FEATURES
{
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
COMMON
{
	#include "system.fxc" // This should always be the first include in COMMON
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
CS
{
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

	// The module also keeps generated LODs' levels per view (borealis_resident_cull_cs.slang): declared here for its bindings
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
		uint g_nResidentMeshletRuns;	// bindless slot of the meshlet runs; meshlets are culled where the pass module runs
		uint g_nResidentLodTable;		// generated LODs are picked where the pass module runs
		uint g_nResidentLodCount;
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
	void MainCs( uint3 vGroupId : SV_GroupID, uint nThread : SV_GroupIndex )
	{
		// Never true (a slot or None): keeps the levels' binding, which the module uses
		if ( g_nResidentLodTable == 0xfffffffeu )
			g_Levels.Store( 0, g_Levels.Load( 0 ) );

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

			// Forward over a fragment its prepass view draws ids for this frame: listed for the compaction pass. The prepass
			// keeps it as this view does; a view whose flags say so knows from the entry that it draws ids for it, and the rest
			// read the stamps the prepass's culling wrote in an earlier dispatch
			bool bListed = bKept && ( vVisibility.z & 2 ) != 0 && nFragment != None && nVertexBase != None;
			if ( bListed && ( vViewOut.w & 1 ) != 0 )
				bListed = bPrepassIds;
			else if ( bListed )
				bListed = g_Stamps.Load( nFragment * 4 ) == g_nResidentFrame;

			uint nAt = WaveAppend( bListed, vViewOut.z );
			if ( bListed )
			{
				uint nListed = vViewOut.z + 16 + nAt * 32;
				g_Frame.Store( nListed, nTransform );
				g_Frame.Store( nListed + 4, nDrawRecord );
				g_Frame.Store( nListed + 8, nVertexBase + vDraw.z );
				g_Frame.Store( nListed + 12, vBatch.w );
				g_Frame.Store( nListed + 16, vVisibility.y );
			}

			// The next slot of its multi-draw in this view
			bool bDrawn = bKept && !bListed;
			uint nSlot = WaveTakeSlot( bDrawn, vView.w, vBatch.w, frame );
			if ( bDrawn )
			{
				g_Frame.Store( vView.y + nSlot * 8, nTransform );
				g_Frame.Store( vView.y + nSlot * 8 + 4, nDrawRecord );

				uint nCommand = vView.z + nSlot * 20;
				g_Frame.Store( nCommand, vDraw.y );
				g_Frame.Store( nCommand + 4, 1 );
				g_Frame.Store( nCommand + 8, vDraw.x );
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
}

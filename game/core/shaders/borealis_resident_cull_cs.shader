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

	cbuffer BorealisResidentCull_t
	{
		uint g_nResidentItems;		// byte offset of the items, 32 bytes each (ResidentCullItem)
		uint g_nResidentItemCount;
		uint g_nResidentViews;		// byte offset of the views, 32 bytes each (ResidentCullView)
		uint g_nResidentEntries;	// threads: every item's entries
		uint g_nResidentFragments;	// bindless slot of the fragment table
		uint g_nResidentDraws;		// bindless slot of the draw table
		uint g_nResidentFrame;		// this frame's stamp
		uint g_nResidentPad0;
	};

	static const uint None = 0xffffffffu;

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

	// A chunk of an item's views, read once per group: each view's 32-byte record, then its 160-byte view record
	static const uint ViewChunk = 16;
	static const uint ViewWords = 12;
	groupshared uint4 g_vViews[192]; // ViewChunk * ViewWords

	[numthreads( 64, 1, 1 )]
	void MainCs( uint3 vGroupId : SV_GroupID, uint nThread : SV_GroupIndex )
	{
		// Its item: the last whose first entry is at or before the group's. An item is one plan's batches and the views that
		// cull them; items start on a multiple of 64 threads, so a group reads one item
		uint nGroupEntry = vGroupId.x * 64;
		uint nItem = 0;
		uint nHigh = g_nResidentItemCount > 0 ? g_nResidentItemCount - 1 : 0;
		while ( nItem < nHigh )
		{
			uint nMid = ( nItem + nHigh + 1 ) / 2;
			if ( g_Frame.Load( g_nResidentItems + nMid * 32 ) <= nGroupEntry )
				nItem = nMid;
			else
				nHigh = nMid - 1;
		}

		// Item: first entry, entries, the bindless slot of its plan's records and its entry map's offset in them; its first view and views
		uint4 vItem = g_Frame.Load4( g_nResidentItems + nItem * 32 );
		uint2 vItemViews = g_Frame.Load2( g_nResidentItems + nItem * 32 + 16 );
		uint nLocal = nGroupEntry + nThread - vItem.x;
		bool bActive = g_nResidentItemCount > 0 && nGroupEntry + nThread < g_nResidentEntries && nLocal < vItem.y;

		// The entry: its batch and its place in it
		ByteAddressBuffer records = g_bindless_VertexBuffers[ vItem.z ];
		uint2 vMap = bActive ? records.Load2( vItem.w + nLocal * 8 ) : 0;
		uint i = vMap.y;

		// Batch record, as borealis_gpu_driven_cull_cs reads it: first fragment record (resident meshes: their group's header),
		// fragments, the first fragment's transform slot (resident meshes: the draw record, bit 31 a list of one per mesh), its
		// multi-draw; then its first visibility fragment, its multi-draw of compacted triangles, its flags (1: ids, 2: lists,
		// 8: resident meshes, bits 4-7 their LOD, the top 24 bits the vertex stride) and its first vertex bit
		uint4 vBatch = bActive ? records.Load4( vMap.x * 32 ) : 0;
		uint4 vVisibility = bActive ? records.Load4( vMap.x * 32 + 16 ) : 0;
		bool bMeshes = ( vVisibility.z & 8 ) != 0;
		uint nBatchLod = ( vVisibility.z >> 4 ) & 15;
		uint nStride = vVisibility.z >> 8;

		ByteAddressBuffer fragments = g_bindless_VertexBuffers[ g_nResidentFragments ];
		ByteAddressBuffer draws = g_bindless_VertexBuffers[ g_nResidentDraws ];

		// The entry's bounds, transform slot and draw record, read once for every view
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
			if ( bMeshes )
			{
				// Resident mesh record: centre and LOD scale, extents and transform slot
				vHeader0 = fragments.Load4( vBatch.x * 32 );
				vHeader1 = fragments.Load4( vBatch.x * 32 + 16 );
				uint4 a = fragments.Load4( ( vBatch.x + 1 + i ) * 32 );
				uint4 b = fragments.Load4( ( vBatch.x + 1 + i ) * 32 + 16 );
				vCenter = asfloat( a.xyz );
				vExtents = asfloat( b.xyz );
				flLodScale = asfloat( a.w );
				nTransform = b.w;
				nDrawRecord = ( vBatch.z & 0x80000000u ) != 0 ? fragments.Load( ( vBatch.z & 0x7fffffffu ) * 32 + i * 4 ) : vBatch.z;
			}
			else
			{
				// Fragment record: centre and draw record, extents and fragment index (bit 31: casts no shadow)
				uint4 a = fragments.Load4( ( vBatch.x + i ) * 32 );
				uint4 b = fragments.Load4( ( vBatch.x + i ) * 32 + 16 );
				vCenter = asfloat( a.xyz );
				vExtents = asfloat( b.xyz );
				bNoShadow = ( b.w & 0x80000000u ) != 0;
				nTransform = vBatch.z + ( b.w & 0x7fffffffu );
				nDrawRecord = a.w;
			}

			// Draw record's second half: first index, index count, base vertex, index page
			vDraw = draws.Load4( nDrawRecord * 32 + 16 );
		}

		uint nFragment = vVisibility.x != None ? vVisibility.x + i : None;
		uint nVertexBase = vVisibility.w != None ? vVisibility.w + i * nStride : None;
		float flRadiusSquared = dot( vExtents, vExtents );

		for ( uint nChunk = 0; nChunk < vItemViews.y; nChunk += ViewChunk )
		{
			// The chunk's views into groupshared memory: its 32-byte record (view record, instance records, commands, multi-draws;
			// first instance, fragment words, listed fragments), then the 160-byte view record (six planes, the camera and
			// squared size threshold, whether it's a shadow view and whether resident meshes skip the size test, the LOD camera,
			// and the LOD width and whether it's texels per unit)
			uint nViews = min( ViewChunk, vItemViews.y - nChunk );
			GroupMemoryBarrierWithGroupSync();
			for ( uint w = nThread; w < nViews * ViewWords; w += 64 )
			{
				uint nView = w / ViewWords;
				uint nWord = w % ViewWords;
				uint nViewAt = g_nResidentViews + ( vItemViews.x + nChunk + nView ) * 32;
				g_vViews[w] = nWord < 2 ? g_Frame.Load4( nViewAt + nWord * 16 ) : g_Frame.Load4( g_Frame.Load( nViewAt ) + ( nWord - 2 ) * 16 );
			}
			GroupMemoryBarrierWithGroupSync();

			if ( !bActive )
				continue;

			for ( uint v = 0; v < nViews; v++ )
			{
				uint nBase = v * ViewWords;
				uint4 vView = g_vViews[nBase];
				uint4 vViewOut = g_vViews[nBase + 1];
				uint4 vViewFlags = g_vViews[nBase + 2 + 7];
				bool bVisible = !( vViewFlags.x != 0 && bNoShadow );

				[unroll]
				for ( uint p = 0; p < 6; p++ )
					bVisible = bVisible && Inside( asfloat( g_vViews[nBase + 2 + p] ), vCenter, vExtents );
				if ( !bVisible )
					continue;

				// Borealis's size cull: too small for the threshold unless the camera is inside the bounding sphere. A negative
				// threshold is a sun cascade's, by its texel size: the squared radius below which it culls, wherever it is
				float4 vCamera = asfloat( g_vViews[nBase + 2 + 6] );
				bool bSize = !bMeshes || ( vViewFlags.y & 1 ) == 0;
				float3 vToCamera = vCenter - vCamera.xyz;
				float flDistanceSquared = dot( vToCamera, vToCamera );
				if ( bSize && vCamera.w > 0 && flDistanceSquared > flRadiusSquared && flRadiusSquared < vCamera.w * flDistanceSquared )
					continue;
				if ( bSize && vCamera.w < 0 && flRadiusSquared < -vCamera.w )
					continue;

				// A resident mesh is kept only in the batch of its LOD in this view
				if ( bMeshes )
				{
					float4 vLodCamera = asfloat( g_vViews[nBase + 2 + 8] );
					uint4 vLod = g_vViews[nBase + 2 + 9];
					if ( MeshLod( vHeader0, vHeader1, vCenter, flLodScale, vLodCamera, asfloat( vLod.x ), vLod.y != 0 ) != nBatchLod )
						continue;
				}

				// Forward over a fragment its prepass view drew ids for this frame: listed for the compaction pass
				if ( ( vVisibility.z & 2 ) != 0 && nFragment != None && nVertexBase != None && g_Stamps.Load( nFragment * 4 ) == g_nResidentFrame )
				{
					uint nAt;
					g_Frame.InterlockedAdd( vViewOut.z, 1, nAt );
					uint nListed = vViewOut.z + 16 + nAt * 32;
					g_Frame.Store( nListed, nTransform );
					g_Frame.Store( nListed + 4, nDrawRecord );
					g_Frame.Store( nListed + 8, nVertexBase + vDraw.z );
					g_Frame.Store( nListed + 12, vBatch.w );
					g_Frame.Store( nListed + 16, vVisibility.y );
					continue;
				}

				// The next slot of its multi-draw in this view
				uint nIndex;
				g_Frame.InterlockedAdd( vView.w + vBatch.w * 8 + 4, 1, nIndex );
				uint nSlot = g_Frame.Load( vView.w + vBatch.w * 8 ) + nIndex;

				g_Frame.Store( vView.y + nSlot * 8, nTransform );
				g_Frame.Store( vView.y + nSlot * 8 + 4, nDrawRecord );

				uint nCommand = vView.z + nSlot * 20;
				g_Frame.Store( nCommand, vDraw.y );
				g_Frame.Store( nCommand + 4, 1 );
				g_Frame.Store( nCommand + 8, vDraw.x );
				g_Frame.Store( nCommand + 12, vDraw.z );
				g_Frame.Store( nCommand + 16, vViewOut.x + nSlot );

				// The prepass's ids: this instance's fragment, and its stamp for forward this frame
				if ( ( vVisibility.z & 1 ) != 0 && nFragment != None )
				{
					g_Frame.Store( vViewOut.y + ( vViewOut.x + nSlot ) * 4, nVertexBase );
					g_Stamps.Store( nFragment * 4, g_nResidentFrame );
				}
			}
		}
	}
}

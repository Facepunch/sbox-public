//-------------------------------------------------------------------------------------------------------------------------------------------------------------
HEADER
{
	DevShader = true;
	Description = "Basalt GPU-driven aggregates: culls a layer's fragments by frustum and size and writes their instance records and indirect commands.";
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

	// The frame's region: the view and batch records in, instance records and commands out (docs/managed/basalt-gpu-driven.md)
	RWByteAddressBuffer g_Frame < Attribute( "BasaltCullFrame" ); >;

	// The visibility-driven forward pass: each fragment's frame stamp (written where the prepass draws ids, read in
	// forward), the triangles that own a sample (marked after the prepass), and the index buffer of compacted triangles
	RWByteAddressBuffer g_Stamps < Attribute( "BasaltCullStamps" ); >;
	RWByteAddressBuffer g_Mask < Attribute( "BasaltCullMask" ); >;
	RWByteAddressBuffer g_Compacted < Attribute( "BasaltCullCompacted" ); >;

	cbuffer BasaltGpuDrivenCull_t
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
		uint g_nCullCursor;			// byte offset in g_Frame of the compacted indices' cursor
		uint g_nCullCapacity;		// indices g_Compacted holds
		uint g_nCullFragmentWords;	// g_Frame byte offset of instance 0's fragment word, for the id prepass (wraps)
		uint g_nCullPad0;
		uint g_nCullPad1;
		uint g_nCullPad2;
	};

	static const uint None = 0xffffffffu;

	bool Inside( float4 vPlane, float3 vCenter, float3 vExtents )
	{
		return dot( vPlane.xyz, vCenter ) + vPlane.w + dot( vExtents, abs( vPlane.xyz ) ) >= 0;
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
		g_Frame.Store( g_nCullInstances + nSlot * 8, nTransform );
		g_Frame.Store( g_nCullInstances + nSlot * 8 + 4, nDraw );

		uint nCommand = g_nCullCommands + nSlot * 20;
		g_Frame.Store( nCommand, nIndexCount );
		g_Frame.Store( nCommand + 4, nInstanceCount );
		g_Frame.Store( nCommand + 8, nFirstIndex );
		g_Frame.Store( nCommand + 12, nBaseVertex );
		g_Frame.Store( nCommand + 16, g_nCullFirstInstance + nSlot );
	}

	// The next slot of multi-draw nRun
	uint TakeSlot( uint nRun )
	{
		uint nIndex;
		g_Frame.InterlockedAdd( g_nCullRuns + nRun * 8 + 4, 1, nIndex );
		return g_Frame.Load( g_nCullRuns + nRun * 8 ) + nIndex;
	}

	// Fragments of this batch's 64 whose triangles the whole group compacts, and the group's sums
	groupshared uint gs_Listed[64];
	groupshared uint gs_ListCount;
	groupshared uint gs_Sums[64];
	groupshared uint gs_Start;

	uint GroupSum( uint nThread, uint nValue )
	{
		gs_Sums[nThread] = nValue;
		GroupMemoryBarrierWithGroupSync();
		uint nSum = 0;
		for ( uint k = 0; k < 64; k++ )
			nSum += gs_Sums[k];
		GroupMemoryBarrierWithGroupSync();
		return nSum;
	}

	[numthreads( 64, 1, 1 )]
	void MainCs( uint3 vGroupId : SV_GroupID, uint nThread : SV_GroupIndex )
	{
		uint nBatch = vGroupId.x;
		if ( nBatch >= g_nCullBatches )
			return;

		// View record: six planes, the camera and the squared size threshold, then whether it's a shadow view
		float4 vPlanes[6];
		[unroll]
		for ( uint p = 0; p < 6; p++ )
			vPlanes[p] = asfloat( g_Frame.Load4( g_nCullInput + p * 16 ) );
		float4 vCamera = asfloat( g_Frame.Load4( g_nCullInput + 96 ) );
		bool bShadow = g_Frame.Load( g_nCullInput + 112 ) != 0;

		// Batch record: first fragment record, fragments, the first fragment's transform slot, its first command slot (or,
		// compacting, its multi-draw); then its first visibility fragment, its multi-draw of compacted triangles and its
		// flags (1: the prepass draws its ids, 2: forward draws its triangles that own a sample)
		uint4 vBatch = g_Frame.Load4( g_nCullInput + 128 + nBatch * 32 );
		uint4 vVisibility = g_Frame.Load4( g_nCullInput + 128 + nBatch * 32 + 16 );

		ByteAddressBuffer fragments = g_bindless_VertexBuffers[ g_nCullFragments ];
		ByteAddressBuffer draws = g_bindless_VertexBuffers[ g_nCullDraws ];

		for ( uint nBase = 0; nBase < vBatch.y; nBase += 64 )
		{
			if ( nThread == 0 )
				gs_ListCount = 0;
			GroupMemoryBarrierWithGroupSync();

			uint i = nBase + nThread;
			if ( i < vBatch.y )
			{
				// Fragment record: centre and draw record, extents and fragment index (bit 31: casts no shadow)
				uint4 a = fragments.Load4( ( vBatch.x + i ) * 32 );
				uint4 b = fragments.Load4( ( vBatch.x + i ) * 32 + 16 );
				float3 vCenter = asfloat( a.xyz );
				float3 vExtents = asfloat( b.xyz );

				bool bVisible = !( bShadow && ( b.w & 0x80000000u ) != 0 );
				[unroll]
				for ( uint p = 0; p < 6; p++ )
					bVisible = bVisible && Inside( vPlanes[p], vCenter, vExtents );

				// Basalt's size cull: too small for the threshold unless the camera is inside the bounding sphere
				float flRadiusSquared = dot( vExtents, vExtents );
				float3 vToCamera = vCenter - vCamera.xyz;
				float flDistanceSquared = dot( vToCamera, vToCamera );
				if ( vCamera.w > 0 && flDistanceSquared > flRadiusSquared && flRadiusSquared < vCamera.w * flDistanceSquared )
					bVisible = false;

				uint nTransform = vBatch.z + ( b.w & 0x7fffffffu );

				// Draw record's second half: first index, index count, base vertex, index page
				uint4 vDraw = draws.Load4( a.w * 32 + 16 );
				uint nFragment = vVisibility.x != None ? vVisibility.x + i : None;

				if ( g_nCullRuns == 0 )
				{
					// Without compacting every fragment keeps its slot, with no instance where it's culled
					WriteDraw( vBatch.w + i, nTransform, a.w, vDraw.y, bVisible ? 1u : 0u, vDraw.x, vDraw.z );
				}
				else if ( bVisible )
				{
					// Forward over a fragment the prepass drew ids for this frame: the group compacts its triangles below
					bool bListed = false;
					if ( ( vVisibility.z & 2 ) != 0 && nFragment != None && g_Stamps.Load( nFragment * 4 ) == g_nCullFrame && g_bindless_VertexBuffers[ g_nCullVisibility ].Load( nFragment * 4 ) != None )
					{
						uint nAt;
						InterlockedAdd( gs_ListCount, 1, nAt );
						gs_Listed[nAt] = i;
						bListed = true;
					}

					if ( !bListed )
					{
						uint nSlot = TakeSlot( vBatch.w );
						WriteDraw( nSlot, nTransform, a.w, vDraw.y, 1, vDraw.x, vDraw.z );

						// The prepass's ids: this instance's fragment, and its stamp for forward this frame
						if ( ( vVisibility.z & 1 ) != 0 && nFragment != None )
						{
							g_Frame.Store( g_nCullFragmentWords + ( g_nCullFirstInstance + nSlot ) * 4, nFragment );
							g_Stamps.Store( nFragment * 4, g_nCullFrame );
						}
					}
				}
			}

			GroupMemoryBarrierWithGroupSync();
			uint nListed = gs_ListCount;

			// Each listed fragment by the whole group: its triangles that own a sample, copied where they're few
			for ( uint k = 0; k < nListed; k++ )
			{
				uint f = gs_Listed[k];
				uint4 a = fragments.Load4( ( vBatch.x + f ) * 32 );
				uint4 b = fragments.Load4( ( vBatch.x + f ) * 32 + 16 );
				uint nTransform = vBatch.z + ( b.w & 0x7fffffffu );
				uint4 vDrawHead = draws.Load4( a.w * 32 );
				uint4 vDraw = draws.Load4( a.w * 32 + 16 );
				uint nFragment = vVisibility.x + f;
				uint nWord0 = g_bindless_VertexBuffers[ g_nCullVisibility ].Load( nFragment * 4 ) / 32;
				uint nTriangles = vDraw.y / 3;
				uint nWords = ( nTriangles + 31 ) / 32;

				uint nMine = 0;
				for ( uint w = nThread; w < nWords; w += 64 )
					nMine += countbits( g_Mask.Load( ( nWord0 + w ) * 4 ) );
				uint nOwned = GroupSum( nThread, nMine );

				if ( nOwned == 0 )
					continue;

				// Copied where few own a sample and there's room; else the whole draw
				if ( nThread == 0 )
				{
					uint nStart = None;
					if ( nOwned * 2 < nTriangles )
					{
						g_Frame.InterlockedAdd( g_nCullCursor, nOwned * 3, nStart );
						if ( nStart + nOwned * 3 > g_nCullCapacity )
							nStart = None;
					}

					gs_Start = nStart;
					if ( nStart == None )
						WriteDraw( TakeSlot( vBatch.w ), nTransform, a.w, vDraw.y, 1, vDraw.x, vDraw.z );
					else
						WriteDraw( TakeSlot( vVisibility.y ), nTransform, a.w, nOwned * 3, 1, nStart, vDraw.z );
				}

				GroupMemoryBarrierWithGroupSync();
				uint nCursor = gs_Start;
				GroupMemoryBarrierWithGroupSync();
				if ( nCursor == None )
					continue;

				// 64 words (2,048 triangles) at a time: a word a thread, an exclusive prefix over the group, then its triangles
				ByteAddressBuffer page = g_bindless_VertexBuffers[ NonUniformResourceIndex( vDraw.w ) ];
				bool b32 = ( vDrawHead.w & 2 ) != 0;
				for ( uint nWordBase = 0; nWordBase < nWords; nWordBase += 64 )
				{
					uint w = nWordBase + nThread;
					uint nBits = w < nWords ? g_Mask.Load( ( nWord0 + w ) * 4 ) : 0;
					gs_Sums[nThread] = countbits( nBits );
					GroupMemoryBarrierWithGroupSync();
					uint nBefore = 0, nSum = 0;
					for ( uint q = 0; q < 64; q++ )
					{
						uint c = gs_Sums[q];
						nBefore += q < nThread ? c : 0;
						nSum += c;
					}

					uint nAt = nCursor + nBefore * 3;
					while ( nBits != 0 )
					{
						uint t = w * 32 + firstbitlow( nBits );
						nBits &= nBits - 1;
						uint nSource = vDraw.x + t * 3;
						g_Compacted.Store( ( nAt + 0 ) * 4, LoadIndex( page, nSource + 0, b32 ) );
						g_Compacted.Store( ( nAt + 1 ) * 4, LoadIndex( page, nSource + 1, b32 ) );
						g_Compacted.Store( ( nAt + 2 ) * 4, LoadIndex( page, nSource + 2, b32 ) );
						nAt += 3;
					}

					nCursor += nSum * 3;
					GroupMemoryBarrierWithGroupSync();
				}
			}
		}
	}
}
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
HEADER
{
	DevShader = true;
	Description = "Borealis GPU-driven aggregates: culls a layer's fragments by frustum and size and writes their instance records and indirect commands.";
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

	// The frame's region: the view and batch records in, instance records and commands out (docs/managed/borealis-gpu-driven.md)
	RWByteAddressBuffer g_Frame < Attribute( "BorealisCullFrame" ); >;

	// The visibility-driven forward pass: each fragment's frame stamp (written where the prepass draws ids, read in
	// forward), the triangles that own a sample (marked after the prepass), and the index buffer of compacted triangles
	RWByteAddressBuffer g_Stamps < Attribute( "BorealisCullStamps" ); >;
	RWByteAddressBuffer g_Mask < Attribute( "BorealisCullMask" ); >;
	RWByteAddressBuffer g_Compacted < Attribute( "BorealisCullCompacted" ); >;

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
		uint g_nCullPad0;			// the compacted indices' cursor is g_Compacted's first word (borealis_gpu_driven_compact_cs)
		uint g_nCullCapacity;		// indices g_Compacted holds
		uint g_nCullFragmentWords;	// g_Frame byte offset of instance 0's fragment word, for the id prepass (wraps)
		uint g_nCullListed;			// byte offset of the fragments forward compacts: dispatch arguments, then 32 bytes each
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
		// compacting, its multi-draw); then its first visibility fragment, its multi-draw of compacted triangles, its flags
		// (1: the prepass draws its ids, 2: forward draws its triangles that own a sample, 4: a mesh draw's instances) and
		// its first vertex bit. A mesh draw's instances have its draw record first, and the g_Frame byte offset of their
		// transform slots third; the CPU culled them; each has its own vertex bits, the flags' top 24 bits apart
		uint4 vBatch = g_Frame.Load4( g_nCullInput + 128 + nBatch * 32 );
		uint4 vVisibility = g_Frame.Load4( g_nCullInput + 128 + nBatch * 32 + 16 );
		bool bInstances = ( vVisibility.z & 4 ) != 0;
		uint nStride = vVisibility.z >> 8;

		ByteAddressBuffer fragments = g_bindless_VertexBuffers[ g_nCullFragments ];
		ByteAddressBuffer draws = g_bindless_VertexBuffers[ g_nCullDraws ];

		for ( uint nBase = 0; nBase < vBatch.y; nBase += 64 )
		{

			uint i = nBase + nThread;
			if ( i < vBatch.y )
			{
				bool bVisible = true;
				uint nTransform;
				uint nDrawRecord;
				if ( bInstances )
				{
					nTransform = g_Frame.Load( vBatch.z + i * 4 );
					nDrawRecord = vBatch.x;
				}
				else
				{
					// Fragment record: centre and draw record, extents and fragment index (bit 31: casts no shadow)
					uint4 a = fragments.Load4( ( vBatch.x + i ) * 32 );
					uint4 b = fragments.Load4( ( vBatch.x + i ) * 32 + 16 );
					float3 vCenter = asfloat( a.xyz );
					float3 vExtents = asfloat( b.xyz );

					bVisible = !( bShadow && ( b.w & 0x80000000u ) != 0 );
					[unroll]
					for ( uint p = 0; p < 6; p++ )
						bVisible = bVisible && Inside( vPlanes[p], vCenter, vExtents );

					// Borealis's size cull: too small for the threshold unless the camera is inside the bounding sphere
					float flRadiusSquared = dot( vExtents, vExtents );
					float3 vToCamera = vCenter - vCamera.xyz;
					float flDistanceSquared = dot( vToCamera, vToCamera );
					if ( vCamera.w > 0 && flDistanceSquared > flRadiusSquared && flRadiusSquared < vCamera.w * flDistanceSquared )
						bVisible = false;

					nTransform = vBatch.z + ( b.w & 0x7fffffffu );
					nDrawRecord = a.w;
				}

				// Draw record's second half: first index, index count, base vertex, index page
				uint4 vDraw = draws.Load4( nDrawRecord * 32 + 16 );
				uint nFragment = vVisibility.x != None ? vVisibility.x + i : None;
				uint nVertexBase = vVisibility.w != None ? vVisibility.w + i * nStride : None;

				if ( g_nCullRuns == 0 )
				{
					// Without compacting every fragment keeps its slot, with no instance where it's culled
					WriteDraw( vBatch.w + i, nTransform, nDrawRecord, vDraw.y, bVisible ? 1u : 0u, vDraw.x, vDraw.z );
				}
				else if ( bVisible )
				{
					// Forward over a fragment the prepass drew ids for this frame: listed for the compaction pass
					bool bListed = false;
					if ( ( vVisibility.z & 2 ) != 0 && nFragment != None && nVertexBase != None && g_Stamps.Load( nFragment * 4 ) == g_nCullFrame )
					{
						uint nAt;
						g_Frame.InterlockedAdd( g_nCullListed, 1, nAt );
						uint nEntry = g_nCullListed + 16 + nAt * 32;
						g_Frame.Store( nEntry, nTransform );
						g_Frame.Store( nEntry + 4, nDrawRecord );
						g_Frame.Store( nEntry + 8, nVertexBase + vDraw.z );
						g_Frame.Store( nEntry + 12, vBatch.w );
						g_Frame.Store( nEntry + 16, vVisibility.y );
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
	}
}
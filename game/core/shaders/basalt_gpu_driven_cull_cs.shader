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
	};

	bool Inside( float4 vPlane, float3 vCenter, float3 vExtents )
	{
		return dot( vPlane.xyz, vCenter ) + vPlane.w + dot( vExtents, abs( vPlane.xyz ) ) >= 0;
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
		// compacting, its multi-draw)
		uint4 vBatch = g_Frame.Load4( g_nCullInput + 128 + nBatch * 16 );

		ByteAddressBuffer fragments = g_bindless_VertexBuffers[ g_nCullFragments ];
		ByteAddressBuffer draws = g_bindless_VertexBuffers[ g_nCullDraws ];

		for ( uint i = nThread; i < vBatch.y; i += 64 )
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

			// Compacting, a culled fragment has no command; a kept one takes the multi-draw's next slot
			uint nSlot = vBatch.w + i;
			if ( g_nCullRuns != 0 )
			{
				if ( !bVisible )
					continue;

				uint nIndex;
				g_Frame.InterlockedAdd( g_nCullRuns + vBatch.w * 8 + 4, 1, nIndex );
				nSlot = g_Frame.Load( g_nCullRuns + vBatch.w * 8 ) + nIndex;
			}

			// Draw record's second half: first index, index count, base vertex, index page
			uint4 vDraw = draws.Load4( a.w * 32 + 16 );

			g_Frame.Store( g_nCullInstances + nSlot * 8, vBatch.z + ( b.w & 0x7fffffffu ) );
			g_Frame.Store( g_nCullInstances + nSlot * 8 + 4, a.w );

			uint nCommand = g_nCullCommands + nSlot * 20;
			g_Frame.Store( nCommand, vDraw.y );
			g_Frame.Store( nCommand + 4, bVisible ? 1u : 0u );
			g_Frame.Store( nCommand + 8, vDraw.x );
			g_Frame.Store( nCommand + 12, vDraw.z );
			g_Frame.Store( nCommand + 16, g_nCullFirstInstance + nSlot );
		}
	}
}

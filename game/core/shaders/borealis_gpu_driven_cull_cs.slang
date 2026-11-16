// borealis_gpu_driven_cull_cs.shader as a pass module: Borealis.Gpu draws this; the .shader stays for the other routes
// and as the fallback (docs/managed/shader-format-replacement.md, "Pass modules").
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "Borealis GPU-driven aggregates: culls a layer's fragments by frustum and size and writes their instance records and indirect commands.";

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
	uint g_nCullMeshletRuns;		// a prepass that culls meshlets: bindless slot of the meshlet runs (16 bytes per draw record); else None
	uint g_nCullPad2;
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

// A batch record's sixth word with this bit: its bucket's meshlets are drawn with task and mesh shaders, from the task run
// the rest names (its first slot and count, then a run of 1 and 1, which make the count a task draw's arguments)
static const uint TaskRun = 0x80000000u;

// The next slot of multi-draw nRun
uint TakeSlot( uint nRun )
{
	uint nIndex;
	g_Frame.InterlockedAdd( g_nCullRuns + nRun * 8 + 4, 1, nIndex );
	return g_Frame.Load( g_nCullRuns + nRun * 8 ) + nIndex;
}

[numthreads( 64, 1, 1 )]
[shader( "compute" )] void MainCs( uint3 vGroupId : SV_GroupID, uint nThread : SV_GroupIndex )
{
	uint nBatch = vGroupId.x;
	if ( nBatch >= g_nCullBatches )
		return;

	// View record (160 bytes): six planes, the camera and the squared size threshold, then whether it's a shadow view
	float4 vPlanes[6];
	[unroll]
	for ( uint p = 0; p < 6; p++ )
		vPlanes[p] = asfloat( g_Frame.Load4( g_nCullInput + p * 16 ) );
	float4 vCamera = asfloat( g_Frame.Load4( g_nCullInput + 96 ) );
	uint4 vViewFlags = g_Frame.Load4( g_nCullInput + 112 );
	bool bShadow = ( vViewFlags.x & 1 ) != 0;

	// A resident filter (bits 1-2) and its settled and restless stamps: a batch's casters are kept by when each last moved,
	// their words found by its record's sixth word (none: every one is kept)
	uint nFilter = ( vViewFlags.x >> 1 ) & 3;

	// Resident meshes: whether their size cull is off (a static shadow cache, kept while the camera moves), and their LOD's
	// camera and tangent of half its field of view, and screen width or (a texel view) texels per unit
	bool bMeshSize = ( vViewFlags.y & 1 ) == 0;
	float4 vLodCamera = asfloat( g_Frame.Load4( g_nCullInput + 128 ) );
	uint4 vLod = g_Frame.Load4( g_nCullInput + 144 );
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
	uint4 vBatch = g_Frame.Load4( g_nCullInput + 160 + nBatch * 32 );
	uint4 vVisibility = g_Frame.Load4( g_nCullInput + 160 + nBatch * 32 + 16 );
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
		vHeader0 = fragments.Load4( vBatch.x * 32 );
		vHeader1 = fragments.Load4( vBatch.x * 32 + 16 );
	}

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
			else if ( bMeshes )
			{
				// Resident mesh record: centre and LOD scale, extents and transform slot. Culled as the CPU culls meshes, then
				// kept only where its LOD is the batch's
				uint4 a = fragments.Load4( ( vBatch.x + 1 + i ) * 32 );
				uint4 b = fragments.Load4( ( vBatch.x + 1 + i ) * 32 + 16 );
				float3 vCenter = asfloat( a.xyz );
				float3 vExtents = asfloat( b.xyz );

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

				bVisible = bVisible && MeshLod( vHeader0, vHeader1, vCenter, asfloat( a.w ), vLodCamera, flLodWidth, bLodTexels ) == nBatchLod;
				nTransform = b.w;
				nDrawRecord = ( vBatch.z & 0x80000000u ) != 0 ? fragments.Load( ( vBatch.z & 0x7fffffffu ) * 32 + i * 4 ) : vBatch.z;
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

				// Borealis's size cull: too small for the threshold unless the camera is inside the bounding sphere. A negative
				// threshold is a sun cascade's, by its texel size: the squared radius below which it culls, wherever it is
				float flRadiusSquared = dot( vExtents, vExtents );
				float3 vToCamera = vCenter - vCamera.xyz;
				float flDistanceSquared = dot( vToCamera, vToCamera );
				if ( vCamera.w > 0 && flDistanceSquared > flRadiusSquared && flRadiusSquared < vCamera.w * flDistanceSquared )
					bVisible = false;
				if ( vCamera.w < 0 && flRadiusSquared < -vCamera.w )
					bVisible = false;

				nTransform = vBatch.z + ( b.w & 0x7fffffffu );
				nDrawRecord = a.w;
			}

			if ( nFilter != 0 && vVisibility.y != None && !TakesMoved( nFilter, g_Frame.Load( vVisibility.y + i * 4 ), vViewFlags.z, vViewFlags.w ) )
				bVisible = false;

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
					g_Frame.Store( nEntry + 28, nFragment );
					bListed = true;
				}

				// A prepass that culls meshlets: an ids fragment whose draw has meshlets is listed for the meshlet pass
				// (borealis_meshlet_cull_cs), which draws those that can reach a sample; or, in a bucket drawn with task and
				// mesh shaders (its sixth word's top bit), takes a slot of the bucket's task run, a task group of
				// borealis_meshlet_ids each
				bool bMeshlets = !bListed && ( vVisibility.z & 1 ) != 0 && g_nCullMeshletRuns != None && g_bindless_VertexBuffers[ g_nCullMeshletRuns ].Load( nDrawRecord * 16 + 8 ) - 1 < 4096u;
				if ( bMeshlets && ( vVisibility.y & TaskRun ) != 0 )
				{
					uint nSlot = TakeSlot( vVisibility.y & ~TaskRun );
					g_Frame.Store2( g_nCullInstances + nSlot * 8, uint2( nTransform, nDrawRecord ) );
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
					uint nEntry = g_nCullListed + 16 + nAt * 32;
					g_Frame.Store( nEntry, nTransform );
					g_Frame.Store( nEntry + 4, nDrawRecord );
					g_Frame.Store( nEntry + 8, nFragment != None ? nVertexBase : None );
					g_Frame.Store( nEntry + 12, vBatch.w );
					g_Frame.Store( nEntry + 16, vVisibility.y );
					g_Frame.Store( nEntry + 20, 0 );
					g_Frame.Store( nEntry + 24, 0 );
					g_Frame.Store( nEntry + 28, nFragment );
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
}

#endif // CS

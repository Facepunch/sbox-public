// borealis_meshlet_ids.shader as a pass module: Borealis.Gpu draws this, on devices with mesh shaders. The .shader only
// names it (vfx has no task or mesh stages).
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "Borealis meshlet ids prepass: a task group per 16 listed fragments culls their meshlets as borealis_meshlet_cull_cs does and launches a mesh group per kept meshlet, which draws its triangles' depth and ids as borealis_gpu_driven_ids does.";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// MODES: Default();

#include "sbox/pass.slangh"

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "system.fxc" // This should always be the first include in COMMON
#include "vr_common.fxc"
#include "math_general.fxc"

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_INPUT
{
	// The vertex's bit in the view's mask; flat, so a pixel gets its triangle's first (provoking) vertex's
	nointerpolation uint nVertexBit : TEXCOORD0;

	#if ( PROGRAM != VFX_PROGRAM_PS )
		noperspective float4 vPositionPs : SV_Position;
	#endif
};

// Meshlets a listed fragment has at most (the culls list no fragment with more)
static const uint MaxMeshlets = 4096;

// Slots of the task run a task group takes (BackendContext.TaskSlots; the culls count a task group per TaskSlots slots)
static const uint TaskSlots = 16;

// What a task group hands its mesh groups: where its kept meshlets are listed in g_MeshletStamps (entries; None: there was
// no room, and a mesh group per meshlet of its fragments draws it unculled), and per slot its instance record, meshlet
// page, first meshlet, where its kept triangles' numbers start (words; None for none) and the meshlets of the slots
// before it
struct MeshletPayload
{
	uint nListAt;
	uint nSlots;
	uint4 vSlot[TaskSlots];
	uint nBefore[TaskSlots];
};

// Per visibility fragment a 16-byte stamp for forward's compaction (frame, where its kept triangles' numbers start in
// words, how many, 2); from nKeptAt the kept triangles' cursor and the numbers, each kept triangle's among its draw's; from
// nListAt the kept meshlets' cursor and the meshlets, 8 bytes each: its slot among its task group's and its index in its
// run (slot | index << 8), and where its triangles' numbers start among its fragment's kept ones
RWByteAddressBuffer g_MeshletStamps < Attribute( "BorealisMeshletStamps" ); >;

// Per draw: the view the meshlets are culled against, where the bucket's task run is in the region (its first slot and
// count), the region's first instance record and its commands (a task slot's first word is its visibility fragment), the
// meshlet run table's bindless slot, the frame's stamp, where the kept triangles are in g_MeshletStamps and how many fit,
// the faces the pipeline culls (0 none, 1 front, 2 back), and where the kept meshlets are and how many fit
struct BorealisMeshletTask_t
{
	float4 vClipX;
	float4 vClipY;
	float4 vClipZ;
	float4 vClipW;
	float2 vViewport;
	uint nSamples;
	uint nRunAt;
	uint nFirstInstance;
	uint nRunTable;
	uint nCommands;
	uint nFrame;
	uint nKeptAt;
	uint nKeptCapacity;
	uint nCullMode;
	uint nListAt;
	uint nListCapacity;
};

[[vk::push_constant]] ConstantBuffer<BorealisMeshletTask_t> g_MeshletTask;

static const uint None = 0xffffffffu;

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_TS )
#include "common.fxc"
#include "transform_buffer.fxc"
#include "borealis_gpu_driven.fxc"
#include "borealis_meshlet.fxc"

groupshared MeshletPayload gs_Payload;

// Each slot's instance record, transform, draw record and visibility fragment, and its meshlet run (page, first, count)
groupshared uint4 gs_Slot[TaskSlots];
groupshared uint4 gs_Run[TaskSlots];

// A round's kept meshlets (count | triangles << 8), and where the group's kept meshlets go
groupshared uint gs_Round;
groupshared uint gs_ListAt;

// A group per TaskSlots slots of the bucket's task run, which the culling filled with the fragments it listed: takes room
// for every meshlet of its fragments, tests each fragment's meshlets 64 a round and lists the kept ones there, takes room
// for each fragment's kept triangles' numbers and stamps it for forward's compaction, then launches a mesh group per kept
// meshlet
[numthreads( 64, 1, 1 )]
[shader( "amplification" )] void MainTs( uint3 vGroupId : SV_GroupID, uint nThread : SV_GroupIndex )
{
	ByteAddressBuffer frame = g_bindless_VertexBuffers[ g_nBorealisInstances ];
	uint2 vTaskRun = frame.Load2( g_MeshletTask.nRunAt );
	uint nFirst = vGroupId.x * TaskSlots;
	uint nSlots = min( TaskSlots, vTaskRun.y - min( nFirst, vTaskRun.y ) );
	if ( nThread < nSlots )
	{
		uint nSlot = vTaskRun.x + nFirst + nThread;
		uint nInstance = g_MeshletTask.nFirstInstance + nSlot;
		uint2 vInstance = frame.Load2( nInstance * 8 );
		uint nFragment = frame.Load( g_MeshletTask.nCommands + nSlot * 20 );
		uint4 vRun = g_bindless_VertexBuffers[ g_MeshletTask.nRunTable ].Load4( vInstance.y * 16 );
		gs_Slot[nThread] = uint4( nInstance, vInstance.x, vInstance.y, nFragment );
		gs_Run[nThread] = uint4( vRun.x, vRun.y, min( vRun.z, MaxMeshlets ), 0 );
	}

	GroupMemoryBarrierWithGroupSync();
	if ( nThread == 0 )
	{
		uint nAll = 0;
		for ( uint s = 0; s < nSlots; s++ )
		{
			gs_Payload.nBefore[s] = nAll;
			nAll += gs_Run[s].z;
		}

		uint nAt;
		g_MeshletStamps.InterlockedAdd( g_MeshletTask.nListAt, nAll, nAt );
		gs_ListAt = nAt + nAll <= g_MeshletTask.nListCapacity ? nAt : None;
	}

	GroupMemoryBarrierWithGroupSync();
	uint nListAt = gs_ListAt;
	uint nEntries = g_MeshletTask.nListAt + 4 + nListAt * 8;

	MeshletView view;
	view.vClipX = g_MeshletTask.vClipX;
	view.vClipY = g_MeshletTask.vClipY;
	view.vClipZ = g_MeshletTask.vClipZ;
	view.vClipW = g_MeshletTask.vClipW;
	view.vViewport = g_MeshletTask.vViewport;
	view.nSamples = g_MeshletTask.nSamples;

	uint nKept = 0;
	for ( uint s = 0; s < nSlots; s++ )
	{
		uint4 vSlot = gs_Slot[s];
		uint4 vRun = gs_Run[s];
		ByteAddressBuffer meshlets = g_bindless_VertexBuffers[ vRun.x ];
		float3x4 mTransform = g_TransformData[ vSlot.y ].transform;
		uint nFragmentFirst = nKept;
		uint nFragmentTriangles = 0;
		for ( uint nBase = 0; nBase < vRun.z; nBase += 64 )
		{
			if ( nThread == 0 )
				gs_Round = 0;
			GroupMemoryBarrierWithGroupSync();

			// Without room every meshlet draws, unlisted
			uint m = nBase + nThread;
			if ( m < vRun.z )
			{
				uint nAt = ( vRun.y + m ) * 32;
				uint4 r0 = meshlets.Load4( nAt );
				uint4 r1 = meshlets.Load4( nAt + 16 );
				if ( nListAt == None || !MeshletCulled( view, asfloat( r0.xyz ), asfloat( r1.xyz ), mTransform ) )
				{
					uint nWas;
					InterlockedAdd( gs_Round, 1u | ( r1.w << 8 ), nWas );
					if ( nListAt != None )
						g_MeshletStamps.Store2( nEntries + ( nKept + ( nWas & 0xff ) ) * 8, uint2( s | m << 8, nFragmentTriangles + ( nWas >> 8 ) ) );
				}
			}

			GroupMemoryBarrierWithGroupSync();
			uint nRound = gs_Round;
			nKept += nRound & 0xff;
			nFragmentTriangles += nRound >> 8;
			GroupMemoryBarrierWithGroupSync();
		}

		if ( nThread == 0 )
		{
			// Forward's compaction scans only the kept triangles, from their numbers where there's room; none kept, nothing
			uint nFragment = vSlot.w;
			uint nFragmentKept = nKept - nFragmentFirst;
			uint nTrianglesAt = None;
			if ( nFragment != None && nFragmentKept > 0 && nListAt != None )
			{
				g_MeshletStamps.InterlockedAdd( g_MeshletTask.nKeptAt, nFragmentTriangles, nTrianglesAt );
				nTrianglesAt = nTrianglesAt + nFragmentTriangles <= g_MeshletTask.nKeptCapacity ? g_MeshletTask.nKeptAt / 4 + 1 + nTrianglesAt : None;
			}

			if ( nFragment != None )
			{
				uint nTriangles = g_bindless_VertexBuffers[ g_nBorealisDraws ].Load( vSlot.z * 32 + 20 ) / 3;
				g_MeshletStamps.Store4( nFragment * 16, nTrianglesAt != None ? uint4( g_MeshletTask.nFrame, nTrianglesAt, nFragmentTriangles, 2 )
					: uint4( g_MeshletTask.nFrame, None, nFragmentKept == 0 ? 0 : nTriangles, 0 ) );
			}

			gs_Payload.vSlot[s] = uint4( vSlot.x, vRun.x, vRun.y, nTrianglesAt );
		}
	}

	if ( nThread == 0 )
	{
		gs_Payload.nListAt = nListAt;
		gs_Payload.nSlots = nSlots;
	}

	GroupMemoryBarrierWithGroupSync();
	DispatchMesh( nKept, 1, 1, gs_Payload );
}

#endif // TS

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_MS )
#include "instancing.fxc"
#include "borealis_gpu_driven.fxc"

// The meshlet builder's limits this stage takes (64x124 and smaller; BackendDevice.MeshletTasks)
static const uint MaxVertices = 64;
static const uint MaxTriangles = 128;

// Where a meshlet page's local records start: after its records (MeshletStore.PageRecords of 32 bytes). Each is the data
// page's bindless slot, where the meshlet's triangles (a word each: its corners among the meshlet's vertices, a byte each)
// and its vertices (index values in the draw's index size) start there in bytes, and its vertex count
static const uint LocalRecordsAt = 131072 * 32;

// A group per kept meshlet: a lane per vertex of its list, positioned and with its bit as borealis_gpu_driven_ids's vertex
// shader does, and a lane per triangle, its corners in their order (so the first is the provoking vertex, as in the index
// buffer). The hardware culls the triangles that reach no sample: culling them here costs more than it saves
[numthreads( 128, 1, 1 )]
[outputtopology( "triangle" )]
[shader( "mesh" )] void MainMs( uint3 vGroupId : SV_GroupID, uint nThread : SV_GroupIndex, in payload MeshletPayload payload, out vertices PS_INPUT vertices[MaxVertices], out indices uint3 triangles[MaxTriangles] )
{
	// Its slot and meshlet: from the task group's list, or without one its place among every meshlet of the group's slots.
	// The group's records are the same for every lane: read once, as scalars (WaveReadLaneFirst tells the compiler so)
	uint nListAt = payload.nListAt;
	uint2 vEntry;
	if ( nListAt != None )
		vEntry = g_MeshletStamps.Load2( g_MeshletTask.nListAt + 4 + ( nListAt + vGroupId.x ) * 8 );
	else
	{
		uint s = 0;
		while ( s + 1 < payload.nSlots && payload.nBefore[s + 1] <= vGroupId.x )
			s++;
		vEntry = uint2( s | ( vGroupId.x - payload.nBefore[s] ) << 8, 0 );
	}

	vEntry = WaveReadLaneFirst( vEntry );
	uint4 vSlot = WaveReadLaneFirst( payload.vSlot[vEntry.x & 0xff] );
	uint nInstance = vSlot.x;
	uint nMeshlet = vSlot.z + ( vEntry.x >> 8 );
	uint2 vInstance = WaveReadLaneFirst( BorealisGpuDrivenInstance( nInstance ) );
	uint4 vDrawHead = WaveReadLaneFirst( BorealisGpuDrivenDraw( vInstance.y ) );
	uint4 vDraw = WaveReadLaneFirst( g_bindless_VertexBuffers[ g_nBorealisDraws ].Load4( vInstance.y * 32 + 16 ) );
	bool b32 = ( vDrawHead.w & 2 ) != 0;
	ByteAddressBuffer meshlets = g_bindless_VertexBuffers[ vSlot.y ];
	uint2 vMeshlet = WaveReadLaneFirst( uint2( meshlets.Load( nMeshlet * 32 + 12 ), meshlets.Load( nMeshlet * 32 + 28 ) ) );
	uint4 vLocal = WaveReadLaneFirst( meshlets.Load4( LocalRecordsAt + nMeshlet * 16 ) );
	uint nFirstTriangle = vMeshlet.x;
	uint nTriangles = min( vMeshlet.y, MaxTriangles );
	uint nVertices = min( vLocal.w, MaxVertices );
	SetMeshOutputCounts( nVertices, nTriangles );
	ByteAddressBuffer data = g_bindless_VertexBuffers[ vLocal.x ];

	// The draw's vertex as its vertex shader sees it: the index plus the base vertex
	if ( nThread < nVertices )
	{
		uint nIndex;
		if ( b32 )
			nIndex = data.Load( vLocal.z + nThread * 4 );
		else
		{
			uint nAt = vLocal.z + nThread * 2;
			nIndex = ( data.Load( nAt & ~3u ) >> ( ( nAt & 2 ) * 8 ) ) & 0xffff;
		}

		uint nVertex = nIndex + vDraw.z;
		float3 vPositionOs = asfloat( BorealisGpuDrivenFetch( vDrawHead.x, 0, nVertex, true ).xyz );
		float3x4 matObjectToWorld = GetTransformMatrix( vInstance.x, 0 );
		float3 vPositionWs = mul( matObjectToWorld, float4( vPositionOs.xyz, 1.0 ) );
		PS_INPUT o;
		o.vPositionPs.xyzw = Position3WsToPs( vPositionWs.xyz );
		// As the vertex stage's compile flips it (SlangCompiler.CompilePass)
		o.vPositionPs.y = -o.vPositionPs.y;
		o.nVertexBit = BorealisGpuDrivenVertexBit( nInstance, nVertex );
		vertices[nThread] = o;
	}

	if ( nThread < nTriangles )
	{
		uint nPacked = data.Load( vLocal.y + nThread * 4 );
		triangles[nThread] = uint3( nPacked & 0xff, ( nPacked >> 8 ) & 0xff, ( nPacked >> 16 ) & 0xff );

		// Each triangle's number among its draw's, for forward's compaction, where the task group made room
		if ( vSlot.w != None )
			g_MeshletStamps.Store( ( vSlot.w + vEntry.y + nThread ) * 4, nFirstTriangle + nThread );
	}
}

#endif // MS

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_PS )
RenderState( DepthEnable, true );
RenderState( DepthWriteEnable, true );
RenderState( DepthFunc, GREATER_EQUAL );
RenderState( CullMode, DEFAULT );

// As borealis_gpu_driven_ids: the first vertex's bit + 1 at target 1; target 0 goes nowhere
struct PS_OUTPUT
{
	float4 vUnused : SV_Target0;
	uint nId : SV_Target1;
};

[shader( "pixel" )] PS_OUTPUT MainPs( PS_INPUT i )
{
	PS_OUTPUT o;
	o.vUnused = 1.0;
	o.nId = i.nVertexBit + 1;
	return o;
}

#endif // PS

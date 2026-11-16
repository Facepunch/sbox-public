// borealis_meshlet_ids.shader as a pass module: Borealis.Gpu draws this, on devices with mesh shaders. The .shader only
// names it (vfx has no task or mesh stages).
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "Borealis meshlet ids prepass: a task group per listed fragment culls its meshlets as borealis_meshlet_cull_cs does and launches a mesh group per kept meshlet, which draws its triangles' depth and ids as borealis_gpu_driven_ids does.";

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

// A fragment with more kept meshlets than this has no kept triangles written: forward's compaction scans it whole
static const uint MaxListed = 1024;

// What a task group hands its mesh groups: the fragment's instance record, its meshlets, the kept ones' indices among
// them, two to a word, and where the first MaxListed write their triangles' numbers in g_MeshletStamps (words; None for
// none)
struct MeshletPayload
{
	uint nInstance;
	uint nMeshletPage;
	uint nFirstMeshlet;
	uint nListed;
	uint nKept[MaxMeshlets / 2];
	uint nTrianglesAt[MaxListed];
};

// Per visibility fragment a 16-byte stamp for forward's compaction (frame, where its kept triangles' numbers start in
// words, how many, 2), then from nKeptAt the kept triangles' cursor and the numbers: each kept triangle's among its draw's
RWByteAddressBuffer g_MeshletStamps < Attribute( "BorealisMeshletStamps" ); >;

// Per draw: the view the meshlets are culled against, where the bucket's task run is in the region (its first slot), the
// region's first instance record and its commands (a task slot's first word is its visibility fragment), the meshlet
// run table's bindless slot, the frame's stamp, where the kept triangles are in g_MeshletStamps and how many fit, and the
// faces the pipeline culls (0 none, 1 front, 2 back)
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
groupshared uint gs_Kept[MaxMeshlets / 2];
groupshared uint gs_Listed[MaxListed];
groupshared uint gs_Scan[64];
groupshared uint gs_Count;
groupshared uint gs_ListAt;

// A group per slot of the bucket's task run, which the culling filled with the fragments it listed: tests each of the
// fragment's meshlets and launches a mesh group for each that can reach a sample
[numthreads( 64, 1, 1 )]
[shader( "amplification" )] void MainTs( uint3 vGroupId : SV_GroupID, uint nThread : SV_GroupIndex )
{
	ByteAddressBuffer frame = g_bindless_VertexBuffers[ g_nBorealisInstances ];
	uint nSlot = frame.Load( g_MeshletTask.nRunAt ) + vGroupId.x;
	uint nInstance = g_MeshletTask.nFirstInstance + nSlot;
	uint2 vInstance = frame.Load2( nInstance * 8 );
	uint nFragment = frame.Load( g_MeshletTask.nCommands + nSlot * 20 );
	uint4 vRun = g_bindless_VertexBuffers[ g_MeshletTask.nRunTable ].Load4( vInstance.y * 16 );
	uint nMeshlets = min( vRun.z, MaxMeshlets );
	ByteAddressBuffer meshlets = g_bindless_VertexBuffers[ vRun.x ];
	float3x4 mTransform = g_TransformData[ vInstance.x ].transform;

	MeshletView view;
	view.vClipX = g_MeshletTask.vClipX;
	view.vClipY = g_MeshletTask.vClipY;
	view.vClipZ = g_MeshletTask.vClipZ;
	view.vClipW = g_MeshletTask.vClipW;
	view.vViewport = g_MeshletTask.vViewport;
	view.nSamples = g_MeshletTask.nSamples;

	for ( uint w = nThread; w * 2 < nMeshlets; w += 64 )
		gs_Kept[w] = 0;
	if ( nThread == 0 )
		gs_Count = 0;
	GroupMemoryBarrierWithGroupSync();

	// Each meshlet tested once; a kept one's index joins the list
	for ( uint m = nThread; m < nMeshlets; m += 64 )
	{
		uint nAt = ( vRun.y + m ) * 32;
		uint4 r0 = meshlets.Load4( nAt );
		uint4 r1 = meshlets.Load4( nAt + 16 );
		if ( !MeshletCulled( view, asfloat( r0.xyz ), asfloat( r1.xyz ), mTransform ) )
		{
			uint nKeptAt;
			InterlockedAdd( gs_Count, 1, nKeptAt );
			InterlockedOr( gs_Kept[nKeptAt >> 1], m << ( ( nKeptAt & 1 ) * 16 ) );
			if ( nKeptAt < MaxListed )
				gs_Listed[nKeptAt] = r1.w;
		}
	}

	GroupMemoryBarrierWithGroupSync();
	uint nKept = gs_Count;
	for ( uint k = nThread; k * 2 < nKept; k += 64 )
		gs_Payload.nKept[k] = gs_Kept[k];

	// Where each kept meshlet's triangles go among the fragment's kept ones: sixteen a lane, then a scan of the lanes
	bool bListed = nFragment != None && nKept > 0 && nKept <= MaxListed;
	uint nMine = 0;
	for ( uint j = 0; j < 16; j++ )
		nMine += bListed && nThread * 16 + j < nKept ? gs_Listed[nThread * 16 + j] : 0;
	gs_Scan[nThread] = nMine;
	GroupMemoryBarrierWithGroupSync();
	[unroll]
	for ( uint d = 1; d < 64; d <<= 1 )
	{
		uint nAdd = nThread >= d ? gs_Scan[nThread - d] : 0;
		GroupMemoryBarrierWithGroupSync();
		gs_Scan[nThread] += nAdd;
		GroupMemoryBarrierWithGroupSync();
	}

	uint nKeptTriangles = gs_Scan[63];
	if ( nThread == 0 )
	{
		gs_Payload.nInstance = nInstance;
		gs_Payload.nMeshletPage = vRun.x;
		gs_Payload.nFirstMeshlet = vRun.y;

		// Forward's compaction scans only the kept triangles, from their numbers where there's room; none kept, nothing
		uint nListAt = None;
		if ( bListed )
		{
			g_MeshletStamps.InterlockedAdd( g_MeshletTask.nKeptAt, nKeptTriangles, nListAt );
			if ( nListAt + nKeptTriangles > g_MeshletTask.nKeptCapacity )
				nListAt = None;
		}

		if ( nFragment != None )
		{
			uint nTriangles = g_bindless_VertexBuffers[ g_nBorealisDraws ].Load( vInstance.y * 32 + 20 ) / 3;
			uint nWord = g_MeshletTask.nKeptAt / 4 + 1 + nListAt;
			g_MeshletStamps.Store4( nFragment * 16, nListAt != None ? uint4( g_MeshletTask.nFrame, nWord, nKeptTriangles, 2 )
				: uint4( g_MeshletTask.nFrame, None, nKept == 0 ? 0 : nTriangles, 0 ) );
		}

		gs_ListAt = nListAt;
		gs_Payload.nListed = nListAt;
	}

	GroupMemoryBarrierWithGroupSync();
	uint nListAt = gs_ListAt;
	uint nAt = g_MeshletTask.nKeptAt / 4 + 1 + nListAt + gs_Scan[nThread] - nMine;
	for ( uint i = 0; nListAt != None && i < 16; i++ )
	{
		uint k = nThread * 16 + i;
		if ( k >= nKept )
			break;
		gs_Payload.nTrianglesAt[k] = nAt;
		nAt += gs_Listed[k];
	}

	GroupMemoryBarrierWithGroupSync();
	DispatchMesh( nKept, 1, 1, gs_Payload );
}

#endif // TS

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_MS )
#include "instancing.fxc"
#include "borealis_gpu_driven.fxc"
#include "borealis_meshlet.fxc"

// The meshlet builder's limits this stage takes (64x124 and smaller; BackendDevice.MeshletTasks)
static const uint MaxVertices = 64;
static const uint MaxTriangles = 128;

// The meshlet's vertices by index, through a table twice their count: each index's slot, its vertex there, and the index
// of each vertex
static const uint TableSize = 128;
groupshared uint gs_Index[TableSize];
groupshared uint gs_Vertex[TableSize];
groupshared uint gs_IndexOf[MaxVertices];
groupshared uint gs_Vertices;

// Each vertex's position, for the triangles' culling, and each wave's kept triangles
groupshared float4 gs_Position[MaxVertices];
groupshared uint gs_WaveKept[4];

// Whether a triangle can't reach a sample, as the hardware would find (a vertex shader's triangles are culled so before
// they reach the rasterizer; a mesh shader's aren't): wholly outside a clip plane, facing a way the pipeline culls, or in
// front of the camera with no sample of the grid inside its screen bounds. Positions as they're output (y negated)
bool TriangleCulled( float4 a, float4 b, float4 c )
{
	if ( ( a.x < -a.w && b.x < -b.w && c.x < -c.w ) || ( a.x > a.w && b.x > b.w && c.x > c.w )
		|| ( a.y < -a.w && b.y < -b.w && c.y < -c.w ) || ( a.y > a.w && b.y > b.w && c.y > c.w )
		|| ( a.z < 0 && b.z < 0 && c.z < 0 ) || ( a.z > a.w && b.z > b.w && c.z > c.w ) )
		return true;

	// Through the camera plane: the hardware clips it
	if ( a.w <= 0 || b.w <= 0 || c.w <= 0 )
		return false;

	// Framebuffer coordinates (y down); counter-clockwise there is front facing, a negative cross product. A sliver whose
	// facing rounding could flip goes to the hardware
	float2 vSize = g_MeshletTask.vViewport;
	float2 pa = ( a.xy / a.w * 0.5 + 0.5 ) * vSize;
	float2 pb = ( b.xy / b.w * 0.5 + 0.5 ) * vSize;
	float2 pc = ( c.xy / c.w * 0.5 + 0.5 ) * vSize;
	float flCross = ( pb.x - pa.x ) * ( pc.y - pa.y ) - ( pc.x - pa.x ) * ( pb.y - pa.y );
	if ( g_MeshletTask.nCullMode == 2 && flCross > 1.0 / 256 )
		return true;
	if ( g_MeshletTask.nCullMode == 1 && flCross < -1.0 / 256 )
		return true;

	// No sample inside its bounds, widened by the hardware's subpixel step
	uint nSamples = g_MeshletTask.nSamples;
	float2 vMin = min( pa, min( pb, pc ) ) - 1.0 / 128;
	float2 vMax = max( pa, max( pb, pc ) ) + 1.0 / 128;
	return ( nSamples == 1 || nSamples == 4 ) && !MeshletHoldsSample( nSamples, vMin, vMax );
}

uint LoadIndex( ByteAddressBuffer page, uint nIndex, bool b32 )
{
	if ( b32 )
		return page.Load( nIndex * 4 );

	uint nWord = page.Load( ( nIndex * 2 ) & ~3u );
	return ( nIndex & 1 ) != 0 ? nWord >> 16 : nWord & 0xffff;
}

// The slot of an index in the table, adding it (and numbering its vertex) when it's the first there
uint Place( uint nIndex )
{
	uint nSlot = ( nIndex * 0x9e3779b1u ) >> 25;
	for ( uint nProbe = 0; nProbe < TableSize; nProbe++ )
	{
		uint nWas;
		InterlockedCompareExchange( gs_Index[nSlot], None, nIndex, nWas );
		if ( nWas == None )
		{
			uint nVertex;
			InterlockedAdd( gs_Vertices, 1, nVertex );
			gs_Vertex[nSlot] = nVertex;
			if ( nVertex < MaxVertices )
				gs_IndexOf[nVertex] = nIndex;
			return nSlot;
		}

		if ( nWas == nIndex )
			return nSlot;
		nSlot = ( nSlot + 1 ) & ( TableSize - 1 );
	}

	return 0;
}

// A group per kept meshlet: its triangles, each corner in its order (so the first is the provoking vertex, as in the index
// buffer), and its vertices once each, positioned and with their bits as borealis_gpu_driven_ids's vertex shader does
[numthreads( 128, 1, 1 )]
[outputtopology( "triangle" )]
[shader( "mesh" )] void MainMs( uint3 vGroupId : SV_GroupID, uint nThread : SV_GroupIndex, in payload MeshletPayload payload, out vertices PS_INPUT vertices[MaxVertices], out indices uint3 triangles[MaxTriangles] )
{
	uint nInstance = payload.nInstance;
	uint nMeshlet = payload.nFirstMeshlet + ( ( payload.nKept[vGroupId.x >> 1] >> ( ( vGroupId.x & 1 ) * 16 ) ) & 0xffff );
	uint2 vInstance = BorealisGpuDrivenInstance( nInstance );
	uint4 vDrawHead = BorealisGpuDrivenDraw( vInstance.y );
	uint4 vDraw = g_bindless_VertexBuffers[ g_nBorealisDraws ].Load4( vInstance.y * 32 + 16 );
	bool b32 = ( vDrawHead.w & 2 ) != 0;
	ByteAddressBuffer meshlets = g_bindless_VertexBuffers[ payload.nMeshletPage ];
	uint nFirstTriangle = meshlets.Load( nMeshlet * 32 + 12 );
	uint nTriangles = min( meshlets.Load( nMeshlet * 32 + 28 ), MaxTriangles );

	gs_Index[nThread] = None;
	if ( nThread == 0 )
		gs_Vertices = 0;
	GroupMemoryBarrierWithGroupSync();

	uint3 vSlots = 0;
	if ( nThread < nTriangles )
	{
		ByteAddressBuffer page = g_bindless_VertexBuffers[ vDraw.w ];
		uint nAt = vDraw.x + ( nFirstTriangle + nThread ) * 3;
		vSlots.x = Place( LoadIndex( page, nAt, b32 ) );
		vSlots.y = Place( LoadIndex( page, nAt + 1, b32 ) );
		vSlots.z = Place( LoadIndex( page, nAt + 2, b32 ) );
	}

	GroupMemoryBarrierWithGroupSync();
	uint nVertices = min( gs_Vertices, MaxVertices );

	// The draw's vertex as its vertex shader sees it: the index plus the base vertex
	PS_INPUT o;
	if ( nThread < nVertices )
	{
		uint nVertex = gs_IndexOf[nThread] + vDraw.z;
		float3 vPositionOs = asfloat( BorealisGpuDrivenFetch( vDrawHead.x, 0, nVertex, true ).xyz );
		float3x4 matObjectToWorld = GetTransformMatrix( vInstance.x, 0 );
		float3 vPositionWs = mul( matObjectToWorld, float4( vPositionOs.xyz, 1.0 ) );
		o.vPositionPs.xyzw = Position3WsToPs( vPositionWs.xyz );
		// As the vertex stage's compile flips it (SlangCompiler.CompilePass)
		o.vPositionPs.y = -o.vPositionPs.y;
		o.nVertexBit = BorealisGpuDrivenVertexBit( nInstance, nVertex );
		gs_Position[nThread] = o.vPositionPs;
	}

	GroupMemoryBarrierWithGroupSync();

	// Only the triangles that can reach a sample, in their order
	uint3 vCorners = uint3( gs_Vertex[vSlots.x], gs_Vertex[vSlots.y], gs_Vertex[vSlots.z] );
	bool bKeep = nThread < nTriangles && !TriangleCulled( gs_Position[vCorners.x], gs_Position[vCorners.y], gs_Position[vCorners.z] );
	uint nWave = nThread / WaveGetLaneCount();
	uint nBefore = WavePrefixCountBits( bKeep );
	uint nWaveKept = WaveActiveCountBits( bKeep );
	if ( WaveIsFirstLane() )
		gs_WaveKept[nWave] = nWaveKept;
	GroupMemoryBarrierWithGroupSync();
	uint nKept = 0;
	for ( uint w = 0; w * WaveGetLaneCount() < 128; w++ )
	{
		if ( w < nWave )
			nBefore += gs_WaveKept[w];
		nKept += gs_WaveKept[w];
	}

	SetMeshOutputCounts( nVertices, nKept );
	if ( nThread < nVertices )
		vertices[nThread] = o;
	if ( bKeep )
		triangles[nBefore] = vCorners;

	// Each triangle's number among its draw's, for forward's compaction, where the task group made room
	if ( payload.nListed != None && vGroupId.x < MaxListed && nThread < nTriangles )
		g_MeshletStamps.Store( ( payload.nTrianglesAt[vGroupId.x] + nThread ) * 4, nFirstTriangle + nThread );
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

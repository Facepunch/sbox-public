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

// A round's kept meshlets (in the order they were found) and its count and triangles (count | triangles << 8)
groupshared uint gs_Round[64];
groupshared uint gs_RoundCount;

// A group per slot of the bucket's task run, which the culling filled with the fragments it listed: tests the fragment's
// meshlets, 64 a round, lists the kept ones in the payload as it goes (where each one's triangles go among the fragment's
// kept ones too) and launches a mesh group for each
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

	// Kept so far, their triangles, and an odd one's index waiting for its word's other half
	uint nKept = 0;
	uint nKeptTriangles = 0;
	uint nCarry = 0;
	for ( uint nBase = 0; nBase < nMeshlets; nBase += 64 )
	{
		if ( nThread == 0 )
			gs_RoundCount = 0;
		GroupMemoryBarrierWithGroupSync();

		uint m = nBase + nThread;
		if ( m < nMeshlets )
		{
			uint nAt = ( vRun.y + m ) * 32;
			uint4 r0 = meshlets.Load4( nAt );
			uint4 r1 = meshlets.Load4( nAt + 16 );
			if ( !MeshletCulled( view, asfloat( r0.xyz ), asfloat( r1.xyz ), mTransform ) )
			{
				uint nWas;
				InterlockedAdd( gs_RoundCount, 1u | ( r1.w << 8 ), nWas );
				gs_Round[nWas & 0xff] = m;
				uint k = nKept + ( nWas & 0xff );
				if ( k < MaxListed )
					gs_Payload.nTrianglesAt[k] = nKeptTriangles + ( nWas >> 8 );
			}
		}

		GroupMemoryBarrierWithGroupSync();
		uint nRound = gs_RoundCount;

		// The words this round completes: two indices each, the first after a carried odd one
		uint nFirstWord = nKept >> 1;
		uint nEnd = nKept + ( nRound & 0xff );
		uint w = nFirstWord + nThread;
		if ( w * 2 + 1 < nEnd )
		{
			uint e = w * 2;
			uint nLow = e < nKept ? nCarry : gs_Round[e - nKept];
			gs_Payload.nKept[w] = nLow | gs_Round[e + 1 - nKept] << 16;
		}

		if ( ( nEnd & 1 ) != 0 )
			nCarry = nEnd - 1 < nKept ? nCarry : gs_Round[nEnd - 1 - nKept];
		nKept = nEnd;
		nKeptTriangles += nRound >> 8;
		GroupMemoryBarrierWithGroupSync();
	}

	if ( nThread == 0 )
	{
		if ( ( nKept & 1 ) != 0 )
			gs_Payload.nKept[nKept >> 1] = nCarry;
		gs_Payload.nInstance = nInstance;
		gs_Payload.nMeshletPage = vRun.x;
		gs_Payload.nFirstMeshlet = vRun.y;

		// Forward's compaction scans only the kept triangles, from their numbers where there's room; none kept, nothing
		bool bListed = nFragment != None && nKept > 0 && nKept <= MaxListed;
		uint nListAt = None;
		if ( bListed )
		{
			g_MeshletStamps.InterlockedAdd( g_MeshletTask.nKeptAt, nKeptTriangles, nListAt );
			if ( nListAt + nKeptTriangles > g_MeshletTask.nKeptCapacity )
				nListAt = None;
		}

		uint nWord = g_MeshletTask.nKeptAt / 4 + 1 + nListAt;
		if ( nFragment != None )
		{
			uint nTriangles = g_bindless_VertexBuffers[ g_nBorealisDraws ].Load( vInstance.y * 32 + 20 ) / 3;
			g_MeshletStamps.Store4( nFragment * 16, nListAt != None ? uint4( g_MeshletTask.nFrame, nWord, nKeptTriangles, 2 )
				: uint4( g_MeshletTask.nFrame, None, nKept == 0 ? 0 : nTriangles, 0 ) );
		}

		gs_Payload.nListed = nListAt != None ? nWord : None;
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

// Where a meshlet page's local records start: after its records (MeshletStore.PageRecords of 32 bytes). Each is the data
// page's bindless slot, where the meshlet's triangles (a word each: its corners among the meshlet's vertices, a byte each)
// and its vertices (index values in the draw's index size) start there in bytes, and its vertex count
static const uint LocalRecordsAt = 131072 * 32;

// Each vertex's outcodes and pixel position, for its triangles' culls
groupshared uint gs_Code[MaxVertices];
groupshared float2 gs_Pixel[MaxVertices];

struct MeshletPrimitive
{
	bool bCull : SV_CullPrimitive;
};

// A vertex's outcodes (outside x-, x+, y-, y+, z-, z+; 64: w <= 0, the hardware clips it). Positions as they're output
// (y negated)
uint VertexCode( float4 p )
{
	uint n = ( p.x < -p.w ? 1u : 0u ) | ( p.x > p.w ? 2u : 0u ) | ( p.y < -p.w ? 4u : 0u ) | ( p.y > p.w ? 8u : 0u ) | ( p.z < 0 ? 16u : 0u ) | ( p.z > p.w ? 32u : 0u );
	return n | ( p.w <= 0 ? 64u : 0u );
}

// Whether a triangle can't reach a sample, as the hardware would find (a vertex shader's triangles are culled so before
// they reach the rasterizer; a mesh shader's aren't): wholly outside a clip plane, facing a way the pipeline culls, or in
// front of the camera with no sample of the grid inside its screen bounds
bool TriangleCulled( uint3 v )
{
	uint a = gs_Code[v.x], b = gs_Code[v.y], c = gs_Code[v.z];
	if ( ( a & b & c & 63u ) != 0 )
		return true;

	// Through the camera plane: the hardware clips it
	if ( ( ( a | b | c ) & 64u ) != 0 )
		return false;

	// Framebuffer coordinates (y down); counter-clockwise there is front facing, a negative cross product. A sliver whose
	// facing rounding could flip goes to the hardware
	float2 pa = gs_Pixel[v.x], pb = gs_Pixel[v.y], pc = gs_Pixel[v.z];
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

// A group per kept meshlet: a lane per vertex of its list, positioned and with its bit as borealis_gpu_driven_ids's vertex
// shader does, and a lane per triangle, its corners in their order (so the first is the provoking vertex, as in the index
// buffer), flagged culled when it can't reach a sample
[numthreads( 128, 1, 1 )]
[outputtopology( "triangle" )]
[shader( "mesh" )] void MainMs( uint3 vGroupId : SV_GroupID, uint nThread : SV_GroupIndex, in payload MeshletPayload payload, out vertices PS_INPUT vertices[MaxVertices], out indices uint3 triangles[MaxTriangles], out primitives MeshletPrimitive primitives[MaxTriangles] )
{
	// The group's records are the same for every lane: read once, as scalars (WaveReadLaneFirst tells the compiler so)
	uint nInstance = WaveReadLaneFirst( payload.nInstance );
	uint nMeshlet = WaveReadLaneFirst( payload.nFirstMeshlet + ( ( payload.nKept[vGroupId.x >> 1] >> ( ( vGroupId.x & 1 ) * 16 ) ) & 0xffff ) );
	uint2 vInstance = WaveReadLaneFirst( BorealisGpuDrivenInstance( nInstance ) );
	uint4 vDrawHead = WaveReadLaneFirst( BorealisGpuDrivenDraw( vInstance.y ) );
	uint4 vDraw = WaveReadLaneFirst( g_bindless_VertexBuffers[ g_nBorealisDraws ].Load4( vInstance.y * 32 + 16 ) );
	bool b32 = ( vDrawHead.w & 2 ) != 0;
	ByteAddressBuffer meshlets = g_bindless_VertexBuffers[ WaveReadLaneFirst( payload.nMeshletPage ) ];
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
		gs_Code[nThread] = VertexCode( o.vPositionPs );
		gs_Pixel[nThread] = ( o.vPositionPs.xy / o.vPositionPs.w * 0.5 + 0.5 ) * g_MeshletTask.vViewport;
	}

	uint3 vCorners = 0;
	if ( nThread < nTriangles )
	{
		uint nPacked = data.Load( vLocal.y + nThread * 4 );
		vCorners = uint3( nPacked & 0xff, ( nPacked >> 8 ) & 0xff, ( nPacked >> 16 ) & 0xff );
	}

	GroupMemoryBarrierWithGroupSync();
	if ( nThread < nTriangles )
	{
		triangles[nThread] = vCorners;
		primitives[nThread].bCull = TriangleCulled( vCorners );
	}

	// Each triangle's number among its draw's, for forward's compaction, where the task group made room
	if ( payload.nListed != None && vGroupId.x < MaxListed && nThread < nTriangles )
		g_MeshletStamps.Store( ( payload.nListed + payload.nTrianglesAt[vGroupId.x] + nThread ) * 4, nFirstTriangle + nThread );
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

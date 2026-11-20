// borealis_meshlet_ids: a standalone module, the whole shader for Borealis.Gpu, drawn on devices with mesh shaders (task and mesh
// stages; docs/reference/borealis-standalone-modules.md).
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "Borealis meshlet ids prepass: a task group per listed fragment culls its meshlets as borealis_meshlet_cull_cs does and launches a mesh group per kept meshlet, which draws its triangles' depth and ids as borealis_gpu_driven_ids does.";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#include "sbox/pass.slangh"

Mode( Default );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "system.fxc" // This should always be the first include in COMMON
#include "vr_common.fxc"
#include "math_general.fxc"

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_INPUT
{
	// The vertex's bit in the view's mask and its instance record (BorealisGpuDrivenIds); flat, so a pixel gets its
	// triangle's first (provoking) vertex's
	nointerpolation uint2 vIds : TEXCOORD0;

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
	uint4 vVertices;
	uint4 vPosition;
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

// The stream a semantic of a geometry record reads (as BorealisGpuDrivenFetch finds it), or zero when it has none
uint4 StreamOf( uint nGeometry, uint nSemantic )
{
	ByteAddressBuffer geometry = g_bindless_VertexBuffers[ g_nBorealisGeometry ];
	uint nMaskWord = nSemantic >> 5;
	uint nBit = 1u << ( nSemantic & 31 );
	uint nMask = geometry.Load( nGeometry + nMaskWord * 4 );
	if ( ( nMask & nBit ) == 0 )
		return 0;

	uint nAttribute = countbits( nMask & ( nBit - 1 ) );
	for ( uint i = 0; i < nMaskWord; i++ )
		nAttribute += countbits( geometry.Load( nGeometry + i * 4 ) );
	return geometry.Load4( nGeometry + 32 + nAttribute * 16 );
}
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
		uint4 vDrawHead = BorealisGpuDrivenDraw( vInstance.y );
		uint4 vDraw = g_bindless_VertexBuffers[ g_nBorealisDraws ].Load4( vInstance.y * 32 + 16 );
		gs_Payload.vVertices = uint4( vInstance.x, vDraw.z, frame.Load( g_nBorealisVertexBases + nInstance * 4 ), ( vDrawHead.w & 2 ) != 0 ? 1 : 0 );
		gs_Payload.vPosition = StreamOf( vDrawHead.x, 0 );
		gs_Payload.nMeshletPage = vRun.x;
		gs_Payload.nFirstMeshlet = vRun.y;

		// Forward's compaction scans only the kept triangles, from their numbers where there's room; none kept, nothing
		bool bListed = nFragment != None && nKept > 0 && nKept <= MaxListed;
		uint nListAt = None;
		if ( bListed )
		{
			g_MeshletStamps.InterlockedAdd( g_MeshletTask.nKeptAt, nKeptTriangles, nListAt );
			if ( nListAt >= g_MeshletTask.nKeptCapacity || nKeptTriangles > g_MeshletTask.nKeptCapacity - nListAt )
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

// The meshlet builder's limits this stage takes (64x124 and smaller; BackendDevice.MeshletTasks)
static const uint MaxVertices = 64;
static const uint MaxTriangles = 128;

// Where a meshlet page's local records start: after its records (MeshletStore.PageRecords of 32 bytes). Each is the data
// page's bindless slot, where the meshlet's triangles (a word each: its corners among the meshlet's vertices, a byte each)
// and its vertices (index values in the draw's index size) start there in bytes, and its vertex count
static const uint LocalRecordsAt = 131072 * 32;

// A vertex's position from its stream, as BorealisGpuDrivenFetch decodes it (the stream is the group's, so its page is too)
float3 FetchPosition( uint4 attribute, uint nVertex )
{
	uint4 result = uint4( 0, 0, 0, asuint( 1.0f ) );
	if ( nVertex >= ( attribute.w >> 1 ) )
		return 0;

	uint nFormat = attribute.z;
	uint nStride = nFormat >> 16;
	uint nBits = nFormat & 63;
	uint nCount = ( nFormat >> 6 ) & 7;
	uint nConversion = ( nFormat >> 9 ) & 7;
	uint nOffset = attribute.y + nVertex * nStride;
	ByteAddressBuffer page = g_bindless_VertexBuffers[ NonUniformResourceIndex( attribute.x - 1 ) ];

	// 32-bit components need no decoding
	if ( nBits == 32 )
	{
		if ( nCount >= 3 ) result.xyz = page.Load3( nOffset );
		else if ( nCount == 2 ) result.xy = page.Load2( nOffset );
		else result.x = page.Load( nOffset );
		return asfloat( result.xyz );
	}

	// 10:10:10:2
	if ( nBits == 10 )
	{
		uint4 values = ( page.Load( nOffset ) >> uint4( 0, 10, 20, 30 ) ) & uint4( 1023, 1023, 1023, 3 );
		return asfloat( ( nConversion == 1 ? asuint( float4( values ) / float4( 1023, 1023, 1023, 3 ) ) : values ).xyz );
	}

	// 8 and 16-bit components, from up to three words
	uint nBitOffset = ( nOffset & 3 ) * 8;
	uint nAligned = nOffset & ~3u;
	uint nBitCount = nBits * nCount;
	uint3 words = uint3( page.Load( nAligned ), 0, 0 );
	if ( nBitOffset + nBitCount > 32 ) words.y = page.Load( nAligned + 4 );
	if ( nBitOffset + nBitCount > 64 ) words.z = page.Load( nAligned + 8 );

	uint4 shifts = nBitOffset + uint4( 0, 1, 2, 3 ) * nBits;
	uint4 packed = shifts < 32 ? words.xxxx : ( shifts < 64 ? words.yyyy : words.zzzz );
	uint nMaskBits = 0xffffffffu >> ( 32 - nBits );
	uint4 values = ( packed >> ( shifts & 31 ) ) & nMaskBits;
	if ( nConversion == 0 )
		values = asuint( f16tof32( values ) );
	else if ( nConversion == 1 )
		values = asuint( float4( values ) * ( nBits == 8 ? 1.0f / 255.0f : 1.0f / 65535.0f ) );
	else if ( nConversion == 2 || nConversion == 4 )
	{
		int4 signedValues = int4( values << ( 32 - nBits ) ) >> ( 32 - nBits );
		values = nConversion == 2 ? asuint( max( -1.0f, float4( signedValues ) * ( nBits == 8 ? 1.0f / 127.0f : 1.0f / 32767.0f ) ) ) : asuint( signedValues );
	}
	result = uint4( 0, 1, 2, 3 ) < nCount ? values : result;
	if ( ( nFormat & 4096 ) != 0 )
		result.xyz = result.zyx;
	return asfloat( result.xyz );
}

// A group per kept meshlet: a lane per vertex of its list, positioned and with its bit as borealis_gpu_driven_ids's vertex
// shader does, and a lane per triangle, its corners in their order (so the first is the provoking vertex, as in the index
// buffer). The triangles that can't reach a sample go to the hardware, which culls them for less than a test here costs
[numthreads( 128, 1, 1 )]
[outputtopology( "triangle" )]
[shader( "mesh" )] void MainMs( uint3 vGroupId : SV_GroupID, uint nThread : SV_GroupIndex, in payload MeshletPayload payload, out vertices PS_INPUT vertices[MaxVertices], out indices uint3 triangles[MaxTriangles] )
{
	// The group's records are the same for every lane: read once, as scalars (WaveReadLaneFirst tells the compiler so)
	uint nInstance = WaveReadLaneFirst( payload.nInstance );
	uint nMeshlet = WaveReadLaneFirst( payload.nFirstMeshlet + ( ( payload.nKept[vGroupId.x >> 1] >> ( ( vGroupId.x & 1 ) * 16 ) ) & 0xffff ) );
	uint4 vVertices = WaveReadLaneFirst( payload.vVertices );
	uint4 vStream = WaveReadLaneFirst( payload.vPosition );
	bool b32 = vVertices.w != 0;
	ByteAddressBuffer meshlets = g_bindless_VertexBuffers[ WaveReadLaneFirst( payload.nMeshletPage ) ];
	uint2 vMeshlet = WaveReadLaneFirst( uint2( meshlets.Load( nMeshlet * 32 + 12 ), meshlets.Load( nMeshlet * 32 + 28 ) ) );
	uint4 vLocal = WaveReadLaneFirst( meshlets.Load4( LocalRecordsAt + nMeshlet * 16 ) );
	uint nFirstTriangle = vMeshlet.x;
	uint nTriangles = min( vMeshlet.y, MaxTriangles );
	uint nVertices = min( vLocal.w, MaxVertices );
	uint nPointer = nInstance < ( 1u << 22 ) && g_nBorealisInstances < 1024 ? ( g_nBorealisInstances << 22 ) | nInstance : None;
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

		uint nVertex = nIndex + vVertices.y;
		float3 vPositionOs = FetchPosition( vStream, nVertex );
		float3x4 matObjectToWorld = GetTransformMatrix( vVertices.x, 0 );
		float3 vPositionWs = mul( matObjectToWorld, float4( vPositionOs.xyz, 1.0 ) );
		PS_INPUT o;
		o.vPositionPs.xyzw = Position3WsToPs( vPositionWs.xyz );
		// As the vertex stage's compile flips it (SlangCompiler.CompilePass)
		o.vPositionPs.y = -o.vPositionPs.y;
		o.vIds = uint2( vVertices.z == None ? None : vVertices.z + nVertex, nPointer );
		vertices[nThread] = o;
	}

	if ( nThread < nTriangles )
	{
		uint nPacked = data.Load( vLocal.y + nThread * 4 );
		triangles[nThread] = uint3( nPacked & 0xff, ( nPacked >> 8 ) & 0xff, ( nPacked >> 16 ) & 0xff );

		// Each triangle's number among its draw's, for forward's compaction, where the task group made room
		if ( payload.nListed != None && vGroupId.x < MaxListed )
			g_MeshletStamps.Store( ( payload.nListed + payload.nTrianglesAt[vGroupId.x] + nThread ) * 4, nFirstTriangle + nThread );
	}
}

#endif // MS

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_PS )
RenderState( DepthEnable, true );
RenderState( DepthWriteEnable, true );
RenderState( DepthFunc, GREATER_EQUAL );
RenderState( CullMode, DEFAULT );

// As borealis_gpu_driven_ids: the first vertex's bit + 1 and its instance record at target 1; target 0 goes nowhere
struct PS_OUTPUT
{
	float4 vUnused : SV_Target0;
	uint2 vId : SV_Target1;
};

[shader( "pixel" )] PS_OUTPUT MainPs( PS_INPUT i )
{
	PS_OUTPUT o;
	o.vUnused = 1.0;
	o.vId = uint2( i.vIds.x + 1, i.vIds.y );
	return o;
}

#endif // PS

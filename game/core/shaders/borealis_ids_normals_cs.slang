// borealis_ids_normals_cs: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md). It
// draws only with -ids-normals (docs/reference/borealis-gpu-driven.md, "Normals from the ids").
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "Borealis normals from the visibility ids: each pixel's triangle, found from its id's instance record and first corner, and that triangle's normal, for ambient occlusion.";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#include "sbox/pass.slangh"

Mode( Default );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "system.fxc" // This should always be the first include in COMMON

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_CS )
#include "common.fxc"
#include "transform_buffer.fxc"
#include "borealis_gpu_driven.fxc"

DynamicCombo( D_MSAA, 0..1, Sys( ALL ) );

// The view's ids (vertex bit + 1, instance record: BorealisGpuDrivenIds) and depth, read at sample 0, which the depth
// chain's first mip holds
#if D_MSAA
	Texture2DMS<uint2> g_tIds < Attribute( "BorealisIds" ); >;
	Texture2DMS<float> g_tDepth < Attribute( "BorealisIdsDepth" ); >;
#else
	Texture2D<uint2> g_tIds < Attribute( "BorealisIds" ); >;
	Texture2D<float> g_tDepth < Attribute( "BorealisIdsDepth" ); >;
#endif

// The calls that drew the ids this frame (BackendDevice.VisibilityView.IdsCalls): 16 bytes each, the frame chunk's bindless
// slot, the first instance record and how many, and where their fragment words are as g_nBorealisVertexBases names them;
// zeros past the last
ByteAddressBuffer g_Calls < Attribute( "BorealisIdsCalls" ); >;

// The normals, a texel per pixel of the viewport: the world-space normal of the pixel's triangle, facing the camera, and 1;
// zeros where the pixel has none (no id, no table, or a triangle that isn't at its depth)
RWTexture2D<float4> g_Out < Attribute( "BorealisIdsNormalsOut" ); >;

// Constants: the float members are g_n-named too, as the backend's checker reads one prefix
cbuffer BorealisIdsNormals_t
{
	uint g_nIdsWidth;			// the viewport's size: the threads and the texels written
	uint g_nIdsHeight;
	uint g_nIdsOffsetX;			// its corner in the ids and depth targets
	uint g_nIdsOffsetY;
	uint g_nIdsCorners;			// bindless slot of the corner runs (Meshlets.MeshletStore.CornerSlotCovering), a run per draw record; None for none
	uint g_nIdsCalls;			// calls in g_Calls at most
	uint g_nIdsPad0;
	uint g_nIdsPad1;
	float4 g_nIdsSample;		// the depth range the view draws into (its minimum and maximum), and sample 0's place in its pixel
	float4 g_nIdsWorldX;		// clip to world: x, y, z, w = dot( row, clip ), clip y up (before the vertex shaders negate it)
	float4 g_nIdsWorldY;
	float4 g_nIdsWorldZ;
	float4 g_nIdsWorldW;
};

static const uint None = 0xffffffffu;

// A first corner's triangles tried at most: the rest of a fan is left to depth
static const uint MaxCandidates = 16;

// One index of a draw, from its index page (16 or 32 bits)
uint LoadIndex( ByteAddressBuffer page, uint nIndex, bool b32 )
{
	if ( b32 )
		return page.Load( nIndex * 4 );

	uint nWord = page.Load( ( nIndex * 2 ) & ~3u );
	return ( nIndex & 1 ) != 0 ? nWord >> 16 : nWord & 0xffff;
}

float3 ClipToWorld( float2 vNdc, float flZ )
{
	float4 vClip = float4( vNdc, flZ, 1.0 );
	float4 vWorld = float4( dot( g_nIdsWorldX, vClip ), dot( g_nIdsWorldY, vClip ), dot( g_nIdsWorldZ, vClip ), dot( g_nIdsWorldW, vClip ) );
	return vWorld.xyz / vWorld.w;
}

// The normal of the pixel's triangle and 1, or zeros. The id names its instance record (in a call's region of a frame chunk)
// and its triangle's first corner: the call's fragment words give the vertex, the draw record its range, the range's
// first-corner table its triangles, and of those the one sample 0's ray meets at its depth is the pixel's
float4 Resolve( uint2 vPixel, uint2 vId, float flDepth )
{
	if ( vId.x == 0 || vId.y == 0 || vId.y == None || g_nIdsCorners == None )
		return 0;

	uint nChunk = vId.y >> 22;
	uint nInstance = vId.y & 0x3fffff;
	uint4 vCall = 0;
	bool bFound = false;
	for ( uint k = 0; k < g_nIdsCalls; k++ )
	{
		uint4 vEntry = g_Calls.Load4( k * 16 );
		if ( vEntry.z == 0 )
			break;
		if ( vEntry.x == nChunk && nInstance - vEntry.y < vEntry.z )
		{
			vCall = vEntry;
			bFound = true;
			break;
		}
	}

	if ( !bFound )
		return 0;

	// The instance record (transform slot, draw record) and its vertex base; skinned transforms move their vertices off the
	// bind pose this reads, so they're left to depth
	ByteAddressBuffer chunk = g_bindless_VertexBuffers[ NonUniformResourceIndex( nChunk ) ];
	uint2 vInstance = chunk.Load2( nInstance * 8 );
	uint nBase = chunk.Load( vCall.w + nInstance * 4 );
	if ( nBase == None || g_TransformData[ vInstance.x ].m_nBlendWeightCount > 0 )
		return 0;

	// The draw: geometry record and flags, then first index, index count, base vertex and index page
	uint4 vDraw = BorealisGpuDrivenDraw( vInstance.y );
	uint4 vRange = g_bindless_VertexBuffers[ g_nBorealisDraws ].Load4( vInstance.y * 32 + 16 );
	uint nVertex = ( vId.x - 1 ) - nBase;

	// Its range's triangles whose first corner is the vertex: the corner run (data slot + 1, table, lowest value, values)
	uint4 vCorners = g_bindless_VertexBuffers[ g_nIdsCorners ].Load4( vInstance.y * 16 );
	uint nValue = nVertex - vRange.z - vCorners.z;
	if ( vCorners.x == 0 || nValue >= vCorners.w )
		return 0;

	ByteAddressBuffer data = g_bindless_VertexBuffers[ NonUniformResourceIndex( vCorners.x - 1 ) ];
	uint2 vRun = data.Load2( vCorners.y + nValue * 4 );
	uint nTrianglesAt = vCorners.y + ( vCorners.w + 1 ) * 4;

	// Sample 0's ray, from the near plane (reverse-Z: 1) to its depth, in the view's depth range
	float2 vSample = ( float2( vPixel ) + g_nIdsSample.zw ) / float2( g_nIdsWidth, g_nIdsHeight );
	float2 vNdc = float2( vSample.x * 2.0 - 1.0, 1.0 - vSample.y * 2.0 );
	float flZ = ( flDepth - g_nIdsSample.x ) / max( g_nIdsSample.y - g_nIdsSample.x, 1e-6 );
	float3 vNear = ClipToWorld( vNdc, 1.0 );
	float3 vRay = ClipToWorld( vNdc, flZ ) - vNear;

	ByteAddressBuffer page = g_bindless_VertexBuffers[ NonUniformResourceIndex( vRange.w ) ];
	bool b32 = ( vDraw.w & 2 ) != 0;
	float3x4 matObjectToWorld = g_TransformData[ vInstance.x ].transform;

	// The triangle the ray meets nearest its depth (1 along it), within its edges
	float flBest = 2e-3;
	float3 vBest = 0;
	uint nEnd = min( vRun.y, vRun.x + MaxCandidates );
	for ( uint k = vRun.x; k < nEnd; k++ )
	{
		uint nFirst = vRange.x + data.Load( nTrianglesAt + k * 4 ) * 3;
		float3 p[3];
		[unroll]
		for ( uint c = 0; c < 3; c++ )
		{
			float3 vPositionOs = asfloat( BorealisGpuDrivenFetch( vDraw.x, 0, vRange.z + LoadIndex( page, nFirst + c, b32 ), true ).xyz );
			p[c] = mul( matObjectToWorld, float4( vPositionOs, 1.0 ) );
		}

		float3 e1 = p[1] - p[0];
		float3 e2 = p[2] - p[0];
		float3 h = cross( vRay, e2 );
		float a = dot( e1, h );
		if ( abs( a ) < 1e-20 )
			continue;

		float f = 1.0 / a;
		float3 s = vNear - p[0];
		float u = f * dot( s, h );
		float3 q = cross( s, e1 );
		float v = f * dot( vRay, q );
		float t = f * dot( e2, q );
		float flOutside = max( max( -u, -v ), u + v - 1.0 );
		float flMiss = abs( t - 1.0 );
		if ( flOutside < 1e-3 && flMiss < flBest )
		{
			flBest = flMiss;
			vBest = cross( e1, e2 );
		}
	}

	if ( all( vBest == 0 ) )
		return 0;

	float3 vNormal = normalize( vBest );
	return float4( dot( vNormal, vRay ) > 0 ? -vNormal : vNormal, 1.0 );
}

[numthreads( 8, 8, 1 )]
[shader( "compute" )] void MainCs( uint3 vThreadId : SV_DispatchThreadID )
{
	if ( vThreadId.x >= g_nIdsWidth || vThreadId.y >= g_nIdsHeight )
		return;

	int2 vAt = int2( vThreadId.xy + uint2( g_nIdsOffsetX, g_nIdsOffsetY ) );
	#if D_MSAA
		uint2 vId = g_tIds.Load( vAt, 0 );
		float flDepth = g_tDepth.Load( vAt, 0 );
	#else
		uint2 vId = g_tIds.Load( int3( vAt, 0 ) );
		float flDepth = g_tDepth.Load( int3( vAt, 0 ) );
	#endif

	g_Out[vThreadId.xy] = Resolve( vThreadId.xy, vId, flDepth );
}

#endif // CS

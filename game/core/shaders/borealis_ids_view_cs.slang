// borealis_ids_view_cs: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md). It
// draws the visibility ids views (docs/reference/borealis.md, "Visibility ids views").
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "Borealis visibility ids view: each pixel's visibility id as a colour by its triangle, instance or draw.";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#include "sbox/pass.slangh"

Mode( Default );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "sbox/system.slangh" // This should always be the first include in COMMON

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_CS )
#include "sbox/view.slangh"
#include "sbox/gpu_driven.slangh"

DynamicCombo( D_MSAA, 0..1, Sys( ALL ) );

// The view's ids (vertex bit + 1, instance record: BorealisGpuDrivenIds), read at sample 0. Unknown format, as the mark's,
// so either ids target's view matches; a one-word target reads 0 for the second word
#if D_MSAA
	[format( "unknown" )] Texture2DMS<uint2> g_tIds < Attribute( "BorealisIds" ); >;
#else
	[format( "unknown" )] Texture2D<uint2> g_tIds < Attribute( "BorealisIds" ); >;
#endif

// A texel per pixel of the viewport: the colour and 1 where the pixel has an id, transparent black where it has none
RWTexture2D<float4> g_Out < Attribute( "BorealisIdsViewOut" ); >;

cbuffer BorealisIdsView_t
{
	uint g_nIdsViewWidth;		// the viewport's size: the threads and the texels written
	uint g_nIdsViewHeight;
	uint g_nIdsViewOffsetX;		// its corner in the ids target
	uint g_nIdsViewOffsetY;
	uint g_nIdsViewMode;		// IdsView: 1 triangles (the id), 2 instances (the instance record), 3 draws (the instance's draw record)
	uint g_nIdsViewPad0;
	uint g_nIdsViewPad1;
	uint g_nIdsViewPad2;
};

static const uint None = 0xffffffffu;

// A well-mixed hash, so neighbouring ids get unrelated colours
uint Hash( uint x )
{
	x ^= x >> 16;
	x *= 0x7feb352du;
	x ^= x >> 15;
	x *= 0x846ca68bu;
	x ^= x >> 16;
	return x;
}

// A bright, saturated colour from a hash: its hue from the low bits, its value from the next
float3 ColorOf( uint nKey )
{
	uint h = Hash( nKey );
	float flHue = ( h & 0xffff ) / 65536.0;
	float3 vRgb = saturate( abs( frac( flHue + float3( 0.0, 2.0 / 3.0, 1.0 / 3.0 ) ) * 6.0 - 3.0 ) - 1.0 );
	float flValue = 0.6 + 0.4 * ( ( h >> 16 ) & 0xff ) / 255.0;
	return ( 0.15 + 0.85 * vRgb ) * flValue;
}

// The colour of a pixel's id, and 1; transparent black (the frame shows through, darkened) where it has none
float4 Resolve( uint2 vId )
{
	if ( vId.x == 0 )
		return float4( 0, 0, 0, 0.7 );

	if ( g_nIdsViewMode == 1 )
		return float4( ColorOf( vId.x ), 1.0 );

	// The instance record: its frame chunk's bindless slot (top 10 bits) and index; none where its writer had none
	if ( vId.y == 0 || vId.y == None )
		return float4( 0.25, 0.25, 0.25, 1.0 );

	if ( g_nIdsViewMode == 2 )
		return float4( ColorOf( vId.y ), 1.0 );

	// The draw record the instance draws (its record's second word)
	uint nChunk = vId.y >> 22;
	uint nInstance = vId.y & 0x3fffff;
	uint nDraw = g_bindless_VertexBuffers[ NonUniformResourceIndex( nChunk ) ].Load( nInstance * 8 + 4 );
	return float4( ColorOf( nDraw ^ 0x9e3779b9u ), 1.0 );
}

[numthreads( 8, 8, 1 )]
[shader( "compute" )] void MainCs( uint3 vThreadId : SV_DispatchThreadID )
{
	if ( vThreadId.x >= g_nIdsViewWidth || vThreadId.y >= g_nIdsViewHeight )
		return;

	int2 vAt = int2( vThreadId.xy + uint2( g_nIdsViewOffsetX, g_nIdsViewOffsetY ) );
	#if D_MSAA
		uint2 vId = g_tIds.Load( vAt, 0 );
	#else
		uint2 vId = g_tIds.Load( int3( vAt, 0 ) );
	#endif

	g_Out[vThreadId.xy] = Resolve( vId );
}

#endif // CS

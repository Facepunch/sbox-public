// borealis_shadow_cache_copy: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "Borealis shadows: a cached shadow map's depth into a map, unwrapped by the cache's offset, as an exact copy.";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#include "sbox/pass.slangh"

Mode( Default );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "sbox/system.slangh" // This should always be the first include in COMMON

// The texel of the cache each texel of the map starts at (xy, in [0, size)) and the cache's size (zw)
float4 g_vShadowCacheWrap < Attribute( "ShadowCacheWrap" ); >;

// Each 32x32 tile of the cache's min and max depth, and how they're used: 0 not at all, every texel copied; 1 the map was
// cleared, and a texel of a tile whose farthest depth is the clear's (it holds no caster) is left as cleared; 2 as 1, drawn
// as an instance a tile, those with no caster collapsed by the vertex shader (the wrap a whole number of tiles)
Texture2D<float2> g_tShadowCacheTiles < Attribute( "ShadowCacheTiles" ); >;
float g_flShadowCacheSparse < Attribute( "ShadowCacheSparse" ); Default( 0 ); >;

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_VS )
#include "sbox/view.slangh"

struct VS_INPUT
{
	float3 vPositionOs : POSITION < Semantic( PosXyz ); >;
	float2 vTexCoord : TEXCOORD0 < Semantic( LowPrecisionUv ); >;
};

struct VS_OUTPUT
{
	float4 vPositionPs : SV_Position;
	float2 vTexCoord : TEXCOORD0;
};

[shader( "vertex" )] VS_OUTPUT MainVs( VS_INPUT i, uint nInstance : SV_InstanceID )
{
	VS_OUTPUT o;
	o.vPositionPs = float4( i.vPositionOs.xyz, 1.0 );
	o.vTexCoord = i.vTexCoord;

	if ( g_flShadowCacheSparse > 1 )
	{
		// The instance's tile of the cache, and where it lands in the map: the quad's uvs (0 to 1, top left first) over it
		uint2 vSize = uint2( g_vShadowCacheWrap.zw );
		uint nTilesX = vSize.x / 32;
		uint2 vTile = uint2( nInstance % nTilesX, nInstance / nTilesX );
		uint2 vAt = ( vTile * 32 + vSize - uint2( g_vShadowCacheWrap.xy ) ) % vSize;
		float2 vTexel = float2( vAt ) + i.vTexCoord * 32.0;
		o.vPositionPs = float4( vTexel.x / vSize.x * 2.0 - 1.0, 1.0 - vTexel.y / vSize.y * 2.0, i.vPositionOs.z, 1.0 );

		// No caster in it: every corner in one place, nothing drawn
		if ( g_tShadowCacheTiles.Load( int3( vTile, 0 ) ).y <= 0 )
			o.vPositionPs = float4( -2.0, -2.0, 0.0, 1.0 );
	}

	return o;
}

#endif // VS

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_PS )
	#include "sbox/view.slangh"

	Texture2D<float> g_tShadowCache < Attribute( "ShadowCacheDepth" ); >;

	// Every texel written with the cache's depth as it is: no test, no colour
	RenderState( ColorWriteEnable0, false );
	RenderState( DepthEnable, true );
	RenderState( DepthWriteEnable, true );
	RenderState( DepthFunc, ALWAYS );
	RenderState( BlendEnable, false );
	RenderState( CullMode, NONE );

	[shader( "pixel" )] float MainPs( float4 vPositionPs : SV_Position, float2 vTexCoord : TEXCOORD0 ) : SV_Depth
	{
		uint2 vSize = uint2( g_vShadowCacheWrap.zw );
		uint2 vTexel = ( uint2( vPositionPs.xy ) + uint2( g_vShadowCacheWrap.xy ) ) % vSize;
		if ( g_flShadowCacheSparse == 1 && g_tShadowCacheTiles.Load( int3( vTexel / 32, 0 ) ).y <= 0 )
			discard;
		return g_tShadowCache.Load( int3( vTexel, 0 ) );
	}

#endif // PS

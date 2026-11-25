// borealis_shadow_tiles_cs: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "Borealis sun cascades: the nearest and farthest caster depth in each 32x32 texel tile, widened by a tile, for lit shaders to skip the shadow filter.";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#include "sbox/pass.slangh"

Mode( Default );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "sbox/system.slangh" // This should always be the first include in COMMON

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_CS )
	#include "sbox/view.slangh"

	// 0: a cascade's depth into the min and max of each tile. 1: each tile's min and max over it and its eight neighbours,
	// so a lit shader reading one tile covers any filter footprint up to a tile wide (DirectionalLightShadow.hlsl)
	DynamicCombo( D_WIDEN, 0..1, Sys( ALL ) );

	// x: the cascade's first column of tiles in the atlas, y: tiles along each side of a cascade
	float4 g_vShadowTiles < Attribute( "ShadowTiles" ); >;

	#if D_WIDEN
		Texture2D<float2> g_tTiles < Attribute( "ShadowTilesIn" ); >;
	#else
		Texture2D<float> g_tShadowDepth < Attribute( "ShadowTileDepth" ); >;
	#endif

	RWTexture2D<float2> g_tTilesOut < Attribute( "ShadowTilesOut" ); >;

	#if D_WIDEN
		[numthreads( 8, 8, 1 )]
		[shader( "compute" )] void MainCs( uint3 vThreadId : SV_DispatchThreadID )
		{
			int nFirst = (int)g_vShadowTiles.x;
			int nTiles = (int)g_vShadowTiles.y;
			int2 vTile = int2( vThreadId.xy );
			if ( vTile.x >= nTiles || vTile.y >= nTiles )
				return;

			float2 vMinMax = float2( 1, 0 );
			[unroll]
			for ( int y = -1; y <= 1; y++ )
			{
				[unroll]
				for ( int x = -1; x <= 1; x++ )
				{
					int2 vAt = clamp( vTile + int2( x, y ), 0, nTiles - 1 );
					float2 v = g_tTiles.Load( int3( nFirst + vAt.x, vAt.y, 0 ) );
					vMinMax = float2( min( vMinMax.x, v.x ), max( vMinMax.y, v.y ) );
				}
			}

			g_tTilesOut[ int2( nFirst + vTile.x, vTile.y ) ] = vMinMax;
		}
	#else
		SamplerState g_sPoint < Filter( POINT ); AddressU( CLAMP ); AddressV( CLAMP ); >;

		// The min and max of each wave, for the first lane to fold together
		groupshared float2 s_vWaveMinMax[ 64 ];

		// A group per tile. Each thread gathers four 2x2 quads, one from each 16x16 quarter of the tile, so each gather
		// instruction reads a contiguous block across the group rather than texels four apart
		[numthreads( 8, 8, 1 )]
		[shader( "compute" )] void MainCs( uint3 vGroupId : SV_GroupID, uint3 vGroupThreadId : SV_GroupThreadID, uint nIndex : SV_GroupIndex )
		{
			uint nWidth, nHeight;
			g_tShadowDepth.GetDimensions( nWidth, nHeight );
			float2 vInvSize = 1.0 / float2( nWidth, nHeight );
			int2 vBase = int2( vGroupId.xy ) * 32 + int2( vGroupThreadId.xy ) * 2;

			float2 vMinMax = float2( 1, 0 );
			[unroll]
			for ( int q = 0; q < 4; q++ )
			{
				// The quad's shared corner: a gather there returns its four texels
				int2 vQuad = vBase + int2( q & 1, q >> 1 ) * 16;
				float4 d = g_tShadowDepth.GatherRed( g_sPoint, float2( vQuad + 1 ) * vInvSize );
				vMinMax = float2( min( vMinMax.x, min( min( d.x, d.y ), min( d.z, d.w ) ) ), max( vMinMax.y, max( max( d.x, d.y ), max( d.z, d.w ) ) ) );
			}

			vMinMax = float2( WaveActiveMin( vMinMax.x ), WaveActiveMax( vMinMax.y ) );
			uint nLanes = WaveGetLaneCount();
			if ( WaveIsFirstLane() )
				s_vWaveMinMax[ nIndex / nLanes ] = vMinMax;
			GroupMemoryBarrierWithGroupSync();

			if ( nIndex == 0 )
			{
				uint nWaves = ( 64 + nLanes - 1 ) / nLanes;
				for ( uint w = 1; w < nWaves; w++ )
					vMinMax = float2( min( vMinMax.x, s_vWaveMinMax[ w ].x ), max( vMinMax.y, s_vWaveMinMax[ w ].y ) );
				g_tTilesOut[ int2( (int)g_vShadowTiles.x + (int)vGroupId.x, vGroupId.y ) ] = vMinMax;
			}
		}
	#endif
#endif // CS

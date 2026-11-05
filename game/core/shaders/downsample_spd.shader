//-------------------------------------------------------------------------------------------------------------------------------------------------------------
HEADER
{
	DevShader = true;
	Description = "FidelityFX SPD style mip chain downsample, six mips a dispatch, for the non-blur methods of downsample_cs.";
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

	// Same values as downsample_cs, the blur methods aren't supported here
	enum DownsampleMethod
	{
		Box = 0,
		Max = 3,
		Min = 4,
		MinMax = 5
	};
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
//
// Each 256 thread group reduces a 64x64 tile of the source down to one texel of the sixth mip after it, as FidelityFX SPD
// does: lanes remapped so a quad is a 2x2 block, the second mip reduced across quads, the rest through groupshared memory.
// Past six mips the caller dispatches again from the sixth. SPD instead hands the sixth mip to the last group to finish,
// but on RDNA 2 (Steam Deck) that group read other groups' texels before they were visible.
//
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
CS
{
	#include "common.fxc"

	DynamicCombo( D_DOWNSAMPLE_METHOD, 0..5, Sys( ALL ) );

	// Resolve depth into the first mip as the first dispatch reads it, instead of a pass before: a min/max chain
	DynamicCombo( D_FUSED_DEPTH, 0..1, Sys( ALL ) );
	DynamicCombo( D_MSAA, 0..1, Sys( ALL ) );

#if D_MSAA
	Texture2DMS<float> SourceDepth < Attribute( "SourceDepth" ); >;
#else
	Texture2D<float> SourceDepth < Attribute( "SourceDepth" ); >;
#endif
	RWTexture2D<float2> Mip0Out < Attribute( "Mip0Out" ); >;

	Texture2D<float4> Mip0 < Attribute( "Mip0" ); >;
	RWTexture2D<float4> Mip1 < Attribute( "Mip1" ); >;
	RWTexture2D<float4> Mip2 < Attribute( "Mip2" ); >;
	RWTexture2D<float4> Mip3 < Attribute( "Mip3" ); >;
	RWTexture2D<float4> Mip4 < Attribute( "Mip4" ); >;
	RWTexture2D<float4> Mip5 < Attribute( "Mip5" ); >;
	RWTexture2D<float4> Mip6 < Attribute( "Mip6" ); >;

	int2 SourceSize < Attribute( "SourceSize" ); >;
	int MipCount < Attribute( "MipCount" ); >; // Mips to write, 1..6

	// Ping-ponged so each mip needs one barrier
	groupshared float4 Lds[2][16][16];

	float4 Reduce( float4 a, float4 b, float4 c, float4 d )
	{
		if ( D_DOWNSAMPLE_METHOD == DownsampleMethod::Max )
			return float4( max( max( a, b ), max( c, d ) ).rgb, 1 );
		else if ( D_DOWNSAMPLE_METHOD == DownsampleMethod::Min )
			return float4( min( min( a, b ), min( c, d ) ).rgb, 1 );
		else if ( D_DOWNSAMPLE_METHOD == DownsampleMethod::MinMax ) // Min in R, max in G
			return float4( min( min( a.x, b.x ), min( c.x, d.x ) ), max( max( a.y, b.y ), max( c.y, d.y ) ), 0, 1 );
		else
			return ( a + b + c + d ) * 0.25f;
	}

	// Clamped, past the edge only feeds texels outside every mip
	float4 Load( int2 p )
	{
		return max( Mip0.Load( int3( min( p, SourceSize - 1 ), 0 ) ), 0.0f );
	}

	// Every lane of a quad gets the reduction of its 2x2 block
	float4 ReduceQuad( float4 v )
	{
		return Reduce( v, QuadReadAcrossX( v ), QuadReadAcrossY( v ), QuadReadAcrossDiagonal( v ) );
	}

	// FidelityFX SPD's ARmpRed8x8: each wave of 64 covers an 8x8 block in 2x2 quads, the four waves 16x16
	uint2 RemapLane( uint i )
	{
		uint2 p = uint2( ( ( i >> 2 ) & 6 ) | ( i & 1 ), ( ( i >> 3 ) & 4 ) | ( ( i >> 1 ) & 3 ) );
		return p + uint2( ( i >> 6 ) & 1, i >> 7 ) * 8;
	}

	void Store( uint mip, uint2 p, float4 v )
	{
		switch ( mip )
		{
			case 1: Mip1[p] = v; break;
			case 2: Mip2[p] = v; break;
			case 3: Mip3[p] = v; break;
			case 4: Mip4[p] = v; break;
			case 5: Mip5[p] = v; break;
			case 6: Mip6[p] = v; break;
		}
	}

	[numthreads( 256, 1, 1 )]
	void MainCs( uint2 group : SV_GroupID, uint thread : SV_GroupIndex )
	{
		uint2 p = RemapLane( thread );
		bool leader = ( thread & 3 ) == 0;

		// The first mip, a texel in each 16x16 quadrant. Every load before any store: the compiler can't move a load past a
		// store, so interleaving them would wait out each load's latency in turn.
		float4 v[4];
		#if D_FUSED_DEPTH
		{
			// A quadrant a time: its four depths, stored as the first mip once they're all in, then reduced
			[unroll]
			for ( uint q = 0; q < 4; q++ )
			{
				float d[4];
				int2 t[4];
				[unroll]
				for ( uint i = 0; i < 4; i++ )
				{
					t[i] = ( group * 32 + uint2( q & 1, q >> 1 ) * 16 + p ) * 2 + int2( i & 1, i >> 1 );
					int2 c = int2( g_vViewportOffset ) + min( t[i], SourceSize - 1 );
					#if D_MSAA
						d[i] = SourceDepth.Load( c, 0 );
					#else
						d[i] = SourceDepth.Load( int3( c, 0 ) );
					#endif
				}

				[unroll]
				for ( uint i = 0; i < 4; i++ )
					Mip0Out[t[i]] = float2( d[i], d[i] );

				v[q] = Reduce( float4( d[0].xx, 0, 1 ), float4( d[1].xx, 0, 1 ), float4( d[2].xx, 0, 1 ), float4( d[3].xx, 0, 1 ) );
			}
		}
		#else
		[unroll]
		for ( uint i = 0; i < 4; i++ )
		{
			int2 s = ( group * 32 + uint2( i & 1, i >> 1 ) * 16 + p ) * 2;
			v[i] = Reduce( Load( s ), Load( s + int2( 1, 0 ) ), Load( s + int2( 0, 1 ) ), Load( s + int2( 1, 1 ) ) );
		}
		#endif

		// The second across quads, into groupshared memory for the rest
		[unroll]
		for ( uint i = 0; i < 4; i++ )
		{
			uint2 quadrant = uint2( i & 1, i >> 1 );
			Store( 1, group * 32 + quadrant * 16 + p, v[i] );
			if ( MipCount < 2 )
				continue;

			float4 r = ReduceQuad( v[i] );
			uint2 p2 = quadrant * 8 + p / 2;
			if ( leader )
			{
				Store( 2, group * 16 + p2, r );
				Lds[0][p2.y][p2.x] = r;
			}
		}

		// Unrolled so each mip's store and LDS bank are fixed, MipCount is uniform so the early out is too
		[unroll]
		for ( uint mip = 3; mip <= 6; mip++ )
		{
			if ( mip > (uint)MipCount )
				break;

			GroupMemoryBarrierWithGroupSync();

			uint size = 64 >> ( mip - 1 ); // Of the mip read
			if ( thread < size * size )
			{
				float4 r = ReduceQuad( Lds[( mip + 1 ) & 1][p.y][p.x] );
				if ( leader )
				{
					Store( mip, group * ( size / 2 ) + p / 2, r );
					Lds[mip & 1][p.y / 2][p.x / 2] = r;
				}
			}
		}
	}
}

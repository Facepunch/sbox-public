// framecopy_tail_cs: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "The frame buffer copy's last mips, 32x32 and smaller, in one group";
//
// framecopy_mips_cs's mips, the frame buffer copy's from the first of 32x32 or under down to 1x1, in one dispatch of one
// group instead of a dispatch and a barrier each. Each mip as it makes one: the 9-tap gaussian, at the mip's resolution, of
// the bilinear taps of the mip above, blurred in sRGB gamma. The first reads the mip above from the copy; each after it
// reads the one before from groupshared memory, as halves (as the copy holds them), with the bilinear clamp done here.
//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#include "sbox/pass.slangh"

Mode( Default );

#include "sbox/system.slangh"
#include "sbox/math.slangh"

#if ( PROGRAM == VFX_PROGRAM_CS )
	// The mip above the first made, and the mips made (the first TailInfo.w of them)
	Texture2D MipLevel0 < Attribute( "MipLevel0" ); >;
	RWTexture2D<float4> Tail0 < Attribute( "TailMip0" ); >;
	RWTexture2D<float4> Tail1 < Attribute( "TailMip1" ); >;
	RWTexture2D<float4> Tail2 < Attribute( "TailMip2" ); >;
	RWTexture2D<float4> Tail3 < Attribute( "TailMip3" ); >;
	RWTexture2D<float4> Tail4 < Attribute( "TailMip4" ); >;
	RWTexture2D<float4> Tail5 < Attribute( "TailMip5" ); >;

	// x, y: the copy's viewport size; z: the first mip made; w: how many
	float4 TailInfo < Attribute( "TailInfo" ); >;

	SamplerState BilinearClamp < Filter( BILINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); AddressW( CLAMP ); >;

	static const float Weights[5] = { 70.0f / 256.0f, 56.0f / 256.0f, 28.0f / 256.0f, 8.0f / 256.0f, 1.0f / 256.0f };

	float4 BlurPixels( float4 a, float4 b, float4 c, float4 d, float4 e, float4 f, float4 g, float4 h, float4 i )
	{
		return Weights[0]*e + Weights[1]*(d+f) + Weights[2]*(c+g) + Weights[3]*(b+h) + Weights[4]*(a+i);
	}

	uint2 PackHalf4( float4 v ) { return uint2( f32tof16( v.r ) | f32tof16( v.g ) << 16, f32tof16( v.b ) | f32tof16( v.a ) << 16 ); }
	float4 UnpackHalf4( uint2 p ) { return float4( f16tof32( p.x ), f16tof32( p.x >> 16 ), f16tof32( p.y ), f16tof32( p.y >> 16 ) ); }

	// A mip of up to 32x32: its taps with the blur's 4 on each side, its rows blurred across, and the mip itself for the
	// next to read (written over the one before it, whose taps are taken by then), all as halves: 31,232 bytes, under the 32 KiB
	// AMD's Windows driver allows (the rows as floats made 41,472)
	static const uint MaxSize = 32;
	static const uint TapsSide = MaxSize + 8;
	groupshared uint2 Taps[TapsSide * TapsSide];
	groupshared uint2 Rows[TapsSide * MaxSize];
	groupshared uint2 Level[MaxSize * MaxSize];

	static const uint Threads = 256;

	int2 MipSize( uint mip ) { return max( int2( TailInfo.xy ) >> mip, 1 ); }

	// The mip above, at the bilinear tap for a texel of the mip being made, as a clamped bilinear sampler takes it
	float4 Above( uint level, int2 pixel, int2 size, int2 above )
	{
		float2 uv = ( pixel + 0.5f ) / float2( size );
		if ( level == 0 )
			return MipLevel0.SampleLevel( BilinearClamp, uv, 0 );

		float2 t = uv * float2( above ) - 0.5f;
		int2 p0 = int2( floor( t ) );
		float2 f = t - p0;
		int2 a = clamp( p0, 0, above - 1 );
		int2 b = clamp( p0 + 1, 0, above - 1 );
		float4 c00 = UnpackHalf4( Level[a.y * MaxSize + a.x] );
		float4 c10 = UnpackHalf4( Level[a.y * MaxSize + b.x] );
		float4 c01 = UnpackHalf4( Level[b.y * MaxSize + a.x] );
		float4 c11 = UnpackHalf4( Level[b.y * MaxSize + b.x] );
		return lerp( lerp( c00, c10, f.x ), lerp( c01, c11, f.x ), f.y );
	}

	void Store( uint level, int2 pixel, float4 color )
	{
		if ( level == 0 ) Tail0[pixel] = color;
		else if ( level == 1 ) Tail1[pixel] = color;
		else if ( level == 2 ) Tail2[pixel] = color;
		else if ( level == 3 ) Tail3[pixel] = color;
		else if ( level == 4 ) Tail4[pixel] = color;
		else Tail5[pixel] = color;
	}

	[numthreads( 256, 1, 1 )]
	[shader( "compute" )] void MainCs( uint vThread : SV_GroupIndex )
	{
		const uint first = uint( TailInfo.z );
		const uint count = min( uint( TailInfo.w ), 6u );

		for ( uint level = 0; level < count; level++ )
		{
			const int2 size = MipSize( first + level );
			const int2 above = MipSize( first + level - 1 );
			const int2 taps = size + 8;

			// The taps, as halves in sRGB gamma (downsample_cs's LoadColorSRGB)
			for ( uint t = vThread; t < uint( taps.x * taps.y ); t += Threads )
			{
				int2 at = int2( t % taps.x, t / taps.x );
				float4 color = max( Above( level, at - 4, size, above ), 0.0f );
				color.rgb = SrgbLinearToGamma( color.rgb );
				Taps[at.y * TapsSide + at.x] = PackHalf4( color );
			}

			GroupMemoryBarrierWithGroupSync();

			// Across
			for ( uint r = vThread; r < uint( taps.y * size.x ); r += Threads )
			{
				uint row = r / size.x, col = r % size.x;
				float4 s[9];
				[unroll]
				for ( uint i = 0; i < 9; i++ )
					s[i] = UnpackHalf4( Taps[row * TapsSide + col + i] );
				Rows[row * MaxSize + col] = PackHalf4( BlurPixels( s[0], s[1], s[2], s[3], s[4], s[5], s[6], s[7], s[8] ) );
			}

			GroupMemoryBarrierWithGroupSync();

			// Down, then stored (downsample_cs's StoreColor) and kept for the next mip
			for ( uint o = vThread; o < uint( size.x * size.y ); o += Threads )
			{
				int2 at = int2( o % size.x, o / size.x );
				float4 s[9];
				[unroll]
				for ( uint i = 0; i < 9; i++ )
					s[i] = UnpackHalf4( Rows[( at.y + i ) * MaxSize + at.x] );
				float4 color = BlurPixels( s[0], s[1], s[2], s[3], s[4], s[5], s[6], s[7], s[8] );
				color.rgb = SrgbGammaToLinear( color.rgb );
				color.rgb = clamp( color.rgb, 0.0f, 65504.0f );
				Store( level, at, color );
				Level[at.y * MaxSize + at.x] = PackHalf4( color );
			}

			GroupMemoryBarrierWithGroupSync();
		}
	}

#endif // CS

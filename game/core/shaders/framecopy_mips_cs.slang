// framecopy_mips_cs: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "The frame buffer copy's mips: downsample_cs's gaussian blur, a 16x16 tile a group";
//
// The same mip as downsample_cs's D_DOWNSAMPLE_METHOD 1 makes, texel for texel: each texel the 9-tap gaussian, at the
// mip's resolution, of the bilinear taps between the 2x2 texels above it, blurred in sRGB gamma. downsample_cs's group
// writes 8x8 texels from 16x16 taps; here a group writes 16x16 from 24x24, so a texel takes 2.25 taps, not 4.
//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#include "sbox/pass.slangh"

Mode( Default );

#include "sbox/system.slangh"
#include "sbox/math.slangh"

#if ( PROGRAM == VFX_PROGRAM_CS )
	Texture2D MipLevel0 < Attribute( "MipLevel0" ); >;
	RWTexture2D<float4> MipLevel1 < Attribute( "MipLevel1" ); >;

	// The written mip's size, and the texels this dispatch writes: x, y its first (a group's tiles from there), z, w past its last
	float2 TextureSize < Attribute( "TextureSize" ); >;
	float2 InvTextureSize < Attribute( "InvTextureSize" ); >;
	float4 Region < Attribute( "MipRegion" ); >;

	SamplerState BilinearClamp < Filter( BILINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); AddressW( CLAMP ); >;

	// downsample_cs's LoadColorSRGB and StoreColor for the gaussian blur
	float4 LoadColorSRGB( int2 pixelCoord )
	{
		float4 color = max( MipLevel0.SampleLevel( BilinearClamp, ( pixelCoord + 0.5f ) * InvTextureSize, 0 ), 0.0f );
		color.rgb = SrgbLinearToGamma( color.rgb );
		return color;
	}

	void StoreColor( int2 pixelCoord, float4 color )
	{
		color.rgb = SrgbGammaToLinear( color.rgb );
		color.rgb = clamp( color.rgb, 0.0f, 65504.0f ); // Clamp to Float16 avoid NaN
		MipLevel1[ pixelCoord ] = color;
	}

	static const float Weights[5] = { 70.0f / 256.0f, 56.0f / 256.0f, 28.0f / 256.0f, 8.0f / 256.0f, 1.0f / 256.0f };

	float4 BlurPixels( float4 a, float4 b, float4 c, float4 d, float4 e, float4 f, float4 g, float4 h, float4 i )
	{
		return Weights[0]*e + Weights[1]*(d+f) + Weights[2]*(c+g) + Weights[3]*(b+h) + Weights[4]*(a+i);
	}

	// The taps as halves (as downsample_cs keeps them), then the rows blurred across, in full floats (as it keeps those)
	groupshared uint2 Taps[24 * 24];
	groupshared float4 Rows[24 * 16];

	uint2 PackHalf4( float4 v ) { return uint2( f32tof16( v.r ) | f32tof16( v.g ) << 16, f32tof16( v.b ) | f32tof16( v.a ) << 16 ); }
	float4 UnpackHalf4( uint2 p ) { return float4( f16tof32( p.x ), f16tof32( p.x >> 16 ), f16tof32( p.y ), f16tof32( p.y >> 16 ) ); }

	[numthreads( 8, 8, 1 )]
	[shader( "compute" )] void MainCs( uint2 vGroupID : SV_GroupID, uint2 vThread : SV_GroupThreadID )
	{
		const int2 vTile = int2( Region.xy ) + int2( vGroupID * 16 );
		const int2 vTileOrigin = vTile - 4;

		[unroll]
		for ( uint y = 0; y < 3; y++ )
		{
			[unroll]
			for ( uint x = 0; x < 3; x++ )
			{
				uint2 t = vThread * 3 + uint2( x, y );
				Taps[t.y * 24 + t.x] = PackHalf4( LoadColorSRGB( vTileOrigin + int2( t ) ) );
			}
		}

		GroupMemoryBarrierWithGroupSync();

		[unroll]
		for ( uint r = 0; r < 3; r++ )
		{
			uint row = vThread.y * 3 + r;
			uint col = vThread.x * 2;
			float4 s[10];
			[unroll]
			for ( uint i = 0; i < 10; i++ )
				s[i] = UnpackHalf4( Taps[row * 24 + col + i] );

			Rows[row * 16 + col] = BlurPixels( s[0], s[1], s[2], s[3], s[4], s[5], s[6], s[7], s[8] );
			Rows[row * 16 + col + 1] = BlurPixels( s[1], s[2], s[3], s[4], s[5], s[6], s[7], s[8], s[9] );
		}

		GroupMemoryBarrierWithGroupSync();

		const int2 vEnd = int2( min( TextureSize, Region.zw ) );
		[unroll]
		for ( uint c = 0; c < 2; c++ )
		{
			uint col = vThread.x * 2 + c;
			float4 s[10];
			[unroll]
			for ( uint i = 0; i < 10; i++ )
				s[i] = Rows[( vThread.y * 2 + i ) * 16 + col];

			int2 vOut = vTile + int2( col, vThread.y * 2 );
			if ( vOut.x < vEnd.x && vOut.y < vEnd.y ) StoreColor( vOut, BlurPixels( s[0], s[1], s[2], s[3], s[4], s[5], s[6], s[7], s[8] ) );
			if ( vOut.x < vEnd.x && vOut.y + 1 < vEnd.y ) StoreColor( vOut + int2( 0, 1 ), BlurPixels( s[1], s[2], s[3], s[4], s[5], s[6], s[7], s[8], s[9] ) );
		}
	}

#endif // CS

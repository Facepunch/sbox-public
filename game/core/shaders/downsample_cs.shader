//-------------------------------------------------------------------------------------------------------------------------------------------------------------
HEADER
{
	DevShader = true;
	Description = "Compute Shader for gaussian and GGX mipmap generation, downsample_spd does the other methods.";
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
	#include "math_general.fxc"
	
	enum DownsampleMethod
	{
		GaussianBlur = 1,
		GaussianBorder = 2,
		GGX = 6
	};
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
CS
{
	// Includes -----------------------------------------------------------------------------------------------------------------------------------------------

	// Combos -------------------------------------------------------------------------------------------------------------------------------------------------
	DynamicCombo( D_DOWNSAMPLE_METHOD, 1..6, Sys( ALL ) );
	
	// System Textures ----------------------------------------------------------------------------------------------------------------------------------------
	Texture2D 			MipLevel0 < Attribute( "MipLevel0" ); >;
	RWTexture2D<float4> MipLevel1 < Attribute( "MipLevel1" ); >;

	// System Constants ---------------------------------------------------------------------------------------------------------------------------------------
	float2 TextureSize < Attribute( "TextureSize" ); >;
	float2 InvTextureSize < Attribute( "InvTextureSize" ); >;

    SamplerState BilinearClamp      < Filter(BILINEAR); AddressU(CLAMP); AddressV(CLAMP); AddressW(CLAMP); >;
	SamplerState BilinearBorder     < Filter(BILINEAR); AddressU(BORDER); AddressV(BORDER); AddressW(BORDER); BorderColor( float4(0,0,0,0); ); >;
	
	//---------------------------------------------------------------------------------------------------------------------------------------------------------
	bool WantsSRGB()
	{
		return D_DOWNSAMPLE_METHOD == DownsampleMethod::GaussianBlur || D_DOWNSAMPLE_METHOD == DownsampleMethod::GGX;
	}

	//---------------------------------------------------------------------------------------------------------------------------------------------------------
	// One bilinear tap on the shared corner averages the 2x2 source texels of an output texel
	float4 LoadColorSRGB( int2 pixelCoord )
	{
		SamplerState sampler = D_DOWNSAMPLE_METHOD == DownsampleMethod::GaussianBorder ? BilinearBorder : BilinearClamp;

		float4 color = max( MipLevel0.SampleLevel( sampler, ( pixelCoord + 0.5f ) * InvTextureSize, 0 ), 0.0f );
		// GaussianBorder is handed a premultiplied UI layer in whatever encoding the layer stores, and like CSS blurs
		// the stored values as they are. The rest are handed linear colour and average it in sRGB gamma
		if ( D_DOWNSAMPLE_METHOD != DownsampleMethod::GaussianBorder )
			color.rgb = SrgbLinearToGamma( color.rgb );
		return color;
	}

	void StoreColor( int2 pixelCoord, float4 color )
	{
		if( WantsSRGB() )
		{
			color.rgb = SrgbGammaToLinear( color.rgb );
			color.rgb = clamp( color.rgb, 0.0f, 65504.0f ); // Clamp to Float16 avoid NaN
		}

		MipLevel1[ pixelCoord ] = color;
	}
	

	//-------------------------------------------------------------------------------------------------------------------------------------------------------------
	//
	// Gaussian Blur Path from MSFT
	// https://github.com/Microsoft/DirectX-Graphics-Samples/blob/master/MiniEngine/Core/Shaders/BlurCS.hlsl
	//
	//-------------------------------------------------------------------------------------------------------------------------------------------------------------
	// The guassian blur weights (derived from Pascal's triangle)
	static const float Weights[5] = { 70.0f / 256.0f, 56.0f / 256.0f, 28.0f / 256.0f, 8.0f / 256.0f, 1.0f / 256.0f };
	// GGX-like long-tailed kernel, 1 / ( 1 + ( x / 1.5 )^2 )^2 normalised
	static const float GgxWeights[5] = { 0.430f, 0.206f, 0.0555f, 0.0172f, 0.0065f };
	float W( int i ) { return D_DOWNSAMPLE_METHOD == DownsampleMethod::GGX ? GgxWeights[i] : Weights[i]; }

	float4 BlurPixels( float4 a, float4 b, float4 c, float4 d, float4 e, float4 f, float4 g, float4 h, float4 i )
	{
		return W(0)*e + W(1)*(d+f) + W(2)*(c+g) + W(3)*(b+h) + W(4)*(a+i);
	}

	// 16x16 pixels with an 8x8 center that we will be blurring writing out.  Each uint is two color channels packed together
	groupshared uint CacheR[128];
	groupshared uint CacheG[128];
	groupshared uint CacheB[128];
	groupshared uint CacheA[128];

	void Store2Pixels( uint index, float4 pixel1, float4 pixel2 )
	{
		CacheR[index] = f32tof16(pixel1.r) | f32tof16(pixel2.r) << 16;
		CacheG[index] = f32tof16(pixel1.g) | f32tof16(pixel2.g) << 16;
		CacheB[index] = f32tof16(pixel1.b) | f32tof16(pixel2.b) << 16;
		CacheA[index] = f32tof16(pixel1.a) | f32tof16(pixel2.a) << 16;
	}

	void Load2Pixels( uint index, out float4 pixel1, out float4 pixel2 )
	{
		uint rr = CacheR[index];
		uint gg = CacheG[index];
		uint bb = CacheB[index];
		uint aa = CacheA[index];
		pixel1 = float4( f16tof32(rr      ), f16tof32(gg      ), f16tof32(bb      ), f16tof32(aa      ) );
		pixel2 = float4( f16tof32(rr >> 16), f16tof32(gg >> 16), f16tof32(bb >> 16), f16tof32(aa >> 16) );
	}

	void Store1Pixel( uint index, float4 pixel )
	{
		CacheR[index] = asuint(pixel.r);
		CacheG[index] = asuint(pixel.g);
		CacheB[index] = asuint(pixel.b);
		CacheA[index] = asuint(pixel.a);
	}

	void Load1Pixel( uint index, out float4 pixel )
	{
		pixel = asfloat( uint4(CacheR[index], CacheG[index], CacheB[index], CacheA[index]) );
	}

	// Blur two pixels horizontally.  This reduces LDS reads and pixel unpacking.
	void BlurHorizontally( uint outIndex, uint leftMostIndex )
	{
		float4 s0, s1, s2, s3, s4, s5, s6, s7, s8, s9;
		Load2Pixels( leftMostIndex + 0, s0, s1 );
		Load2Pixels( leftMostIndex + 1, s2, s3 );
		Load2Pixels( leftMostIndex + 2, s4, s5 );
		Load2Pixels( leftMostIndex + 3, s6, s7 );
		Load2Pixels( leftMostIndex + 4, s8, s9 );
		
		Store1Pixel(outIndex  , BlurPixels(s0, s1, s2, s3, s4, s5, s6, s7, s8));
		Store1Pixel(outIndex+1, BlurPixels(s1, s2, s3, s4, s5, s6, s7, s8, s9));
	}

	float4 BlurVertically( uint2 pixelCoord, uint topMostIndex )
	{
		float4 s0, s1, s2, s3, s4, s5, s6, s7, s8;
		Load1Pixel( topMostIndex   , s0 );
		Load1Pixel( topMostIndex+ 8, s1 );
		Load1Pixel( topMostIndex+16, s2 );
		Load1Pixel( topMostIndex+24, s3 );
		Load1Pixel( topMostIndex+32, s4 );
		Load1Pixel( topMostIndex+40, s5 );
		Load1Pixel( topMostIndex+48, s6 );
		Load1Pixel( topMostIndex+56, s7 );
		Load1Pixel( topMostIndex+64, s8 );

		return BlurPixels(s0, s1, s2, s3, s4, s5, s6, s7, s8);
	}

	float4 FilterGaussianBlur( uint2 vGroupID : SV_GroupID, uint2 vGroupThreadID : SV_GroupThreadID, uint2 vDispatchId : SV_DispatchThreadID )
	{
		//
		// Load 4 pixels per thread into LDS
		//
		int2 GroupUL = (vGroupID.xy << 3) - 4;                // Upper-left pixel coordinate of group read location
		int2 ThreadUL = (vGroupThreadID.xy << 1) + GroupUL;        // Upper-left pixel coordinate of quad that this thread will read

		//
		// Store 4 unblurred pixels in LDS
		//
		int destIdx = vGroupThreadID.x + (vGroupThreadID.y << 4);
		Store2Pixels(destIdx+0, LoadColorSRGB( ThreadUL + uint2(0, 0)) , LoadColorSRGB( ThreadUL + uint2(1, 0)) );
		Store2Pixels(destIdx+8, LoadColorSRGB( ThreadUL + uint2(0, 1)) , LoadColorSRGB( ThreadUL + uint2(1, 1)) );

		GroupMemoryBarrierWithGroupSync();

		//
		// Horizontally blur the pixels in Cache
		//
		uint row = vGroupThreadID.y << 4;
		BlurHorizontally(row + (vGroupThreadID.x << 1), row + vGroupThreadID.x + (vGroupThreadID.x & 4));

		GroupMemoryBarrierWithGroupSync();

		//
		// Vertically blur the pixels and write the result to memory
		//
		return BlurVertically(vDispatchId.xy, (vGroupThreadID.y << 3) + vGroupThreadID.x);
	}

	// GGX: each 8x8 group blurs a 16x16 output tile, so the apron costs 2.25 fetches per texel instead of 4
	groupshared uint2 GgxTile[24 * 24];
	groupshared uint2 GgxRows[24 * 16];

	uint2 PackHalf4( float4 v ) { return uint2( f32tof16( v.r ) | f32tof16( v.g ) << 16, f32tof16( v.b ) | f32tof16( v.a ) << 16 ); }
	float4 UnpackHalf4( uint2 p ) { return float4( f16tof32( p.x ), f16tof32( p.x >> 16 ), f16tof32( p.y ), f16tof32( p.y >> 16 ) ); }

	void FilterGGX( uint2 vGroupID, uint2 vThread )
	{
		int2 vTileOrigin = int2( vGroupID * 16 ) - 4;

		[unroll]
		for ( uint y = 0; y < 3; y++ )
		{
			[unroll]
			for ( uint x = 0; x < 3; x++ )
			{
				uint2 t = vThread * 3 + uint2( x, y );
				GgxTile[t.y * 24 + t.x] = PackHalf4( LoadColorSRGB( vTileOrigin + int2( t ) ) );
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
				s[i] = UnpackHalf4( GgxTile[row * 24 + col + i] );

			GgxRows[row * 16 + col] = PackHalf4( BlurPixels( s[0], s[1], s[2], s[3], s[4], s[5], s[6], s[7], s[8] ) );
			GgxRows[row * 16 + col + 1] = PackHalf4( BlurPixels( s[1], s[2], s[3], s[4], s[5], s[6], s[7], s[8], s[9] ) );
		}

		GroupMemoryBarrierWithGroupSync();

		[unroll]
		for ( uint c = 0; c < 2; c++ )
		{
			uint col = vThread.x * 2 + c;
			float4 s[10];
			[unroll]
			for ( uint i = 0; i < 10; i++ )
				s[i] = UnpackHalf4( GgxRows[( vThread.y * 2 + i ) * 16 + col] );

			uint2 vOut = vGroupID * 16 + uint2( col, vThread.y * 2 );
			StoreColor( vOut, BlurPixels( s[0], s[1], s[2], s[3], s[4], s[5], s[6], s[7], s[8] ) );
			StoreColor( vOut + uint2( 0, 1 ), BlurPixels( s[1], s[2], s[3], s[4], s[5], s[6], s[7], s[8], s[9] ) );
		}
	}

	[numthreads( 8, 8, 1 )]
	void MainCs( uint2 vGroupID : SV_GroupID, uint2 vGroupThreadID : SV_GroupThreadID, uint2 vDispatchId : SV_DispatchThreadID )
	{
		if ( D_DOWNSAMPLE_METHOD == DownsampleMethod::GGX )
			FilterGGX( vGroupID, vGroupThreadID );
		else
			StoreColor( vDispatchId.xy, FilterGaussianBlur( vGroupID, vGroupThreadID, vDispatchId ) );
	}
	
}

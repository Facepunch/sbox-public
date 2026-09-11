//-------------------------------------------------------------------------------------------------------------------------------------------------------------
HEADER
{
	DevShader = true;
	Description = "Compute Shader for accelerated mipmap generation, with support to multiple downsampling algorithms.";
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
	
	enum DownsampleMethod
	{
		Box = 0,
		GaussianBlur = 1,
		GaussianBorder = 2,
		Max = 3,
		Min = 4,
		MinMax = 5,
		GaussianBlurAlpha = 6
	};
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
CS
{
	// Includes -----------------------------------------------------------------------------------------------------------------------------------------------

	// Combos -------------------------------------------------------------------------------------------------------------------------------------------------
	DynamicCombo( D_DOWNSAMPLE_METHOD, 0..6, Sys( ALL ) );
	
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
		return D_DOWNSAMPLE_METHOD == DownsampleMethod::GaussianBlur || D_DOWNSAMPLE_METHOD == DownsampleMethod::GaussianBorder;
	}

	//---------------------------------------------------------------------------------------------------------------------------------------------------------
	float3 LoadColorSRGB( int2 pixelCoord )
	{
		SamplerState sampler = D_DOWNSAMPLE_METHOD == DownsampleMethod::GaussianBorder ? BilinearBorder : BilinearClamp;

		float3 color = MipLevel0.SampleLevel( sampler, pixelCoord * InvTextureSize * 0.5f, 0 ).rgb;
		color = pow( color, 1.0f / 2.2f );
		return max( color, 0.0f );
	}

	float3 LoadColor( int2 pixelCoord )
	{
		if( WantsSRGB() )
			return LoadColorSRGB( pixelCoord );

		return max( MipLevel0[ pixelCoord ].rgb, 0.0f );
	}

	void StoreColor( int2 pixelCoord, float4 color )
	{
		if( WantsSRGB() )
		{
			color.rgb = pow( color.rgb, 2.2f );
			color.rgb = clamp( color.rgb, 0.0f, 65504.0f ); // Clamp to Float16 avoid NaN
		}

		MipLevel1[ pixelCoord ] = color;
	}
	
	//---------------------------------------------------------------------------------------------------------------------------------------------------------

	float3 FilterBilinear( int2 pixelCoord )
	{
		uint2 vCoord = pixelCoord * 2.0f;
		float3 vColor = ( LoadColor( vCoord ) + LoadColor( vCoord + uint2( 1, 0 ) ) + LoadColor( vCoord + uint2( 0, 1 ) ) + LoadColor( vCoord + uint2( 1, 1 ) ) ) * 0.25f;
		return vColor.rgb;
	}

	float3 FilterMax( int2 pixelCoord )
	{
		uint2 vCoord = pixelCoord * 2.0f;
		float3 vColor = max( LoadColor( vCoord ), max( LoadColor( vCoord + uint2( 1, 0 ) ), max( LoadColor( vCoord + uint2( 0, 1 ) ), LoadColor( vCoord + uint2( 1, 1 ) ) ) ) );
		return vColor.rgb;
	}

	float3 FilterMin( int2 pixelCoord )
	{
		uint2 vCoord = pixelCoord * 2.0f;
		float3 vColor = min( LoadColor( vCoord ), min( LoadColor( vCoord + uint2( 1, 0 ) ), min( LoadColor( vCoord + uint2( 0, 1 ) ), LoadColor( vCoord + uint2( 1, 1 ) ) ) ) );
		return vColor.rgb;
	}

	float2 FilterMinMax( int2 pixelCoord )
	{
		// Stores min in R and max in G
		uint2 vCoord = pixelCoord * 2.0f;
		float flMin = min( LoadColor( vCoord ).x, min( LoadColor( vCoord + uint2( 1, 0 ) ).x, min( LoadColor( vCoord + uint2( 0, 1 ) ).x, LoadColor( vCoord + uint2( 1, 1 ) ).x ) ) );
		float flMax = max( LoadColor( vCoord ).y, max( LoadColor( vCoord + uint2( 1, 0 ) ).y, max( LoadColor( vCoord + uint2( 0, 1 ) ).y, LoadColor( vCoord + uint2( 1, 1 ) ).y ) ) );
		return float2( flMin, flMax );	
	}

	//-------------------------------------------------------------------------------------------------------------------------------------------------------------
	//
	// Gaussian Blur Path from MSFT
	// https://github.com/Microsoft/DirectX-Graphics-Samples/blob/master/MiniEngine/Core/Shaders/BlurCS.hlsl
	//
	//-------------------------------------------------------------------------------------------------------------------------------------------------------------
	// The guassian blur weights (derived from Pascal's triangle)
	static const float Weights[5] = { 70.0f / 256.0f, 56.0f / 256.0f, 28.0f / 256.0f, 8.0f / 256.0f, 1.0f / 256.0f };

	float3 BlurPixels( float3 a, float3 b, float3 c, float3 d, float3 e, float3 f, float3 g, float3 h, float3 i )
	{
		return Weights[0]*e + Weights[1]*(d+f) + Weights[2]*(c+g) + Weights[3]*(b+h) + Weights[4]*(a+i);
	}

	// 16x16 pixels with an 8x8 center that we will be blurring writing out.  Each uint is two color channels packed together
	groupshared uint CacheR[128];
	groupshared uint CacheG[128];
	groupshared uint CacheB[128];

	void Store2Pixels( uint index, float3 pixel1, float3 pixel2 )
	{
		CacheR[index] = f32tof16(pixel1.r) | f32tof16(pixel2.r) << 16;
		CacheG[index] = f32tof16(pixel1.g) | f32tof16(pixel2.g) << 16;
		CacheB[index] = f32tof16(pixel1.b) | f32tof16(pixel2.b) << 16;
	}

	void Load2Pixels( uint index, out float3 pixel1, out float3 pixel2 )
	{
		uint rr = CacheR[index];
		uint gg = CacheG[index];
		uint bb = CacheB[index];
		pixel1 = float3( f16tof32(rr      ), f16tof32(gg      ), f16tof32(bb      ) );
		pixel2 = float3( f16tof32(rr >> 16), f16tof32(gg >> 16), f16tof32(bb >> 16) );
	}

	void Store1Pixel( uint index, float3 pixel )
	{
		CacheR[index] = asuint(pixel.r);
		CacheG[index] = asuint(pixel.g);
		CacheB[index] = asuint(pixel.b);
	}

	void Load1Pixel( uint index, out float3 pixel )
	{
		pixel = asfloat( uint3(CacheR[index], CacheG[index], CacheB[index]) );
	}

	// Blur two pixels horizontally.  This reduces LDS reads and pixel unpacking.
	void BlurHorizontally( uint outIndex, uint leftMostIndex )
	{
		float3 s0, s1, s2, s3, s4, s5, s6, s7, s8, s9;
		Load2Pixels( leftMostIndex + 0, s0, s1 );
		Load2Pixels( leftMostIndex + 1, s2, s3 );
		Load2Pixels( leftMostIndex + 2, s4, s5 );
		Load2Pixels( leftMostIndex + 3, s6, s7 );
		Load2Pixels( leftMostIndex + 4, s8, s9 );
		
		Store1Pixel(outIndex  , BlurPixels(s0, s1, s2, s3, s4, s5, s6, s7, s8));
		Store1Pixel(outIndex+1, BlurPixels(s1, s2, s3, s4, s5, s6, s7, s8, s9));
	}

	float3 BlurVertically( uint2 pixelCoord, uint topMostIndex )
	{
		float3 s0, s1, s2, s3, s4, s5, s6, s7, s8;
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
		Store2Pixels(destIdx+0, FilterBilinear( ThreadUL + uint2(0, 0)) , FilterBilinear( ThreadUL + uint2(1, 0)) );
		Store2Pixels(destIdx+8, FilterBilinear( ThreadUL + uint2(0, 1)) , FilterBilinear( ThreadUL + uint2(1, 1)) );

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
		return float4( BlurVertically(vDispatchId.xy, (vGroupThreadID.y << 3) + vGroupThreadID.x), 1.0f );
	}

	//-------------------------------------------------------------------------------------------------------------------------------------------------------------
	//
	// Gaussian with alpha, for layers that get blurred from their mips (panel filter layers). Same 9-tap binomial as
	// above, but on all four channels, in linear space like the sampler reads mip 0, and centred: a 2x2 box of the
	// four parent texels under each child, so mips don't creep up-left. Texels outside the image read as transparent.
	//
	//-------------------------------------------------------------------------------------------------------------------------------------------------------------
	groupshared uint CacheA[128];

	float4 LoadColorAlpha( int2 pixelCoord )
	{
		uint width, height;
		MipLevel0.GetDimensions( width, height );
		if ( any( pixelCoord < 0 ) || pixelCoord.x >= (int)width || pixelCoord.y >= (int)height )
			return 0;

		return max( MipLevel0[ pixelCoord ], 0.0f );
	}

	float4 FilterBoxAlpha( int2 pixelCoord )
	{
		int2 vCoord = pixelCoord * 2;
		return ( LoadColorAlpha( vCoord ) + LoadColorAlpha( vCoord + int2( 1, 0 ) ) + LoadColorAlpha( vCoord + int2( 0, 1 ) ) + LoadColorAlpha( vCoord + int2( 1, 1 ) ) ) * 0.25f;
	}

	float4 BlurPixelsAlpha( float4 a, float4 b, float4 c, float4 d, float4 e, float4 f, float4 g, float4 h, float4 i )
	{
		return Weights[0]*e + Weights[1]*(d+f) + Weights[2]*(c+g) + Weights[3]*(b+h) + Weights[4]*(a+i);
	}

	void Store2PixelsAlpha( uint index, float4 pixel1, float4 pixel2 )
	{
		Store2Pixels( index, pixel1.rgb, pixel2.rgb );
		CacheA[index] = f32tof16(pixel1.a) | f32tof16(pixel2.a) << 16;
	}

	void Load2PixelsAlpha( uint index, out float4 pixel1, out float4 pixel2 )
	{
		float3 rgb1, rgb2;
		Load2Pixels( index, rgb1, rgb2 );
		uint aa = CacheA[index];
		pixel1 = float4( rgb1, f16tof32( aa ) );
		pixel2 = float4( rgb2, f16tof32( aa >> 16 ) );
	}

	void Store1PixelAlpha( uint index, float4 pixel )
	{
		Store1Pixel( index, pixel.rgb );
		CacheA[index] = asuint( pixel.a );
	}

	void Load1PixelAlpha( uint index, out float4 pixel )
	{
		float3 rgb;
		Load1Pixel( index, rgb );
		pixel = float4( rgb, asfloat( CacheA[index] ) );
	}

	void BlurHorizontallyAlpha( uint outIndex, uint leftMostIndex )
	{
		float4 s0, s1, s2, s3, s4, s5, s6, s7, s8, s9;
		Load2PixelsAlpha( leftMostIndex + 0, s0, s1 );
		Load2PixelsAlpha( leftMostIndex + 1, s2, s3 );
		Load2PixelsAlpha( leftMostIndex + 2, s4, s5 );
		Load2PixelsAlpha( leftMostIndex + 3, s6, s7 );
		Load2PixelsAlpha( leftMostIndex + 4, s8, s9 );

		Store1PixelAlpha( outIndex    , BlurPixelsAlpha( s0, s1, s2, s3, s4, s5, s6, s7, s8 ) );
		Store1PixelAlpha( outIndex + 1, BlurPixelsAlpha( s1, s2, s3, s4, s5, s6, s7, s8, s9 ) );
	}

	float4 BlurVerticallyAlpha( uint topMostIndex )
	{
		float4 s0, s1, s2, s3, s4, s5, s6, s7, s8;
		Load1PixelAlpha( topMostIndex     , s0 );
		Load1PixelAlpha( topMostIndex +  8, s1 );
		Load1PixelAlpha( topMostIndex + 16, s2 );
		Load1PixelAlpha( topMostIndex + 24, s3 );
		Load1PixelAlpha( topMostIndex + 32, s4 );
		Load1PixelAlpha( topMostIndex + 40, s5 );
		Load1PixelAlpha( topMostIndex + 48, s6 );
		Load1PixelAlpha( topMostIndex + 56, s7 );
		Load1PixelAlpha( topMostIndex + 64, s8 );

		return BlurPixelsAlpha( s0, s1, s2, s3, s4, s5, s6, s7, s8 );
	}

	float4 FilterGaussianBlurAlpha( uint2 vGroupID, uint2 vGroupThreadID )
	{
		// Same layout as FilterGaussianBlur: a 16x16 block of box-filtered texels around the group's 8x8 outputs
		int2 GroupUL = ( vGroupID.xy << 3 ) - 4;
		int2 ThreadUL = ( vGroupThreadID.xy << 1 ) + GroupUL;

		int destIdx = vGroupThreadID.x + ( vGroupThreadID.y << 4 );
		Store2PixelsAlpha( destIdx + 0, FilterBoxAlpha( ThreadUL + int2( 0, 0 ) ), FilterBoxAlpha( ThreadUL + int2( 1, 0 ) ) );
		Store2PixelsAlpha( destIdx + 8, FilterBoxAlpha( ThreadUL + int2( 0, 1 ) ), FilterBoxAlpha( ThreadUL + int2( 1, 1 ) ) );

		GroupMemoryBarrierWithGroupSync();

		uint row = vGroupThreadID.y << 4;
		BlurHorizontallyAlpha( row + ( vGroupThreadID.x << 1 ), row + vGroupThreadID.x + ( vGroupThreadID.x & 4 ) );

		GroupMemoryBarrierWithGroupSync();

		return BlurVerticallyAlpha( ( vGroupThreadID.y << 3 ) + vGroupThreadID.x );
	}

	[numthreads( 8, 8, 1 )]
	void MainCs( uint2 vGroupID : SV_GroupID, uint2 vGroupThreadID : SV_GroupThreadID, uint2 vDispatchId : SV_DispatchThreadID )
	{
		if ( D_DOWNSAMPLE_METHOD == DownsampleMethod::Box )
			StoreColor( vDispatchId.xy, float4( FilterBilinear( vDispatchId.xy ), 1.0f ) );
		else if( D_DOWNSAMPLE_METHOD == DownsampleMethod::GaussianBlur )
			StoreColor( vDispatchId.xy, FilterGaussianBlur( vGroupID, vGroupThreadID, vDispatchId ) );
		else if( D_DOWNSAMPLE_METHOD == DownsampleMethod::GaussianBorder )
			StoreColor( vDispatchId.xy, FilterGaussianBlur( vGroupID, vGroupThreadID, vDispatchId ) );
		else if( D_DOWNSAMPLE_METHOD == DownsampleMethod::Max )
			StoreColor( vDispatchId.xy, float4( FilterMax( vDispatchId.xy ), 1.0f ) );
		else if( D_DOWNSAMPLE_METHOD == DownsampleMethod::Min )
			StoreColor( vDispatchId.xy, float4( FilterMin( vDispatchId.xy ), 1.0f ) );
		else if( D_DOWNSAMPLE_METHOD == DownsampleMethod::MinMax )
			StoreColor( vDispatchId.xy, float4( FilterMinMax( vDispatchId.xy ), 0.0f, 1.0f ) );
		else if( D_DOWNSAMPLE_METHOD == DownsampleMethod::GaussianBlurAlpha )
			MipLevel1[ vDispatchId.xy ] = FilterGaussianBlurAlpha( vGroupID, vGroupThreadID );
	}
	
}

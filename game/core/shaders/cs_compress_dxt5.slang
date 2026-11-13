// cs_compress_dxt5.shader as a pass module: Basalt.Gpu draws this; the .shader stays for the other routes
// and as the fallback (docs/managed/shader-format-replacement.md, "Pass modules").
// Copyright (c) Valve Corporation, All rights reserved. ======================================================================================================

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: CompileTargets = ( IS_SM_40 || IS_SM_50 );
// HEADER: Description = "Compute shader DXT5 texture compression";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// MODES: Default();

#include "sbox/pass.slangh"

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "system.fxc" // This should always be the first include in COMMON

#define DISPATCH_2x1 0		// Process 2x1 DXT5 blocks (32 threads)
#define DISPATCH_2x2 1		// Process 2x2 DXT5 blocks (64 threads)
#define DISPATCH_TIP 2		// Process "tip" of mip chain (just three DXT5 blocks: the 4x4, 2x2 and 1x1 mip levels)

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_CS )
	DynamicCombo( D_DISPATCH_MODE, 0..2, Sys( ALL ) );
	DynamicCombo( D_PERCEPTUAL_ERROR_METRIC, 0..1, Sys( ALL ) );
	DynamicCombo( D_ENDPOINT_OPTIMIZATION, 0..1, Sys( ALL ) );

#if D_DISPATCH_MODE == DISPATCH_2x1
#define NUM_BLOCKS 2
#else
#define NUM_BLOCKS 4
#endif

	Texture2D g_tSrc < Attribute( "SrcTex" ); SrgbRead( false ); > ;

#if ( D_DISPATCH_MODE == DISPATCH_TIP )
	RWTexture2D<uint4> OutputDXT5Texture_4x4 < Attribute( "OutputDXT5Texture_4x4" ); > ;
	RWTexture2D<uint4> OutputDXT5Texture_2x2 < Attribute( "OutputDXT5Texture_2x2" ); > ;
	RWTexture2D<uint4> OutputDXT5Texture_1x1 < Attribute( "OutputDXT5Texture_1x1" ); > ;
#else
	RWTexture2D<uint4> OutputDXT5Texture < Attribute( "OutputDXT5Texture" ); > ;
#endif

	static const float s_midpoints5[32] = {
		0.015686f, 0.047059f, 0.078431f, 0.111765f, 0.145098f, 0.176471f, 0.207843f, 0.241176f, 0.274510f, 0.305882f, 0.337255f, 0.370588f, 0.403922f, 0.435294f, 0.466667f, 0.5f,
		0.533333f, 0.564706f, 0.596078f, 0.629412f, 0.662745f, 0.694118f, 0.725490f, 0.758824f, 0.792157f, 0.823529f, 0.854902f, 0.888235f, 0.921569f, 0.952941f, 0.984314f, 1000000.f
	};

	static const float s_midpoints6[64] = {
		0.007843f, 0.023529f, 0.039216f, 0.054902f, 0.070588f, 0.086275f, 0.101961f, 0.117647f, 0.133333f, 0.149020f, 0.164706f, 0.180392f, 0.196078f, 0.211765f, 0.227451f, 0.245098f,
		0.262745f, 0.278431f, 0.294118f, 0.309804f, 0.325490f, 0.341176f, 0.356863f, 0.372549f, 0.388235f, 0.403922f, 0.419608f, 0.435294f, 0.450980f, 0.466667f, 0.482353f, 0.500000f,
		0.517647f, 0.533333f, 0.549020f, 0.564706f, 0.580392f, 0.596078f, 0.611765f, 0.627451f, 0.643137f, 0.658824f, 0.674510f, 0.690196f, 0.705882f, 0.721569f, 0.737255f, 0.754902f,
		0.772549f, 0.788235f, 0.803922f, 0.819608f, 0.835294f, 0.850980f, 0.866667f, 0.882353f, 0.898039f, 0.913725f, 0.929412f, 0.945098f, 0.960784f, 0.976471f, 0.992157f, 1000000.f
	};

	struct BlockEndpoints
	{
		uint e0_565;
		uint e1_565;
	};

	struct BlockPalette
	{
		float3 p[4];
	};

	uint g_nMipLevel < Attribute( "MipLevel" ); > ;

	groupshared uint gs_values[NUM_BLOCKS][16];			// Data fetched from texture
	groupshared uint gs_maxAlpha[NUM_BLOCKS];			// Max Alpha
	groupshared uint gs_minAlpha[NUM_BLOCKS];			// Min Alpha
	groupshared uint gs_color_indices[NUM_BLOCKS];		// Bitfield for color block
	groupshared uint gs_alpha_indices[NUM_BLOCKS][2];	// Bitfield for alpha block (48 bits, spread across two "banks")

	groupshared BlockEndpoints gs_endpoints[NUM_BLOCKS];
	groupshared uint gs_blockError[NUM_BLOCKS][2];
	groupshared bool gs_solidColor[NUM_BLOCKS];

	uint3 ColorToColor565_Rounded( float3 rgb )
	{
		uint r = uint( saturate( rgb.r ) * 31.0 );
		uint g = uint( saturate( rgb.g ) * 63.0 );
		uint b = uint( saturate( rgb.b ) * 31.0 );

		// do exact rounding
		r += ( rgb.x > s_midpoints5[r] );
		g += ( rgb.y > s_midpoints6[g] );
		b += ( rgb.z > s_midpoints5[b] );

		return uint3( r, g, b );
	}

	// Bitpack the 565 color to 16 bits
	uint Color565ToPacked565( uint3 _565 )
	{
		return ( _565.r << 11 ) | ( _565.g << 5 ) | ( _565.b );
	}

	uint3 ColorPacked565ToColor888_Expand( uint _565 )
	{
		uint r = (_565 >> 11) & 31;
		uint g = (_565 >> 5 ) & 63;
		uint b = (_565 >> 0 ) & 31;

		r = (r << 3) | (r >> 2);
		g = (g << 2) | (g >> 4);
		b = (b << 3) | (b >> 2);

		return uint3( r, g, b );
	}

	float3 Color888ToColor( uint3 c )
	{
		return float3( c.r, c.g, c.b ) * (1.0 / 255.0);
	}

	// Assumes that the
	BlockPalette EvaluateBlockPalette( BlockEndpoints be )
	{
		uint c0 = be.e0_565;
		uint c1 = be.e1_565;

		uint3 p[4];
		p[0] = ColorPacked565ToColor888_Expand( c0 );
		p[1] = ColorPacked565ToColor888_Expand( c1 );

		// We've always hardcoded to a 4 color palette
		//if ( c0 > c1 )
		{
			// Reference D3D10 decoding
			/*
			p[2].r = (2 * p[0].r + p[1].r) / 3;
			p[2].g = (2 * p[0].g + p[1].g) / 3;
			p[2].b = (2 * p[0].b + p[1].b) / 3;

			p[3].r = (2 * p[1].r + p[0].r) / 3;
			p[3].g = (2 * p[1].g + p[0].g) / 3;
			p[3].b = (2 * p[1].b + p[0].b) / 3;
			*/

			// AMD's decoding -- using this as it doesn't need integer divide
			p[2].r = (43 * p[0].r + 21 * p[1].r + 32) >> 6;
			p[2].g = (43 * p[0].g + 21 * p[1].g + 32) >> 6;
			p[2].b = (43 * p[0].b + 21 * p[1].b + 32) >> 6;

			p[3].r = (43 * p[1].r + 21 * p[0].r + 32) >> 6;
			p[3].g = (43 * p[1].g + 21 * p[0].g + 32) >> 6;
			p[3].b = (43 * p[1].b + 21 * p[0].b + 32) >> 6;
		}

		BlockPalette bp;
		for ( uint i = 0; i < 4; ++i )
		{
			bp.p[i] = Color888ToColor( p[i] );
		}
		return bp;
	}

	// Store texture data into groupshared memory
	void PackInputRGB( uint blockNum, uint blockThread, uint3 rgbInput )
	{
		gs_values[blockNum][blockThread] = ( ( rgbInput.x << 16 ) & 0x00FF0000 ) | ( ( rgbInput.y << 8 ) & 0x0000FF00 ) | ( rgbInput.z & 0x000000FF );
	}

	// Read texture data stored in groupshared memory
	uint3 UnpackInputRGB( uint blockNum, uint idx )
	{
		uint c = gs_values[blockNum][idx];
		return uint3( ( c >> 16 ) & 0x000000FF, ( c >> 8 ) & 0x000000FF, c & 0x000000FF );
	}

	void InitGroupSharedStorage( uint blockNum )
	{
		gs_color_indices[blockNum] = 0;
		gs_alpha_indices[blockNum][0] = 0;
		gs_alpha_indices[blockNum][1] = 0;
		gs_minAlpha[blockNum] = 0xFFFFFFFF;
		gs_maxAlpha[blockNum] = 0;
		gs_blockError[blockNum][0] = 0;
		gs_blockError[blockNum][1] = 0;
	}

    // Returns if the block is ~ solid colid (all relatively similar colors -- can be 
	// used as indication that endpoint optimization won't be too helpful)
	bool ComputeBlockEndpoints( uint blockNum )
	{
		uint i;

		// compute color bbox
		uint3 umax = uint3( 0, 0, 0 );
		uint3 umin = uint3( 255, 255, 255 );
		for ( i = 0; i < 16; i++ )
		{
			uint3 ucolor = UnpackInputRGB( blockNum, i );
			umin = min( umin, ucolor );
			umax = max( umax, ucolor );
		}

		float3 max = umax * ( 1.0 / 255.0 );
		float3 min = umin * ( 1.0 / 255.0 );

		// Now inset the box
		float bias = (8.0f / 255.0f) / 16.0f;
		float3 inset = saturate( (max - min) / 16.0f - (bias) );

		max = saturate( max - inset );
		min = saturate( min + inset );

		// "solid color enough" -- this is an indicator that we don't
		// need to run optimization on the endpoints.
		bool bSolidColorBlock = all( ( max - min ) < .01 );

		// compute the diagonal
		float3 center = ( min + max ) * .5f;
		float cov_xz = 0.0f;
		float cov_yz = 0.0f;
		for ( i = 0; i < 16; i++ )
		{
			float3 color = UnpackInputRGB( blockNum, i ) * (1.f / 255.f);
			float3 t = color - center;
			cov_xz += t.x * t.z;
			cov_yz += t.y * t.z;
		}

		float x0 = max.x;
		float y0 = max.y;
		float x1 = min.x;
		float y1 = min.y;

		if ( cov_xz < 0 )
		{
			float tmp = x1;
			x1 = x0;
			x0 = tmp;
		}
		if ( cov_yz < 0 )
		{
			float tmp = y1;
			y1 = y0;
			y0 = tmp;
		}

		max = float3( x0, y0, max.z );
		min = float3( x1, y1, min.z );

		uint c0 = Color565ToPacked565( ColorToColor565_Rounded( max ) );
		uint c1 = Color565ToPacked565( ColorToColor565_Rounded( min ) );

		// Always using the 4 color palette
		if ( c0 < c1 ) 
		{ 
			uint tmp = c0; 
			c0 = c1; 
			c1 = tmp;
		}

		BlockEndpoints be;
		be.e0_565 = c0;
		be.e1_565 = c1;
		gs_endpoints[blockNum] = be;

		return bSolidColorBlock;
	}

	void OptimizeBlockEndpoints( uint blockNum )
	{
		float alpha2_sum = 0.0f;
		float beta2_sum = 0.0f;
		float alphabeta_sum = 0.0f;
		float3 alphax_sum = 0;
		float3 betax_sum = 0;

		for ( uint i = 0; i < 16; i++ )
		{
			float3 color = UnpackInputRGB( blockNum, i ) * (1.f / 255.f);
			const uint bits = gs_color_indices[blockNum] >> (2 * i);

			float beta = float( bits & 1 );
			if ( bits & 2 ) beta = (1 + beta) / 3.0f;
			float alpha = 1.0f - beta;

			alpha2_sum += alpha * alpha;
			beta2_sum += beta * beta;
			alphabeta_sum += alpha * beta;
			alphax_sum += alpha * color;
			betax_sum += beta * color;
		}

		float denom = alpha2_sum * beta2_sum - alphabeta_sum * alphabeta_sum;
		if ( abs( denom ) > 1e-4 )
		{
			float factor = 1.0f / denom;

			float3 c0 = saturate( (alphax_sum * beta2_sum - betax_sum * alphabeta_sum) * factor );
			float3 c1 = saturate( (betax_sum * alpha2_sum - alphax_sum * alphabeta_sum) * factor );

			uint e0 = Color565ToPacked565( ColorToColor565_Rounded( c0 ) );
			uint e1 = Color565ToPacked565( ColorToColor565_Rounded( c1 ) );

			if ( e0 != e1 )
			{
				// Always using the 4 color palette
				if ( e0 < e1 )
				{
					uint tmp = e0;
					e0 = e1;
					e1 = tmp;
				}

				BlockEndpoints be;
				be.e0_565 = e0;
				be.e1_565 = e1;
				gs_endpoints[blockNum] = be;
			}
		}
	}

	//
	// Calculate the color index directly from the results of comparing the sums of
	// absolute differences between the texel color and the four available colors
	// See: "Real-Time DXT Compression" by J.M.P. van Waveren, May 20th 2006
	// http://mrelusive.com/publications/papers/Real-Time-Dxt-Compression.pdf
	//
	float ComputeColorIndex( uint blockNum, uint blockThread, float3 rgbInput )
	{
		BlockPalette bp = EvaluateBlockPalette( gs_endpoints[ blockNum ] );

		float3 err0 = bp.p[0] - rgbInput;
		float3 err1 = bp.p[1] - rgbInput;
		float3 err2 = bp.p[2] - rgbInput;
		float3 err3 = bp.p[3] - rgbInput;

		#if ( D_PERCEPTUAL_ERROR_METRIC != 0 )
			float3 errorWeights = float3( 0.3, 0.6, 0.15 );	 // tweaked to measure blue a little more than straight luminance
		#else
			float3 errorWeights = float3( 1, 1, 1 );
		#endif

		// Always using squared error works best
		float d0 = dot( err0*err0, errorWeights );
		float d1 = dot( err1*err1, errorWeights );
		float d2 = dot( err2*err2, errorWeights );
		float d3 = dot( err3*err3, errorWeights );

		uint b0 = d0 > d3;
		uint b1 = d1 > d2;
		uint b2 = d0 > d2;
		uint b3 = d1 > d3;
		uint b4 = d2 > d3;

		uint x0 = b1 & b2;
		uint x1 = b0 & b3;
		uint x2 = b0 & b4;

		uint bits = x2 | ( ( x0 | x1 ) << 1 );

		InterlockedOr( gs_color_indices[blockNum], bits << ( blockThread << 1 ) );

		return min( min( d0, d1 ), min( d2, d3 ) );
	}

	//
	// Determine bias and emit alpha index
	// Given the choice of gs_maxAlpha / gs_minAlpha, these indices are optimal:
	// http://fgiesen.wordpress.com/2009/12/15/dxt5-alpha-block-index-determination/
	//
	void ComputeAlphaIndex( uint blockNum, uint blockThread, uint alpha )
	{
		uint dist = gs_maxAlpha[blockNum] - gs_minAlpha[blockNum];
		uint dist4 = dist * 4;
		uint dist2 = dist * 2;
		int bias = ( dist < 8 ) ? ( dist - 1 ) : ( dist / 2 + 2 );
		bias -= gs_minAlpha[blockNum] * 7;

		int a = alpha * 7 + bias;

		// Select index.  This is a "linear scale" lerp factor between 0 (val = min) and 7 (val = max).
		int t = ( a >= ( int )dist4 ) ? -1 : 0;
		int ind = t & 4;
		a -= dist4 & t;

		t = ( a >= ( int )dist2 ) ? -1 : 0;
		ind += t & 2;
		a -= dist2 & t;
		ind += ( a >= ( int )dist );

		// Turn linear scale into DXT index (0/1 are extremal pts)
		ind = -ind & 7;
		ind ^= ( 2 > ind );

		if ( blockThread == 5 )
		{
			InterlockedOr( gs_alpha_indices[blockNum][0], uint( ( ind & 1 ) << 15 ) );	// Thread 5 is a weird case
			InterlockedOr( gs_alpha_indices[blockNum][1], uint( ( ind & 7 ) >> 1 ) );	// OR bits into both index banks
		}
		else
		{
			uint bank = ( blockThread < 5 ) ? 0 : 1;
			uint shiftBits = ( blockThread < 5 ) ? ( blockThread * 3 ) : ( ( ( blockThread - 6 ) * 3 ) + 2 );
			InterlockedOr( gs_alpha_indices[blockNum][bank], uint( ( ind & 7 ) << shiftBits ) );
		}
	 }

#if D_DISPATCH_MODE == DISPATCH_2x1
	[numthreads( 8, 4, 1)]
#elif D_DISPATCH_MODE == DISPATCH_2x2
	[numthreads( 8, 8, 1)]
#else // DISPATCH_TIP
	[numthreads( 64, 1, 1)]
#endif
	[shader( "compute" )] void MainCs( uint threadID : SV_GroupIndex, uint3 groupID : SV_GroupID, uint3 inputTexelAddress : SV_DispatchThreadID )
	{
		// In order to occupy all compute units (32 on NVIDIA and 64 on AMD), we run 8x4 or 8x8 threadgroups, assigning one thread per input texel.
		// For NVIDIA, this covers two DXT compression blocks, on AMD it covers four.  The threadIDs (0..31 or 0..63) linearly span all of input
		// blocks as shown below.  From threadID, we derive blockNum and blockThread, which are layed out like this:
		//
		//   threadID:                              blockNum:                            blockThread:
		//                  +                                     +                                     +
		//    0   1   2   3 |  4   5   6   7        0   0   0   0 | 1   1   1   1         0   1   2   3 |  0   1   2   3 
		//    8   9  10  11 | 12  13  14  15        0   0   0   0 | 1   1   1   1         4   5   6   7 |  4   5   6   7 
		//   16  17  18  19 | 20  21  22  23        0   0   0   0 | 1   1   1   1         8   9  10  11 |  8   9  10  11 
		//   24  25  26  27 | 28  29  30  31        0   0   0   0 | 1   1   1   1        12  13  14  15 | 12  13  14  15 
		// +----------------+----------------+    +---------------+---------------+    +----------------+----------------+  ^--- NVIDIA only does upper two blocks (blockNum will only be 0 or 1)
		//   32  33  34  35 | 36  37  38  39        2   2   2   2 | 3   3   3   3         0   1   2   3 |  0   1   2   3 
		//   40  41  42  43 | 44  45  46  47        2   2   2   2 | 3   3   3   3         4   5   6   7 |  4   5   6   7 
		//   48  49  50  51 | 52  53  54  55        2   2   2   2 | 3   3   3   3         8   9  10  11 |  8   9  10  11 
		//   56  57  58  59 | 60  61  62  63        2   2   2   2 | 3   3   3   3        12  13  14  15 | 12  13  14  15 
		//                  +                                     +                                     +

		uint blockNum = ( ( threadID & 0x04 ) >> 2 ) | ( ( threadID & 0x20 ) >> 4 );
		uint blockThread = threadID - ( ( threadID & 0x18 ) >> 1 ) - ( ( blockNum & 0x01 ) << 2 ) - ( ( blockNum & 0x02 ) << 4 );

		if ( blockThread == 0 )										// -----  blockThread zero performs initialization
		{
			InitGroupSharedStorage( blockNum );
		}

		uint4 rgbaInput = uint4( 0xFF, 0x00, 0x00, 0xFF );
		int nMipLevel = g_nMipLevel;
		int u = inputTexelAddress.x;
		int v = inputTexelAddress.y;

		//
		// For DISPATCH_TIP mode, we run just one wave of 64 threads, processing three DXT5 blocks
		// The blocks are mapped to blockNum and blockThread as above, though we ignore inputTexelAddress and generate uv below
		//   blockNum 0 will process g_nMipLevel (assumed to be 4x4)
		//   blockNum 1 will process g_nMipLevel + 1 (assumed to be 2x2)
		//   blockNum 2 will process g_nMipLevel + 2 (assumed to be 1x1)
		//   blockNum 3 redundantly processes g_nMipLevel + 2 but generates no output
		//
		#if ( D_DISPATCH_MODE == DISPATCH_TIP )
		{
			nMipLevel += clamp( blockNum, 0, 2 );

			if ( blockNum == 0 )						// 4x4 mip level
			{
				u = blockThread % 4;
				v = blockThread / 4;
			}
			else if ( blockNum == 1 )					// 2x2 mip level
			{
				u = ( blockThread % 4 ) / 2;
				v = blockThread / 8;
			}
			else										// 1x1 mip level
			{
				u = v = 0;
			}
		}
		#endif

		// Load texel into groupshared memory
		float4 srcTexel = g_tSrc.Load( int3( u, v, nMipLevel ) );
		rgbaInput = uint4( saturate( srcTexel ) * 255.0f );
		PackInputRGB( blockNum, blockThread, rgbaInput.xyz );

		GroupMemoryBarrierWithGroupSync();							// -----  Colors loaded

		// Track min/max alpha
		InterlockedMin( gs_minAlpha[blockNum], rgbaInput.w );
		InterlockedMax( gs_maxAlpha[blockNum], rgbaInput.w );

		if ( blockThread == 0 )										// -----  blockThread zero begins block-wide computation
		{
			gs_solidColor[blockNum] = ComputeBlockEndpoints( blockNum );
		}

		GroupMemoryBarrierWithGroupSync();							// -----  Min/Max Alpha computed and blockThread 0 has completed per-block computation into groupshared storage

		float baseError = ComputeColorIndex( blockNum, blockThread, srcTexel.rgb );
		ComputeAlphaIndex( blockNum, blockThread, rgbaInput.w );

		GroupMemoryBarrierWithGroupSync();							// -----  Threads have computed their individual indices

		#if ( D_ENDPOINT_OPTIMIZATION )
		{
			BlockEndpoints prevEndpoints = gs_endpoints[blockNum];
			uint prevIndices = gs_color_indices[blockNum];

			if ( !gs_solidColor[blockNum] )
			{
				InterlockedAdd( gs_blockError[blockNum][0], uint( baseError * 10000.f ) );

				if ( blockThread == 0 )
				{
					OptimizeBlockEndpoints( blockNum );
				}
			}

			GroupMemoryBarrierWithGroupSync();

			if ( !gs_solidColor[blockNum] )
			{
				float optimizedError = ComputeColorIndex( blockNum, blockThread, srcTexel.rgb );
				InterlockedAdd( gs_blockError[blockNum][1], uint( optimizedError * 10000.f ) );
			}

			GroupMemoryBarrierWithGroupSync();

			if ( blockThread == 0 && !gs_solidColor[blockNum] )
			{
				if ( gs_blockError[blockNum][1] > gs_blockError[blockNum][0] ) 
				{
					gs_endpoints[blockNum] = prevEndpoints;
					gs_color_indices[blockNum] = prevIndices;
				}

				/* show black where we skip endpoint optimization
				if ( bSolidColorBlock )
				{
					gs_endpoints[blockNum] = {};
					gs_color_indices[blockNum] = 0;
				}
				*/
			}
		}
		#endif

		if ( blockThread == 0 )										// -----  blockThread 0 writes output
		{
			uint4 outputBlock;
			outputBlock.x = gs_maxAlpha[blockNum] | ( gs_minAlpha[blockNum] << 8 ) | ( gs_alpha_indices[blockNum][0] << 16 );
			outputBlock.y = gs_alpha_indices[blockNum][1];
			outputBlock.z = ( gs_endpoints[blockNum].e1_565 << 16 ) | gs_endpoints[blockNum].e0_565;
			outputBlock.w = gs_color_indices[blockNum];

			#if ( D_DISPATCH_MODE == DISPATCH_TIP )
			{
				if ( blockNum == 0 )
				{
					OutputDXT5Texture_4x4[ uint2( 0, 0 ) ] = outputBlock;
				}
				else if ( blockNum == 1 )
				{
					OutputDXT5Texture_2x2[ uint2( 0, 0 ) ] = outputBlock;
				}
				else if ( blockNum == 2 )
				{
					OutputDXT5Texture_1x1[ uint2( 0, 0 ) ] = outputBlock;
				}
				// blockNum == 3 generates no output by design
			}
			#else
			{
				OutputDXT5Texture[groupID.xy * uint2( 2, NUM_BLOCKS / 2 ) + uint2( blockNum & 0x01, ( blockNum & 0x02 ) >> 1 )] = outputBlock;
			}
			#endif
		}
	}

#endif // CS


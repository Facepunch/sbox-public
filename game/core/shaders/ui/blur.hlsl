#ifndef UI_BLUR_HLSL
#define UI_BLUR_HLSL

// Gaussian texture blur --------------------------------------------------------------------------------------------------------------------------------
//
// Reads the layer's mip chain (downsample_cs GaussianBlurAlpha: a centred 2x2 box, then a 9-tap binomial, per level),
// which is itself a gaussian: sigma_chain(m) = sqrt( 2.75 * ( 4^m - 1 ) + 4^m / 6 ) texels of mip 0, the last term
// being the bilinear read of level m. The deepest level whose chain stays within 90% of sigma carries most of the
// blur; a small grid of taps on it covers the rest. Taps no further apart than 1.5 chain sigmas keep a thin line
// smooth, so the grid grows from 5 to 9 taps an axis just past a level boundary and shrinks again as the chain
// catches up. Below the first level, mip 0 gets an exact gaussian, two texels per bilinear fetch.
// Sigma is in texels of mip 0: filter: blur() passes the radius, filter: drop-shadow() half the blur radius.

#define GAUSSIAN_BLUR_CHAIN_SHARE 0.9

float GaussianBlurChainSigma( int mip )
{
	float texels = exp2( 2.0 * mip );
	return sqrt( 2.75 * ( texels - 1.0 ) + texels / 6.0 );
}

// Exact gaussian on mip 0 texels out to three sigma, adjacent pairs merged into one bilinear fetch
float4 GaussianBlurTexels( Texture2D tex, SamplerState s, float2 uv, float sigma, float2 invTexDim )
{
	int radius = min( (int)ceil( 3.0 * sigma ), 10 );
	float k = -0.5 / ( sigma * sigma );

	float4 sum = 0;
	float weight = 0;

	[loop]
	for ( int y = -radius; y <= radius; y += 2 )
	{
		float wy0 = exp( y * y * k );
		float wy1 = y < radius ? exp( ( y + 1 ) * ( y + 1 ) * k ) : 0.0;
		float wy = wy0 + wy1;
		float oy = y + wy1 / wy;

		[loop]
		for ( int x = -radius; x <= radius; x += 2 )
		{
			float wx0 = exp( x * x * k );
			float wx1 = x < radius ? exp( ( x + 1 ) * ( x + 1 ) * k ) : 0.0;
			float wx = wx0 + wx1;
			float ox = x + wx1 / wx;

			sum += tex.SampleLevel( s, uv + float2( ox, oy ) * invTexDim, 0 ) * ( wx * wy );
			weight += wx * wy;
		}
	}

	return sum / weight;
}

float4 GaussianBlurTexture( Texture2D tex, SamplerState s, float2 uv, float sigma, float2 invTexDim )
{
	// The layer can carry a mip chain, and an implicit-LOD read of a scaled-down panel would pick a blurred mip
	if ( sigma <= 0.05 )
		return tex.SampleLevel( s, uv, 0 );

	uint width, height, levels;
	tex.GetDimensions( 0, width, height, levels );

	int mip = 0;
	[loop]
	while ( mip + 1 < (int)levels && GaussianBlurChainSigma( mip + 1 ) <= GAUSSIAN_BLUR_CHAIN_SHARE * sigma )
		mip++;

	if ( mip == 0 )
		return GaussianBlurTexels( tex, s, uv, sigma, invTexDim );

	float chain = GaussianBlurChainSigma( mip );
	float tapSigma = sqrt( max( sigma * sigma - chain * chain, 0.25 ) );
	int taps = clamp( (int)ceil( 4.0 * tapSigma / chain ) + 1, 5, 9 ) | 1;

	float step = 6.0 * tapSigma / ( taps - 1 );
	float start = -3.0 * tapSigma;
	float k = -0.5 / ( tapSigma * tapSigma );

	float4 sum = 0;
	float weight = 0;

	[loop]
	for ( int y = 0; y < taps; y++ )
	{
		float oy = start + y * step;
		float wy = exp( oy * oy * k );

		[loop]
		for ( int x = 0; x < taps; x++ )
		{
			float ox = start + x * step;
			float w = wy * exp( ox * ox * k );
			sum += tex.SampleLevel( s, uv + float2( ox, oy ) * invTexDim, mip ) * w;
			weight += w;
		}
	}

	return sum / weight;
}

#endif // UI_BLUR_HLSL

#ifndef UI_BLUR_HLSL
#define UI_BLUR_HLSL

// Gaussian texture blur --------------------------------------------------------------------------------------------------------------------------------
//
// One pass, no intermediate target: a grid of taps out to three sigma, gaussian weighted. The taps spread out as
// sigma grows so the cost is fixed; that doesn't alias because the gaussian is smooth - its spectrum is gone long
// before the tap spacing matters. Sigma is in texels of the source. Used for filter: blur() (sigma = the radius) and
// filter: drop-shadow() (sigma = half the blur radius), the same definitions the web uses.

#define GAUSSIAN_BLUR_TAPS 15

float4 GaussianBlurTexture( Texture2D tex, SamplerState s, float2 uv, float sigma, float2 invTexDim )
{
	if ( sigma <= 0.05 )
		return tex.Sample( s, uv );

	// Taps never closer than a texel; below that the grid just reaches past three sigma
	float step = max( 6.0 * sigma / ( GAUSSIAN_BLUR_TAPS - 1 ), 1.0 );
	float start = -step * ( GAUSSIAN_BLUR_TAPS - 1 ) * 0.5;
	float k = -0.5 / ( sigma * sigma );

	float4 sum = 0;
	float weight = 0;

	[unroll]
	for ( int y = 0; y < GAUSSIAN_BLUR_TAPS; y++ )
	{
		float oy = start + y * step;
		float wy = exp( oy * oy * k );

		[unroll]
		for ( int x = 0; x < GAUSSIAN_BLUR_TAPS; x++ )
		{
			float ox = start + x * step;
			float w = wy * exp( ox * ox * k );
			sum += tex.Sample( s, uv + float2( ox, oy ) * invTexDim ) * w;
			weight += w;
		}
	}

	return sum / weight;
}

#endif // UI_BLUR_HLSL

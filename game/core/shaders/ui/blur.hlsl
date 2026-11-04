#ifndef UI_BLUR_HLSL
#define UI_BLUR_HLSL

// Gaussian texture blur --------------------------------------------------------------------------------------------------------------------------------
//
// The panel layer is padded by the blur's reach and carries a gaussian mip chain (Panel.Layer.cs), so past the first
// level the blur is already sitting in the pyramid and a trilinear fetch is the whole job. downsample_cs builds a
// level with a 2x2 box on the source grid and a 9-tap binomial on the destination - 1/4 + 2 of the level's own texels
// squared in variance - and the bilinear magnification adds another 1/6, so level L reads as
// sigma^2 = 35/12 * 4^L - 11/4 texels of mip 0. Sigma is in texels of mip 0: filter: blur() passes the radius,
// drop-shadow() half the blur radius, as the web does.

float4 GaussianBlurTexture( Texture2D tex, SamplerState s, float2 uv, float sigma, float2 invTexDim )
{
	if ( sigma <= 0.05 ) return tex.SampleLevel( s, uv, 0 );

	float var = sigma * sigma;
	float lod = 0.5 * log2( max( ( var + 11.0 / 4.0 ) * ( 12.0 / 35.0 ), 1.0 ) );
	if ( lod >= 1.0 )
		return tex.SampleLevel( s, uv, lod );

	// Below level 1 there's nothing to blend with - mip 0 is sharp, and lerping it against mip 1 ghosts rather than
	// blurs. So split the variance: four taps on the diagonals take half, and the fetch drops to the level carrying
	// the other half, which between levels 0 and 1 is linear in the lod (1/6 at level 0, 8.75 more at level 1)
	lod = max( ( 0.5 * var - 1.0 / 6.0 ) / 8.75, 0.0 );
	float2 d = sigma * 0.7071 * invTexDim;

	return 0.25 * ( tex.SampleLevel( s, uv + float2( -d.x, -d.y ), lod ) + tex.SampleLevel( s, uv + float2( d.x, -d.y ), lod )
	              + tex.SampleLevel( s, uv + float2( -d.x,  d.y ), lod ) + tex.SampleLevel( s, uv + float2( d.x,  d.y ), lod ) );
}

#endif // UI_BLUR_HLSL

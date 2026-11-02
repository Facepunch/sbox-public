#ifndef WATER_DETAIL_HLSL
#define WATER_DETAIL_HLSL

// Linear tangent-space surface normals. Blue is normal Z, not a height channel.
CreateInputTexture2D( DetailNormal, Linear, 8, "", "_normal", "Detail,20/10", Default3( 0.5, 0.5, 1 ) );
Texture2D g_tDetailNormal < Channel( RGB, Box( DetailNormal ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
// Width of the largest layer in world inches, its animation speed, and the strength of the normals in lighting
// and distortion. They don't displace the geometry. The WaterBody reads them too, with the same limits, and
// animates the layers with DetailTime, its own clock keeping the phase when the speed changes.
float DetailScale < Default( 376.988 ); Range( 1, 4096 ); UiGroup( "Detail,20/20" ); >;
float DetailSpeed < Default( 0.607 ); Range( 0, 10 ); UiGroup( "Detail,20/30" ); >;
float DetailStrength < Default( 0.156 ); Range( 0, 4 ); UiGroup( "Detail,20/40" ); >;
float DetailTime < Attribute( "DetailTime" ); Default( 0 ); >;
SamplerState TextureFiltering < Filter( ANISOTROPIC ); MaxAniso( 8 ); >;
// Fixed spectral recipe. The body chooses scale, speed and strength, not layer mechanics.
static const float DetailWarp = 0.35;
static const float DetailPhaseScale = 0.4;
static const float3 DetailWeights = { 0.6, 0.35, 0.15 };
static const int DetailLayers = 3;
static const float2 DetailDirections[3] =
{
	float2( 1, 0 ),
	float2( -0.5, 0.8660254 ),
	float2( -0.5, -0.8660254 )
};

float2 WaterNormalTexel( float2 uv, float2 dx, float2 dy )
{
	return g_tDetailNormal.SampleGrad( TextureFiltering, uv, dx, dy ).rg - 0.5;
}

// Each scale blends two equally weighted normals moving against each other.
// The three fixed axes keep the pattern crossing without an authored travel heading.
// Smaller detail layers scroll more slowly in world space (sqrt scale).
float2 WaterBumpSlope( float2 point )
{
	float strength = clamp( DetailStrength, 0, 4 );
	if ( !( strength > 0 ) )
		return 0;

	float2 dx = ddx( point );
	float2 dy = ddy( point );
	float scale = clamp( DetailScale, 1, 4096 );
	float2 phaseOffset = 0;
	float2 slope = 0;
	float totalWeight = 0;

	// Keep the existing six texture reads: two opposing samples for each band.
	[unroll]
	for ( int layer = 0; layer < DetailLayers; layer++ )
	{
		float weight = DetailWeights[layer];
		float2 uv = (point + phaseOffset) / scale;
		float2 travel = DetailDirections[layer] * DetailTime / sqrt( scale );
		float2 first = WaterNormalTexel( uv + travel, dx / scale, dy / scale );
		// Keep distortion small enough to avoid folding the dense texture features.
		float2 warp = first * (DetailWarp * 0.1 * weight);
		// Separate the phases so the two samples do not coincide at time zero.
		float2 second = WaterNormalTexel( uv - travel + float2( 0.37, 0.61 ) + warp, dx / scale, dy / scale );
		slope += (first + second) * weight;
		phaseOffset += warp * scale * weight * DetailPhaseScale;
		totalWeight += weight;
		scale *= DetailPhaseScale;
	}

	return slope * (strength * 0.3) / max( totalWeight, 0.001 );
}

#endif

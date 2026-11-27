#ifndef WATER_WAVES_HLSL
#define WATER_WAVES_HLSL

// World-space axes and Origin are declared by the including water shader.
float WaveTime < Attribute( "WaveTime" ); >;

// Four Gerstner bands: xy direction, z wavenumber, w angular frequency. WaterBody.SampleSurface
// inverts their horizontal displacement on the CPU for matching gameplay heights and normals.
float WaveHeight < Attribute( "WaveHeight" ); Default( 0 ); >;
float WaveSteepness < Attribute( "WaveSteepness" ); Default( 0.5 ); >;
float4 Wave0 < Attribute( "Wave0" ); >;
float4 Wave1 < Attribute( "Wave1" ); >;
float4 Wave2 < Attribute( "Wave2" ); >;
float4 Wave3 < Attribute( "Wave3" ); >;

float3 WaterLocal( float3 position )
{
	float3 p = position - Origin;
	return float3( dot( p, AxisX ), dot( p, AxisY ), dot( p, SurfaceNormal ) );
}

float3 WaterWorld( float3 position )
{
	return Origin + AxisX * position.x + AxisY * position.y + SurfaceNormal * position.z;
}

// Band fading by sample spacing, as wavenumber times spacing. Pixels keep a band at full strength
// up to about twelve samples per wavelength so normals don't shimmer. Clipmap vertices stay fixed
// in the world, so the mesh keeps a band down to four vertices per wavelength and drops it at two.
#define WATER_PIXEL_FADE float2( 0.5, 3 )
#define WATER_MESH_FADE float2( 1.5708, 3.1416 )

// Displacement and height gradient at a rest position, in world-sized surface coordinates.
// Fade unresolved bands in every displacement axis. Treat the local footprint weight as
// constant when differentiating, so filtering does not introduce an extra camera-dependent tilt.
void WaterWaves( float2 rest, float footprint, float2 fade, out float3 offset, out float2 slope, out float3 jacobian )
{
	float4 waves[4] = { Wave0, Wave1, Wave2, Wave3 };
	float weights[4] = { 0.5, 0.27, 0.15, 0.08 };
	offset = 0;
	slope = 0;
	jacobian = float3( 1, 0, 1 );
	if ( WaveHeight <= 0 )
		return;

	// Steepness controls compression independently of height, including low, long waves.
	// Band weights share a total budget below one to keep the Jacobian invertible.
	float steepness = saturate( WaveSteepness );
	float compressionLimit = 0.99;
	float2 tangentX = float2( 1, 0 );
	float2 tangentY = float2( 0, 1 );

	[unroll]
	for ( int i = 0; i < 4; i++ )
	{
		float4 w = waves[i];
		float resolved = 1 - smoothstep( fade.x, fade.y, footprint * w.z );
		float baseAmplitude = WaveHeight * weights[i];
		float amplitude = baseAmplitude * resolved;
		float horizontal = steepness * compressionLimit * weights[i] / max( w.z, 0.0001 ) * resolved;
		float phase = w.z * dot( w.xy, rest ) - w.w * WaveTime + i * 1.7;
		float s = sin( phase );
		float c = cos( phase );
		offset.xy += w.xy * (horizontal * c);
		offset.z += amplitude * s;
		slope += w.xy * (w.z * amplitude * c);
		float compression = w.z * horizontal * s;
		tangentX -= w.xy * (compression * w.x);
		tangentY -= w.xy * (compression * w.y);
	}

	// Transform the rest-space height derivative by the inverse transpose of the horizontal
	// Jacobian. This retains cross-direction terms that a scalar crest correction loses.
	float determinant = tangentX.x * tangentY.y - tangentY.x * tangentX.y;
	jacobian = float3( tangentX.x, tangentX.y, tangentY.y );
	slope = float2(
		tangentY.y * slope.x - tangentX.y * slope.y,
		tangentX.x * slope.y - tangentY.x * slope.x ) / max( determinant, 0.000001 );
}

// Mesh sampling: the footprint is the spacing between vertices.
void WaterWaves( float2 rest, float footprint, out float3 offset, out float2 slope, out float3 jacobian )
{
	WaterWaves( rest, footprint, WATER_MESH_FADE, offset, slope, jacobian );
}

void WaterWaves( float2 rest, float footprint, out float3 offset, out float2 slope )
{
	float3 jacobian;
	WaterWaves( rest, footprint, offset, slope, jacobian );
}

// Gameplay-aligned queries use the complete unfiltered wave system.
void WaterWaves( float2 rest, out float3 offset, out float2 slope, out float3 jacobian )
{
	WaterWaves( rest, 0, offset, slope, jacobian );
}

void WaterWaves( float2 rest, out float3 offset, out float2 slope )
{
	WaterWaves( rest, 0, offset, slope );
}

// Invert Gerstner horizontal displacement for a world-space XY query.
// The bounded Newton solve follows the CPU query and also anchors finite pool edges.
float2 WaterRestPoint( float2 point, float footprint, out float3 offset, out float2 slope )
{
	float2 rest = point;
	float3 jacobian;
	WaterWaves( rest, footprint, offset, slope, jacobian );
	[loop]
	for ( int iteration = 0; iteration < 8; iteration++ )
	{
		float2 error = rest + offset.xy - point;
		float errorSquared = dot( error, error );
		if ( errorSquared < 0.000001 )
			break;

		float determinant = max( jacobian.x * jacobian.z - jacobian.y * jacobian.y, 0.000001 );
		float2 delta = float2( jacobian.z * error.x - jacobian.y * error.y,
			jacobian.x * error.y - jacobian.y * error.x ) / determinant;
		bool advanced = false;
		[loop]
		for ( int attempt = 0; attempt < 4; attempt++ )
		{
			float2 candidate = rest - delta;
			float3 candidateOffset, candidateJacobian;
			float2 candidateSlope;
			WaterWaves( candidate, footprint, candidateOffset, candidateSlope, candidateJacobian );
			float2 candidateError = candidate + candidateOffset.xy - point;
			if ( dot( candidateError, candidateError ) < errorSquared )
			{
				rest = candidate;
				offset = candidateOffset;
				jacobian = candidateJacobian;
				slope = candidateSlope;
				advanced = true;
				break;
			}

			delta *= 0.5;
		}

		if ( !advanced )
			break;
	}

	return rest;
}

float2 WaterRestPoint( float2 point, out float3 offset, out float2 slope )
{
	return WaterRestPoint( point, 0, offset, slope );
}
// Residual bounds prevent an unresolved steep crest from erasing an exposed reflected object.
float WaterHeightLowerBound( float2 point, float footprint )
{
	float3 offset;
	float2 slope;
	float2 rest = WaterRestPoint( point, footprint, offset, slope );
	float heightGradientBound = WaveHeight * dot( float4( Wave0.z, Wave1.z, Wave2.z, Wave3.z ), float4( 0.5, 0.27, 0.15, 0.08 ) );
	float errorBound = length( rest + offset.xy - point ) * heightGradientBound / (1 - saturate( WaveSteepness ) * 0.99);
	return offset.z - errorBound;
}

float WaterHeightLowerBound( float2 point )
{
	return WaterHeightLowerBound( point, 0 );
}

#endif

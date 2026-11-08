#ifndef UNDERWATER_PARTICLES_HLSL
#define UNDERWATER_PARTICLES_HLSL

#include "common/classes/Depth.hlsl"
float3 Origin < Attribute( "Origin" ); >;
float3 AxisX < Attribute( "AxisX" ); Default3( 1, 0, 0 ); >;
float3 AxisY < Attribute( "AxisY" ); Default3( 0, 1, 0 ); >;
float3 SurfaceNormal < Attribute( "SurfaceNormal" ); Default3( 0, 0, 1 ); >;
float2 HalfSize < Attribute( "HalfSize" ); >;
#include "water/waves.hlsl"

float ParticleDensity < Attribute( "ParticleDensity" ); Default( 0 ); >;
float2 ParticleSizeRange < Attribute( "ParticleSizeRange" ); Default2( 0.325, 0.975 ); >;
float3 ParticleColor < Attribute( "ParticleColor" ); Default3( 0.55, 0.62, 0.58 ); >;
float ParticleTime < Attribute( "ParticleTime" ); >;
bool ParticleInfinite < Attribute( "ParticleInfinite" ); Default( 0 ); >;
float ParticleWaterDepth < Attribute( "ParticleWaterDepth" ); Default( 1024 ); >;
float3 ParticleFog < Attribute( "ParticleFog" ); Default3( 0, 128, 1 ); >;

// Integer hashing keeps each cell's speck fixed in world space, including negative coordinates.
float4 SedimentHash( int3 cell )
{
	uint seed = asuint( cell.x ) * 1597334677u ^ asuint( cell.y ) * 3812015801u ^ asuint( cell.z ) * 2798796415u;
	uint4 h = seed ^ uint4( 951274213u, 374761393u, 668265263u, 2246822519u );
	h = (h ^ (h >> 16)) * 2246822519u;
	h = (h ^ (h >> 13)) * 3266489917u;
	return float4( h ^ (h >> 16) ) * (1.0 / 4294967296.0);
}

// Different fixed orientations and offsets hide the traversal lattice in the combined field.
// Rodrigues rotation has an exact inverse (negate sine), preserving world-space distances.
float3 SedimentRotate( float3 position, float sine, float cosine )
{
	const float3 axis = float3( 0.26726124, 0.53452248, 0.80178373 );
	return position * cosine + cross( axis, position ) * sine + axis * dot( axis, position ) * (1 - cosine);
}

float SedimentLayer( float3 worldEye, float3 worldRay, float sceneDistance, float2 pixelFootprint, int layer )
{
	float sine, cosine;
	sincos( 0.73 + layer * 1.37, sine, cosine );
	float3 layerOffset = float3( 37.1, -53.7, 91.3 ) * layer;
	float3 eye = SedimentRotate( worldEye, sine, cosine ) + layerOffset;
	float3 ray = SedimentRotate( worldRay, sine, cosine );
	float limit = min( sceneDistance, 192 );

	// Traversal cells stay fixed. Only the independently seeded specks inside them wander.
	const float cellSize = 12;
	float3 gridEye = eye / cellSize;
	int3 cell = int3( floor( gridEye ) );
	float3 stepDirection = float3( ray.x >= 0 ? 1 : -1, ray.y >= 0 ? 1 : -1, ray.z >= 0 ? 1 : -1 );
	float3 stepTime = cellSize / max( abs( ray ), 0.000001 );
	float3 nextBoundary = float3( cell ) + step( 0, ray );
	float3 nextTime = (nextBoundary - gridEye) * stepDirection * stepTime;
	float entry = 0;
	float opacity = 0;

	// At most 31 cells intersect a 192-inch ray through a 12-inch grid.
	// Cost is bounded by visible distance, independent of the number of particles in the world.
	[loop]
	for ( int iteration = 0; iteration < 32 && entry < limit; iteration++ )
	{
		float4 random = SedimentHash( cell + int3( 113, 271, 467 ) * layer );
		if ( random.w < ParticleDensity / 32 )
		{
			// Independent phases AND frequencies on each axis give every speck its own
			// changing direction. There is no translation of the field or shared flow.
			float3 motion = SedimentHash( cell + int3( 719, 883, 991 ) + layer * 137 ).xyz;
			float3 phase = random.xyz * 6.283185;
			float3 wander = sin( ParticleTime * (0.12 + motion * 0.2) + phase ) * 0.23;
			wander += sin( ParticleTime * (0.31 + motion.zxy * 0.23) + phase.yzx ) * 0.02;
			// Keep the complete speck inside its cell, avoiding seams during traversal.
			float3 center = (float3( cell ) + 0.5 + wander) * cellSize;
			float3 relative = center - eye;
			float distance = dot( relative, ray );
			float radius = lerp( ParticleSizeRange.x, ParticleSizeRange.y, random.z ) * 0.5;
			float feather = max( distance * pixelFootprint.x + pixelFootprint.y, 0.02 );
			float extent = radius + feather;
			float miss = length( relative - ray * distance );
			if ( distance > 3 && distance < limit && miss < extent )
			{
				float3 worldCenter = SedimentRotate( center - layerOffset, -sine, cosine );
				float3 local = WaterLocal( worldCenter );
				bool inside = local.z >= -ParticleWaterDepth && local.z <= WaveHeight;
				inside = inside && (ParticleInfinite || all( abs( local.xy ) <= HalfSize ));
				// Only the rare pixels hitting a speck near the surface need a wave query.
				if ( inside && local.z > -WaveHeight )
					inside = local.z <= WaterHeightLowerBound( local.xy );

				if ( inside )
				{
					float coverage = 1 - smoothstep( max( radius - feather, 0 ), extent, miss );
					coverage *= min( 1, radius * radius / max( feather * feather, 0.0001 ) );
					float fade = smoothstep( 3, 12, distance ) * (1 - smoothstep( 144, 192, distance ));
					fade *= saturate( (sceneDistance - distance) / 2 );
					// Fade only the new specks; the scene already contains the water's fog.
					float fog = min( 1 - exp( -max( distance - ParticleFog.x, 0 ) / max( ParticleFog.y, 1 ) ), ParticleFog.z );
					float alpha = coverage * fade * (1 - fog) * 0.55;
					opacity += (1 - opacity) * alpha;
				}
			}
		}

		// Advance one cell along the ray. Tied corners are harmless zero-length cells.
		if ( nextTime.x <= nextTime.y && nextTime.x <= nextTime.z )
		{
			entry = nextTime.x;
			nextTime.x += stepTime.x;
			cell.x += int( stepDirection.x );
		}
		else if ( nextTime.y <= nextTime.z )
		{
			entry = nextTime.y;
			nextTime.y += stepTime.y;
			cell.y += int( stepDirection.y );
		}
		else
		{
			entry = nextTime.z;
			nextTime.z += stepTime.z;
			cell.z += int( stepDirection.z );
		}
	}

	return opacity;
}

float3 UnderwaterSediment( float3 color, float2 uv )
{
	if ( ParticleDensity <= 0 )
		return color;

	float3 eye = g_vCameraPositionWs;
	float3 scenePosition = Depth::GetWorldPosition( uv * g_vViewportSize );
	float3 ray = scenePosition - eye;
	float sceneDistance = length( ray );
	ray /= max( sceneDistance, 0.001 );
	// Parallel orthographic rays start on the camera plane at this pixel's XY.
	// Their physical pixel width stays constant with distance instead of shrinking to zero.
	bool orthographic = g_matViewToProjection[3].w != 0;
	if ( orthographic )
	{
		ray = normalize( g_vCameraDirWs.xyz );
		sceneDistance = max( dot( scenePosition - g_vCameraPositionWs, ray ), 0 );
		eye = scenePosition - ray * sceneDistance;
	}
	// Derivatives must be evaluated outside divergent traversal.
	float2 pixelFootprint = orthographic
		? float2( 0, max( length( ddx( eye ) ), length( ddy( eye ) ) ) )
		: float2( max( length( ddx( ray ) ), length( ddy( ray ) ) ), 0 );
	float opacity = 0;

	// Superimposed independent orientations remove aligned rows and empty grid corridors.
	// Each field contributes independently at the authored density.
	[loop]
	for ( int layer = 0; layer < 4; layer++ )
	{
		float alpha = SedimentLayer( eye, ray, sceneDistance, pixelFootprint, layer );
		opacity += (1 - opacity) * alpha;
	}

	return lerp( color, ParticleColor, opacity );
}

#endif

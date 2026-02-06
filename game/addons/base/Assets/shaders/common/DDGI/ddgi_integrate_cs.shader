//---------------------------------------------------------------------------------------------------------------------
HEADER
{
	DevShader = true;
	Description = "Integrate rasterized probe captures into DDGI volumes";
}

//---------------------------------------------------------------------------------------------------------------------
MODES
{
	Default();
}

//---------------------------------------------------------------------------------------------------------------------
FEATURES
{
}

//---------------------------------------------------------------------------------------------------------------------
COMMON
{
	#include "common.fxc"
	#include "math_general.fxc"
	#include "common_samplers.fxc"
	#include "common/DDGI/DDGI.hlsl"
	#include "common/Bindless.hlsl"
	#include "common/classes/Depth.hlsl"
	
	TextureCube SourceProbe < Attribute( "SourceProbe" ); >;
	TextureCube SourceDepth < Attribute( "SourceDepth" ); >;
	
	RWTexture3D<float4> IrradianceVolume < Attribute( "IrradianceVolume" ); >;
	RWTexture3D<float2> DistanceVolume < Attribute( "DistanceVolume" ); >;
	
	float MaxProbeDistance < Attribute( "MaxProbeDistance" ); Default( 1000.0f ); >;
	float EnergyLoss < Attribute( "EnergyLoss" ); Default( 2.0f ); >;
	
	int3 ProbeIndex < Attribute( "ProbeIndex" ); >;
	#define ProbeSampler g_sTrilinearClamp
	
	float3 FibonacciDirection( uint index, uint count )
	{
		const float goldenRatio = 1.61803398874989484820459;
		const float PI = 3.14159265358979323846264;
		float i = (index + 0.5f);
		float phi = 2.0f * PI * goldenRatio * i;
		float cosTheta = 1.0f - 2.0f * (i / count);
		float sinTheta = sqrt( saturate( 1.0f - cosTheta * cosTheta ) );
		return float3( cos( phi ) * sinTheta, sin( phi ) * sinTheta, cosTheta );
	}

	float GetDepthDistance( TextureCube depthTex, float3 direction )
	{
		float depth = depthTex.SampleLevel( ProbeSampler, direction, 0.0f ).r;
		depth = Depth::Normalize( depth );
		depth = Depth::Linearize( depth );
		
		// Convert to perpendicular distance
		float3 absDir = abs( direction );
		float maxComponent = max( absDir.x, max( absDir.y, absDir.z ) );
		
		// Scale by the ratio of ray length to its dominant axis projection
		float rayLengthFactor = length( direction ) / maxComponent;
		
		return depth * rayLengthFactor;
	}

	float3 SampleProbeIrradiance( TextureCube tex, float3 targetDirection )
	{
		const uint sampleCount = 1024;
		
		float3 result = 0.0f;
		float totalWeight = 0.0f;
		
		[loop]
		for ( uint i = 0; i < sampleCount; ++i )
		{
			float3 rayDirection = FibonacciDirection( i, sampleCount );
			float weight = max( 0.0f, dot( targetDirection, rayDirection ) );
			
			if ( weight > 0.0f )
			{
				float3 radiance = tex.SampleLevel( ProbeSampler, rayDirection, 0.0f ).rgb;
				radiance = pow( radiance, 1.0f / EnergyLoss );
				result += radiance * weight;
				totalWeight += weight;
			}
		}
		
		if ( totalWeight > 0.0f )
			result /= totalWeight;
		
		result = pow( result, EnergyLoss );
		return result;
	}

	float2 SampleProbeDistance( TextureCube depthTex, float3 targetDirection )
	{
		const uint sampleCount = 256;
		// Very tight cone - approximately 1 octahedral texel
		const float coneAngle = 0.04f;
		
		float weightedDistance = 0.0f;
		float weightedDistance2 = 0.0f;
		float totalWeight = 0.0f;
		
		// Track min distance for conservative depth estimation
		float minDistance = MaxProbeDistance;
		
		// Build orthonormal basis around target direction
		float3 N = normalize( targetDirection );
		float3 up = abs( N.z ) < 0.999f ? float3( 0, 0, 1 ) : float3( 1, 0, 0 );
		float3 T = normalize( cross( up, N ) );
		float3 B = cross( N, T );
		
		const float goldenRatio = 1.61803398874989484820459;
		const float PI = 3.14159265358979323846264;
		
		[loop]
		for ( uint i = 0; i < sampleCount; ++i )
		{
			// Fibonacci spiral within cone
			float fi = (i + 0.5f);
			float phi = 2.0f * PI * goldenRatio * fi;
			float r = sqrt( fi / sampleCount );
			
			float theta = r * coneAngle;
			float sinTheta = sin( theta );
			float cosTheta = cos( theta );
			
			// Local direction in tangent space
			float3 localDir = float3( cos( phi ) * sinTheta, sin( phi ) * sinTheta, cosTheta );
			
			// Transform to world space
			float3 rayDirection = normalize( T * localDir.x + B * localDir.y + N * localDir.z );
			
			// Weight by cosine falloff from center
			float weight = cosTheta * cosTheta; // Squared for tighter center focus
			
			float distance = GetDepthDistance( depthTex, rayDirection );
			distance = clamp( distance, 0.01f, MaxProbeDistance );
			
			minDistance = min( minDistance, distance );
			
			weightedDistance += distance * weight;
			weightedDistance2 += distance * distance * weight;
			totalWeight += weight;
		}
		
		if ( totalWeight > 0.0f )
		{
			float mean = weightedDistance / totalWeight;
			float meanSq = weightedDistance2 / totalWeight;
			float variance = max( 0.0f, meanSq - mean * mean );
			
			// Conservative depth: bias strongly toward minimum distance
			// This is the key to preventing light leaks - we'd rather have
			// slightly darker shadows than any light bleeding through
			float depthRange = mean - minDistance;
			float conservativeBias = saturate( depthRange / max( mean, 0.1f ) );
			
			// Blend 50% toward minimum when there's depth discontinuity
			mean = lerp( mean, minDistance, conservativeBias * 0.5f );
			
			// Also reduce variance at discontinuities to sharpen the shadow
			variance *= (1.0f - conservativeBias * 0.5f);
			
			return float2( mean, variance );
		}
		
		return float2( MaxProbeDistance, 0.0f );
	}


}

//---------------------------------------------------------------------------------------------------------------------
CS
{
	DynamicCombo( D_PASS, 0..1, Sys( ALL ) );

	#if D_PASS == 0
		#define DDGI_OCT_RESOLUTION DDGI_IRRADIANCE_OCT_RESOLUTION
	#else
		#define DDGI_OCT_RESOLUTION DDGI_DISTANCE_OCT_RESOLUTION
	#endif

	#define TILE_SIZE (DDGI_OCT_RESOLUTION + 2)

	groupshared float4 gs_ProbeData[TILE_SIZE][TILE_SIZE];

	// Integrate irradiance from cubemap for a given direction
	float4 IntegrateIrradiance( float3 direction )
	{
		float3 radiance = SampleProbeIrradiance( SourceProbe, direction );
		radiance = min( radiance, 65504.0f );
		return float4( radiance, 1.0f );
	}

	// Integrate distance moments from depth cubemap for a given direction
	float4 IntegrateDistance( float3 direction )
	{
		float2 distanceData = SampleProbeDistance( SourceDepth, direction );
		float mean = min( distanceData.x, 65504.0f );
		float variance = min( distanceData.y, 65504.0f );
		return float4( mean, variance, 0, 0 );
	}

	// Get the source texel for border pixels (octahedral wrap)
	uint2 GetBorderSourceTexel( uint2 localPos, uint octResolution, uint tileSize )
	{
		bool isLeft   = ( localPos.x == 0 );
		bool isRight  = ( localPos.x == tileSize - 1 );
		bool isBottom = ( localPos.y == 0 );
		bool isTop    = ( localPos.y == tileSize - 1 );

		bool isCorner = ( isLeft || isRight ) && ( isBottom || isTop );
		bool isRowTexel = !isLeft && !isRight;

		if ( isCorner )
		{
			// Corners: diagonally opposite interior corner
			return uint2( isRight ? 1 : octResolution, isTop ? 1 : octResolution );
		}
		else if ( isRowTexel )
		{
			// Top/bottom border: mirror X, step Y into interior
			return uint2( ( tileSize - 1 ) - localPos.x, localPos.y + ( isTop ? -1 : 1 ) );
		}
		else
		{
			// Left/right border: step X into interior, mirror Y
			return uint2( localPos.x + ( isRight ? -1 : 1 ), ( tileSize - 1 ) - localPos.y );
		}
	}

	[numthreads( TILE_SIZE, TILE_SIZE, 1 )]
	void MainCs( uint3 vGroupThreadId : SV_GroupThreadID )
	{
		uint2 localPos = vGroupThreadId.xy;
		uint3 probeIndex = (uint3)ProbeIndex;
		
		uint2 baseCoord = DDGI::BaseCoordinate( probeIndex.xy, DDGI_OCT_RESOLUTION );
		bool isInterior = all( localPos >= 1 && localPos <= DDGI_OCT_RESOLUTION );

		// Phase 1: Interior threads integrate, others initialize to zero
		gs_ProbeData[localPos.y][localPos.x] = 0;

		if ( isInterior )
		{
			uint2 interiorIdx = localPos - 1;
			float2 octCoord = DDGI::TexelToOctahedralCoord( interiorIdx, DDGI_OCT_RESOLUTION );
			float3 direction = DDGI::OctahedralDecode( octCoord );

			#if D_PASS == 0
				gs_ProbeData[localPos.y][localPos.x] = IntegrateIrradiance( direction );
			#else
				gs_ProbeData[localPos.y][localPos.x] = IntegrateDistance( direction );
			#endif
		}

		GroupMemoryBarrierWithGroupSync();

		// Phase 2: Resolve source texel and write to output
		uint2 srcPos = isInterior ? localPos : GetBorderSourceTexel( localPos, DDGI_OCT_RESOLUTION, TILE_SIZE );
		float4 data = gs_ProbeData[srcPos.y][srcPos.x];
		uint3 dstCoord = uint3( baseCoord + localPos, probeIndex.z );

		#if D_PASS == 0
			IrradianceVolume[dstCoord] = data;
		#else
			DistanceVolume[dstCoord] = data.xy;
		#endif
	}
}


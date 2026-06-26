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
		const uint sampleCount = 1024;

		const float probeDistanceExponent = 50.0f;

		// result = ( sum(dist * w), sum(dist^2 * w), sum(w) )
		float3 result = 0.0f;

		[loop]
		for ( uint i = 0; i < sampleCount; ++i )
		{
			float3 rayDirection = FibonacciDirection( i, sampleCount );

			float weight = pow( max( 0.0f, dot( targetDirection, rayDirection ) ), probeDistanceExponent );
			if ( weight <= 0.0f )
				continue;

			float distance = abs( GetDepthDistance( depthTex, rayDirection ) );
			distance = min( distance, MaxProbeDistance );

			result += float3( distance * weight, distance * distance * weight, weight );
		}

		if ( result.z > 1e-9f )
			result.xy /= result.z;
		else
			return float2( MaxProbeDistance, MaxProbeDistance * MaxProbeDistance );

		// Store the two distance moments: mean and mean of distance-squared.
		return result.xy;
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

	#define TILE_SIZE DDGI_OCT_RESOLUTION

	// Staging buffer for the whole tile so we can blend octahedral edges before writing out
	groupshared float4 g_TileResults[TILE_SIZE][TILE_SIZE];

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
		float mean = min( distanceData.x, 65504.0f );        // Mean distance
		float meanSquared = min( distanceData.y, 65504.0f ); // Mean of distance squared
		return float4( mean, meanSquared, 0, 0 );
	}

	[numthreads( TILE_SIZE, TILE_SIZE, 1 )]
	void MainCs( uint3 vGroupThreadId : SV_GroupThreadID )
	{
		uint2 localPos = vGroupThreadId.xy;
		uint3 probeIndex = (uint3)ProbeIndex;

		uint2 baseCoord = DDGI::BaseCoordinate( probeIndex.xy, DDGI_OCT_RESOLUTION );

		// Borderless: every texel maps directly to an octahedral direction
		float2 octCoord = DDGI::TexelToOctahedralCoord( localPos, DDGI_OCT_RESOLUTION );
		float3 direction = DDGI::OctahedralDecode( octCoord );

		#if D_PASS == 0
			float4 value = IntegrateIrradiance( direction );
		#else
			float4 value = IntegrateDistance( direction );
		#endif

		// Stage the result so neighbouring threads can read it while blending the seam
		g_TileResults[localPos.y][localPos.x] = value;

		GroupMemoryBarrierWithGroupSync();

		// The octahedral map is borderless, so the boundary texels are discontinuous across the
		// seam. Each edge texel is identified with a mirrored texel on the same edge (corners map
		// to the diagonally opposite corner). Blend the two 50/50 so the seam stays continuous.
		const uint N = DDGI_OCT_RESOLUTION;
		bool onX = ( localPos.x == 0 || localPos.x == N - 1 );
		bool onY = ( localPos.y == 0 || localPos.y == N - 1 );
		if ( onX || onY )
		{
			uint2 mirror = localPos;
			if ( onY ) mirror.x = ( N - 1 ) - localPos.x;
			if ( onX ) mirror.y = ( N - 1 ) - localPos.y;

			value = 0.5f * value + 0.5f * g_TileResults[mirror.y][mirror.x];
		}

		uint3 dstCoord = uint3( baseCoord + localPos, probeIndex.z );

		#if D_PASS == 0
			IrradianceVolume[dstCoord] = value;
		#else
			DistanceVolume[dstCoord] = value.xy;
		#endif
	}
}


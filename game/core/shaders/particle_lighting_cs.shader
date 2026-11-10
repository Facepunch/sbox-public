// Lights lit sprites per froxel, 4x4x2 to a light cluster, in the clusters around them (ParticleLightingSystem)
HEADER
{
	DevShader = true;
	Description = "Lights particles per froxel in the light clusters around them";
}

MODES
{
	Default();
}

FEATURES
{
}

COMMON
{
	#define FORCE_BILINEAR_PCF_SHADOWS_ONLY
	#define COMPUTE_SHADER

	#include "system.fxc"
	#include "vr_common.fxc"
	#include "light_probe_volume.fxc"
	#include "common/lightbinner.hlsl"
	#include "common/DDGI/DDGI.hlsl"
}

CS
{
	#include "math_general.fxc"
	#include "vr_lighting.fxc"

	DynamicCombo( D_PASS, 0..1, Sys( ALL ) );

	#define FROXELS_PER_CLUSTER uint3( 4, 4, 2 )

	int3 g_vFirstCluster < Attribute( "ParticleLightingFirstCluster" ); >;
	int3 g_vClusterCount < Attribute( "ParticleLightingClusterCount" ); >;
	int2 g_vJitterOffset < Attribute( "ParticleLightingJitter" ); >;
	float4x4 g_matLastWorldToProjection < Attribute( "ParticleLightingLastWorldToProjection" ); >;

	Texture3D<float4> InLighting < Attribute( "ParticleLightingRaw" ); >;
	Texture3D<float4> InHistory < Attribute( "ParticleLightingHistory" ); >;
	RWTexture3D<float4> OutLighting < Attribute( "ParticleLightingOutRaw" ); >;
	RWTexture3D<float4> OutHistory < Attribute( "ParticleLightingOut" ); >;

	static const float HistoryWeight = 0.95f;

	float3 Froxels() { return uint3( ClusterCounts.xyz ) * FROXELS_PER_CLUSTER; }

	// Camera-relative position at a viewport uv and view depth
	float3 PositionAt( float2 uv, float depth )
	{
		float4 p = mul( g_matProjectionToWorld, float4( uv.x * 2.0f - 1.0f, 1.0f - uv.y * 2.0f, 0.5f, 1.0f ) );
		float3 onRay = p.xyz / p.w;

		// Orthographic: rays are parallel
		if ( g_matViewToProjection[3].w != 0 )
			return onRay + g_vCameraDirWs * ( depth - dot( onRay, g_vCameraDirWs ) );

		return onRay * ( depth / dot( onRay, g_vCameraDirWs ) );
	}

	float3 FroxelPosition( float3 froxel )
	{
		float3 uvw = froxel / Froxels();
		return PositionAt( uvw.xy, Cluster::SliceToDepth( uvw.z * ClusterCounts.z ) );
	}

	// 0 to 1, a different pattern each frame
	float3 BlueNoise( uint3 froxel, uint2 shift )
	{
		uint2 vSize = TextureDimensions2D( g_tBlueNoise, 0 );
		return g_tBlueNoise.Load( int3( ( froxel.xy + froxel.z * uint2( 37, 17 ) + uint2( g_vJitterOffset ) + shift ) % vSize, 0 ) ).rgb;
	}

	// As vr_lighting lights a sprite, with g_bNonDirectionalDiffuseLighting
	void AddLight( float3 vPositionWs, BinnedLight light, inout float3 vLight )
	{
		if ( !light.IsDiffuseEnabled() )
			return;

		float3 vPositionToLightRayWs = light.GetPosition() - vPositionWs;
		float flDistToLightSq = dot( vPositionToLightRayWs, vPositionToLightRayWs );
		float3 vPositionToLightDirWs = vPositionToLightRayWs * rsqrt( flDistToLightSq );
		float flConeToDirection = dot( vPositionToLightDirWs, -light.GetDirection() ) - light.SpotLightInnerOuterConeCosines.y;

		if ( ( flDistToLightSq > light.RadiusSquared ) | ( flConeToDirection <= 0.0 ) )
			return;

		float3 vLightColor = light.Color;
		float flSpotAtten = flConeToDirection * light.SpotLightInnerOuterConeCosines.z;

		[branch]
		if ( light.HasLightCookie() )
		{
			float4 vCookieSample = light.SampleLightCookie( light.GetCookieUV( vPositionWs ) );
			vLightColor *= vCookieSample.rgb * vCookieSample.a;
			flSpotAtten = 1.0f;
		}

		float flLightFalloff = CalculateDistanceFalloff( flDistToLightSq, light.LinearFalloff, light.QuadraticFalloff, light.FalloffBias, 1.0 );

		float flShadow = 1.0f;
		if ( light.HasDynamicShadows() )
		{
			if ( light.Type == LightType::LightTypePoint )
				flShadow = ProjectedShadowCube::GetVisibility( light.ShadowMapIndex, vPositionWs );
			else if ( light.Type == LightType::LightTypeSpot )
				flShadow = ProjectedShadow::GetVisibility( light.ShadowMapIndex, vPositionWs, float2( 0, 0 ) );
		}

		vLight += vLightColor * ( flLightFalloff * flSpotAtten * flShadow );
	}

	// As vr_lighting lights a sprite facing the camera
	float3 IndirectLight( float3 vPositionWs, float3 vToCameraWs, uint cluster )
	{
		float3 vAmbient = lerp( 1.0f, AmbientLightColor.rgb, AmbientLightColor.a );

		float3 vCube = 0.0f;
		float flDistAccumulated = 0.0f;
		ClusterRange envRange = Cluster::Query( ClusterItemType_EnvMap, cluster );
		ClusterForEach( envRange )
		{
			const uint index = envRange.Item;
			const float3 vCubePos = mul( float4( vPositionWs, 1.0 ), EnvMapWorldToLocal( index ) ).xyz;
			const float flEdgeFeathering = EnvMapFeathering( index );
			const float3 vIntersectA = min( vCubePos - EnvMapBoxMins( index ), EnvMapBoxMaxs( index ) - vCubePos );
			const float3 vIntersectB = min( vCubePos - EnvMapBoxMaxs( index ), EnvMapBoxMins( index ) - vCubePos );
			const float flDistance = min( min( vIntersectA.x, min( vIntersectA.y, vIntersectA.z ) ), min( -vIntersectB.x, -min( vIntersectB.y, vIntersectB.z ) ) ) + 0.5f;

			if ( flDistance + max( flEdgeFeathering, 0.0f ) < 0.0 )
				continue;

			const float3 vDirLocal = mul( float4( vToCameraWs, 0.0f ), EnvMapWorldToLocal( index ) ).xyz;
			vCube = lerp( vCube, SampleEnvironmentMapLevel( vDirLocal, 1.0f, index, EnvMapTexture( asDynamicUniform( UniformIndex( index ) ) ) ), 1.0 - flDistAccumulated );

			flDistAccumulated += RemapValClamped( flDistance, min( -flEdgeFeathering, 0.0f ), max( -flEdgeFeathering, 0.0f ), 0.0, 1.0 );
			if ( flDistAccumulated >= 1.0 )
				break;
		}

		float3 vIndirect = lerp( vCube, vAmbient, AmbientLightColor.w );

		if ( DDGI::IsEnabled() )
		{
			float4 ddgi = DDGI::EvaluateVolumes( vPositionWs, vToCameraWs, vToCameraWs );
			vIndirect = ddgi.rgb + ( 1.0f - ddgi.a ) * vIndirect;
		}

		return vIndirect;
	}

	// At a different point in the froxel each frame, for the denoise pass to average
	void LightPass( uint3 cluster, uint3 froxel )
	{
		float3 vRelativeWs = FroxelPosition( froxel + BlueNoise( froxel, 0 ) );
		float3 vPositionWs = vRelativeWs + g_vCameraPositionWs;

		float3 vLight = 0.0f;

		if ( g_DirectionalLightEnabled )
		{
			float flShadow = g_DirectionalLightCascadeCount > 0 ? DirectionalLightShadow::GetVisibility( vPositionWs, float4( 0, 0, 0, 0 ) ) : 1.0f;
			vLight += g_DirectionalLightColor.rgb * flShadow;
		}

		// The group is one cluster, so its lights are wave-uniform
		const uint flat = Cluster::Flatten( cluster );
		ClusterRange lightRange = Cluster::Query( ClusterItemType_Light, flat );
		ClusterForEach( lightRange )
			AddLight( vPositionWs, DynamicLightConstantByIndex( lightRange.Item ), vLight );

		vLight += IndirectLight( vPositionWs, normalize( -vRelativeWs ), flat );

		// Clamp to what R11G11B10F can hold
		OutLighting[froxel] = float4( clamp( vLight, 0.0f, 65000.0f ), 1.0f );
	}

	// The mean of this frame's light around the froxel, blended with last frame's clamped to the box of it, as the fog's
	// temporal antialiasing
	void DenoisePass( uint3 froxel )
	{
		const float3 vTexel = 1.0f / Froxels();
		const float3 vCentre = froxel + 0.5f;

		float3 vRelativeWs = FroxelPosition( vCentre );
		float4 vLastClip = mul( g_matLastWorldToProjection, float4( vRelativeWs + g_vCameraPositionWs, 1.0f ) );
		float flLastDepth = g_matViewToProjection[3].w != 0 ? dot( vRelativeWs, g_vCameraDirWs ) : vLastClip.w;
		float3 vLastUvw = float3( vLastClip.xy / vLastClip.w * float2( 0.5f, -0.5f ) + 0.5f, Cluster::DepthToSlice( clamp( flLastDepth, ClusterZParams.z, ClusterZParams.w ) ) * ClusterInvCounts.z );
		float3 vLast = InHistory.SampleLevel( g_sTrilinearClamp, vLastUvw, 0 ).rgb;

		// Its corners cover its 3x3x3 neighbours, kept in the froxels lit this frame
		const float3 vLo = ( g_vFirstCluster * FROXELS_PER_CLUSTER + 0.5f ) * vTexel;
		const float3 vHi = ( ( g_vFirstCluster + g_vClusterCount ) * FROXELS_PER_CLUSTER - 0.5f ) * vTexel;

		float3 vMin = 65504.0f, vMax = 0.0f, vSum = 0.0f;
		[unroll]
		for ( int c = 0; c < 8; c++ )
		{
			float3 vCorner = vCentre + 0.5f * float3( ( c & 1 ) ? 1 : -1, ( c & 2 ) ? 1 : -1, ( c & 4 ) ? 1 : -1 );
			float3 vColor = InLighting.SampleLevel( g_sTrilinearClamp, clamp( vCorner * vTexel, vLo, vHi ), 0 ).rgb;
			vMin = min( vMin, vColor );
			vMax = max( vMax, vColor );
			vSum += vColor;
		}

		float3 vTemporal = lerp( vSum / 8.0f, clamp( vLast, vMin, vMax ), HistoryWeight );

		// Round to R11G11B10F's 6 bit mantissas (5 in blue) stochastically, or the blend's small steps round away
		float3 vStep = exp2( floor( log2( max( vTemporal, 1e-6f ) ) ) - float3( 6, 6, 5 ) );
		OutHistory[froxel] = float4( floor( vTemporal / vStep + BlueNoise( froxel, uint2( 101, 53 ) ) ) * vStep, 1.0f );
	}

	[numthreads( 4, 4, 2 )]
	void MainCs( uint3 nGroupId : SV_GroupID, uint3 nGroupThreadId : SV_GroupThreadID )
	{
		const uint3 cluster = uint3( g_vFirstCluster ) + nGroupId;
		const uint3 froxel = cluster * FROXELS_PER_CLUSTER + nGroupThreadId;

		if ( D_PASS == 0 )
			LightPass( cluster, froxel );
		else
			DenoisePass( froxel );
	}
}

// cs_volumetric_fog: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
// Copyright (c) Valve Corporation, All rights reserved. ======================================================================================================
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
//
// Volumetric Fogging Compute Shaders
//
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: CompileTargets = ( IS_SM_50 );
// HEADER: Description = "Applies volumetric fog to each pixel on screen";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#include "sbox/pass.slangh"

Mode( Default );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
	#define FORCE_BILINEAR_PCF_SHADOWS_ONLY
	#define COMPUTE_SHADER
	
	#include "system.fxc" // This should always be the first include in COMMON
	#include "vr_common.fxc"

	#include "sbox/volumetric_fog.slangh"
	#include "sbox/light_probe_volume.slangh"
	
    #include "common/lightbinner.hlsl"
    #include "common/classes/Depth.hlsl"
	#include "common/DDGI/DDGI.hlsl"

	//------------------------------------------------------------------------------------------------------------------------------
	cbuffer VolumetricFogConstantBuffer_t
	{
		float4		g_vViewDX;
		float4		g_vViewDY;
		float4		g_vViewCorner;
		float4		g_vViewPos;
		float4		g_vViewDir;

		float4x4	g_matLastViewFrustum;

		float4		g_vNearFarClipPlaneReciprocal;
		float4		g_flVolumeScale_Shift_Near_Far;
		float4		g_flAniosotrophy_Scattering_XYJitterScale;

		float4		g_vIndirectBBScale_flIndirectStrength;
		float4		g_vIndirectBBOffset;

		float4		g_vPostWorldToFrustumScale;
		float4		g_vPostWorldToFrustumBias;

		float4		g_vThreadToVoxelScale;
		float4		g_vThreadToVoxelBias;

		float4		g_vFogFadeInScaleBias;
	
		int4		g_nNumFogVolumeBoundingBoxes;
		float4		g_vFogVolumeBoundingBoxMin[128]; 	// Spherical on .w
		float4		g_vFogVolumeBoundingBoxMax[128];	// Fog strength on .w
		float4		g_vFogVolumeBoundingBoxColorExponent[128];
		float4x4   	g_matFogVolumeBoundingBoxWorldToVolume[128];

		uint4		g_nVolFogWidthHeightDepth;
		float4		g_vOoVogFogWidthHeightDepth;

		int4		g_vFroxelMin;		// The froxels the fog volumes cover, max exclusive: only these are lit
		int4		g_vFroxelMax;
	};

	//------------------------------------------------------------------------------------------------------------------------------
	#define g_flVolumeScale		g_flVolumeScale_Shift_Near_Far.x
	#define g_flXShift			g_flVolumeScale_Shift_Near_Far.y
	#define g_flNearClipPlane	g_flVolumeScale_Shift_Near_Far.z
	#define g_flClipPlaneRange	g_flVolumeScale_Shift_Near_Far.w
	#define g_flFarClipPlane	g_vNearFarClipPlaneReciprocal.z

	#define g_flOffsetNear		g_flOffsetNear_OffsetFar_ScaleNear_ScaleFar.x
	#define g_flOffsetFar		g_flOffsetNear_OffsetFar_ScaleNear_ScaleFar.y
	#define g_flScaleNear		g_flOffsetNear_OffsetFar_ScaleNear_ScaleFar.z
	#define g_flScaleFar		g_flOffsetNear_OffsetFar_ScaleNear_ScaleFar.w

	#define g_flAnisotropy		g_flAniosotrophy_Scattering_XYJitterScale.x
	#define g_flScattering		g_flAniosotrophy_Scattering_XYJitterScale.y
	#define g_vXYJitterScale	g_flAniosotrophy_Scattering_XYJitterScale.zw

	#define g_flNoiseOffset		g_vFogNoiseOffset_Scale_UVScale0_UVScale1.x
	#define g_flNoiseScale		g_vFogNoiseOffset_Scale_UVScale0_UVScale1.y
	#define g_flNoiseUVScale0	g_vFogNoiseOffset_Scale_UVScale0_UVScale1.z
	#define g_flNoiseUVScale1	g_vFogNoiseOffset_Scale_UVScale0_UVScale1.w

	//------------------------------------------------------------------------------------------------------------------------------

	int		g_nJitterOffset	< Attribute( "g_nJitterOffset" ); >;

	//------------------------------------------------------------------------------------------------------------------------------
	float GetPhaseFunction( float cosPhi, float gFactor )
	{
		// Smoother falloff
		gFactor = sqrt( gFactor );
		gFactor = min( gFactor, 0.8f ); // Avoid singularity at 1

		float gFactor2 = gFactor*gFactor;

		return ( 1 - gFactor2 ) / pow( abs( 1 + gFactor2 - 2 * gFactor * cosPhi ), 1.5f ) / ( 4.0f * 3.1415926f );
	}

	//------------------------------------------------------------------------------------------------------------------------------

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_CS )
	// Includes -----------------------------------------------------------------------------------------------------------------------------------------------

	#include "sbox/math.slangh"
	#include "sbox/light_probe_volume.slangh"
	#include "sbox/lighting.slangh"
	
	DynamicCombo( D_PASS, 0..1, Sys( PC || VULKAN ) );
	DynamicCombo( D_ENABLE_BAKED_FOG_TEXTURE, 0..1, Sys( ALL ) );
	DynamicCombo( D_INDIRECT_LIGHTING_FROM_IBL, 0..1, Sys( ALL ) );

	#define D_JITTER 1

	// fp32: Slang has no min16float, and its real fp16 (float16_t) needs the shaderFloat16 device
	// feature, which is optional below GTX 1600 / RTX 2000.
	#define lpfloat4 float4

	// -----------------------------------------------------------------------------------------------------------------------------------------------------------

	enum FogPasses
	{
		Lighting,
		Integrate,
		Last
	};

    //-------------------------------------------------------------------------------------------------------------------------------------------------------------
    // Textures
	// -----------------------------------------------------------------------------------------------------------------------------------------------------------
    Texture2D<float4> BlueNoise < Attribute("BlueNoise"); >;
	
	Texture3D<lpfloat4> InFogIndirectVolume 			< Attribute("InBakedFogTexture"); > ;
	Texture3D<lpfloat4> InLightingTexture 				< Attribute( "InLightingTexture" ); > ;
    Texture3D<lpfloat4> InAccumulationTexture 			< Attribute("InAccumulationTexture" ); > ;
	
	RWTexture3D<lpfloat4> OutAccumulationTexture 		< Attribute( "OutAccumulationTexture" ); > ;
	RWTexture3D<lpfloat4> OutScatteringTexture 			< Attribute( "OutScatteringTexture" ); > ;
	RWTexture3D<lpfloat4> OutLightingTexture 			< Attribute( "OutLightingTexture" ); > ;

	SamplerState VolumeSampler < Filter( BILINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); AddressW( CLAMP ); >;;

    // --------------------------------------------------------------------------------------------------------------------------------------------------------
    //
    // Calculate the lighting at the voxel position. Only reached from ClusterForEach, so the light and every
    // index read from it are wave-uniform: its data loads once per wave and its shadow map skips the bindless loop
    //
    // --------------------------------------------------------------------------------------------------------------------------------------------------------
    float3 CalculateLightingAtPoint( float3 vPositionWs, float3 vViewDirWs, float flDensity, BinnedLight light )
	{
		float3 vPositionToLightRayWs = light.GetPosition() - vPositionWs.xyz; // "L"
		float flDistToLightSq = dot( vPositionToLightRayWs.xyz, vPositionToLightRayWs.xyz );
		float3 vPositionToLightDirWs = vPositionToLightRayWs * rsqrt( flDistToLightSq );
		float flConeToDirection = dot( vPositionToLightDirWs.xyz, -light.GetDirection() ) - light.SpotLightInnerOuterConeCosines.y;

		// Outside the light's range or spotlight cone, one branch
		if ( ( flDistToLightSq > light.RadiusSquared ) | ( flConeToDirection <= 0.0 ) )
			return 0;

		float3 vLightColor = light.Color;
		float flSpotAtten = flConeToDirection * light.SpotLightInnerOuterConeCosines.z;

		[branch]
		if ( light.HasLightCookie() )
		{
			float4 vCookieSample = light.SampleLightCookie( light.GetCookieUV( vPositionWs.xyz ) );
			vLightColor.rgb *= vCookieSample.rgb * vCookieSample.a;
			flSpotAtten = 1.0f;
		}

		float flLightFalloff = CalculateDistanceFalloff( flDistToLightSq, light.LinearFalloff, light.QuadraticFalloff, light.FalloffBias, 1.0 );

		float flShadowScalar = 1.0;
		if ( light.HasFogShadows() )
		{
			if ( light.Type == LightType::LightTypePoint )
				flShadowScalar = ProjectedShadowCube::GetVisibility( light.ShadowMapIndex, vPositionWs );
			else if ( light.Type == LightType::LightTypeSpot )
				flShadowScalar = ProjectedShadow::GetVisibility( light.ShadowMapIndex, vPositionWs, float2( 0, 0 ) );
		}

		// Filter this source-to-froxel segment before accumulating any other light.
		// Reuse the surface path's cached candidates and explicit-LOD texture sampling.
		[branch]
		if ( LightModulators::Active() && light.LightModulatorCount > 0 )
		{
			vLightColor *= LightModulators::Evaluate( vPositionWs, vPositionToLightDirWs, sqrt( flDistToLightSq ),
				light.LightModulatorOffset, light.LightModulatorCount );
		}

		float flLightScattering = GetPhaseFunction( dot( vPositionToLightDirWs, vViewDirWs ), flDensity );

		return vLightColor.rgb * ( flShadowScalar * flLightFalloff * flSpotAtten * flLightScattering * abs( light.FogIntensity ) );
	}

	// ---------------------------------------------------------------------------------------------------------------------------------------------------

	// Returns rgb = tint of dominant volume at this point, a = accumulated density contribution.
	float4 GetContributionInBoundingVolumes( float3 vWorldPos )
	{
		float flContribution = 0.0f;
		float3 vTint = float3( 1.0f, 1.0f, 1.0f );
		for ( int n = 0; n < g_nNumFogVolumeBoundingBoxes.x; n++ )
		{
			float3 vMin = g_vFogVolumeBoundingBoxMin[n].xyz;
			float3 vMax = g_vFogVolumeBoundingBoxMax[n].xyz;

			//Transform the world position into the local space of the bounding box
			float3 vPositionInVolumeWs = mul( float4( vWorldPos.xyz, 1.0f ),  g_matFogVolumeBoundingBoxWorldToVolume[n] ).xyz;

			float3 vNormalizedPosInBB = saturate( ( vPositionInVolumeWs - vMin ) / ( vMax - vMin ) );
			
			if ( ( dot(1.f, vNormalizedPosInBB.xyz - saturate(vNormalizedPosInBB.xyz)) == 0.0f ) )
			{
				float3 vRelToCenter = abs( vNormalizedPosInBB.xyz * 2.0f - float3( 1.0f, 1.0f, 1.0f ) );
				float flMaxDist = g_vFogVolumeBoundingBoxMin[ n ].w == 0.0f ? max( max( vRelToCenter.x, vRelToCenter.y ), vRelToCenter.z ) : length(vRelToCenter);
				float flFalloff = 1.0f - flMaxDist;
				// pow( 0, 0 ) is NaN on some GPUs (AMD), froxels on or outside the volume edge contribute nothing
				float flLocal = flFalloff > 0.0f ? pow( flFalloff, g_vFogVolumeBoundingBoxColorExponent[ n ].a ) * g_vFogVolumeBoundingBoxMax[ n ].w : 0.0f;
				// Take tint from the volume that contributes most at this voxel - keeps the math trivial and avoids order-dependence.
				if ( flLocal > flContribution )
				{
					vTint = g_vFogVolumeBoundingBoxColorExponent[ n ].rgb;
				}
				flContribution = max( flContribution, flLocal );
			}
		}
		return float4( vTint, saturate( flContribution ) );
	}

	// ---------------------------------------------------------------------------------------------------------------------------------------------------
	
	float3 IndirectLightingFromIBL( float3 vWorldPos )
    {
		#if (!D_INDIRECT_LIGHTING_FROM_IBL)
				return 0.0f;
		#endif
		if ( NumEnvironmentMaps == 0 )
			return 0.0f;

		float3 vIndirectLighting = 0.0f;
		float flDistAccumulated = 0.0f;
		
		for( uint i=0; i<1; i++ )
		{
			uint index = i;
			float3 vCubePos = mul( float4( vWorldPos.xyz, 1.0 ), EnvMapWorldToLocal( index ) ).xyz;

			const float3 vEnvMapMin = EnvMapBoxMins(index);
			const float3 vEnvMapMax = EnvMapBoxMaxs(index);

			const float flEdgeFeathering = EnvMapFeathering( index );

			float3 vIntersectA = min( ( vCubePos - vEnvMapMin ), ( vEnvMapMax - vCubePos ) ) ;
			float3 vIntersectB = min( ( vCubePos - vEnvMapMax ), ( vEnvMapMin - vCubePos ) ) ;

			float flDistance = min(
				min( vIntersectA.x, min( vIntersectA.y, vIntersectA.z ) ),
				min( -vIntersectB.x, -min( vIntersectB.y, vIntersectB.z ) )
			);

			// Branch, but better than sampling all cubes
			if( flDistance < 0.0 )
				continue;

			float3 vPositionCubemapLocal = mul(float4(vWorldPos.xyz, 1.0), EnvMapWorldToLocal(index)).xyz;
			float flLod = flDistance / length( vEnvMapMax - vEnvMapMin ); // More scattered the further from edges

			float3 vParallaxCubemapLocal = CubeMapBoxProjection( normalize(vPositionCubemapLocal), vPositionCubemapLocal.xyz, EnvMapBoxMins(index).xyz, EnvMapBoxMaxs(index).xyz);

            float3 vCubeMapTexelDiffuse = SampleEnvironmentMapLevel( vParallaxCubemapLocal, max( flLod * 4, 0.25f ), index );

			vIndirectLighting = lerp( vIndirectLighting, vCubeMapTexelDiffuse, 1.0 - flDistAccumulated );

			// Update the accumulated distance
			flDistAccumulated += RemapValClamped(flDistance, min(-flEdgeFeathering, 0.0f), max(-flEdgeFeathering, 0.0f), 0.0, 1.0);

			// Break if we've accumulated enough light
			if (flDistAccumulated >= 1.0)
				break;

		}
		return vIndirectLighting;
	}
	
	float3 GetViewVector( uint2 nThreadId )
	{
		return g_vViewCorner.xyz 
			+ ( float( nThreadId.x ) + 0.5f ) * g_vViewDX.xyz 
			+ ( float( nThreadId.y ) + 0.5f ) * g_vViewDY.xyz;
	}

    float GetScattering(float3 vViewVector, float flDepthSlice, float flDepthDelta)
    {
        float flScatteringAlongView = g_flScattering * length(vViewVector);
        float flScattering = flDepthDelta * flScatteringAlongView;

        flScattering *= saturate(flDepthSlice * g_vFogFadeInScaleBias.x + g_vFogFadeInScaleBias.y);

		return flScattering;
    }

    //-----------------------------------------------------------------------------------------------------------------
	//
	// Per-Frame Update Light Volume
	//
	// ----------------------------------------------------------------------------------------------------------------
	// Where slice z starts and where its centre is, in view depth
	float SliceStartDepth( uint z ) { return VoxelSliceZToRealDepth( max( z - 0.5f, 0.0f ) * g_vOoVogFogWidthHeightDepth.z ); }
	float SliceDepth( uint z ) { return VoxelSliceZToRealDepth( ( z + 0.5f ) * g_vOoVogFogWidthHeightDepth.z ); }

	// One froxel per thread: the slices don't depend on each other, and a thread per column left too few waves to fill the GPU
	void LightingPass( uint3 nThreadId, float flDepthMax )
	{
		const bool bDebugTiled = false;
		const uint z = nThreadId.z;

		// Froxel rows run bottom to top, viewport uvs top to bottom
		const float2 vClusterUv = float2( nThreadId.x + 0.5f, g_nVolFogWidthHeightDepth.y - nThreadId.y - 0.5f ) * g_vOoVogFogWidthHeightDepth.xy;
		const float flCurrentSliceDepth = SliceDepth( z );

		if ( bDebugTiled )
		{
			float flLights = Cluster::Query( ClusterItemType_Light, vClusterUv, flCurrentSliceDepth ).Count;
			OutLightingTexture[nThreadId] = lpfloat4( lerp( flLights / 16.0f, float4( 0, 1, 0, 1 ), float4( 1, 0, 0, 1 ) ) );
			return;
		}

		// Lit up to the first slice that starts past the depth
		if ( z > 0 && SliceStartDepth( z - 1 ) > flDepthMax )
			return;

		float3 vViewVector = GetViewVector( nThreadId.xy );
		float vJitterValue = 0.0f;

		#if (D_JITTER)
		{
			int2 vBlueNoiseCoord = ( int2( nThreadId.xy ) + int2( g_nJitterOffset, g_nJitterOffset ) ) & 63;
			vJitterValue = BlueNoise[uint2( vBlueNoiseCoord )].x * 2.0f;
		}
		#endif

		float flCurrentDeltaDepth = flCurrentSliceDepth - SliceStartDepth( z );

		float4 vIncomingLightAndDensity = float4(0, 0, 0, 0);
		// The view vector is unit depth, so this is view depth
		float flJitteredDepth = flCurrentSliceDepth - vJitterValue * flCurrentDeltaDepth;
		float3 vJitteredWorldPos = g_vViewPos.xyz + vViewVector * flJitteredDepth;

		// Calculate contributions
		float4 vVolumeContribution = GetContributionInBoundingVolumes(vJitteredWorldPos);
		vIncomingLightAndDensity.a = vVolumeContribution.a;

		// Special path for Directional light - let's change this back please 🥺
		if( g_DirectionalLightCascadeCount > 0 && g_DirectionalLightColor.w > 0.0f )
		{
			float flDirFogScale = g_DirectionalLightColor.w;
			if ( flDirFogScale > 0.0f )
			{
				float3 vDirLightColor = g_DirectionalLightColor.rgb;
				float3 vPositionToLightDirWs = -g_DirectionalLightDirection.xyz;

				float flShadowScalar = DirectionalLightShadow::GetVisibility( vJitteredWorldPos, float4( 0, 0, 0, 0 ) );

				if ( flShadowScalar > 0.0f )
				{
					// Sun rays have unlimited reach, but still cross each physical aperture.
					[branch]
					if ( LightModulators::Active() )
						vDirLightColor *= LightModulators::EvaluateSun( vJitteredWorldPos, vPositionToLightDirWs );

					const float flDensity = vIncomingLightAndDensity.a;
					float flLightScattering = GetPhaseFunction( dot( vPositionToLightDirWs, CalculateCameraToPositionDirWs( vJitteredWorldPos ) ), flDensity );

					vIncomingLightAndDensity.rgb += vDirLightColor * flShadowScalar * flLightScattering * flDirFogScale;
				}
			}
		}

		// Lights from the froxel's cluster, one per wave
		ClusterRange lightRange = Cluster::Query( ClusterItemType_Light, vClusterUv, flJitteredDepth );
		ClusterForEach( lightRange )
		{
			BinnedLight light = DynamicLightConstantByIndex( lightRange.Item );

			// Lights with no fog contribution (NONE or BAKED modes)
			if ( light.FogIntensity != 0.0f )
				vIncomingLightAndDensity.rgb += CalculateLightingAtPoint( vJitteredWorldPos, CalculateCameraToPositionDirWs( vJitteredWorldPos ), vIncomingLightAndDensity.a, light );
		}

		float4 ddgi = DDGI::EvaluateVolumes( vJitteredWorldPos, vViewVector.xyz );
		vIncomingLightAndDensity.rgb += ddgi.rgb;
		if ( ddgi.a < 1.0f )
		{
			#if (D_ENABLE_BAKED_FOG_TEXTURE)
			{
				float3 vLocalPosInIndirectVolume = vJitteredWorldPos * g_vIndirectBBScale_flIndirectStrength.xyz + g_vIndirectBBOffset.xyz;
				vIncomingLightAndDensity.rgb += (1.0f - ddgi.a) * InFogIndirectVolume.SampleLevel(VolumeSampler, vLocalPosInIndirectVolume, 0).rgb * g_vIndirectBBScale_flIndirectStrength.w;
			}
			#else
			{
				vIncomingLightAndDensity.rgb += (1.0f - ddgi.a) * IndirectLightingFromIBL(vJitteredWorldPos).rgb;
			}
			#endif
		}

		// Tint the in-scattered light by the dominant fog volume's color.
		vIncomingLightAndDensity.rgb *= vVolumeContribution.rgb;

		// Encode it to Gamma to Linear so that TAA is softer
		vIncomingLightAndDensity = sqrt(vIncomingLightAndDensity);

		// Clamp to max representable float16
		OutLightingTexture[nThreadId] = lpfloat4(min(vIncomingLightAndDensity, 65504.0f));
	}

	//-----------------------------------------------------------------------------------------------------------------
	//
	// Update Fogging Volume
	// Does temporal antialiasing and flattens the fogging volume for compositing
	//
	//-----------------------------------------------------------------------------------------------------------------
	// Min and max of the four trilinear samples at a froxel's corners in the plane at depth coordinate flW: with the four of
	// the plane below, the bounds of its 3x3x3 neighbourhood. Clamped to the lit froxels' centres, as the .shader clamps its
	// eight: froxels outside them weren't lit this frame
	void CornerBounds( float2 vCenterUV, float2 vHalfTexel, float flW, float3 vLitMin, float3 vLitMax, out float4 vMin, out float4 vMax )
	{
		vMin = 65504.0f;
		vMax = 0.0f;
		[unroll]
		for ( int x = -1; x <= 1; x += 2 )
		{
			[unroll]
			for ( int y = -1; y <= 1; y += 2 )
			{
				float4 color = InLightingTexture.SampleLevel( VolumeSampler, clamp( float3( vCenterUV + float2( x, y ) * vHalfTexel, flW ), vLitMin, vLitMax ), 0 );
				vMin = min( vMin, color );
				vMax = max( vMax, color );
			}
		}
	}

	void IntegratePass( uint3 nThreadId, float flDepthMax )
	{
		uint nView = nThreadId.z;

		float3 vViewVector			= GetViewVector( nThreadId.xy );

		float flSliceSize			= 1.0f * g_vOoVogFogWidthHeightDepth.z;
		float flCurrentSlice		= 0.5 * g_vOoVogFogWidthHeightDepth.z;
		float flCurrentSliceDepth	= VoxelSliceZToRealDepth( flCurrentSlice );
		float flLastSliceDepth		= 0.0f;

        float4 vCurrentColorAndDensity = float4(0.0f, 0.0f, 0.0f, 0.0f);

		// The lit froxels' centres, which the history clamp's samples stay within
		float3 vLitMin = ( g_vFroxelMin.xyz + 0.5f ) * g_vOoVogFogWidthHeightDepth.xyz;
		float3 vLitMax = ( g_vFroxelMax.xyz - 0.5f ) * g_vOoVogFogWidthHeightDepth.xyz;

		// Bounds of the four trilinear corner samples below the first slice
		float4 vBelowMin, vBelowMax;
		CornerBounds( ( float2( nThreadId.xy ) + 0.5f ) * g_vOoVogFogWidthHeightDepth.xy, 0.5f * g_vOoVogFogWidthHeightDepth.xy, 0.0f, vLitMin, vLitMax, vBelowMin, vBelowMax );

		[unroll]
		for ( uint zSlice = 0; zSlice < g_nVolFogWidthHeightDepth.z; zSlice++ )
        {
            const uint3 vCoord = uint3( nThreadId.x, nThreadId.y, zSlice );
			float flCurrentDeltaDepth = ( flCurrentSliceDepth - flLastSliceDepth );

			float4 vTemporalColor = 0.0f;
            float4 vCurrentColor = InLightingTexture[vCoord];

			//
			// Do denoising from previous frame using temporal antialiasing, only where the volumes are: there's no fog anywhere else
			//
			[branch]
			if ( all( int3( vCoord ) >= g_vFroxelMin.xyz ) && all( int3( vCoord ) < g_vFroxelMax.xyz ) )
            {
				//
                // Previous position at reprojected location
				// Use view-relative position to avoid precision loss far from world origin.
				// g_matLastViewFrustum bakes in the camera translation on the CPU side.
                //
				float4 vVoxelPos = float4(vViewVector * flCurrentSliceDepth, 1.0f);
                vVoxelPos = mul(g_matLastViewFrustum, vVoxelPos);
                vVoxelPos.xy /= vVoxelPos.w;
                vVoxelPos.xyz = vVoxelPos.xyw * g_vPostWorldToFrustumScale.xyz + g_vPostWorldToFrustumBias.xyz;
                vVoxelPos.z = FogFrustumDepthToVoxelSliceZ(vVoxelPos.z);

				float4 vPreviousColor = InAccumulationTexture.SampleLevel( VolumeSampler, vVoxelPos.xyz, 0 );

                //
				// Clamp to TAA bounds using color bounding box
				// https://de45xmedrsdbp.cloudfront.net/Resources/files/TemporalAA_small-59732822.pdf#page=30
				// 
                float4 minColor = 65504.0f, maxColor = 0.0f;
                
				// 8 trilinear samples at texel corners to cover 3x3x3 neighborhood
				float3 vCenterUV = (float3(vCoord) + 0.5f) * g_vOoVogFogWidthHeightDepth.xyz;
				float3 vHalfTexel = 0.5f * g_vOoVogFogWidthHeightDepth.xyz;

				// The four corners above this froxel are the four below the next one: sample only those, and keep them
				float4 vAboveMin, vAboveMax;
				CornerBounds( vCenterUV.xy, vHalfTexel.xy, float( zSlice + 1 ) * g_vOoVogFogWidthHeightDepth.z, vLitMin, vLitMax, vAboveMin, vAboveMax );
				minColor = min( vBelowMin, vAboveMin );
				maxColor = max( vBelowMax, vAboveMax );
				vBelowMin = vAboveMin;
				vBelowMax = vAboveMax;

				// Clamp our previous color to the TAA bounds
                vTemporalColor = clamp(vPreviousColor, minColor, maxColor);

				// Let it have a few of the current samples to not overly soften it
                vTemporalColor = lerp(vCurrentColor, vTemporalColor, 0.999f);

				// Write back to accumulation texture
				OutAccumulationTexture[vCoord] = lpfloat4(vTemporalColor);

				// Decode it back to our color space
				vTemporalColor = vTemporalColor * vTemporalColor;
            }
			else
			{
				OutAccumulationTexture[vCoord] = 0;

				// The next froxel's plane below is this one's above: kept, whether or not this one is lit
				float3 vCenterUV = (float3(vCoord) + 0.5f) * g_vOoVogFogWidthHeightDepth.xyz;
				CornerBounds( vCenterUV.xy, 0.5f * g_vOoVogFogWidthHeightDepth.xy, float( zSlice + 1 ) * g_vOoVogFogWidthHeightDepth.z, vLitMin, vLitMax, vBelowMin, vBelowMax );
			}

			//
			// Integrate accumulated samples and pancake the fogging volume
			//
            {
				// Adjust sample by scattering constant
                vTemporalColor.a *= GetScattering( vViewVector, flCurrentSliceDepth, flCurrentDeltaDepth );
                vTemporalColor.rgb *= vTemporalColor.a;

				vCurrentColorAndDensity = float4(vCurrentColorAndDensity.rgb + saturate(exp(-vCurrentColorAndDensity.a)) * vTemporalColor.rgb,
                                                vCurrentColorAndDensity.a + vTemporalColor.a);

				// Clamp to max representable float16
				float4 vOutColor = float4( vCurrentColorAndDensity.rgb , saturate( exp( -vCurrentColorAndDensity.a ) ) );
				OutScatteringTexture[vCoord] = lpfloat4(min(vOutColor, 65504.0f));
			}

			// Early out if we are past the depth + next slice
			if( flCurrentSliceDepth > flDepthMax + flCurrentDeltaDepth )
				return;
							
			//
			// Move to next voxel
			//
			flLastSliceDepth	 = flCurrentSliceDepth;
			flCurrentSlice		 += flSliceSize;
			flCurrentSliceDepth	 = VoxelSliceZToRealDepth( flCurrentSlice );
		}
	}

	//------------------------------------------------------------------------------------------------------------------------------
	// Farthest depth around this froxel column, a bit conservative to avoid disocclusion ghosting
	float GetDepthMax( uint2 nThreadId )
	{
		// Rows run bottom to top in the froxels, top to bottom in the depth chain
		nThreadId.y = g_nVolFogWidthHeightDepth.y - 1 - nThreadId.y;

		const int nLod = 6;
		float2 vPixelSize = g_vViewportSize * g_vOoVogFogWidthHeightDepth.xy;

		// The neighbours of the edge froxels fall outside the chain's mip, whose loads read zero, the far plane, which would
		// keep those froxels from stopping at the depth: clamp them to the edge texels
		uint nWidth, nHeight, nMips;
		g_tDepthChain.GetDimensions( nLod, nWidth, nHeight, nMips );

		// Pixel size + bilinearity doesn't match the power of two in the depth chain, sample a few neighbors, we just want far value
		float flDepthMax = 0.0f;
		[unroll]
		for ( int i = 0; i < 4; i++ )
		{
			float2 vOffset = float2( ( i & 1 ) ? 1 : -1, ( i & 2 ) ? 1 : -1 );
			int2 vTexel = clamp( int2( floor( ( nThreadId * vPixelSize + vOffset * vPixelSize ) / ( 1 << nLod ) ) ), 0, int2( nWidth, nHeight ) - 1 );
			float flProjectedDepth = g_tDepthChain.Load( int3( vTexel, nLod ) ).x;
			flDepthMax = max( flDepthMax, Depth::Linearize( Depth::Normalize( flProjectedDepth ) ) );
		}

		return flDepthMax;
	}
	//------------------------------------------------------------------------------------------------------------------------------
        
	[ numthreads( 8, 8, 1 ) ]
	[shader( "compute" )] void MainCs( uint3 nThreadId : SV_DispatchThreadID )
	{
		// Lighting is dispatched over the lit froxels only
		if ( D_PASS == FogPasses::Lighting )
		{
			nThreadId += g_vFroxelMin.xyz;
			if ( any( int3( nThreadId ) >= g_vFroxelMax.xyz ) )
				return;
		}

		if( nThreadId.x >= g_nVolFogWidthHeightDepth.x || nThreadId.y >= g_nVolFogWidthHeightDepth.y )
			return;

		float flDepthMax = GetDepthMax( nThreadId.xy );

		if ( D_PASS == FogPasses::Lighting)
			LightingPass( nThreadId, flDepthMax );
		else if ( D_PASS == FogPasses::Integrate )
			IntegratePass( nThreadId, flDepthMax );
	}

#endif // CS


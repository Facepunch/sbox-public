HEADER
{
	DevShader = true;
	Description = "Dynamic Sky Shader";
	Version = 1;
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
MODES
{
	Forward();
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
FEATURES
{
	#include "vr_common_features.fxc"
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
COMMON
{
	#include "system.fxc"
	#include "vr_common.fxc"

	static const float kAtmosPi = 3.141592f;
	static const int kAtmosPrimarySteps = 16;
	static const int kAtmosSecondarySteps = 2;

	float2 RaySphereIntersection( float3 origin, float3 direction, float radius )
	{
		float a = dot( direction, direction );
		float b = 2.0f * dot( direction, origin );
		float c = dot( origin, origin ) - ( radius * radius );
		float discriminant = ( b * b ) - 4.0f * a * c;

		if ( discriminant < 0.0f )
		{
			return float2( 1e5f, -1e5f );
		}

		float sqrtDiscriminant = sqrt( discriminant );
		float invDenominator = 0.5f / a;
		return float2(
			( -b - sqrtDiscriminant ) * invDenominator,
			( -b + sqrtDiscriminant ) * invDenominator
		);
	}

	float3 atmosphere( float3 viewDir, float3 rayOrigin, float3 sunDirection, float sunIntensity, float planetRadius, float atmosphereRadius, float3 rayleighCoefficients, float mieCoefficient, float rayleighScaleHeight, float mieScaleHeight, float miePreferredScatteringDirection )
	{
		sunDirection = normalize( sunDirection );
		viewDir = normalize( viewDir );

		float2 intersections = RaySphereIntersection( rayOrigin, viewDir, atmosphereRadius );
		if ( intersections.x > intersections.y )
		{
			return 0.0f;
		}

		intersections.y = min( intersections.y, RaySphereIntersection( rayOrigin, viewDir, planetRadius ).x );
		float primaryStepSize = ( intersections.y - intersections.x ) / float( kAtmosPrimarySteps );

		float rayleighOpticalDepth = 0.0f;
		float mieOpticalDepth = 0.0f;
		float3 accumulatedRayleigh = 0.0f;
		float3 accumulatedMie = 0.0f;

		float cosTheta = dot( viewDir, sunDirection );
		float cosThetaSquared = cosTheta * cosTheta;
		float g = miePreferredScatteringDirection;
		float gSquared = g * g;
		float rayleighPhase = ( 3.0f / ( 16.0f * kAtmosPi ) ) * ( 1.0f + cosThetaSquared );
		float miePhaseDenominator = pow( 1.0f + gSquared - 2.0f * cosTheta * g, 1.5f ) * ( 2.0f + gSquared );
		float miePhaseNumerator = ( 1.0f - gSquared ) * ( cosThetaSquared + 1.0f );
		float miePhase = ( 3.0f / ( 8.0f * kAtmosPi ) ) * ( miePhaseNumerator / miePhaseDenominator );

		float primaryTime = 0.0f;

		[loop]
		for ( int primaryStep = 0; primaryStep < kAtmosPrimarySteps; ++primaryStep )
		{
			float3 samplePosition = rayOrigin + viewDir * ( primaryTime + primaryStepSize * 0.5f );
			float sampleHeight = length( samplePosition ) - planetRadius;

			float rayleighStep = exp( -sampleHeight / rayleighScaleHeight ) * primaryStepSize;
			float mieStep = exp( -sampleHeight / mieScaleHeight ) * primaryStepSize;

			rayleighOpticalDepth += rayleighStep;
			mieOpticalDepth += mieStep;

			float secondaryStepSize = RaySphereIntersection( samplePosition, sunDirection, atmosphereRadius ).y / float( kAtmosSecondarySteps );
			float secondaryTime = 0.0f;
			float secondaryRayleighOpticalDepth = 0.0f;
			float secondaryMieOpticalDepth = 0.0f;

			[loop]
			for ( int secondaryStep = 0; secondaryStep < kAtmosSecondarySteps; ++secondaryStep )
			{
				float3 secondaryPosition = samplePosition + sunDirection * ( secondaryTime + secondaryStepSize * 0.5f );
				float secondaryHeight = length( secondaryPosition ) - planetRadius;

				secondaryRayleighOpticalDepth += exp( -secondaryHeight / rayleighScaleHeight ) * secondaryStepSize;
				secondaryMieOpticalDepth += exp( -secondaryHeight / mieScaleHeight ) * secondaryStepSize;

				secondaryTime += secondaryStepSize;
			}

			float3 transmittance = exp( -( mieCoefficient * ( mieOpticalDepth + secondaryMieOpticalDepth ) + rayleighCoefficients * ( rayleighOpticalDepth + secondaryRayleighOpticalDepth ) ) );

			accumulatedRayleigh += rayleighStep * transmittance;
			accumulatedMie += mieStep * transmittance;

			primaryTime += primaryStepSize;
		}

		float3 rayleighContribution = rayleighPhase * rayleighCoefficients * accumulatedRayleigh;
		float3 mieContribution = ( miePhase * mieCoefficient ) * accumulatedMie;

		return sunIntensity * ( rayleighContribution + mieContribution );
	}
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct VS_INPUT
{
	float4 vPositionOs : POSITION < Semantic( PosXyz ); >;
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_INPUT
{
	float3 vPositionWs : TEXCOORD1;
	nointerpolation float3 SunDirectionWs : TEXCOORD2;
	nointerpolation float3 SunColor : TEXCOORD3;

	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs	: SV_Position;
	#endif
	#if ( PROGRAM == VFX_PROGRAM_PS )
		float4 vPositionSs : SV_Position;
	#endif
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
VS
{
	// Includes -----------------------------------------------------------------------------------------------------------------------------------------------
	#define IS_SPRITECARD 1
	#include "system.fxc"
	#include "vr_lighting.fxc"

	// Combos -------------------------------------------------------------------------------------------------------------------------------------------------

	// Constants ----------------------------------------------------------------------------------------------------------------------------------------------

	// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
	int FindSunLightIndex()
	{
		[loop]
		for ( uint lightIndex = 0u; lightIndex < uint( NumDynamicLights ); ++lightIndex )
		{
			BinnedLight light = BinnedLightBuffer[ lightIndex ];

			float3 lightPosition = light.GetPosition();
			float FLOAT32_MAX = 3.402823466e+38;

			if ( light.GetRadius() == FLOAT32_MAX )
			{
				return lightIndex;
			}
		}
		return -1;
	}

	void FetchSunLight( out float3 sunDirectionWs, out float3 sunColor )
	{
		sunDirectionWs = float3( 0.0f, 0.0f, 0.0f );
		sunColor = float3( 0.0f, 0.0f, 0.0f );

		int sunLightIndex = FindSunLightIndex();
		
		if ( sunLightIndex == -1 )
			return;

		BinnedLight sunLight = BinnedLightBuffer[ sunLightIndex ];

		sunDirectionWs = normalize( sunLight.GetPosition() );
		sunColor = sunLight.GetColor();
	}

	PS_INPUT MainVs( const VS_INPUT i )
	{
		PS_INPUT o;
		float flSkyboxScale = g_flNearPlane + g_flFarPlane;
		float3 vPositionWs = g_vCameraPositionWs.xyz + i.vPositionOs.xyz * flSkyboxScale;

		o.vPositionPs = Position3WsToPs( vPositionWs );
		o.vPositionWs = vPositionWs;

		// Precalculate sun light direction and color instead of doing it in the pixel shader
		FetchSunLight( o.SunDirectionWs, o.SunColor );
		
		return o;
	}
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
PS
{
	#include "vr_lighting.fxc"
	#include "volumetric_fog.fxc"
	
	// Combos -------------------------------------------------------------------------------------------------------------------------------------------------

	// Render State -------------------------------------------------------------------------------------------------------------------------------------------
	RenderState( CullMode, NONE );
	RenderState( DepthWriteEnable, false );
	RenderState( DepthEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );
	
	// Attributes ---------------------------------------------------------------------------------------------------------------------------------------------
	BoolAttribute( sky, true );

	// Output -------------------------------------------------------------------------------------------------------------------------------------------------
	struct PS_OUTPUT
	{
		float4 vColor0 : SV_Target0;
	};

	// Constants ----------------------------------------------------------------------------------------------------------------------------------------------

	// Main ---------------------------------------------------------------------------------------------------------------------------------------------------

	float noise( in float3 x )
	{
		float3 p = floor(x);
		float3 f = frac(x);
		f = f*f*(3.0-2.0*f);
		float2 uv = (p.xy+float2(37.0,17.0) * p.z) + f.xy;
		float2 rg = g_tBlueNoise.Sample( g_sBilinearWrap, (uv + 0.5 )/256.0 ).xy;
		return lerp( rg.x, rg.y, f.z );
	}

	float3 GetAtmosphere( float3 ray, float3 sunDirectionWs, float3 sunColor )
	{
		float fPlanetSize = 6371e3;
		float fAtmosphereSize = 100e3;
		float fSeaLevel = 512.0f;

		float3 color = atmosphere
		(
			ray.xzy,           													// normalized ray direction
			float3(0,fPlanetSize + g_vCameraPositionWs.z + fSeaLevel,0),        // ray origin
			sunDirectionWs.xzy,                        							// position of the sun
			50,                           										// intensity of the sun
			fPlanetSize,                         								// radius of the planet in meters
			fPlanetSize + fAtmosphereSize,                         				// radius of the atmosphere in meters
			float3(5.5e-6, 13.0e-6, 22.4e-6), 									// Rayleigh scattering coefficient
			21e-6,                          									// Mie scattering coefficient
			8e3,                            									// Rayleigh scale height
			1.2e3,                          									// Mie scale height
			0.758                          										// Mie preferred scattering direction
		);

		return color * sunColor;
	}

	float Stars( in float3 vRay )
	{
		const float fStarScale = 0.3;
		const float fStarAmount = 1.0;

		float vStars = noise(vRay * ( g_vViewportSize.y * fStarScale ) * 0.75 );
		vStars += noise(vRay * ( g_vViewportSize.y * fStarScale ) * 0.5 );
		vStars += noise(vRay * ( g_vViewportSize.y * fStarScale ) * 0.25);
		vStars += noise(vRay * ( g_vViewportSize.y * fStarScale ) * 0.1 );
		vStars += noise(vRay * ( g_vViewportSize.y * fStarScale ) ) * (1.0 - fStarAmount);

		vStars = clamp(vStars, 0.0, 1.0);
		vStars = (1.0 - vStars);

		vStars *= saturate( vRay.z * 100 );

		return vStars;
	}

	float3 Sun( in float3 vRay, float3 sunDirectionWs, float3 sunColor )
	{
		float fSun = pow( saturate(dot( vRay, sunDirectionWs ) + 0.00025 ), 10000.0f ) * 10;

		fSun *= saturate( vRay.z ); // Fade sun when below horizon
		return sunColor * fSun;
	}

	PS_OUTPUT MainPs( PS_INPUT i )
	{
		PS_OUTPUT o;
		float3 vPositionWs = i.vPositionWs;
		float3 vRay = normalize( vPositionWs - g_vCameraPositionWs );
		float3 vColor = Stars( vRay );

		const float sunDirLengthSq = dot( i.SunDirectionWs, i.SunDirectionWs );
		const float sunColorLengthSq = dot( i.SunColor, i.SunColor );
		if ( sunDirLengthSq > 0.0f && sunColorLengthSq > 0.0f )
		{
			vColor += GetAtmosphere( vRay, i.SunDirectionWs, i.SunColor );
			vColor += Sun( vRay, i.SunDirectionWs, i.SunColor );
		}

		o.vColor0 = float4( vColor, 1.0 );
		o.vColor0.rgb = ApplyVolumetricFog( o.vColor0.rgb, i.vPositionWs, i.vPositionSs.xy );
		return o;
	}
}

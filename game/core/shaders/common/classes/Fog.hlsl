#ifndef FOG_HLSL
#define FOG_HLSL

#include "system.fxc"
#include "vr_common.fxc"
#include "common_samplers.fxc"
#include "math_general.fxc"
#include "volumetric_fog.fxc"
#include "common/classes/Bindless.hlsl"

/// <summary>
/// Overlay blending that multiplies the underlying surface by twice the decal color.
/// </summary>
static const int FOG_OVERLAY_MOD2X = 3;

/// <summary>
/// Overlay blending that multiplies the underlying surface by the decal color.
/// </summary>
static const int FOG_OVERLAY_MULTIPLY = 5;

/// <summary>
/// Overlay blending that modulates the underlying surface and adds decal emission.
/// </summary>
static const int FOG_OVERLAY_MOD_THEN_ADD = 6;

CreateTextureCubeWithoutSampler( g_fogCubemapTexture ) < Attribute( "CubemapFogTexture" ); SrgbRead( true ); >;
float3 g_fogCubemapTint < Attribute( "CubemapFogColor" ); Default3( 1.0f, 1.0f, 1.0f ); >;

// The light binner selects the sun after camera fog has been packed.
float3 FogSunColor < Attribute( "FogSunColor" ); Default3( 0, 0, 0 ); >;
float3 FogSunDirection < Attribute( "FogSunDirection" ); Default3( 0, 0, 0 ); >;

// One immutable buffer per view, shared by every material. Layout matches Sandbox.Rendering.FogConstants.
cbuffer FogConstants
{
	// Weight, maximum opacity, distance mode (0 = ramp, 1 = exponential), height contribution.
	float4 FogControl;
	// Start distance, end distance, ramp exponent, exponential attenuation distance.
	float4 FogDistance;
	// Base height, signed gradient length, sky ray length, sky opacity scale.
	float4 FogHeight;
	float3 FogColor;
	float FogOpacityScale;
	float4 FogHeightColor;
	// Distance enable contribution, independent height exponent; sky contribution and mip level.
	float2 FogLayerParams;
	float2 FogSky;
	// Blur start distance, inverse distance range, far mip and maximum valid mip.
	float4 FogSkyBlurRamp;
	// Sun intensity and peak-normalized HG shape; height sky contribution and mip level.
	float2 FogSun;
	float2 FogHeightSky;
	// Sun start distance, saturation, fade distance, padding.
	float4 FogSunSettings;
	// Encoding (0 = RGB, 1 = YCoCg, 2 = RGBM) and cube-array flag, per layer.
	float2 FogSkyFormat;
	float2 FogHeightSkyFormat;
	int FogSkyTextureIndex;
	int FogHeightSkyTextureIndex;
	float FogHeightSkyShared;
	float FogPadding;
	float4x4 FogSkyTransform;
	float4x4 FogHeightSkyTransform;
};

/// <summary>
/// Evaluate and apply camera fog in material shaders.
/// </summary>
struct Fog
{
	/// <summary>
	/// Evaluate the cubemap fog amount before opacity clamping and mip selection.
	/// </summary>
	static float CalculateCubemapAmount( float3 worldPosition, float3 ray )
	{
		float distance = length( ray );
		float amount = pow( max( 0, distance * g_flCubeFogScale + g_flCubeFogOffset ), g_flCubeFogExponent );
		return max( amount, pow( max( 0, worldPosition.z * g_flCubeFogHeightScale + g_flCubeFogHeightOffset ), g_flCubeFogHeightExponent ) );
	}

	/// <summary>
	/// Evaluate cubemap fog opacity without sampling its color texture.
	/// </summary>
	static float CalculateCubemapOpacity( float3 worldPosition, float3 ray )
	{
		return saturate( CalculateCubemapAmount( worldPosition, ray ) );
	}

	/// <summary>
	/// Evaluate the GradientFog component as color and opacity.
	/// </summary>
	static float4 CalculateGradient( float3 worldPosition, float3 ray )
	{
		float2 distanceAndHeight = float2( length( ray.xy ), worldPosition.z );

		float2 range = saturate( g_vGradientFogBiasAndScale.xy + g_vGradientFogBiasAndScale.zw * distanceAndHeight );
		float heightOpacity = pow( range.y, m_vGradientFogExponents.y );
		float opacity = pow( range.x, m_vGradientFogExponents.x ) * heightOpacity * g_vGradientFogColor_Opacity.w;
		return float4( g_vGradientFogColor_Opacity.rgb, opacity );
	}

	/// <summary>
	/// Whether gradient fog's distance and height bounds contain this surface.
	/// Disabled components use infinite culling bounds to reject all surfaces.
	/// </summary>
	static bool GradientAffects( float3 worldPosition, float3 ray )
	{
		return dot( ray.xy, ray.xy ) > g_vGradientFogCullingParams.x
			&& worldPosition.z < g_vGradientFogCullingParams.y;
	}

	/// <summary>
	/// Evaluate the CubemapFog component as color and opacity.
	/// </summary>
	static float4 CalculateCubemap( float3 worldPosition, float3 ray )
	{
		float amount = CalculateCubemapAmount( worldPosition, ray );
		float3 direction = normalize( mul( float4( ray, 0 ), g_matvCubeFogSkyWsToOs ).xyz );

		float mip = g_flCubeMapSizeLog2 * saturate( 1 - amount * g_flCubeFogBias );
		float4 texel = g_fogCubemapTexture.SampleLevel( g_sTrilinearClamp, direction, mip );

		texel.rgb *= g_fogCubemapTint;

		return float4( texel.rgb, saturate( amount ) );
	}

	/// <summary>
	/// Whether cubemap fog's distance or height bounds contain this surface.
	/// </summary>
	static bool CubemapAffects( float3 worldPosition, float3 ray )
	{
		return dot( ray, ray ) > g_vCubeFogCullingParams.x
			|| worldPosition.z > g_vCubeFogCullingParams.y;
	}

	/// <summary>
	/// Get the view ray from the eye, or from the camera plane for orthographic views.
	/// </summary>
	static void GetRay( float3 worldPosition, out float3 origin, out float3 ray )
	{
		origin = g_vCameraPositionWs;
		ray = worldPosition - origin;

		if ( g_matViewToProjection[3].w != 0 )
		{
			ray = g_vCameraDirWs * max( 0, dot( ray, g_vCameraDirWs ) );
			origin = worldPosition - ray;
		}
	}

	/// <summary>
	/// Evaluate distance fog without applying height or the overall enable contribution.
	/// </summary>
	static float CalculateDistanceOpacity( float3 ray )
	{
		if ( FogControl.y <= 0 || FogLayerParams.x <= 0 )
		{
			return 0;
		}

		float fogLength = max( 0, length( ray ) - FogDistance.x );
		float opacity;

		if ( FogControl.z > 0.5 )
		{
			opacity = 1 - exp( -min( fogLength / FogDistance.w, 80 ) );
		}
		else
		{
			float range = max( FogDistance.y - FogDistance.x, 0.001 );
			opacity = pow( saturate( fogLength / range ), FogDistance.z );
		}

		return min( opacity, FogControl.y ) * FogLayerParams.x;
	}

	/// <summary>
	/// Evaluate the independent height gradient at the surface. Signed lengths reverse its direction.
	/// </summary>
	static float CalculateHeightOpacity( float height )
	{
		if ( FogControl.w <= 0 || FogHeightColor.a <= 0 )
		{
			return 0;
		}

		// Zero length is an intentional hard boundary, including when volume blends pass through zero.
		float gradient = FogHeight.y == 0
			? ( height <= FogHeight.x ? 1.0 : 0.0 )
			: saturate( 1 - ( height - FogHeight.x ) / FogHeight.y );

		return pow( gradient, FogLayerParams.y ) * FogHeightColor.a * FogControl.w;
	}

	/// <summary>
	/// Combined opacity shared by opaque, additive and multiplicative materials.
	/// </summary>
	static float CalculateOpacity( float3 origin, float3 ray )
	{
		float distanceOpacity = CalculateDistanceOpacity( ray );
		float heightOpacity = CalculateHeightOpacity( origin.z + ray.z );
		return ( distanceOpacity + heightOpacity * ( 1 - distanceOpacity ) ) * FogOpacityScale;
	}

	/// <summary>
	/// Sample and decode directional fog color from the configured sky texture.
	/// </summary>
	static float3 SampleSkyColor( float3 direction, float distance, bool height = false )
	{
		float2 format = height ? FogHeightSkyFormat : FogSkyFormat;
		float mip = height ? FogHeightSky.y : FogSky.y;
		if ( !height )
		{
			float t = saturate( ( distance - FogSkyBlurRamp.x ) * FogSkyBlurRamp.y );
			mip = clamp( lerp( mip, FogSkyBlurRamp.z, t ), 0, FogSkyBlurRamp.w );
		}

		float3 skyDirection = mul( float4( direction, 0 ), height ? FogHeightSkyTransform : FogSkyTransform ).xyz;
		float4 texel;
		int textureIndex = height ? FogHeightSkyTextureIndex : FogSkyTextureIndex;

		// Preserve the source texture dimension without copying or binding separate cube/array resources.
		if ( format.y > 0 )
		{
			texel = Bindless::GetTextureCubeArray( textureIndex ).SampleLevel( g_sTrilinearClamp, float4( skyDirection, 0 ), mip );
		}
		else
		{
			texel = Bindless::GetTextureCube( textureIndex ).SampleLevel( g_sTrilinearClamp, skyDirection, mip );
		}

		float3 skyColor = texel.rgb;

		// Match the source sky material's decoding, including legacy compressed skies.
		if ( format.x == 1 )
		{
			float scale = texel.b * ( 255.0 / 8.0 ) + 1;
			float co = ( texel.r - 0.5 * 256.0 / 255.0 ) / scale;
			float cg = ( texel.g - 0.5 * 256.0 / 255.0 ) / scale;
			skyColor = SrgbGammaToLinear( float3( texel.a + co - cg, texel.a + cg, texel.a - co - cg ) );
		}
		else if ( format.x == 2 )
		{
			skyColor = RGBMDecode( texel );
		}

		return skyColor;
	}

	/// <summary>
	/// Evaluate the resolved Fog component as color and opacity.
	/// </summary>
	static float4 Calculate( float3 worldPosition, bool sky = false )
	{
		if ( FogOpacityScale <= 0 || ( sky && FogHeight.w <= 0 ) )
		{
			return float4( 0, 0, 0, 0 );
		}

		float3 origin;
		float3 ray;
		GetRay( worldPosition, origin, ray );
		float3 direction = ray / max( length( ray ), 0.0001 );

		if ( sky )
		{
			ray = direction * FogHeight.z;
		}

		float distanceOpacity = CalculateDistanceOpacity( ray );
		float heightOpacity = CalculateHeightOpacity( origin.z + ray.z );
		float opacity = distanceOpacity + heightOpacity * ( 1 - distanceOpacity );

		if ( opacity <= 0 )
		{
			return float4( 0, 0, 0, 0 );
		}

		float3 distanceColor = FogColor;
		float3 heightColor = FogHeightColor.rgb;
		float3 skyColor = 0;
		float distance = length( ray );
		bool sampleDistanceSky = distanceOpacity > 0 && FogSky.x > 0;

		if ( sampleDistanceSky )
		{
			skyColor = SampleSkyColor( direction, distance );
			distanceColor = lerp( distanceColor, max( distanceColor * skyColor, float3( 0, 0, 0 ) ), FogSky.x );
		}

		if ( distanceOpacity > 0 && FogSun.x != 0 )
		{
			float facingSun = clamp( dot( direction, FogSunDirection ), -1, 1 );
			float lobe = pow( 1 + FogSun.y * ( 1 - facingSun ), -1.5 );
			float luminance = dot( FogSunColor, float3( 0.2126, 0.7152, 0.0722 ) );
			float3 sunColor = max( lerp( luminance.xxx, FogSunColor, FogSunSettings.y ), 0 );
			// Smooth both ends of the distance fade independently of fog density.
			float sunFade = FogSunSettings.z != 0
				? saturate( ( distance - FogSunSettings.x ) / FogSunSettings.z )
				: step( FogSunSettings.x, distance );
			sunFade = sunFade * sunFade * ( 3 - 2 * sunFade );
			distanceColor += sunColor * FogSun.x * lobe * sunFade;
		}

		if ( heightOpacity > 0 && distanceOpacity < 1 && FogHeightSky.x > 0 )
		{
			if ( !sampleDistanceSky || FogHeightSkyShared <= 0 )
			{
				skyColor = SampleSkyColor( direction, distance, true );
			}

			heightColor = lerp( heightColor, max( heightColor * skyColor, float3( 0, 0, 0 ) ), FogHeightSky.x );
		}

		// Distance fog covers height fog as the surface recedes. Normalize the composed
		// color so every material path can apply the same straight-alpha fog result.
		float3 color = ( distanceColor * distanceOpacity
			+ heightColor * heightOpacity * ( 1 - distanceOpacity ) ) / opacity;
		return float4( color, opacity * FogOpacityScale * ( sky ? FogHeight.w : 1 ) );
	}

	/// <summary>
	/// Influence of the resolved Fog component over the gradient and cubemap result.
	/// </summary>
	static float GetWeight()
	{
		return FogControl.x;
	}

	/// <summary>
	/// Apply distance and height fog. A full override skips the gradient and cubemap fallback.
	/// </summary>
	static float3 Apply( float3 worldPosition, float3 color )
	{
		#if PROGRAM != VFX_PROGRAM_CS
			float weight = GetWeight();
			float3 fallbackColor = color;

			if ( weight < 1 )
			{
				float3 ray = worldPosition - g_vCameraPositionWs;

				[branch]
				if ( GradientAffects( worldPosition, ray ) )
				{
					float4 fog = CalculateGradient( worldPosition, ray );
					fallbackColor = lerp( fallbackColor, fog.rgb, fog.a );
				}

				if ( CubemapAffects( worldPosition, ray ) )
				{
					float4 fog = CalculateCubemap( worldPosition, ray );
					fallbackColor = lerp( fallbackColor, fog.rgb, fog.a );
				}
			}

			if ( weight <= 0 )
			{
				return fallbackColor;
			}

			float4 fog = Calculate( worldPosition );
			float3 foggedColor = lerp( color, fog.rgb, fog.a );
			return weight >= 1 ? foggedColor : lerp( fallbackColor, foggedColor, weight );
		#else
			return color;
		#endif
	}

	/// <summary>
	/// Apply distance and height fog followed by volumetric fog using screen coordinates.
	/// Strength blends between the original color and the complete fog result.
	/// </summary>
	static float3 Apply( float3 worldPosition, float2 screenPosition, float3 color, float strength = 1 )
	{
		if ( strength <= 0 )
		{
			return color;
		}

		#if PROGRAM != VFX_PROGRAM_CS
			float3 foggedColor = ApplyVolumetricFog( Fog::Apply( worldPosition, color ), worldPosition, screenPosition );
			return strength == 1 ? foggedColor : lerp( color, foggedColor, strength );
		#endif

		return color;
	}

	/// <summary>
	/// Apply fog to a sky, blending from its existing atmospheric color before volumetric fog.
	/// The override uses the original color, preventing the two atmospheric results from compounding.
	/// </summary>
	static float3 ApplySky( float3 worldPosition, float2 screenPosition, float3 color, float3 fallbackColor )
	{
		#if PROGRAM != VFX_PROGRAM_CS
			float weight = GetWeight();

			if ( weight > 0 )
			{
				float4 fog = Calculate( worldPosition, true );
				float3 foggedColor = lerp( color, fog.rgb, fog.a );
				fallbackColor = weight >= 1 ? foggedColor : lerp( fallbackColor, foggedColor, weight );
			}

			fallbackColor = ApplyVolumetricFog( fallbackColor, worldPosition, screenPosition );
		#endif

		return fallbackColor;
	}

	/// <summary>
	/// Apply analytic and volumetric fog to a sky without an existing atmospheric fog term.
	/// </summary>
	static float3 ApplySky( float3 worldPosition, float2 screenPosition, float3 color )
	{
		return ApplySky( worldPosition, screenPosition, color, color );
	}

	/// <summary>
	/// Evaluate analytic transmittance, optionally using the surface culling bounds.
	/// Additive materials retain their original enabled-flag behavior at ramp boundaries.
	/// </summary>
	static float CalculateTransmittance( float3 worldPosition, bool cull )
	{
		#if PROGRAM != VFX_PROGRAM_CS
			float weight = GetWeight();
			float overrideTransmittance = 1;

			if ( weight > 0 )
			{
				float3 origin;
				float3 ray;
				GetRay( worldPosition, origin, ray );
				overrideTransmittance = 1 - CalculateOpacity( origin, ray );

				if ( weight >= 1 )
				{
					return overrideTransmittance;
				}
			}

			float3 ray = worldPosition - g_vCameraPositionWs;
			float transmittance = 1;

			if ( cull ? GradientAffects( worldPosition, ray ) : g_bGradientFogEnabled )
			{
				transmittance *= 1 - CalculateGradient( worldPosition, ray ).a;
			}

			if ( cull ? CubemapAffects( worldPosition, ray ) : g_bCubemapFogEnabled )
			{
				transmittance *= 1 - CalculateCubemapOpacity( worldPosition, ray );
			}

			return weight > 0 ? lerp( transmittance, overrideTransmittance, weight ) : transmittance;
		#else
			return 1;
		#endif
	}

	/// <summary>
	/// Return volumetric transmittance, or one when volumetric fog is unavailable.
	/// </summary>
	static float VolumetricTransmittance( float3 worldPosition, float2 screenPosition )
	{
		#if PROGRAM != VFX_PROGRAM_CS
			if ( g_bVolumetricFogEnabled )
			{
				return CalculateVolumetricFog( worldPosition, screenPosition ).a;
			}
		#endif

		return 1;
	}

	/// <summary>
	/// Return analytic attenuation for additive materials, without adding fog color twice.
	/// </summary>
	static float Transmittance( float3 worldPosition )
	{
		return CalculateTransmittance( worldPosition, false );
	}

	/// <summary>
	/// Return analytic and volumetric attenuation for additive materials.
	/// Zero strength leaves the material unchanged; one applies the full attenuation.
	/// </summary>
	static float Transmittance( float3 worldPosition, float2 screenPosition, float strength = 1 )
	{
		if ( strength <= 0 )
		{
			return 1;
		}

		float transmittance = Transmittance( worldPosition ) * VolumetricTransmittance( worldPosition, screenPosition );
		return strength == 1 ? transmittance : lerp( 1, transmittance, strength );
	}

	/// <summary>
	/// Return attenuation for multiplicative materials, including surface culling bounds.
	/// Strength blends from no attenuation to the complete analytic and volumetric result.
	/// </summary>
	static float ModulationTransmittance( float3 worldPosition, float2 screenPosition, float strength = 1 )
	{
		if ( strength <= 0 )
		{
			return 1;
		}

		float transmittance = CalculateTransmittance( worldPosition, true ) * VolumetricTransmittance( worldPosition, screenPosition );
		return strength == 1 ? transmittance : lerp( 1, transmittance, strength );
	}

	/// <summary>
	/// Luminance used by the original overlay opacity approximation.
	/// </summary>
	static float OverlayLuminance( float3 color )
	{
		return 0.2126 * color.r + 0.7152 * color.g + 0.0722 * color.b;
	}

	/// <summary>
	/// Color saturation used to preserve fog tint on multiplicative overlays.
	/// </summary>
	static float OverlaySaturation( float3 color )
	{
		float minimum = min( color.r, min( color.g, color.b ) );
		float maximum = max( color.r, max( color.g, color.b ) );
		float delta = maximum - minimum;

		return maximum <= 0 ? 0 : delta / maximum;
	}

	/// <summary>
	/// Normalize overlay color while keeping black finite.
	/// </summary>
	static float3 OverlayChrominance( float3 color )
	{
		float maximum = max( color.r, max( color.g, color.b ) );
		return maximum == 0 ? float3( 0, 0, 0 ) : color / float3( maximum, maximum, maximum );
	}

	/// <summary>
	/// Apply one overlay fog layer using the original artistic approximation.
	/// Volumetric layers carry transmittance in alpha; analytic layers carry opacity.
	/// </summary>
	static float4 ApplyOverlayLayer( float4 fog, float4 color, int mode, bool volumetric )
	{
		// The underlying surface color is unavailable, so retain the approximation
		// that preserves decal opacity in dark fog and avoids diluting the fog tint.
		if ( mode == FOG_OVERLAY_MOD_THEN_ADD )
		{
			color.a *= volumetric ? fog.a : 1 - fog.a;
			return color;
		}

		float3 colorized = color.rgb;
		float colorDensity = OverlaySaturation( fog.rgb ) * fog.a;

		if ( mode == FOG_OVERLAY_MOD2X )
		{
			colorized = lerp( color.rgb, 2.0 * color.rgb * fog.rgb, colorDensity );
		}
		else
		{
			colorized = lerp( max( fog.rgb, color.rgb ), OverlayChrominance( fog.rgb ) * Luminance( color.rgb ) + fog.rgb * 0.5, colorDensity );
		}

		float power = 1;

		if ( mode == FOG_OVERLAY_MOD2X )
		{
			power = lerp( 1.0, 0.5, pow( 1.0 / ( 1.0 + OverlayLuminance( color.rgb ) ), 4.0 ) );
		}
		else
		{
			power = lerp( 1.0, 2.0, pow( 1.0 / ( 1.0 + OverlayLuminance( color.rgb ) ), 4.0 ) );
		}

		fog.a = pow( fog.a, power );
		color.a *= volumetric ? fog.a : 1 - fog.a;

		if ( mode == FOG_OVERLAY_MOD2X )
		{
			color.rgb = lerp( color.rgb * 2.0, colorized, colorDensity );
			color.rgb *= 0.5;
		}
		else
		{
			color.rgb = max( color.rgb, fog.rgb );
			color.rgb = lerp( color.rgb, colorized, colorDensity );
		}

		return color;
	}

	/// <summary>
	/// Apply camera fog to an overlay using a FOG_OVERLAY mode constant.
	/// These constants also match the static overlay shader's blend-mode values.
	/// Preserve each layer's nonlinear color transform before blending the override,
	/// then apply volumetric fog once. Material enable and tool-view gates belong to the caller.
	/// </summary>
	static float4 ApplyOverlay( float3 worldPosition, float2 screenPosition, float4 color, int mode )
	{
		#if PROGRAM != VFX_PROGRAM_CS
			float4 unfoggedColor = color;
			float3 ray = worldPosition - g_vCameraPositionWs;
			float weight = GetWeight();

			if ( weight < 1 && g_bGradientFogEnabled )
			{
				color = ApplyOverlayLayer( CalculateGradient( worldPosition, ray ), color, mode, false );
			}

			if ( weight < 1 && g_bCubemapFogEnabled )
			{
				color = ApplyOverlayLayer( CalculateCubemap( worldPosition, ray ), color, mode, false );
			}

			if ( weight > 0 )
			{
				float4 fogColor = unfoggedColor;
				float4 fog = Calculate( worldPosition );

				// A cleared override must preserve the original decal, including multiply modes.
				if ( fog.a > 0 )
				{
					fogColor = ApplyOverlayLayer( fog, fogColor, mode, false );
				}

				color = lerp( color, fogColor, weight );
			}

			if ( g_bVolumetricFogEnabled )
			{
				color = ApplyOverlayLayer( CalculateVolumetricFog( worldPosition, screenPosition ), color, mode, true );
			}
		#endif

		return color;
	}
};

#endif // FOG_HLSL

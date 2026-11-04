using Sandbox.Rendering;

namespace Sandbox;

public sealed partial class Fog
{
	/// <summary>
	/// Fog supplies material settings during camera preparation without drawing a fullscreen pass.
	/// </summary>
	public override bool IsCameraEffect => true;

	/// <summary>
	/// Blend each owner's complete settings once, preserving the unresolved legacy fog share.
	/// </summary>
	public override void Render()
	{
		var result = new FogSetup { Weight = 0 };
		var components = context.Components;

		for ( int i = 0; i < components.Count; i++ )
		{
			var layer = components[i];

			// The last component on an owner supplies its settings. Apply that owner's weight only once.
			while ( i + 1 < components.Count && components[i + 1].Owner == layer.Owner )
				layer = components[++i];

			result = BlendSettings( result, ((Fog)layer.Effect).GetSettings( Camera ), layer.Weight );
		}

		TargetCamera.Fog = result.Weight > 0 ? result : null;
	}

	/// <summary>
	/// Apply one owner's influence while preserving the unresolved share of legacy fog.
	/// </summary>
	static FogSetup BlendSettings( FogSetup previous, FogSetup target, float weight )
	{
		// The first contributing layer must retain its own settings at the boundary: its weight
		// crossfades the complete legacy result in the shader, rather than fading its settings twice.
		var combinedWeight = previous.Weight * (1 - weight) + weight;
		var fraction = weight / combinedWeight;
		return BlendFog( previous, target, fraction, combinedWeight );
	}

	/// <summary>
	/// Blend normalized new-layer settings while preserving independent enabled and height contributions.
	/// </summary>
	static FogSetup BlendFog( FogSetup from, FogSetup to, float fraction, float weight )
	{
		if ( fraction >= 1 )
		{
			to.Weight = weight;
			return to;
		}

		var result = fraction < 0.5f ? from : to;
		result.Weight = weight;
		result.Color = Color.Lerp( from.Color, to.Color, fraction );
		// A constant layer supplies the same color at both endpoints without changing the active direction.
		result.DirectionalColorEnabled = from.DirectionalColorEnabled || to.DirectionalColorEnabled;
		result.SecondaryColor = Color.Lerp( from.DirectionalColorEnabled ? from.SecondaryColor : from.Color,
			to.DirectionalColorEnabled ? to.SecondaryColor : to.Color, fraction );
		result.ColorDirection = from.DirectionalColorEnabled && to.DirectionalColorEnabled
			? Vector3.Lerp( from.ColorDirection, to.ColorDirection, fraction )
			: from.DirectionalColorEnabled ? from.ColorDirection : to.ColorDirection;
		result.ColorScale = float.Lerp( from.ColorScale, to.ColorScale, fraction );
		result.StartDistance = float.Lerp( from.StartDistance, to.StartDistance, fraction );
		result.EndDistance = float.Lerp( from.EndDistance, to.EndDistance, fraction );
		result.FalloffExponent = float.Lerp( from.FalloffExponent, to.FalloffExponent, fraction );
		result.AttenuationDistance = float.Lerp( from.AttenuationDistance, to.AttenuationDistance, fraction );
		result.BaseHeight = float.Lerp( from.BaseHeight, to.BaseHeight, fraction );
		result.HeightFalloff = float.Lerp( from.HeightFalloff, to.HeightFalloff, fraction );
		result.HeightExponent = float.Lerp( from.HeightExponent, to.HeightExponent, fraction );
		result.HeightColor = Color.Lerp( from.HeightColor, to.HeightColor, fraction );
		result.DistanceEnabled = from.DistanceEnabled || to.DistanceEnabled;
		result.DistanceContribution = float.Lerp( from.DistanceEnabled ? from.DistanceContribution ?? 1 : 0, to.DistanceEnabled ? to.DistanceContribution ?? 1 : 0, fraction );
		result.HeightEnabled = from.HeightEnabled || to.HeightEnabled;
		result.HeightContribution = float.Lerp( from.HeightEnabled ? from.HeightContribution ?? 1 : 0, to.HeightEnabled ? to.HeightContribution ?? 1 : 0, fraction );
		result.SkyAffect = float.Lerp( from.SkyAffect, to.SkyAffect, fraction );
		result.MaxDistance = float.Lerp( from.MaxDistance, to.MaxDistance, fraction );
		result.SkyBlur = float.Lerp( from.SkyBlur, to.SkyBlur, fraction );
		result.SkyBlurDistanceEnabled = from.SkyBlurDistanceEnabled || to.SkyBlurDistanceEnabled;
		result.SkyBlurFar = float.Lerp( from.SkyBlurDistanceEnabled ? from.SkyBlurFar : from.SkyBlur,
			to.SkyBlurDistanceEnabled ? to.SkyBlurFar : to.SkyBlur, fraction );

		// A fixed blur has equal endpoints and inherits the other layer's ramp distances.
		result.SkyBlurDistanceRange = from.SkyBlurDistanceEnabled && to.SkyBlurDistanceEnabled
			? Vector2.Lerp( from.SkyBlurDistanceRange, to.SkyBlurDistanceRange, fraction )
			: from.SkyBlurDistanceEnabled ? from.SkyBlurDistanceRange : to.SkyBlurDistanceRange;
		result.HeightSkyBlur = float.Lerp( from.HeightSkyBlur, to.HeightSkyBlur, fraction );
		result.SunLightingEnabled = from.SunLightingEnabled || to.SunLightingEnabled;
		result.SunIntensity = float.Lerp( from.SunLightingEnabled ? from.SunIntensity : 0, to.SunLightingEnabled ? to.SunIntensity : 0, fraction );
		result.SunLobeWidth = from.SunLightingEnabled && to.SunLightingEnabled
			? float.Lerp( from.SunLobeWidth, to.SunLobeWidth, fraction )
			: from.SunLightingEnabled ? from.SunLobeWidth : to.SunLobeWidth;
		result.SunStartDistance = from.SunLightingEnabled && to.SunLightingEnabled
			? float.Lerp( from.SunStartDistance, to.SunStartDistance, fraction )
			: from.SunLightingEnabled ? from.SunStartDistance : to.SunStartDistance;
		result.SunSaturation = from.SunLightingEnabled && to.SunLightingEnabled
			? float.Lerp( from.SunSaturation, to.SunSaturation, fraction )
			: from.SunLightingEnabled ? from.SunSaturation : to.SunSaturation;
		result.SunFadeDistance = from.SunLightingEnabled && to.SunLightingEnabled
			? float.Lerp( from.SunFadeDistance, to.SunFadeDistance, fraction )
			: from.SunLightingEnabled ? from.SunFadeDistance : to.SunFadeDistance;

		// Fade enablement independently of the authored opacity cap.
		result.Enabled = from.Enabled || to.Enabled;
		result.EnabledContribution = float.Lerp( from.Enabled ? from.EnabledContribution ?? 1 : 0, to.Enabled ? to.EnabledContribution ?? 1 : 0, fraction );
		result.MaximumOpacity = float.Lerp( from.MaximumOpacity, to.MaximumOpacity, fraction );

		// A constant/sky transition needs only one texture. Distinct textures or decoders switch
		// at the dominant layer; blending incompatible sources would require an additional shader sample.
		var sameSkySource = from.SkyTexture == to.SkyTexture && from.SkyEncoding == to.SkyEncoding;
		if ( from.SkyTexture is null || to.SkyTexture is null || sameSkySource )
		{
			result.SkyTexture = to.SkyTexture ?? from.SkyTexture;
			result.SkyEncoding = to.SkyTexture is null ? from.SkyEncoding : to.SkyEncoding;
			result.SkyRotation = from.SkyTexture is not null && to.SkyTexture is not null
				? Rotation.Slerp( from.SkyRotation, to.SkyRotation, fraction )
				: to.SkyTexture is null ? from.SkyRotation : to.SkyRotation;
			result.SkyContribution = float.Lerp( from.SkyContribution, to.SkyContribution, fraction );
		}

		var sameHeightSkySource = from.HeightSkyTexture == to.HeightSkyTexture && from.HeightSkyEncoding == to.HeightSkyEncoding;
		if ( from.HeightSkyTexture is null || to.HeightSkyTexture is null || sameHeightSkySource )
		{
			result.HeightSkyTexture = to.HeightSkyTexture ?? from.HeightSkyTexture;
			result.HeightSkyEncoding = to.HeightSkyTexture is null ? from.HeightSkyEncoding : to.HeightSkyEncoding;
			result.HeightSkyRotation = from.HeightSkyTexture is not null && to.HeightSkyTexture is not null
				? Rotation.Slerp( from.HeightSkyRotation, to.HeightSkyRotation, fraction )
				: to.HeightSkyTexture is null ? from.HeightSkyRotation : to.HeightSkyRotation;
			result.HeightSkyContribution = float.Lerp( from.HeightSkyContribution, to.HeightSkyContribution, fraction );
		}

		return result;
	}
}

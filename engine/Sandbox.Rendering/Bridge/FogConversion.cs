namespace Sandbox.Rendering.Bridge;

/// <summary>
/// The engine's fog settings as Borealis's, and back for SceneLab's native references.
/// </summary>
internal static class FogConversion
{
	public static FogSettings? ToBorealis( this FogSetup? fog ) => fog is { } f ? f.ToBorealis() : null;

	public static FogSettings ToBorealis( this in FogSetup fog ) => new()
	{
		Enabled = fog.Enabled,
		DistanceEnabled = fog.DistanceEnabled,
		DistanceContribution = fog.DistanceContribution,
		EnabledContribution = fog.EnabledContribution,
		Weight = fog.Weight,
		Mode = (Borealis.FogMode)fog.Mode,
		DistanceMetric = (Borealis.FogDistanceMetric)fog.DistanceMetric,
		DirectionalColorEnabled = fog.DirectionalColorEnabled,
		SecondaryColor = fog.SecondaryColor,
		ColorDirection = fog.ColorDirection,
		ColorScale = fog.ColorScale,
		Color = fog.Color,
		StartDistance = fog.StartDistance,
		EndDistance = fog.EndDistance,
		FalloffExponent = fog.FalloffExponent,
		AttenuationDistance = fog.AttenuationDistance,
		HeightEnabled = fog.HeightEnabled,
		HeightContribution = fog.HeightContribution,
		BaseHeight = fog.BaseHeight,
		HeightFalloff = fog.HeightFalloff,
		HeightExponent = fog.HeightExponent,
		HeightColor = fog.HeightColor,
		MaximumOpacity = fog.MaximumOpacity,
		SkyAffect = fog.SkyAffect,
		MaxDistance = fog.MaxDistance,
		SkyTexture = MirrorContent.Texture( fog.SkyTexture ),
		SkyEncoding = (Borealis.FogSkyEncoding)fog.SkyEncoding,
		SkyContribution = fog.SkyContribution,
		SkyBlur = fog.SkyBlur,
		SkyBlurDistanceEnabled = fog.SkyBlurDistanceEnabled,
		SkyBlurDistanceRange = fog.SkyBlurDistanceRange,
		SkyBlurFar = fog.SkyBlurFar,
		SunLightingEnabled = fog.SunLightingEnabled,
		SunIntensity = fog.SunIntensity,
		SunLobeWidth = fog.SunLobeWidth,
		SunStartDistance = fog.SunStartDistance,
		SunFadeDistance = fog.SunFadeDistance,
		SunSaturation = fog.SunSaturation,
		SkyRotation = fog.SkyRotation,
		HeightSkyTexture = MirrorContent.Texture( fog.HeightSkyTexture ),
		HeightSkyEncoding = (Borealis.FogSkyEncoding)fog.HeightSkyEncoding,
		HeightSkyContribution = fog.HeightSkyContribution,
		HeightSkyBlur = fog.HeightSkyBlur,
		HeightSkyRotation = fog.HeightSkyRotation,
	};

	public static GradientFogSettings ToBorealis( this in GradientFogSetup fog ) => new()
	{
		Enabled = fog.Enabled,
		StartDistance = fog.StartDistance,
		EndDistance = fog.EndDistance,
		StartHeight = fog.StartHeight,
		EndHeight = fog.EndHeight,
		MaximumOpacity = fog.MaximumOpacity,
		Color = fog.Color,
		DistanceFalloffExponent = fog.DistanceFalloffExponent,
		VerticalFalloffExponent = fog.VerticalFalloffExponent,
	};

	public static GradientFogSetup ToEngine( this in GradientFogSettings fog ) => new()
	{
		Enabled = fog.Enabled,
		StartDistance = fog.StartDistance,
		EndDistance = fog.EndDistance,
		StartHeight = fog.StartHeight,
		EndHeight = fog.EndHeight,
		MaximumOpacity = fog.MaximumOpacity,
		Color = fog.Color,
		DistanceFalloffExponent = fog.DistanceFalloffExponent,
		VerticalFalloffExponent = fog.VerticalFalloffExponent,
	};
}

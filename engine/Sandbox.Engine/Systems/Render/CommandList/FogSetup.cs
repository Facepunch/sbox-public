using NativeEngine;

namespace Sandbox.Rendering;

/// <summary>
/// The attenuation model used by the fog renderer.
/// </summary>
[Expose]
public enum FogMode
{
	/// <summary>
	/// An artistic distance ramp with a configurable exponent.
	/// </summary>
	Distance,

	/// <summary>
	/// Exponential extinction, with 63.2 percent attenuation at one attenuation distance.
	/// </summary>
	Exponential
}

/// <summary>
/// A camera's fog settings. A zero weight preserves both legacy fog layers;
/// an enabled override replaces their combined result before volumetric fog is applied.
/// Use <c>default(FogSetup)</c> to inherit legacy fog; <c>new FogSetup()</c> creates a full-weight override.
/// </summary>
public struct FogSetup
{
	/// <summary>
	/// Creates a full-weight exponential fog override with default settings.
	/// A zero-initialized default(FogSetup) instead preserves legacy fog.
	/// </summary>
	public FogSetup()
	{
		Enabled = true;
		DistanceEnabled = true;
		Weight = 1;
		Mode = FogMode.Exponential;
		Color = Color.White;
		EndDistance = 4096;
		FalloffExponent = 1;
		AttenuationDistance = 2000;
		HeightFalloff = 1024;
		HeightExponent = 1;
		HeightColor = Color.White;
		MaximumOpacity = 1;
		SkyAffect = 1;
		MaxDistance = 100000;
		SkyBlur = 1;
		SkyBlurDistanceEnabled = true;
		SkyBlurDistanceRange = new( 0, 2000 );
		SunLightingEnabled = true;
		SunIntensity = 1;
		SunLobeWidth = 35;
		SunSaturation = 1;
		SkyRotation = Rotation.Identity;
		HeightSkyRotation = Rotation.Identity;
		HeightSkyBlur = 0.5f;
	}

	/// <summary>
	/// Whether this override adds fog. A disabled override with nonzero weight suppresses legacy fog.
	/// </summary>
	public bool Enabled { get; set; }

	/// <summary>
	/// Whether distance fog contributes, independently of height fog.
	/// </summary>
	public bool DistanceEnabled { get; set; }

	/// <summary>
	/// Resolved distance influence during volume blending. Null uses full influence when enabled.
	/// </summary>
	internal float? DistanceContribution { get; set; }

	/// <summary>
	/// Resolved enable influence during volume blending. Null uses full influence when enabled.
	/// </summary>
	internal float? EnabledContribution { get; set; }

	/// <summary>
	/// Influence over the legacy gradient and cubemap fog result, from zero to one.
	/// </summary>
	public float Weight { get; set; }

	/// <summary>
	/// Distance attenuation model.
	/// </summary>
	public FogMode Mode { get; set; }

	/// <summary>
	/// Distance fog color and sky tint. Alpha is ignored; use MaximumOpacity instead.
	/// </summary>
	public Color Color { get; set; }

	/// <summary>
	/// Clear distance before fog starts, in world units.
	/// </summary>
	public float StartDistance { get; set; }

	/// <summary>
	/// Full-fog distance for the artistic distance model, in world units.
	/// </summary>
	public float EndDistance { get; set; }

	/// <summary>
	/// Exponent applied to the artistic distance ramp.
	/// </summary>
	public float FalloffExponent { get; set; }

	/// <summary>
	/// Distance removing 63.2 percent of background light, in world units.
	/// </summary>
	public float AttenuationDistance { get; set; }

	/// <summary>
	/// Enables the independent height gradient.
	/// </summary>
	public bool HeightEnabled { get; set; }

	/// <summary>
	/// Resolved height influence during volume blending. Null uses full influence when enabled.
	/// </summary>
	internal float? HeightContribution { get; set; }

	/// <summary>
	/// World height where the height gradient reaches full opacity.
	/// </summary>
	public float BaseHeight { get; set; }

	/// <summary>
	/// Signed world distance from full height fog to clear. Zero creates a hard boundary.
	/// </summary>
	public float HeightFalloff { get; set; }

	/// <summary>
	/// Curve applied to the height gradient.
	/// </summary>
	public float HeightExponent { get; set; }

	/// <summary>
	/// Independent height fog color, with alpha controlling its maximum opacity.
	/// </summary>
	public Color HeightColor { get; set; }

	/// <summary>
	/// Upper limit on distance fog opacity, independent of color.
	/// </summary>
	public float MaximumOpacity { get; set; }

	/// <summary>
	/// Additional opacity multiplier for sky pixels, from zero to one.
	/// </summary>
	public float SkyAffect { get; set; }

	/// <summary>
	/// Distance used to evaluate sky fog, independent of the camera's far clip plane.
	/// </summary>
	public float MaxDistance { get; set; }

	/// <summary>
	/// Optional cubemap providing directional fog color. Cube arrays use their first cube.
	/// </summary>
	public Texture SkyTexture { get; set; }

	/// <summary>
	/// Color encoding stored in the sky cubemap. Ordinary HDR and RGB textures use Rgb.
	/// </summary>
	public FogSkyEncoding SkyEncoding { get; set; }

	/// <summary>
	/// Blend between constant color and the tinted cubemap, from zero to one.
	/// </summary>
	public float SkyContribution { get; set; }

	/// <summary>
	/// Cubemap blur, from the sharpest mip at zero to the coarsest mip at one.
	/// </summary>
	public float SkyBlur { get; set; }

	/// <summary>
	/// Use SkyBlur near the camera and SkyBlurFar beyond SkyBlurDistanceRange.
	/// </summary>
	public bool SkyBlurDistanceEnabled { get; set; }

	/// <summary>
	/// Start and end distances of the sky blur ramp, in world units.
	/// </summary>
	public Vector2 SkyBlurDistanceRange { get; set; }

	/// <summary>
	/// Far sky blur, from the sharpest mip at zero to the coarsest mip at one.
	/// </summary>
	public float SkyBlurFar { get; set; }

	/// <summary>
	/// Add an unshadowed glow from the view's dominant sun to distance fog.
	/// </summary>
	public bool SunLightingEnabled { get; set; }

	/// <summary>
	/// Multiplier for the dominant sun's color, independent of its volumetric fog strength.
	/// </summary>
	public float SunIntensity { get; set; }

	/// <summary>
	/// Angle from the sun at half intensity, in degrees.
	/// </summary>
	public float SunLobeWidth { get; set; }

	/// <summary>
	/// Distance from the camera where the sun glow starts fading in, in world units.
	/// </summary>
	public float SunStartDistance { get; set; }

	/// <summary>
	/// Distance over which sun lighting smoothly fades in. Zero gives a hard start; negative values reverse the fade.
	/// </summary>
	public float SunFadeDistance { get; set; }

	/// <summary>
	/// Sun glow saturation. Zero is greyscale, one preserves the light's color, and higher values strengthen it.
	/// </summary>
	public float SunSaturation { get; set; }

	/// <summary>
	/// Rotation from world directions into the sky cubemap's local space.
	/// </summary>
	public Rotation SkyRotation { get; set; }

	/// <summary>
	/// Optional cubemap providing directional height fog color. Cube arrays use their first cube.
	/// </summary>
	public Texture HeightSkyTexture { get; set; }

	/// <summary>
	/// Color encoding stored in the sky cubemap. Ordinary HDR and RGB textures use Rgb.
	/// </summary>
	public FogSkyEncoding HeightSkyEncoding { get; set; }

	/// <summary>
	/// Blend between constant color and the tinted cubemap, from zero to one.
	/// </summary>
	public float HeightSkyContribution { get; set; }

	/// <summary>
	/// Cubemap blur, from the sharpest mip at zero to the coarsest mip at one.
	/// </summary>
	public float HeightSkyBlur { get; set; }

	/// <summary>
	/// Rotation from world directions into the sky cubemap's local space.
	/// </summary>
	public Rotation HeightSkyRotation { get; set; }

	/// <summary>
	/// Packs settings directly into the per-view constant buffer, including scaled 3D skyboxes.
	/// </summary>
	internal FogConstants ToConstants( float worldUnitsPerUnit = 1, float heightOffset = 0 )
	{
		// Native CCameraRenderer::CreateView accepts every positive skybox scale.
		var scale = worldUnitsPerUnit > 0 ? worldUnitsPerUnit : 1.0f;
		var weight = Weight.Clamp( 0, 1 );
		var opacity = Enabled ? MaximumOpacity.Clamp( 0, 1 ) : 0;
		// Preserve the source texture dimension for bindless sampling.
		var texture = SkyTexture is { IsValid: true } && SkyTexture.Desc.IsCube ? SkyTexture : null;
		var isArray = texture?.Desc.IsArray ?? false;
		var skyContribution = texture is not null ? SkyContribution.Clamp( 0, 1 ) : 0;

		// Preserve the source texture dimension for bindless sampling.
		var heightTexture = HeightSkyTexture is { IsValid: true } && HeightSkyTexture.Desc.IsCube ? HeightSkyTexture : null;
		var heightIsArray = heightTexture?.Desc.IsArray ?? false;
		var heightSkyContribution = heightTexture is not null ? HeightSkyContribution.Clamp( 0, 1 ) : 0;

		var constants = new FogConstants();
		constants.Control = new Vector4( weight, opacity, (float)Mode, HeightEnabled ? (HeightContribution ?? 1).Clamp( 0, 1 ) : 0 );
		constants.OpacityScale = Enabled ? (EnabledContribution ?? 1).Clamp( 0, 1 ) : 0;
		// Sanitize before dividing so small distances scale consistently into skybox space.
		constants.Distance = new Vector4( MathF.Max( 0, StartDistance ) / scale, MathF.Max( 0, EndDistance ) / scale, MathF.Max( FalloffExponent, 0.001f ), MathF.Max( AttenuationDistance, 0.001f ) / scale );
		constants.Height = new Vector4( BaseHeight / scale + heightOffset, HeightFalloff / scale, MathF.Max( MaxDistance, 0.001f ) / scale, SkyAffect.Clamp( 0, 1 ) );
		constants.LayerParams = new Vector2( DistanceEnabled ? (DistanceContribution ?? 1).Clamp( 0, 1 ) : 0, MathF.Max( HeightExponent, 0.001f ) );
		constants.HeightColor = new Vector4( HeightColor.r, HeightColor.g, HeightColor.b, HeightColor.a.Clamp( 0, 1 ) );
		constants.Color = new Vector3( Color.r, Color.g, Color.b );

		// Peak-normalized HG reaches half intensity at the authored lobe width.
		// 2*sin(angle/2)^2 avoids cancellation at narrow angles; guard the zero-width singularity.
		var sunSin = MathF.Sin( SunLobeWidth * (MathF.PI / 360) );
		var sunShape = (MathF.Pow( 2, 2.0f / 3.0f ) - 1) / MathF.Max( 2 * sunSin * sunSin, 0.000001f );
		constants.Sun = new Vector2( SunLightingEnabled ? SunIntensity : 0, sunShape );
		constants.SunSettings = new Vector4( SunStartDistance / scale, SunSaturation, SunFadeDistance / scale, 0 );

		var maxMip = Math.Max( 0, (texture?.Mips ?? 1) - 1 );
		var nearMip = SkyBlur.Clamp( 0, 1 ) * maxMip;
		var farMip = SkyBlurDistanceEnabled ? SkyBlurFar.Clamp( 0, 1 ) * maxMip : nearMip;
		var blurStart = MathF.Max( 0, SkyBlurDistanceRange.x );
		var blurLength = MathF.Max( SkyBlurDistanceRange.y - blurStart, 0.001f );

		constants.Sky = new Vector2( skyContribution, nearMip );
		constants.SkyBlurRamp = new Vector4( blurStart / scale, scale / blurLength, farMip, maxMip );
		constants.SkyFormat = new Vector2( (float)SkyEncoding, isArray ? 1 : 0 );
		// Shader matrices use column-major storage.
		constants.SkyTransform = Matrix.FromTransform( new Transform( Vector3.Zero, SkyRotation, 1 ) ).Transpose();

		constants.HeightSky = new Vector2( heightSkyContribution, HeightSkyBlur.Clamp( 0, 1 ) * Math.Max( 0, (heightTexture?.Mips ?? 1) - 1 ) );
		constants.HeightSkyFormat = new Vector2( (float)HeightSkyEncoding, heightIsArray ? 1 : 0 );
		// Shader matrices use column-major storage.
		constants.HeightSkyTransform = Matrix.FromTransform( new Transform( Vector3.Zero, HeightSkyRotation, 1 ) ).Transpose();

		// Reuse a sample only when both layers use the same complete sampling frame.
		constants.HeightSkyShared = texture is not null && texture == heightTexture
			&& SkyEncoding == HeightSkyEncoding && SkyRotation == HeightSkyRotation
			&& nearMip == farMip
			&& SkyBlur.Clamp( 0, 1 ) == HeightSkyBlur.Clamp( 0, 1 ) ? 1.0f : 0.0f;

		constants.SkyTextureIndex = GetTextureIndex( texture );
		constants.HeightSkyTextureIndex = GetTextureIndex( heightTexture );
		return constants;
	}

	/// <summary>
	/// Resolve the sRGB cube or array descriptor and keep its mips resident for bindless sampling.
	/// </summary>
	static int GetTextureIndex( Texture texture )
	{
		if ( texture is null )
			return -1;

		texture.MarkUsed( ushort.MaxValue );
		return g_pRenderDevice.GetTextureViewIndex( texture.native, (byte)RenderColorSpace.RENDER_SRGB, RenderTextureDimension.RENDER_TEXTURE_DIMENSION_INVALID );
	}
}

/// <summary>
/// Color encoding of a sky cubemap sampled by fog.
/// </summary>
public enum FogSkyEncoding
{
	/// <summary>
	/// Ordinary RGB, including floating point and BC6H HDR textures.
	/// </summary>
	Rgb,

	/// <summary>
	/// The legacy sky shader's scaled YCoCg encoding.
	/// </summary>
	YCoCg,

	/// <summary>
	/// The legacy sky shader's RGB multiplier HDR encoding.
	/// </summary>
	Rgbm
}

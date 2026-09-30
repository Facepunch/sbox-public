using Sandbox.Rendering;

namespace Sandbox.SceneRenderer;

/// <summary>
/// World sun, ambient light, sky and fog settings.
/// </summary>
public sealed class SceneLighting
{
	/// <summary>
	/// The direction sunlight travels, pointing away from the sun.
	/// </summary>
	public Vector3 SunDirection { get; set; } = new Vector3( 0.45f, 0.3f, -0.85f ).Normal;

	/// <summary>
	/// Linear color of the sun. Black turns it off.
	/// </summary>
	public Color SunColor { get; set; } = new Color( 0.9f, 0.85f, 0.75f );

	/// <summary>
	/// Enable sun cascades fitted to the main view.
	/// </summary>
	public bool SunShadows { get; set; } = true;

	/// <summary>
	/// Sun cascade count, clamped to 1–4. Defaults to four.
	/// </summary>
	public int SunShadowCascades
	{
		get;
		set => field = Math.Clamp( value, 1, ShadowSystem.MaxCascades );
	} = 4;

	/// <summary>
	/// Cascade spacing: 0 is uniform, 1 logarithmic with more near-field detail.
	/// </summary>
	public float SunShadowSplitRatio { get; set; } = 0.91f;

	/// <summary>
	/// Zero is the softest the shadow filter goes, one the sharpest - <c>Light.ShadowHardness</c>.
	/// </summary>
	public float SunShadowHardness { get; set; }

	/// <summary>
	/// Extra depth bias on the sun's shadow lookups, scaled per cascade - <c>Light.ShadowBias</c>.
	/// </summary>
	public float SunShadowBias { get; set; }

	/// <summary>
	/// Baked-light index, or -1 for none. Enables indexed sun lighting through <see cref="SunBaked"/>.
	/// </summary>
	public int SunBakeIndex { get; set; } = -1;

	/// <summary>
	/// Exclude static casters from cascades because their shadows are baked
	/// (<c>LIGHTTYPE_FLAGS_BAKED</c>, <c>ShadowMapper.FindOrCreateDirectionalShadowMaps</c>).
	/// </summary>
	public bool SunShadowsBaked { get; set; }

	/// <summary>
	/// Enable sun contact shadows for cameras with stages (<c>DirectionalLight.ContactShadows</c>).
	/// </summary>
	public bool SunContactShadows { get; set; }

	/// <summary>
	/// Sun contribution to volumetric fog (<c>g_DirectionalLightColor.w</c>).
	/// </summary>
	public float SunFogStrength { get; set; } = 1.0f;

	/// <summary>
	/// The camera's volumetric fog (<see cref="VolumetricFogSettings"/>), in a world with <see cref="RenderWorld.FogVolumes"/>.
	/// </summary>
	public VolumetricFogSettings VolumetricFog { get; } = new();

	/// <summary>
	/// Bridge-provided baked sun data (<c>PackBakedDirectionalGPULight</c>).
	/// </summary>
	internal Features.LightBinnerFeature.GpuLight? SunBaked { get; set; }

	/// <summary>
	/// Linear ambient colour. Alpha blends probe lighting (0) with flat ambient (1), matching <c>SceneWorld.AmbientLightColor</c>.
	/// </summary>
	public Color AmbientColor { get; set; } = new Color( 0.12f, 0.14f, 0.18f );

	/// <summary>
	/// Distance and height fog (<c>SceneWorld.GradientFog</c>).
	/// </summary>
	public GradientFogSetup GradientFog { get; set; }

	/// <summary>
	/// Cubemap-coloured fog (<c>SceneCamera.CubemapFog</c>).
	/// </summary>
	public CubemapFogController CubemapFog { get; } = new();

	/// <summary>
	/// 2D sky material (<c>SceneSkyBox</c>); null shows clear colour. Add an <see cref="EnvMapObject"/> for sky lighting.
	/// </summary>
	public Material SkyMaterial { get; set; }

	/// <summary>
	/// Multiplied into the sky - <c>SceneSkyBox.SkyTint</c>.
	/// </summary>
	public Color SkyTint { get; set; } = Color.White;

	/// <summary>
	/// Sky orientation.
	/// </summary>
	public Rotation SkyRotation { get; set; } = Rotation.Identity;

	/// <summary>
	/// The sky's own fog - <c>SceneSkyBox.FogParams</c>.
	/// </summary>
	public SceneSkyBox.FogParamInfo SkyFog { get; set; }

	/// <summary>
	/// Wind strength, grass/tree frequencies and high-frequency strength (<c>g_vWindStrengthFreqMulHighStrength</c>).
	/// </summary>
	public Vector4 WindStrengthFreq { get; set; }

	/// <summary>
	/// The wind's direction (<c>g_vWindDirection</c>).
	/// </summary>
	public Vector4 WindDirection { get; set; }

	internal bool SunEnabled => SunColor.r > 0 || SunColor.g > 0 || SunColor.b > 0;
}

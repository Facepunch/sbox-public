using System;
using NativeEngine;


namespace Sandbox.Engine.Settings;

/// <summary>
/// User graphics settings
/// </summary>
public partial class RenderSettings
{
	internal static RenderSettings Instance = new RenderSettings();

	internal CookieContainer VideoSettings { get; } = new( "video", true );

	public event Action OnVideoSettingsChanged;
	internal RenderQualityProfiles Config { get; } = new();

	internal RenderSettings()
	{
		Config.SetDefaults( this );
	}

	public int MaxFrameRate
	{
		get => ConVarSystem.GetInt( "fps_max", 100, true );
		set => ConVarSystem.SetInt( "fps_max", value, true );
	}

	public int MaxFrameRateInactive
	{
		get => ConVarSystem.GetInt( "fps_max_inactive", 100, true );
		set => ConVarSystem.SetInt( "fps_max_inactive", value, true );
	}

	public float DefaultFOV
	{
		get => ConVarSystem.GetFloat( "default_fov", 80, true );
		set => ConVarSystem.SetFloat( "default_fov", value, true );
	}

	public TextureQuality TextureQuality
	{
		get => VideoSettings.Get<TextureQuality>( "texture.quality", TextureQuality.High );
		set
		{
			VideoSettings.Set<TextureQuality>( "texture.quality", value );
			Config.SetGroupConVars( "TextureQuality", value.ToString() );
		}
	}

	public VolumetricFogQuality VolumetricFogQuality
	{
		get => VideoSettings.Get<VolumetricFogQuality>( "volumetricfog.quality", VolumetricFogQuality.High );
		set
		{
			VideoSettings.Set<VolumetricFogQuality>( "volumetricfog.quality", value );
			Config.SetGroupConVars( "VolumetricFogQuality", value.ToString() );
		}
	}

	public PostProcessQuality PostProcessQuality
	{
		get => VideoSettings.Get<PostProcessQuality>( "postprocess.quality", PostProcessQuality.High );
		set
		{
			VideoSettings.Set<PostProcessQuality>( "postprocess.quality", value );
			Config.SetGroupConVars( "PostProcessQuality", value.ToString() );
		}
	}

	public ShadowQuality ShadowQuality
	{
		get => VideoSettings.Get<ShadowQuality>( "shadow.quality", ShadowQuality.High );
		set
		{
			VideoSettings.Set<ShadowQuality>( "shadow.quality", value );
			Config.SetGroupConVars( "ShadowQuality", value.ToString() );
		}
	}

	public float MotionBlurScale
	{
		get => VideoSettings.Get<float>( "motionblur.scale", 1.0f );
		set
		{
			VideoSettings.Set<float>( "motionblur.scale", value );
			MotionBlur.UserScale = value;
		}
	}

	public UpscalerMode UpscalerMode
	{
		get => VideoSettings.Get<UpscalerMode>( "upscaler.mode", UpscalerMode.Off );
		set
		{
			VideoSettings.Set<UpscalerMode>( "upscaler.mode", value );
			ConVarSystem.SetInt( "r_upscaling", (int)value, true );
		}
	}

	/// <summary>
	/// Render-resolution scale used by Stretch (40-100%) and FSR1 (50-100%) modes.
	/// </summary>
	public float UpscalerRenderScale
	{
		get => VideoSettings.Get<float>( "upscaler.render_scale", 0.75f );
		set
		{
			float v = Math.Clamp( value, 0.4f, 1.0f );
			VideoSettings.Set<float>( "upscaler.render_scale", v );
			ConVarSystem.SetFloat( "r_upscaler_render_scale", v, true );
		}
	}

	/// <summary>FSR1 RCAS sharpness in [0..1]. Only used when <see cref="UpscalerMode"/> is FSR1.</summary>
	public float Fsr1Sharpness
	{
		get => VideoSettings.Get<float>( "upscaler.fsr1_sharpness", 0.25f );
		set
		{
			float v = Math.Clamp( value, 0.0f, 1.0f );
			VideoSettings.Set<float>( "upscaler.fsr1_sharpness", v );
			ConVarSystem.SetFloat( "r_fsr_rcas_sharpness", v, true );
		}
	}

	/// <summary>
	/// FSR3 quality preset (Ultra Performance / Performance / Balanced / Quality), maps
	/// to a discrete render-resolution multiplier. Only used when <see cref="UpscalerMode"/>
	/// is <see cref="UpscalerMode.FSR3"/>.
	/// </summary>
	public Fsr3UpscalerQuality Fsr3UpscalerQuality
	{
		get => VideoSettings.Get<Fsr3UpscalerQuality>( "upscaler.quality", Fsr3UpscalerQuality.Performance );
		set
		{
			VideoSettings.Set<Fsr3UpscalerQuality>( "upscaler.quality", value );
			if ( value != Fsr3UpscalerQuality.Off )
				ConVarSystem.SetInt( "r_fsr3_quality", (int)value, true );
		}
	}

	/// <summary>FSR3 RCAS sharpness in [0..1]. Only used when <see cref="UpscalerMode"/> is FSR3.</summary>
	public float Fsr3Sharpness
	{
		get => VideoSettings.Get<float>( "upscaler.fsr3_sharpness", 0.5f );
		set
		{
			float v = Math.Clamp( value, 0.0f, 1.0f );
			VideoSettings.Set<float>( "upscaler.fsr3_sharpness", v );
			ConVarSystem.SetFloat( "r_fsr3_sharpness", v, true );
		}
	}

	public void ResetVideoConfig()
	{
		int desktopWidth = 0;
		int desktopHeight = 0;
		uint desktopRefreshRate = 0;
		EngineGlobal.Plat_GetDesktopResolution( EngineGlobal.Plat_GetDefaultMonitorIndex(), ref desktopWidth, ref desktopHeight, ref desktopRefreshRate );
		ResolutionWidth = desktopWidth;
		ResolutionHeight = desktopHeight;

		Fullscreen = true;
		Borderless = true;
		VSync = true;
		AntiAliasQuality = MultisampleAmount.Multisample8x;
		MaxFrameRate = 300;
		MaxFrameRateInactive = 60;
		DefaultFOV = 75;
		UpscalerMode = UpscalerMode.Off;
		UpscalerRenderScale = 0.75f;
		Fsr1Sharpness = 0.25f;
		Fsr3UpscalerQuality = Fsr3UpscalerQuality.Performance;
		Fsr3Sharpness = 0.5f;

		VideoSettings.Save();
	}

	public void Apply()
	{
		ApplyVideoMode();

		OnVideoSettingsChanged?.Invoke();

		VideoSettings.Save();
	}

	/// <summary>
	/// We want benchmarks to have all similar settings. Set them here.
	/// The only fluctuations we should see are resolution and hardware.
	/// </summary>
	internal void ApplySettingsForBenchmarks()
	{
		ResetVideoConfig();

		Fullscreen = false;
		Borderless = false;
		VSync = false;
		AntiAliasQuality = MultisampleAmount.Multisample8x;
		MaxFrameRate = 10000;
		MaxFrameRateInactive = 10000;
		DefaultFOV = 75;
		ResolutionWidth = 1920;
		ResolutionHeight = 1080;

		NativeEngine.RenderDeviceManager.ChangeVideoMode( Fullscreen, Borderless, VSync, ResolutionWidth, ResolutionHeight, AntiAliasQuality.ToEngine() );
	}

}

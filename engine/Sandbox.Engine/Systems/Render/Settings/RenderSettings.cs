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

	/// <summary>
	/// Push the stored quality levels into their convars. Separate from the constructor because
	/// the instance gets built the first time anything reads a setting, which is well before the
	/// managed convars are registered.
	/// </summary>
	internal void ApplyQualityProfiles()
	{
		MigratePostProcessSettings();

		Config.ApplyAll( this );
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

	public int MaxFrameRateMenu
	{
		get => ConVarSystem.GetInt( "fps_max_menu", 120, true );
		set => ConVarSystem.SetInt( "fps_max_menu", value, true );
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
			ApplyMotionBlur();
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

	/// <summary>
	/// DLSS quality preset (Ultra Performance / Performance / Balanced / Quality / DLAA), maps
	/// to a discrete render-resolution multiplier. Only used when <see cref="UpscalerMode"/>
	/// is <see cref="UpscalerMode.DLSS"/>. DLSS has no sharpness control.
	/// </summary>
	public DlssQuality DlssQuality
	{
		get => VideoSettings.Get<DlssQuality>( "upscaler.dlss_quality", DlssQuality.Performance );
		set
		{
			VideoSettings.Set<DlssQuality>( "upscaler.dlss_quality", value );
			if ( value != DlssQuality.Off )
				ConVarSystem.SetInt( "r_dlss_quality", (int)value, true );
		}
	}

	/// <summary>
	/// Returns whether the given <see cref="UpscalerMode"/> is usable on the current graphics
	/// device. Off / Stretch / FSR1 / FSR3 are always available; DLSS requires NVIDIA hardware
	/// with NGX support and is queried from the native render device.
	/// </summary>
	public static bool IsUpscalerModeSupported( UpscalerMode mode )
	{
		// UpscalerType from src/public/rendersystem/iupscaler.h: NONE=0, AMD_FSR3=2, NVIDIA_DLSS=3.
		const int UPSCALER_NVIDIA_DLSS = 3;

		return mode switch
		{
			UpscalerMode.DLSS => NativeEngine.RenderDeviceManager.IsUpscalerSupported( UPSCALER_NVIDIA_DLSS ),
			_ => true,
		};
	}

	public void ResetVideoConfig()
	{
		ResetDisplayConfig();
		ResetGraphicsConfig();
	}

	/// <summary>Window, resolution, vsync, frame rate caps and field of view.</summary>
	public void ResetDisplayConfig()
	{
		int desktopWidth = 0;
		int desktopHeight = 0;
		uint desktopRefreshRate = 0;
		EngineGlobal.Plat_GetDesktopResolution( EngineGlobal.Plat_GetDefaultMonitorIndex(), ref desktopWidth, ref desktopHeight, ref desktopRefreshRate );
		ResolutionWidth = desktopWidth;
		ResolutionHeight = desktopHeight;

		Fullscreen = false;
		Borderless = true;
		VSync = true;
		MaxFrameRate = 300;
		MaxFrameRateInactive = 60;
		MaxFrameRateMenu = 120;
		DefaultFOV = 75;

		VideoSettings.Save();
	}

	/// <summary>Quality and upscaling, back to whatever this machine detects.</summary>
	public void ResetGraphicsConfig()
	{
		UpscalerMode = UpscalerMode.Off;
		UpscalerRenderScale = 0.75f;
		Fsr1Sharpness = 0.25f;
		Fsr3UpscalerQuality = Fsr3UpscalerQuality.Performance;
		Fsr3Sharpness = 0.5f;
		DlssQuality = DlssQuality.Performance;
		MotionBlurScale = 1.0f;

		ApplyPreset( DetectPreset() );

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

		// Fixed rung, so runs are comparable across machines.
		ApplyPreset( GraphicsPreset.High );

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

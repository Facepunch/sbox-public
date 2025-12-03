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

	public float MotionBlurScale
	{
		get => VideoSettings.Get<float>( "motionblur.scale", 1.0f );
		set
		{
			VideoSettings.Set<float>( "motionblur.scale", value );
			MotionBlur.UserScale = value;
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

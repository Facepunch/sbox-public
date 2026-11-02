using Sandbox.Diagnostics;

namespace Sandbox.Engine.Settings;

public partial class RenderSettings
{
	/// <summary>Quality levels a preset writes. Upscaling is a taste call; every tier uses CMAA2.</summary>
	readonly record struct PresetValues(
		TextureQuality Texture,
		ShadowQuality Shadow,
		PostProcessQuality PostProcess,
		VolumetricFogQuality Fog,
		WaterQuality Water,
		MultisampleAmount AntiAliasQuality,
		AntiAliasMode AntiAliasMode
	);

	static PresetValues ValuesFor( GraphicsPreset preset ) => preset switch
	{
		GraphicsPreset.Low => new( TextureQuality.Low, ShadowQuality.Low, PostProcessQuality.Off, VolumetricFogQuality.Off, WaterQuality.Low, MultisampleAmount.MultisampleNone, AntiAliasMode.Cmaa2 ),
		GraphicsPreset.Medium => new( TextureQuality.Medium, ShadowQuality.Medium, PostProcessQuality.Low, VolumetricFogQuality.Medium, WaterQuality.Medium, MultisampleAmount.Multisample2x, AntiAliasMode.Cmaa2 ),
		GraphicsPreset.High => new( TextureQuality.High, ShadowQuality.High, PostProcessQuality.Medium, VolumetricFogQuality.High, WaterQuality.High, MultisampleAmount.Multisample4x, AntiAliasMode.None ),
		GraphicsPreset.Ultra => new( TextureQuality.Ultra, ShadowQuality.Ultra, PostProcessQuality.High, VolumetricFogQuality.High, WaterQuality.Ultra, MultisampleAmount.Multisample4x, AntiAliasMode.None ),
		_ => default
	};

	static readonly GraphicsPreset[] Presets = [GraphicsPreset.Low, GraphicsPreset.Medium, GraphicsPreset.High, GraphicsPreset.Ultra];

	/// <summary>
	/// Which preset the stored settings match, or <see cref="GraphicsPreset.Custom"/> if none do.
	/// </summary>
	internal GraphicsPreset MatchPreset()
	{
		var current = new PresetValues( TextureQuality, ShadowQuality, PostProcessQuality, VolumetricFogQuality, WaterQuality, AntiAliasQuality, AntiAliasMode );

		foreach ( var preset in Presets )
		{
			if ( ValuesFor( preset ) == current )
				return preset;
		}

		return GraphicsPreset.Custom;
	}

	/// <summary>What a graphics preset writes, by setting name.</summary>
	internal static Dictionary<string, string> SettingsFor( GraphicsPreset preset )
	{
		var values = ValuesFor( preset );
		var settings = SettingsFor( values.PostProcess );

		settings[nameof( TextureQuality )] = values.Texture.ToString();
		settings[nameof( ShadowQuality )] = values.Shadow.ToString();
		settings[nameof( VolumetricFogQuality )] = values.Fog.ToString();
		settings[nameof( WaterQuality )] = values.Water.ToString();
		settings[nameof( AntiAliasQuality )] = GetSupportedAntiAliasQuality( values.AntiAliasQuality ).ToString();
		settings[nameof( AntiAliasMode )] = values.AntiAliasMode.ToString();

		return settings;
	}

	/// <summary>
	/// Write every setting a preset covers. Call <see cref="Apply"/> afterwards to save and take
	/// the new video mode. <see cref="GraphicsPreset.Custom"/> isn't pickable and does nothing.
	/// </summary>
	internal void ApplyPreset( GraphicsPreset preset )
	{
		if ( preset == GraphicsPreset.Custom )
			return;

		var values = ValuesFor( preset );

		TextureQuality = values.Texture;
		ShadowQuality = values.Shadow;
		PostProcessQuality = values.PostProcess;
		VolumetricFogQuality = values.Fog;
		WaterQuality = values.Water;
		AntiAliasQuality = values.AntiAliasQuality;
		AntiAliasMode = values.AntiAliasMode;
	}

	/// <summary>Pick the rung this machine should start on. A starting point, not a benchmark.</summary>
	internal static GraphicsPreset DetectPreset()
	{
		const ulong GB = 1024UL * 1024UL * 1024UL;

		var vram = SystemInfo.GpuMemory;
		var ram = SystemInfo.TotalMemory;
		var cores = SystemInfo.ProcessorCount;

		// Integrated parts report the shared system heap as video memory, so VRAM says nothing.
		if ( SystemInfo.GpuIntegrated )
			return GraphicsPreset.Low;

		if ( vram >= Class( 12, GB ) && ram >= 16 * GB && cores >= 12 )
			return GraphicsPreset.Ultra;

		if ( vram >= Class( 6, GB ) && ram >= 16 * GB && cores >= 8 )
			return GraphicsPreset.High;

		if ( vram >= Class( 4, GB ) && cores >= 6 )
			return GraphicsPreset.Medium;

		return GraphicsPreset.Low;
	}

	/// <summary>
	/// Reported floor for a card sold as this many GB. Device-local heaps read under the capacity
	/// on the box (an 8GB card reads ~7.4GB), so a round threshold demotes boundary cards.
	/// </summary>
	static ulong Class( ulong gigabytes, ulong gb ) => gigabytes * gb - (gb * 3 / 4);

	/// <summary>
	/// Pick for the user on first launch. Runs once and leaves a marker; after that the stored
	/// settings are theirs.
	/// </summary>
	internal void EnsureFirstRunPreset()
	{
		if ( VideoSettings.Get<bool>( AutoDetectedKey, false ) )
			return;

		VideoSettings.Set<bool>( AutoDetectedKey, true );

		var preset = DetectPreset();
		ApplyPreset( preset );

		// Not Apply(): the window was created from the command line, and switching it to the saved
		// mode here would drop -sw/-w/-h (e.g. windowed local instances going borderless).
		ApplyVideoMode( honourCommandLine: true );
		OnVideoSettingsChanged?.Invoke();
		VideoSettings.Save();

		log.Info( $"Detected graphics preset: {preset} (gpu: {SystemInfo.Gpu}, vram: {SystemInfo.GpuMemory / (1024 * 1024)}MB, integrated: {SystemInfo.GpuIntegrated}, cores: {SystemInfo.ProcessorCount}, ram: {SystemInfo.TotalMemory / (1024 * 1024)}MB)" );
	}

	const string AutoDetectedKey = "quality.autodetected";

	static readonly Logger log = new( "RenderSettings" );
}

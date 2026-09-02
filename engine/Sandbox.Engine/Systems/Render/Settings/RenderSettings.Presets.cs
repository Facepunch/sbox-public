using Sandbox.Diagnostics;

namespace Sandbox.Engine.Settings;

public partial class RenderSettings
{
	/// <summary>
	/// What a preset writes. No upscaler, because ghosting is a taste call. No rung turns
	/// anti-aliasing off either, since without it foliage and hair go to a hard cutout.
	/// </summary>
	readonly record struct PresetValues(
		TextureQuality Texture,
		ShadowQuality Shadow,
		PostProcessQuality PostProcess,
		VolumetricFogQuality Fog,
		MultisampleAmount AntiAlias );

	static PresetValues ValuesFor( GraphicsPreset preset ) => preset switch
	{
		GraphicsPreset.Low => new( TextureQuality.Low, ShadowQuality.Low, PostProcessQuality.Off, VolumetricFogQuality.Off, MultisampleAmount.Multisample2x ),
		GraphicsPreset.Medium => new( TextureQuality.Medium, ShadowQuality.Medium, PostProcessQuality.Low, VolumetricFogQuality.Medium, MultisampleAmount.Multisample2x ),
		GraphicsPreset.High => new( TextureQuality.High, ShadowQuality.High, PostProcessQuality.Medium, VolumetricFogQuality.High, MultisampleAmount.Multisample4x ),
		GraphicsPreset.Ultra => new( TextureQuality.Ultra, ShadowQuality.Ultra, PostProcessQuality.High, VolumetricFogQuality.Ultra, MultisampleAmount.Multisample8x ),
		_ => default
	};

	/// <summary>What a graphics preset writes, by setting name.</summary>
	internal static Dictionary<string, string> SettingsFor( GraphicsPreset preset )
	{
		var values = ValuesFor( preset );
		var settings = SettingsFor( values.PostProcess );

		settings[nameof( TextureQuality )] = values.Texture.ToString();
		settings[nameof( ShadowQuality )] = values.Shadow.ToString();
		settings[nameof( VolumetricFogQuality )] = values.Fog.ToString();
		settings[nameof( AntiAliasQuality )] = values.AntiAlias.ToString();

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
		AntiAliasQuality = values.AntiAlias;
	}

	/// <summary>Pick the rung this machine should start on. A starting point, not a benchmark.</summary>
	internal static GraphicsPreset DetectPreset()
	{
		const ulong GB = 1024UL * 1024UL * 1024UL;

		var vram = SystemInfo.GpuMemory;
		var ram = SystemInfo.TotalMemory;
		var cores = SystemInfo.ProcessorCount;

		// Integrated parts report the shared system heap as video memory, so VRAM says nothing.
		if ( IsIntegratedGpu( SystemInfo.Gpu ) )
			return GraphicsPreset.Low;

		if ( vram >= Class( 10, GB ) && ram >= 16 * GB && cores >= 12 )
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

	static bool IsIntegratedGpu( string name )
	{
		if ( string.IsNullOrEmpty( name ) )
			return false;

		// Discrete Intel is Arc; AMD's integrated parts have no model number after "Radeon".
		if ( name.Contains( "Intel", StringComparison.OrdinalIgnoreCase ) && !name.Contains( "Arc", StringComparison.OrdinalIgnoreCase ) )
			return true;

		return name.Contains( "Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase )
			|| name.Contains( "Vega", StringComparison.OrdinalIgnoreCase );
	}

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

		// Anti-aliasing is baked into the window in Source2PreInit, from a config that doesn't
		// exist yet on a first run, so without this it wouldn't apply until the next launch.
		Apply();

		log.Info( $"Detected graphics preset: {preset} (gpu: {SystemInfo.Gpu}, vram: {SystemInfo.GpuMemory / (1024 * 1024)}MB, cores: {SystemInfo.ProcessorCount}, ram: {SystemInfo.TotalMemory / (1024 * 1024)}MB)" );
	}

	const string AutoDetectedKey = "quality.autodetected";

	static readonly Logger log = new( "RenderSettings" );
}

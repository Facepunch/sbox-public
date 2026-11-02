using Sandbox;
using Sandbox.Engine.Settings;
using Sandbox.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MenuProject.Settings;

/// <summary>
/// A tab down the left. Most filter <see cref="SettingsCatalog.Items"/>; the ones marked
/// <see cref="IsCustom"/> bring their own panel instead.
/// </summary>
public class SettingsCategory
{
	public string Id { get; init; }
	public string Title { get; init; }
	public string Icon { get; init; }

	/// <summary>One line under the category's title.</summary>
	public string Blurb { get; init; }

	public bool IsCustom { get; init; }

	public bool ShowPresets { get; init; }
}

/// <summary>
/// Every setting, as data. One per open settings view; holds the staged edits, so nothing is
/// written until you save.
/// </summary>
public class SettingsCatalog
{
	public List<SettingsCategory> Categories { get; } = new();
	public List<SettingItem> Items { get; } = new();

	static RenderSettings Render => MenuUtility.RenderSettings;

	public SettingsCatalog()
	{
		BuildCategories();

		AddDisplay();
		AddGraphics();
		AddAudio();
		AddControls();
		AddGame();
		AddDeveloper();

		foreach ( var item in Items )
			byId[item.Id] = item;

		// Silently dropping out of preset matching is worse than a log line.
		foreach ( var name in MenuUtility.GraphicsPresetValues( GraphicsPreset.High ).Keys )
		{
			if ( Row( name ) is null )
				Log.Warning( $"Settings menu has no row for preset setting '{name}'" );
		}

		Revert();
	}

	/// <summary>
	/// Not in <see cref="Items"/>: the post-processing preset is drawn by its own component, but
	/// the description pane still wants something to show when you hover it.
	/// </summary>
	public SettingItem PostProcessInfo { get; } = new SettingItem
	{
		Id = "gfx.postprocess",
		Category = "graphics",
		Section = "#menu.settings.post_processing",
		Title = "#menu.settings.post_processing",
		Description = "#menu.settings.gfx.postprocess.description"
	};

	readonly Dictionary<string, SettingItem> byId = new();

	/// <summary>Indexed, because the visibility predicates run every frame.</summary>
	public SettingItem Find( string id ) => byId.GetValueOrDefault( id );

	public IEnumerable<SettingItem> InCategory( string category ) => Items.Where( x => x.Category == category && x.Visible );

	public IEnumerable<SettingItem> Search( string query ) => Items.Where( x => x.Visible && x.Matches( query ) );

	public bool IsDirty => Items.Any( x => x.IsDirty );

	/// <summary>Throw the edits away and re-read what's in use.</summary>
	public void Revert()
	{
		foreach ( var item in Items )
			item.Revert();
	}

	/// <summary>Write every edited row through, then take the new video mode once.</summary>
	public void Apply()
	{
		var needsRenderApply = Items.Any( x => x.IsDirty && x.AppliesRenderSettings );

		foreach ( var item in Items )
			item.Commit();

		if ( needsRenderApply )
			Render.Apply();
	}

	/// <summary>Defaults for one category only, so the audio tab can't reset keybinds.</summary>
	public void RestoreDefaults( string category )
	{
		switch ( category )
		{
			case "display":
				Render.ResetDisplayConfig();
				// Resolution and window mode only land on a video mode change.
				Render.Apply();
				break;

			case "graphics":
				Render.ResetGraphicsConfig();
				Render.Apply();
				break;

			case "audio":
				RestoreAudioDefaults();
				break;

			case "controls":
				RestoreControlDefaults();
				break;

			case "game":
				Preferences.ChatEnabled = true;
				Preferences.StreamerMode = false;
				ConsoleSystem.Run( "language", "en" );
				break;

			case "developer":
				ConsoleSystem.SetValue( "consoleoverlay", false );
				break;
		}

		Revert();
	}

	/// <summary>
	/// Row for each setting the presets cover. The engine keys its preset values by its own
	/// property names, and these are the rows they land on. Checked on startup.
	/// </summary>
	static readonly Dictionary<string, string> PresetRows = new()
	{
		["TextureQuality"] = "gfx.texture",
		["ShadowQuality"] = "gfx.shadows",
		["VolumetricFogQuality"] = "gfx.fog",
		["WaterQuality"] = "gfx.water",
		["AntiAliasMode"] = "gfx.aa",
		["AntiAliasQuality"] = "gfx.msaa_samples",
		["AmbientOcclusionQuality"] = "gfx.ao",
		["DepthOfFieldQuality"] = "gfx.dof",
		["ScreenSpaceReflectionQuality"] = "gfx.ssr",
		["MotionBlurQuality"] = "gfx.motionblur_quality",
		["BloomEnabled"] = "gfx.bloom"
	};

	static readonly GraphicsPreset[] Presets =
		[GraphicsPreset.Low, GraphicsPreset.Medium, GraphicsPreset.High, GraphicsPreset.Ultra];

	static readonly PostProcessQuality[] PostProcessPresets =
		[PostProcessQuality.Off, PostProcessQuality.Low, PostProcessQuality.Medium, PostProcessQuality.High];

	/// <summary>What the staged graphics edits add up to.</summary>
	public GraphicsPreset StagedPreset
	{
		get
		{
			foreach ( var preset in Presets )
			{
				if ( Matches( MenuUtility.GraphicsPresetValues( preset ) ) )
					return preset;
			}

			return GraphicsPreset.Custom;
		}
	}

	/// <summary>What the staged post-processing effects add up to.</summary>
	public PostProcessQuality StagedPostProcess
	{
		get
		{
			foreach ( var preset in PostProcessPresets )
			{
				if ( Matches( MenuUtility.PostProcessPresetValues( preset ) ) )
					return preset;
			}

			return PostProcessQuality.Custom;
		}
	}

	/// <summary>Stage a preset's bundle. Still needs a save.</summary>
	public void StagePreset( GraphicsPreset preset )
	{
		if ( preset == GraphicsPreset.Custom )
			return;

		Stage( MenuUtility.GraphicsPresetValues( preset ) );
	}

	/// <summary>Stage the bundle a post-processing preset stands for.</summary>
	public void StagePostProcess( PostProcessQuality preset )
	{
		if ( preset == PostProcessQuality.Custom )
			return;

		Stage( MenuUtility.PostProcessPresetValues( preset ) );
	}

	bool Matches( IReadOnlyDictionary<string, string> values )
	{
		foreach ( var (name, value) in values )
		{
			if ( Row( name ) is not { } item )
				continue;

			if ( !string.Equals( item.Value?.ToString(), RowValue( name, value, values ), StringComparison.OrdinalIgnoreCase ) )
				return false;
		}

		return true;
	}

	void Stage( IReadOnlyDictionary<string, string> values )
	{
		foreach ( var (name, value) in values )
		{
			if ( Row( name ) is { } item )
				item.Value = RowValue( name, value, values );
		}

		ResetOverriddenAntiAliasing();
	}

	/// <summary>The anti-aliasing row folds the preset's mode and sample count into one choice.</summary>
	static string RowValue( string name, string value, IReadOnlyDictionary<string, string> values )
	{
		if ( name != "AntiAliasMode" )
			return value;

		var mode = Enum.Parse<AntiAliasMode>( value );
		var samples = Enum.Parse<MultisampleAmount>( values["AntiAliasQuality"] );
		return ChoiceFor( mode, samples ).ToString();
	}

	SettingItem Row( string settingName ) => PresetRows.TryGetValue( settingName, out var id ) ? Find( id ) : null;

	void BuildCategories()
	{
		Categories.Add( new() { Id = "display", Title = "#menu.settings.display", Icon = "monitor", Blurb = "#menu.settings.display.blurb" } );
		Categories.Add( new() { Id = "graphics", Title = "#menu.settings.graphics", Icon = "auto_awesome", Blurb = "#menu.settings.graphics.blurb", ShowPresets = true } );
		Categories.Add( new() { Id = "audio", Title = "#menu.settings.audio", Icon = "volume_up", Blurb = "#menu.settings.audio.blurb" } );
		Categories.Add( new() { Id = "controls", Title = "#menu.settings.controls", Icon = "mouse", Blurb = "#menu.settings.controls.blurb" } );
		Categories.Add( new() { Id = "keybinds", Title = "#menu.settings.key_binds", Icon = "keyboard", Blurb = "#menu.settings.keybinds.blurb", IsCustom = true } );
		Categories.Add( new() { Id = "game", Title = "#menu.settings.game", Icon = "language", Blurb = "#menu.settings.game.blurb" } );
		Categories.Add( new() { Id = "storage", Title = "#menu.settings.storage", Icon = "sd_card", Blurb = "#menu.settings.storage.blurb", IsCustom = true } );
		Categories.Add( new() { Id = "developer", Title = "#menu.settings.developer", Icon = "build", Blurb = "#menu.settings.developer.blurb", IsCustom = false } );
		Categories.Add( new() { Id = "about", Title = "#menu.settings.about", Icon = "info", Blurb = "#menu.settings.about.blurb", IsCustom = true } );
	}

	static readonly List<Option> OffOn = [new( "#menu.settings.off", false ), new( "#menu.settings.on", true )];

	/// <summary>
	/// Options for a quality ladder. Values are names rather than the enum, because ButtonGroup
	/// matches its buttons by ToString and never matches a negative member like Off. Order is
	/// given rather than taken from the enum, which sorts Off (-1) last.
	/// </summary>
	static List<Option> Levels<T>( params T[] values ) where T : struct, Enum =>
		values.Select( x => new Option( QualityLabel( x ), x.ToString() ) ).ToList();

	/// <summary>
	/// A display token for the shared quality levels, keeping the enum values unchanged.
	/// </summary>
	public static string QualityLabel<T>( T value ) where T : struct, Enum => value.ToString() switch
	{
		"Off" => "#menu.settings.off",
		"Low" => "#menu.settings.low",
		"Medium" => "#menu.settings.medium",
		"High" => "#menu.settings.high",
		"Ultra" => "#menu.settings.ultra",
		"Custom" => "#menu.settings.custom",
		_ => value.ToString()
	};

	static readonly List<Option> EffectLevels =
		Levels( EffectQuality.Off, EffectQuality.Low, EffectQuality.Medium, EffectQuality.High );

	void AddDisplay()
	{
		static string EditorLocked() => Game.IsEditor ? "#menu.settings.the_editor_owns_the_window_run_the_game_to_change_this" : null;

		Items.Add( new SettingItem
		{
			Id = "display.mode",
			Category = "display",
			Section = "#menu.settings.window",
			Title = "#menu.settings.window_mode",
			Description = "#menu.settings.display.mode.description",
			Options = [new( "#menu.settings.windowed", "web_asset", "window" ), new( "#menu.settings.borderless", "fullscreen", "borderless" ), new( "#menu.settings.exclusive", "monitor", "exclusive" )],
			DisabledReason = EditorLocked,
			AppliesRenderSettings = true,
			Read = () => Render.Fullscreen ? "exclusive" : Render.Borderless ? "borderless" : "window",
			Write = value =>
			{
				var mode = value?.ToString();
				Render.Fullscreen = mode == "exclusive";
				Render.Borderless = mode == "borderless";
			}
		} );

		Items.Add( new SettingItem
		{
			Id = "display.resolution",
			Category = "display",
			Section = "#menu.settings.window",
			Title = "#menu.settings.resolution",
			Description = "#menu.settings.display.resolution.description",
			Kind = SettingKind.Dropdown,
			OptionsBuilder = () => ResolutionOptions,
			IsVisible = () => StagedWindowMode != "borderless",
			DisabledReason = EditorLocked,
			AppliesRenderSettings = true,
			Read = () => $"{Render.ResolutionWidth}x{Render.ResolutionHeight}",
			Write = value =>
			{
				var parts = value?.ToString().Split( 'x', 2 );
				if ( parts is not { Length: 2 } ) return;

				Render.ResolutionWidth = parts[0].ToInt();
				Render.ResolutionHeight = parts[1].ToInt();
			}
		} );

		Items.Add( new SettingItem
		{
			Id = "display.vsync",
			Category = "display",
			Section = "#menu.settings.window",
			Title = "#menu.settings.vsync",
			Description = "#menu.settings.display.vsync.description",
			Options = OffOn,
			AppliesRenderSettings = true,
			Read = () => Render.VSync,
			Write = value => Render.VSync = ToBool( value )
		} );

		Items.Add( new SettingItem
		{
			Id = "display.fps",
			Category = "display",
			Section = "#menu.settings.frame_rate",
			Title = "#menu.settings.frame_rate_limit",
			Description = "#menu.settings.display.fps.description",
			Kind = SettingKind.Slider,
			Min = 0,
			Max = 500,
			Read = () => Render.MaxFrameRate,
			Write = value => Render.MaxFrameRate = (int)ToFloat( value )
		} );

		Items.Add( new SettingItem
		{
			Id = "display.fps_menu",
			Category = "display",
			Section = "#menu.settings.frame_rate",
			Title = "#menu.settings.menu_frame_rate_limit",
			Description = "#menu.settings.display.fps_menu.description",
			Kind = SettingKind.Slider,
			Min = 0,
			Max = 500,
			Read = () => Render.MaxFrameRateMenu,
			Write = value => Render.MaxFrameRateMenu = (int)ToFloat( value )
		} );

		Items.Add( new SettingItem
		{
			Id = "display.fps_inactive",
			Category = "display",
			Section = "#menu.settings.frame_rate",
			Title = "#menu.settings.background_frame_rate_limit",
			Description = "#menu.settings.display.fps_inactive.description",
			Kind = SettingKind.Slider,
			Min = 0,
			Max = 500,
			Read = () => Render.MaxFrameRateInactive,
			Write = value => Render.MaxFrameRateInactive = (int)ToFloat( value )
		} );

		Items.Add( new SettingItem
		{
			Id = "display.fov",
			Category = "display",
			Section = "#menu.settings.camera",
			Title = "#menu.settings.field_of_view",
			Description = "#menu.settings.display.fov.description",
			Kind = SettingKind.Slider,
			Min = 45,
			Max = 120,
			Read = () => Render.DefaultFOV,
			Write = value => Render.DefaultFOV = ToFloat( value ).Clamp( 45, 120 )
		} );
	}

	void AddGraphics()
	{
		Items.Add( new SettingItem
		{
			Id = "gfx.texture",
			Category = "graphics",
			Section = "#menu.settings.quality",
			Title = "#menu.settings.texture_detail",
			Description = "#menu.settings.gfx.texture.description",
			Options = Levels( TextureQuality.Low, TextureQuality.Medium, TextureQuality.High, TextureQuality.Ultra ),
			AppliesRenderSettings = true,
			Read = () => Render.TextureQuality.ToString(),
			Write = value => Render.TextureQuality = ToEnum<TextureQuality>( value )
		} );

		Items.Add( new SettingItem
		{
			Id = "gfx.shadows",
			Category = "graphics",
			Section = "#menu.settings.quality",
			Title = "#menu.settings.shadows",
			Description = "#menu.settings.gfx.shadows.description",
			Options = Levels( ShadowQuality.Low, ShadowQuality.Medium, ShadowQuality.High, ShadowQuality.Ultra ),
			AppliesRenderSettings = true,
			Read = () => Render.ShadowQuality.ToString(),
			Write = value => Render.ShadowQuality = ToEnum<ShadowQuality>( value )
		} );

		Items.Add( new SettingItem
		{
			Id = "gfx.fog",
			Category = "graphics",
			Section = "#menu.settings.quality",
			Title = "#menu.settings.volumetric_fog",
			Description = "#menu.settings.gfx.fog.description",
			Options = Levels( VolumetricFogQuality.Off, VolumetricFogQuality.Low, VolumetricFogQuality.Medium, VolumetricFogQuality.High ),
			AppliesRenderSettings = true,
			Read = () => Render.VolumetricFogQuality.ToString(),
			Write = value => Render.VolumetricFogQuality = ToEnum<VolumetricFogQuality>( value )
		} );

		Items.Add( new SettingItem
		{
			Id = "gfx.water",
			Category = "graphics",
			Section = "#menu.settings.quality",
			Title = "#menu.settings.water",
			Description = "#menu.settings.gfx.water.description",
			Options = Levels( WaterQuality.Low, WaterQuality.Medium, WaterQuality.High, WaterQuality.Ultra ),
			AppliesRenderSettings = true,
			Read = () => Render.WaterQuality.ToString(),
			Write = value => Render.WaterQuality = ToEnum<WaterQuality>( value )
		} );

		Items.Add( new SettingItem
		{
			Id = "gfx.aa",
			Category = "graphics",
			Section = "#menu.settings.quality",
			Title = "#menu.settings.anti_aliasing",
			Description = "#menu.settings.gfx.aa_modes.description",
			Kind = SettingKind.Dropdown,
			OptionsBuilder = BuildAntiAliasingOptions,
			DisabledReason = () => StagedTemporalUpscaler ? "#menu.settings.temporal_upscalers_smooth_edges_themselves" : null,
			AppliesRenderSettings = true,
			OnStaged = StageSamples,
			Read = ReadAntiAliasing,
			Write = SetAntiAliasing
		} );

		Items.Add( new SettingItem
		{
			Id = "gfx.msaa_samples",
			Category = "graphics",
			Section = "#menu.settings.quality",
			Title = "#menu.settings.samples",
			Description = "#menu.settings.gfx.aa.description",
			Kind = SettingKind.Dropdown,
			OptionsBuilder = () => StagedAntiAliasing == AntiAliasChoice.Cmaa2
				? msaaSampleOptionsWithOff ??= BuildMsaaSampleOptions( true )
				: msaaSampleOptions ??= BuildMsaaSampleOptions( false ),
			IsVisible = () => StagedAntiAliasing != AntiAliasChoice.Off,
			DisabledReason = () => StagedUpscaler != UpscalerMode.Off ? "#menu.settings.every_upscaler_renders_to_a_target_that_can_t" : null,
			AppliesRenderSettings = true,
			Read = () => RenderSettings.GetSupportedAntiAliasQuality( Render.AntiAliasQuality ).ToString(),
			Write = value =>
			{
				if ( StagedAntiAliasing != AntiAliasChoice.Off && StagedUpscaler == UpscalerMode.Off )
					Render.AntiAliasQuality = ToEnum<MultisampleAmount>( value );
			}
		} );

		Items.Add( new SettingItem
		{
			Id = "gfx.upscaler",
			Category = "graphics",
			Section = "#menu.settings.quality",
			Title = "#menu.settings.upscaler",
			Description = "#menu.settings.gfx.upscaler.description",
			Kind = SettingKind.Dropdown,
			OptionsBuilder = () => BuildUpscalerOptions( StagedUpscaler == UpscalerMode.DLSS ),
			AppliesRenderSettings = true,
			OnStaged = ResetOverriddenAntiAliasing,
			Read = () => Render.UpscalerMode.ToString(),
			Write = SetUpscaler,
			Warning = () => StagedUpscaler switch
			{
				UpscalerMode.FSR3 => "#menu.settings.temporal_upscaler_can_ghost_on_moving_objects_and_in",
				UpscalerMode.DLSS when !RenderSettings.IsUpscalerModeSupported( UpscalerMode.DLSS ) => "#menu.settings.dlss_unavailable",
				UpscalerMode.DLSS => "#menu.settings.temporal_ai_upscaler_can_ghost_on_moving_objects_and",
				_ => null
			}
		} );

		Items.Add( new SettingItem
		{
			Id = "gfx.upscaler_scale",
			Category = "graphics",
			Section = "#menu.settings.quality",
			Title = "#menu.settings.render_quality",
			Description = "#menu.settings.gfx.upscaler_scale.description",
			Kind = SettingKind.Slider,
			Min = 40,
			Max = 100,
			NumberFormat = "0'%'",
			IsVisible = () => StagedUpscaler is UpscalerMode.Off or UpscalerMode.FSR1,
			AppliesRenderSettings = true,
			Read = () => (int)MathF.Round( Render.RenderScale * 100f ),
			Write = value => Render.RenderScale = ToFloat( value ) / 100f
		} );

		Items.Add( new SettingItem
		{
			Id = "gfx.fsr1_sharpness",
			Category = "graphics",
			Section = "#menu.settings.quality",
			Title = "#menu.settings.sharpness",
			Description = "#menu.settings.gfx.fsr1_sharpness.description",
			Kind = SettingKind.Slider,
			Max = 100,
			NumberFormat = "0'%'",
			IsVisible = () => StagedUpscaler == UpscalerMode.FSR1,
			AppliesRenderSettings = true,
			Read = () => (int)MathF.Round( Render.Fsr1Sharpness * 100f ),
			Write = value => Render.Fsr1Sharpness = ToFloat( value ) / 100f
		} );

		Items.Add( new SettingItem
		{
			Id = "gfx.fsr3_quality",
			Category = "graphics",
			Section = "#menu.settings.quality",
			Title = "#menu.settings.fsr3_quality",
			Description = "#menu.settings.gfx.fsr3_quality.description",
			Kind = SettingKind.Dropdown,
			Options =
			[
				new( "#menu.settings.native", Fsr3UpscalerQuality.Native ),
				new( "#menu.settings.quality", Fsr3UpscalerQuality.Quality ),
				new( "#menu.settings.balanced", Fsr3UpscalerQuality.Balanced ),
				new( "#menu.settings.performance", Fsr3UpscalerQuality.Performance ),
				new( "#menu.settings.ultra_performance", Fsr3UpscalerQuality.UltraPerformance )
			],
			IsVisible = () => StagedUpscaler == UpscalerMode.FSR3,
			AppliesRenderSettings = true,
			Read = () => Render.Fsr3UpscalerQuality,
			Write = value => Render.Fsr3UpscalerQuality = ToEnum<Fsr3UpscalerQuality>( value )
		} );

		Items.Add( new SettingItem
		{
			Id = "gfx.fsr3_sharpness",
			Category = "graphics",
			Section = "#menu.settings.quality",
			Title = "#menu.settings.sharpness",
			Description = "#menu.settings.gfx.fsr3_sharpness.description",
			Kind = SettingKind.Slider,
			Max = 100,
			NumberFormat = "0'%'",
			IsVisible = () => StagedUpscaler == UpscalerMode.FSR3,
			AppliesRenderSettings = true,
			Read = () => (int)MathF.Round( Render.Fsr3Sharpness * 100f ),
			Write = value => Render.Fsr3Sharpness = ToFloat( value ) / 100f
		} );

		Items.Add( new SettingItem
		{
			Id = "gfx.dlss_quality",
			Category = "graphics",
			Section = "#menu.settings.quality",
			Title = "#menu.settings.dlss_quality",
			Description = "#menu.settings.gfx.dlss_quality.description",
			Kind = SettingKind.Dropdown,
			Options =
			[
				new( "#menu.settings.dlaa", DlssQuality.DLAA ),
				new( "#menu.settings.quality", DlssQuality.Quality ),
				new( "#menu.settings.balanced", DlssQuality.Balanced ),
				new( "#menu.settings.performance", DlssQuality.Performance ),
				new( "#menu.settings.ultra_performance", DlssQuality.UltraPerformance )
			],
			IsVisible = () => StagedUpscaler == UpscalerMode.DLSS && RenderSettings.IsUpscalerModeSupported( UpscalerMode.DLSS ),
			AppliesRenderSettings = true,
			Read = () => Render.DlssQuality,
			Write = value => Render.DlssQuality = ToEnum<DlssQuality>( value )
		} );

		Items.Add( new SettingItem
		{
			Id = "gfx.ao",
			Category = "graphics",
			Section = "#menu.settings.post_processing",
			Title = "#menu.settings.ambient_occlusion",
			Description = "#menu.settings.gfx.ao.description",
			Options = EffectLevels,
			AppliesRenderSettings = true,
			Read = () => Render.AmbientOcclusionQuality.ToString(),
			Write = value => Render.AmbientOcclusionQuality = ToEnum<EffectQuality>( value )
		} );

		Items.Add( new SettingItem
		{
			Id = "gfx.dof",
			Category = "graphics",
			Section = "#menu.settings.post_processing",
			Title = "#menu.settings.depth_of_field",
			Description = "#menu.settings.gfx.dof.description",
			Options = EffectLevels,
			AppliesRenderSettings = true,
			Read = () => Render.DepthOfFieldQuality.ToString(),
			Write = value => Render.DepthOfFieldQuality = ToEnum<EffectQuality>( value )
		} );

		Items.Add( new SettingItem
		{
			Id = "gfx.ssr",
			Category = "graphics",
			Section = "#menu.settings.post_processing",
			Title = "#menu.settings.screen_space_reflections",
			Description = "#menu.settings.gfx.ssr.description",
			Options = Levels( EffectQuality.Off, EffectQuality.Low, EffectQuality.High ),
			AppliesRenderSettings = true,
			Read = () => Render.ScreenSpaceReflectionQuality.ToString(),
			Write = value => Render.ScreenSpaceReflectionQuality = ToEnum<EffectQuality>( value )
		} );

		Items.Add( new SettingItem
		{
			Id = "gfx.bloom",
			Category = "graphics",
			Section = "#menu.settings.post_processing",
			Title = "#menu.settings.bloom",
			Description = "#menu.settings.gfx.bloom.description",
			Options = OffOn,
			AppliesRenderSettings = true,
			Read = () => Render.BloomEnabled,
			Write = value => Render.BloomEnabled = ToBool( value )
		} );

		Items.Add( new SettingItem
		{
			Id = "gfx.motionblur_quality",
			Category = "graphics",
			Section = "#menu.settings.post_processing",
			Title = "#menu.settings.motion_blur_quality",
			Description = "#menu.settings.gfx.motionblur_quality.description",
			Options = EffectLevels,
			AppliesRenderSettings = true,
			Read = () => Render.MotionBlurQuality.ToString(),
			Write = value => Render.MotionBlurQuality = ToEnum<EffectQuality>( value )
		} );

		Items.Add( new SettingItem
		{
			Id = "gfx.motionblur",
			IsVisible = () => StagedMotionBlur != EffectQuality.Off,
			Category = "graphics",
			Section = "#menu.settings.post_processing",
			Title = "#menu.settings.motion_blur_strength",
			Description = "#menu.settings.gfx.motionblur.description",
			Kind = SettingKind.Slider,
			Min = 0,
			Max = 1,
			Step = 0.01f,
			NumberFormat = "0.##",
			AppliesRenderSettings = true,
			Read = () => Render.MotionBlurScale,
			Write = value => Render.MotionBlurScale = ToFloat( value )
		} );

	}

	// Built once each, and shown in a dropdown: ButtonGroup corrupts its selection when its options change.
	List<Option> msaaSampleOptions;
	List<Option> msaaSampleOptionsWithOff;

	/// <summary>The staged motion blur level, which the strength row follows.</summary>
	EffectQuality StagedMotionBlur => Find( "gfx.motionblur_quality" )?.As<EffectQuality>() ?? EffectQuality.High;

	/// <summary>The selected upscaler, which the rows below it follow.</summary>
	UpscalerMode StagedUpscaler => Enum.TryParse<UpscalerMode>( Find( "gfx.upscaler" )?.Value?.ToString(), out var mode ) ? mode : UpscalerMode.Off;

	/// <summary>FSR3 and DLSS do their own anti-aliasing.</summary>
	bool StagedTemporalUpscaler => StagedUpscaler is UpscalerMode.FSR3 or UpscalerMode.DLSS;

	enum AntiAliasChoice { Off, Cmaa2, Msaa }

	AntiAliasChoice StagedAntiAliasing => Find( "gfx.aa" )?.As<AntiAliasChoice>() ?? AntiAliasChoice.Off;

	static AntiAliasChoice ChoiceFor( AntiAliasMode mode, MultisampleAmount samples ) => mode == AntiAliasMode.Cmaa2
		? AntiAliasChoice.Cmaa2
		: samples == MultisampleAmount.MultisampleNone ? AntiAliasChoice.Off : AntiAliasChoice.Msaa;

	/// <summary>The staged window mode, which the resolution row follows.</summary>
	string StagedWindowMode => Find( "display.mode" )?.Value?.ToString() ?? "borderless";

	static AntiAliasChoice ReadAntiAliasingChoice() =>
		ChoiceFor( Render.AntiAliasMode, RenderSettings.GetSupportedAntiAliasQuality( Render.AntiAliasQuality ) );

	static string ReadAntiAliasing() => ReadAntiAliasingChoice().ToString();

	void SetAntiAliasing( object value )
	{
		if ( StagedTemporalUpscaler )
			return;

		var choice = Enum.Parse<AntiAliasChoice>( value.ToString() );
		var samples = choice == AntiAliasChoice.Off || StagedUpscaler != UpscalerMode.Off
			? MultisampleAmount.MultisampleNone
			: Find( "gfx.msaa_samples" ).As<MultisampleAmount>();

		Render.AntiAliasQuality = samples;
		Render.AntiAliasMode = choice == AntiAliasChoice.Cmaa2 ? AntiAliasMode.Cmaa2 : AntiAliasMode.None;
	}

	/// <summary>Keep the samples row sensible for the staged method.</summary>
	void StageSamples()
	{
		var samples = Find( "gfx.msaa_samples" );
		if ( samples is null )
			return;

		var choice = StagedAntiAliasing;

		if ( choice == AntiAliasChoice.Off )
			samples.Value = nameof( MultisampleAmount.MultisampleNone );
		else if ( choice == AntiAliasChoice.Msaa && samples.As<MultisampleAmount>() == MultisampleAmount.MultisampleNone )
			samples.Value = RenderSettings.GetSupportedAntiAliasQuality( MultisampleAmount.Multisample4x ).ToString();
	}

	/// <summary>Stage what the selected upscaler overrides, so the disabled rows show what will be used.</summary>
	void ResetOverriddenAntiAliasing()
	{
		if ( StagedUpscaler == UpscalerMode.Off )
			return;

		Find( "gfx.msaa_samples" ).Value = nameof( MultisampleAmount.MultisampleNone );

		if ( StagedTemporalUpscaler || StagedAntiAliasing == AntiAliasChoice.Msaa )
			Find( "gfx.aa" ).Value = nameof( AntiAliasChoice.Off );
	}

	void SetUpscaler( object value )
	{
		var upscaler = Enum.Parse<UpscalerMode>( value.ToString() );

		if ( upscaler != UpscalerMode.Off )
			Render.AntiAliasQuality = MultisampleAmount.MultisampleNone;

		if ( upscaler is UpscalerMode.FSR3 or UpscalerMode.DLSS )
			Render.AntiAliasMode = AntiAliasMode.None;

		Render.UpscalerMode = upscaler;
	}

	static List<Option> BuildMsaaSampleOptions( bool includeOff )
	{
		var samples = new (string Label, MultisampleAmount Amount)[]
		{
			( "#menu.settings.off", MultisampleAmount.MultisampleNone ),
			( "#menu.settings.2x", MultisampleAmount.Multisample2x ),
			( "#menu.settings.4x", MultisampleAmount.Multisample4x ),
			( "#menu.settings.8x", MultisampleAmount.Multisample8x )
		};

		return samples
			.Where( x => RenderSettings.GetSupportedAntiAliasQuality( x.Amount ) == x.Amount )
			.Where( x => includeOff || x.Amount != MultisampleAmount.MultisampleNone )
			.Select( x => new Option( x.Label, x.Amount.ToString() ) )
			.ToList();
	}

	List<Option> BuildAntiAliasingOptions()
	{
		var options = new List<Option>
		{
			new( "#menu.settings.off", AntiAliasChoice.Off ),
			new( "CMAA2", AntiAliasChoice.Cmaa2 )
		};

		var msaaAvailable = StagedUpscaler == UpscalerMode.Off
			&& RenderSettings.GetSupportedAntiAliasQuality( MultisampleAmount.Multisample2x ) == MultisampleAmount.Multisample2x;

		if ( msaaAvailable )
			options.Add( new Option( "MSAA", AntiAliasChoice.Msaa ) );

		return options;
	}

	static List<Option> BuildUpscalerOptions( bool includeSelectedDlss )
	{
		var options = new List<Option>
		{
			new( "#menu.settings.off", UpscalerMode.Off ),
			new( "#menu.settings.fsr", UpscalerMode.FSR1 ),
			new( "#menu.settings.fsr3", UpscalerMode.FSR3 )
		};

		if ( includeSelectedDlss || RenderSettings.IsUpscalerModeSupported( UpscalerMode.DLSS ) )
			options.Add( new Option( "#menu.settings.dlss", UpscalerMode.DLSS ) );

		return options;
	}

	void AddAudio()
	{
		Items.Add( new SettingItem
		{
			Id = "audio.device",
			Category = "audio",
			Section = "#menu.settings.device",
			Title = "#menu.settings.output_device",
			Description = "#menu.settings.audio.device.description",
			Kind = SettingKind.Dropdown,
			OptionsBuilder = () => Sandbox.Internal.AudioSettings.GetAudioDevices().Select( x => new Option( x.Name, x.Id ) ).ToList(),
			Read = () => Sandbox.Internal.AudioSettings.GetActiveDevice().Id,
			Write = value => Sandbox.Internal.AudioSettings.SetActiveDevice( value?.ToString() )
		} );

		AddVolume( "audio.volume", "#menu.settings.audio.volume.title", "#menu.settings.audio.volume.description", "volume" );
		AddVolume( "audio.game", "#menu.settings.audio.game.title", "#menu.settings.audio.game.description", "game_volume" );
		AddVolume( "audio.music", "#menu.settings.audio.music.title", "#menu.settings.audio.music.description", "music_volume" );
		AddVolume( "audio.menu_music", "#menu.settings.audio.menu_music.title", "#menu.settings.audio.menu_music.description", "music_volume_menu" );
		AddVolume( "audio.voice", "#menu.settings.audio.voice.title", "#menu.settings.audio.voice.description", "voip_volume" );

		Items.Add( new SettingItem
		{
			Id = "audio.voicemode",
			Category = "audio",
			Section = "#menu.settings.voice",
			Title = "#menu.settings.voice_mode",
			Description = "#menu.settings.audio.voicemode.description",
			Options =
			[
				new( "#menu.settings.push_to_talk", VoiceMode.PushToTalk ),
				new( "#menu.settings.open_mic", VoiceMode.OpenMicrophone ),
				new( "#menu.settings.off", VoiceMode.Disabled )
			],
			Read = () => Enum.TryParse<VoiceMode>( ConsoleSystem.GetValue( "voip_mode" ), true, out var mode ) ? mode : VoiceMode.PushToTalk,
			Write = value => ConsoleSystem.SetValue( "voip_mode", (int)ToEnum<VoiceMode>( value ) )
		} );

		Items.Add( new SettingItem
		{
			Id = "audio.microphone",
			Category = "audio",
			Section = "#menu.settings.voice",
			Title = "#menu.settings.microphone",
			Description = "#menu.settings.audio.microphone.description",
			Kind = SettingKind.Dropdown,
			OptionsBuilder = MicrophoneOptions,
			Read = ReadMicrophone,
			Write = value => ConsoleSystem.SetValue( "voip_device", value?.ToString() ?? "" ),
			Warning = () => IsMicrophoneMissing( Find( "audio.microphone" )?.Value?.ToString() )
				? "#menu.settings.microphone_missing"
				: null
		} );

		Items.Add( new SettingItem
		{
			Id = "audio.threshold",
			Category = "audio",
			Section = "#menu.settings.voice",
			Title = "#menu.settings.voice_threshold",
			Description = "#menu.settings.audio.threshold.description",
			Kind = SettingKind.Custom,
			CreateControl = item => new MicrophoneMeter
			{
				Item = item,
				Device = () => Find( "audio.microphone" )?.Value?.ToString()
			},
			Read = () => ConsoleSystem.GetValue( "voip_threshold" ).ToFloat(),
			Write = value => ConsoleSystem.SetValue( "voip_threshold", ToFloat( value ) )
		} );

		Items.Add( new SettingItem
		{
			Id = "audio.acoustics",
			Category = "audio",
			Section = "#menu.settings.simulation",
			Title = "#menu.settings.acoustic_simulation",
			Description = "#menu.settings.audio.acoustics.description",
			Options = OffOn,
			Read = () => ConsoleSystem.GetValue( "snd_simulation_enable" ).ToBool(),
			Write = value => ConsoleSystem.SetValue( "snd_simulation_enable", ToBool( value ) )
		} );

		Items.Add( new SettingItem
		{
			Id = "audio.background",
			Category = "audio",
			Section = "#menu.settings.misc",
			Title = "#menu.settings.background_audio",
			Description = "#menu.settings.audio.background.description",
			Options = OffOn,
			Read = () => !ConsoleSystem.GetValue( "snd_mute_losefocus" ).ToBool(),
			Write = value => ConsoleSystem.SetValue( "snd_mute_losefocus", !ToBool( value ) )
		} );

		Items.Add( new SettingItem
		{
			Id = "audio.subtitles",
			Category = "audio",
			Section = "#menu.settings.misc",
			Title = "#menu.settings.subtitles",
			Description = "#menu.settings.audio.subtitles.description",
			Options = OffOn,
			Read = () => ConsoleSystem.GetValue( "snd_subtitles" ).ToBool(),
			Write = value => ConsoleSystem.SetValue( "snd_subtitles", ToBool( value ) )
		} );
	}

	void AddVolume( string id, string title, string description, string convar )
	{
		Items.Add( new SettingItem
		{
			Id = id,
			Category = "audio",
			Section = "#menu.settings.volume",
			Title = title,
			Description = description,
			Kind = SettingKind.Slider,
			Min = 0,
			Max = 1,
			Step = 0.01f,
			NumberFormat = "0.##%",
			Read = () => ConsoleSystem.GetValue( convar ).ToFloat(),
			Write = value => ConsoleSystem.SetValue( convar, ToFloat( value ) )
		} );
	}

	void AddControls()
	{
		Items.Add( new SettingItem
		{
			Id = "input.sensitivity",
			Category = "controls",
			Section = "#menu.settings.mouse",
			Title = "#menu.settings.mouse_sensitivity",
			Description = "#menu.settings.input.sensitivity.description",
			Kind = SettingKind.Slider,
			Min = 0,
			Max = 20,
			Step = 0.1f,
			NumberFormat = "0.#",
			Read = () => ConsoleSystem.GetValue( "sensitivity" ).ToFloat(),
			Write = value => ConsoleSystem.SetValue( "sensitivity", ToFloat( value ) )
		} );

		AddConVarToggle( "input.invert_pitch", "controls", "#menu.settings.mouse", "#menu.settings.input.invert_pitch.title", "#menu.settings.input.invert_pitch.description", "mouse_pitch_inverted" );
		AddConVarToggle( "input.invert_yaw", "controls", "#menu.settings.mouse", "#menu.settings.input.invert_yaw.title", "#menu.settings.input.invert_yaw.description", "mouse_yaw_inverted" );

		AddControllerSlider( "input.pad_yaw", "#menu.settings.input.pad_yaw.title", "#menu.settings.input.pad_yaw.description", "controller_look_speed_yaw", 360, 1 );
		AddControllerSlider( "input.pad_pitch", "#menu.settings.input.pad_pitch.title", "#menu.settings.input.pad_pitch.description", "controller_look_speed_pitch", 360, 1 );
		AddControllerSlider( "input.pad_deadzone", "#menu.settings.input.pad_deadzone.title", "#menu.settings.input.pad_deadzone.description", "controller_joystick_deadzone", 50, 0.5f );
	}

	void AddControllerSlider( string id, string title, string description, string convar, float max, float step )
	{
		Items.Add( new SettingItem
		{
			Id = id,
			Category = "controls",
			Section = "#menu.settings.controller",
			Title = title,
			Description = description,
			Kind = SettingKind.Slider,
			Min = 0,
			Max = max,
			Step = step,
			NumberFormat = step < 1 ? "0.#" : "0",
			Read = () => ConsoleSystem.GetValue( convar ).ToFloat(),
			Write = value => ConsoleSystem.SetValue( convar, ToFloat( value ) )
		} );
	}

	void AddGame()
	{
		Items.Add( new SettingItem
		{
			Id = "game.language",
			Category = "game",
			Section = "#menu.settings.localization",
			Title = "#menu.settings.language",
			Description = "#menu.settings.game.language.description",
			Kind = SettingKind.Dropdown,
			OptionsBuilder = () => Sandbox.Localization.Languages.List.Select( x => new Option( x.Title, x.Abbreviation ) ).ToList(),
			Read = () => Sandbox.Language.Current.Abbreviation,
			Write = value => ConsoleSystem.Run( "language", value?.ToString() )
		} );

		Items.Add( new SettingItem
		{
			Id = "game.chat",
			Category = "game",
			Section = "#menu.settings.platform",
			Title = "#menu.settings.chat",
			Description = "#menu.settings.game.chat.description",
			Options = OffOn,
			Read = () => Preferences.ChatEnabled,
			Write = value => Preferences.ChatEnabled = ToBool( value )
		} );

		Items.Add( new SettingItem
		{
			Id = "game.streamer",
			Category = "game",
			Section = "#menu.settings.platform",
			Title = "#menu.settings.streamer_mode",
			Description = "#menu.settings.game.streamer.description",
			Options = OffOn,
			Read = () => Preferences.StreamerMode,
			Write = value => Preferences.StreamerMode = ToBool( value )
		} );
	}

	void AddDeveloper()
	{
		AddConVarToggle( "dev.consoleoverlay", "developer", "#menu.settings.console", "#menu.settings.dev.consoleoverlay.title", "#menu.settings.dev.consoleoverlay.description", "consoleoverlay" );

		Items.Add( new SettingItem
		{
			Id = "dev.benchmark",
			Category = "developer",
			Section = "#menu.settings.benchmark",
			Title = "#menu.settings.run_benchmarks",
			Description = "#menu.settings.dev.benchmark.description",
			Kind = SettingKind.Action,
			ActionLabel = "#menu.settings.run",
			Invoke = () =>
			{
				Sandbox.Game.Overlay.CloseAll();
				Sandbox.Game.Overlay.RunLocalBenchmarks( ["facepunch.benchmark"] );
			}
		} );
	}

	void AddConVarToggle( string id, string category, string section, string title, string description, string convar )
	{
		Items.Add( new SettingItem
		{
			Id = id,
			Category = category,
			Section = section,
			Title = title,
			Description = description,
			Options = OffOn,
			Read = () => ConsoleSystem.GetValue( convar ).ToBool(),
			Write = value => ConsoleSystem.SetValue( convar, ToBool( value ) )
		} );
	}

	/// <summary>Refreshed when the dropdown builds, so the warning doesn't ask SDL every frame.</summary>
	List<string> microphones;

	List<Option> MicrophoneOptions()
	{
		microphones = Sandbox.Internal.AudioSettings.GetRecordingDevices().ToList();

		// The system default is one entry that follows Windows, rather than also listing that device on its own
		var systemDefault = Sandbox.Internal.AudioSettings.GetDefaultRecordingDevice();
		var options = new List<Option> { new( string.IsNullOrEmpty( systemDefault ) ? "#menu.settings.default" : $"{systemDefault} (Default)", "" ) };
		options.AddRange( microphones.Where( x => x != systemDefault ).Select( x => new Option( x, x ) ) );

		// Keep the saved one listed, so the dropdown shows what's picked rather than nothing
		var saved = ConsoleSystem.GetValue( "voip_device" );
		if ( IsMicrophoneMissing( saved ) )
			options.Add( new Option( $"{saved} (not connected)", saved ) );

		return options;
	}

	/// <summary>A saved device that's now the system default shows as the merged default entry.</summary>
	static object ReadMicrophone()
	{
		var saved = ConsoleSystem.GetValue( "voip_device" ) ?? "";
		return saved == Sandbox.Internal.AudioSettings.GetDefaultRecordingDevice() ? "" : saved;
	}

	bool IsMicrophoneMissing( string name )
	{
		if ( string.IsNullOrEmpty( name ) )
			return false;

		microphones ??= Sandbox.Internal.AudioSettings.GetRecordingDevices().ToList();
		return !microphones.Contains( name );
	}

	static void RestoreAudioDefaults()
	{
		var devices = Sandbox.Internal.AudioSettings.GetAudioDevices();
		if ( devices.Any() )
			Sandbox.Internal.AudioSettings.SetActiveDevice( devices.First().Id );

		ConsoleSystem.SetValue( "snd_simulation_enable", true );
		ConsoleSystem.SetValue( "volume", 1.0f );
		ConsoleSystem.SetValue( "game_volume", 1.0f );
		ConsoleSystem.SetValue( "music_volume", 1.0f );
		ConsoleSystem.SetValue( "music_volume_menu", 1.0f );
		ConsoleSystem.SetValue( "voip_volume", 1.0f );
		ConsoleSystem.SetValue( "voip_mode", (int)VoiceMode.PushToTalk );
		ConsoleSystem.SetValue( "voip_device", "" );
		ConsoleSystem.SetValue( "voip_threshold", -50.0f );
		ConsoleSystem.SetValue( "snd_mute_losefocus", false );
		ConsoleSystem.SetValue( "snd_subtitles", false );
	}

	static void RestoreControlDefaults()
	{
		ConsoleSystem.SetValue( "sensitivity", 5 );
		ConsoleSystem.SetValue( "controller_look_speed_yaw", 270 );
		ConsoleSystem.SetValue( "controller_look_speed_pitch", 160 );
		ConsoleSystem.SetValue( "controller_joystick_deadzone", 12.5f );
		ConsoleSystem.SetValue( "mouse_pitch_inverted", false );
		ConsoleSystem.SetValue( "mouse_yaw_inverted", false );
	}

	static bool ToBool( object value ) => value switch
	{
		bool b => b,
		null => false,
		_ => bool.TryParse( value.ToString(), out var parsed ) && parsed
	};

	static float ToFloat( object value ) => value switch
	{
		float f => f,
		int i => i,
		null => 0f,
		_ => value.ToString().ToFloat()
	};

	static T ToEnum<T>( object value ) where T : struct, Enum => value switch
	{
		T typed => typed,
		null => default,
		_ => Enum.TryParse<T>( value.ToString(), true, out var parsed ) ? parsed : default
	};

	/// <summary>
	/// Category id for whatever <c>Game.Overlay.ShowSettingsModal</c> was given. The old page
	/// names shipped in the public API, so they still have to land somewhere.
	/// </summary>
	public static string ResolveCategory( string page ) => page?.ToLowerInvariant() switch
	{
		null or "" => "",
		"video" => "display",
		"input" => "controls",
		var known when known is "display" or "graphics" or "audio" or "controls" or "keybinds" or "game" or "storage" or "developer" or "about" => known,
		_ => ""
	};

	static List<Option> resolutionOptions = [];

	public static List<Option> ResolutionOptions
	{
		get
		{
			if ( resolutionOptions.Count == 0 )
				FetchResolutions();

			return resolutionOptions;
		}
	}

	/// <summary>Asks DXGI what the display can do. Can stutter, so it's done once on startup.</summary>
	public static void FetchResolutions()
	{
		const float smallestAspect = 1.5f;

		resolutionOptions = Render.DisplayModes( false )
			.Where( x => x.Width >= 1280 )
			.Where( x => (float)x.Width / x.Height >= smallestAspect )
			.GroupBy( x => $"{x.Width}x{x.Height}" )
			.Select( x => new Option( x.Key, x.Key ) )
			.ToList();
	}
}

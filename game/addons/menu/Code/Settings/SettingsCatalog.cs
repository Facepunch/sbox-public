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
		Section = "Post Processing",
		Title = "Post Processing",
		Description = "Sets every effect below in one go."
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
		["AntiAliasQuality"] = "gfx.aa",
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

			if ( !string.Equals( item.Value?.ToString(), value, StringComparison.OrdinalIgnoreCase ) )
				return false;
		}

		return true;
	}

	void Stage( IReadOnlyDictionary<string, string> values )
	{
		foreach ( var (name, value) in values )
		{
			if ( Row( name ) is { } item )
				item.Value = value;
		}
	}

	SettingItem Row( string settingName ) => PresetRows.TryGetValue( settingName, out var id ) ? Find( id ) : null;

	void BuildCategories()
	{
		Categories.Add( new() { Id = "display", Title = "Display", Icon = "monitor", Blurb = "Window, resolution and frame rate." } );
		Categories.Add( new() { Id = "graphics", Title = "Graphics", Icon = "auto_awesome", Blurb = "How much the scene costs to draw.", ShowPresets = true } );
		Categories.Add( new() { Id = "audio", Title = "Audio", Icon = "volume_up", Blurb = "Output device, volumes and voice." } );
		Categories.Add( new() { Id = "controls", Title = "Controls", Icon = "mouse", Blurb = "Mouse and controller feel." } );
		Categories.Add( new() { Id = "keybinds", Title = "Key Binds", Icon = "keyboard", Blurb = "What every action is bound to.", IsCustom = true } );
		Categories.Add( new() { Id = "game", Title = "Game", Icon = "language", Blurb = "Language and platform preferences." } );
		Categories.Add( new() { Id = "storage", Title = "Storage", Icon = "sd_card", Blurb = "What downloaded content is using.", IsCustom = true } );
		Categories.Add( new() { Id = "developer", Title = "Developer", Icon = "build", Blurb = "Tools that aren't for everyone.", IsCustom = false } );
		Categories.Add( new() { Id = "about", Title = "About", Icon = "info", Blurb = "Third-party components and licences.", IsCustom = true } );
	}

	static readonly List<Option> OffOn = [new( "Off", false ), new( "On", true )];

	/// <summary>
	/// Options for a quality ladder. Values are names rather than the enum, because ButtonGroup
	/// matches its buttons by ToString and never matches a negative member like Off. Order is
	/// given rather than taken from the enum, which sorts Off (-1) last.
	/// </summary>
	static List<Option> Levels<T>( params T[] values ) where T : struct, Enum =>
		values.Select( x => new Option( x.ToString(), x.ToString() ) ).ToList();

	static readonly List<Option> EffectLevels =
		Levels( EffectQuality.Off, EffectQuality.Low, EffectQuality.Medium, EffectQuality.High );

	void AddDisplay()
	{
		string EditorLocked() => Sandbox.Game.IsEditor ? "The editor owns the window - run the game to change this." : null;

		Items.Add( new SettingItem
		{
			Id = "display.mode",
			Category = "display",
			Section = "Window",
			Title = "Window Mode",
			Description = "Borderless fills the screen at your desktop resolution and alt-tabs instantly. Exclusive takes the display outright, which can shave a frame of latency but is slower to leave.",
			Options = [new( "Windowed", "web_asset", "window" ), new( "Borderless", "fullscreen", "borderless" ), new( "Exclusive", "monitor", "exclusive" )],
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
			Section = "Window",
			Title = "Resolution",
			Description = "How many pixels the game is drawn at. Borderless always uses the desktop resolution, so this only applies to the other two window modes.",
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
			Section = "Window",
			Title = "VSync",
			Description = "Waits for the display before showing a frame. Removes tearing at the cost of some latency, and caps you to the refresh rate.",
			Options = OffOn,
			AppliesRenderSettings = true,
			Read = () => Render.VSync,
			Write = value => Render.VSync = ToBool( value )
		} );

		Items.Add( new SettingItem
		{
			Id = "display.fps",
			Category = "display",
			Section = "Frame Rate",
			Title = "Frame Rate Limit",
			Description = "The ceiling while you're in a game. 0 is uncapped. A limit a little under your refresh rate keeps frame pacing steadier than letting it run free.",
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
			Section = "Frame Rate",
			Title = "Menu Frame Rate Limit",
			Description = "The ceiling while you're in the menu rather than a game. There's rarely a reason for the menu to run as hard as a game.",
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
			Section = "Frame Rate",
			Title = "Background Frame Rate Limit",
			Description = "The ceiling while the window isn't focused. Low is usually right - it gives the machine back to whatever you alt-tabbed to.",
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
			Section = "Camera",
			Title = "Field of View",
			Description = "How wide the default camera sees, in degrees. Games are free to override this with their own.",
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
			Section = "Quality",
			Title = "Texture Detail",
			Description = "Resolution textures stream in at, plus anisotropic filtering. Costs video memory rather than frame time, so lower it if you're short on VRAM.",
			Options = Levels( TextureQuality.Low, TextureQuality.Medium, TextureQuality.High, TextureQuality.Ultra ),
			AppliesRenderSettings = true,
			Read = () => Render.TextureQuality.ToString(),
			Write = value => Render.TextureQuality = ToEnum<TextureQuality>( value )
		} );

		Items.Add( new SettingItem
		{
			Id = "gfx.shadows",
			Category = "graphics",
			Section = "Quality",
			Title = "Shadows",
			Description = "Shadow map resolution, how far the sun casts, and how many lights cast at all. One of the heaviest settings on a busy scene.",
			Options = Levels( ShadowQuality.Low, ShadowQuality.Medium, ShadowQuality.High, ShadowQuality.Ultra ),
			AppliesRenderSettings = true,
			Read = () => Render.ShadowQuality.ToString(),
			Write = value => Render.ShadowQuality = ToEnum<ShadowQuality>( value )
		} );

		Items.Add( new SettingItem
		{
			Id = "gfx.fog",
			Category = "graphics",
			Section = "Quality",
			Title = "Volumetric Fog",
			Description = "Light scattering through fog and smoke. Levels raise the fog volume from 60x40x32 up to 320x200x128, which is the bulk of the cost.",
			Options = Levels( VolumetricFogQuality.Off, VolumetricFogQuality.Low, VolumetricFogQuality.Medium, VolumetricFogQuality.High, VolumetricFogQuality.Ultra ),
			AppliesRenderSettings = true,
			Read = () => Render.VolumetricFogQuality.ToString(),
			Write = value => Render.VolumetricFogQuality = ToEnum<VolumetricFogQuality>( value )
		} );

		Items.Add( new SettingItem
		{
			Id = "gfx.aa",
			Category = "graphics",
			Section = "Quality",
			Title = "Anti-Aliasing",
			Description = "Multisampling on geometry edges. Without it foliage and hair fall back to a hard cutout, so 2x is worth having.",
			Options =
			[
				new( "Off", MultisampleAmount.MultisampleNone ),
				new( "2x", MultisampleAmount.Multisample2x ),
				new( "4x", MultisampleAmount.Multisample4x ),
				new( "8x", MultisampleAmount.Multisample8x )
			],
			AppliesRenderSettings = true,
			Read = () => Render.AntiAliasQuality,
			Write = value => Render.AntiAliasQuality = ToEnum<MultisampleAmount>( value ),
			Warning = () => StagedUpscaler != UpscalerMode.Off
				? "Every upscaler renders to a target that can't be multisampled, so this has no effect while one is on."
				: null
		} );

		Items.Add( new SettingItem
		{
			Id = "gfx.ao",
			Category = "graphics",
			Section = "Post Processing",
			Title = "Ambient Occlusion",
			Description = "Contact shading in creases and where objects meet. Levels raise trace quality and go from quarter, to half, to full resolution. The dearest effect here.",
			Options = EffectLevels,
			AppliesRenderSettings = true,
			Read = () => Render.AmbientOcclusionQuality.ToString(),
			Write = value => Render.AmbientOcclusionQuality = ToEnum<EffectQuality>( value )
		} );

		Items.Add( new SettingItem
		{
			Id = "gfx.dof",
			Category = "graphics",
			Section = "Post Processing",
			Title = "Depth of Field",
			Description = "Blurs whatever a game marks as out of focus. Levels add blur samples. Costs nothing unless a game asks for it.",
			Options = EffectLevels,
			AppliesRenderSettings = true,
			Read = () => Render.DepthOfFieldQuality.ToString(),
			Write = value => Render.DepthOfFieldQuality = ToEnum<EffectQuality>( value )
		} );

		Items.Add( new SettingItem
		{
			Id = "gfx.ssr",
			Category = "graphics",
			Section = "Post Processing",
			Title = "Screen Space Reflections",
			Description = "Reflects on-screen geometry off glossy surfaces. Levels trace at a quarter, half, then full resolution, and the cost climbs with them.",
			Options = EffectLevels,
			AppliesRenderSettings = true,
			Read = () => Render.ScreenSpaceReflectionQuality.ToString(),
			Write = value => Render.ScreenSpaceReflectionQuality = ToEnum<EffectQuality>( value )
		} );

		Items.Add( new SettingItem
		{
			Id = "gfx.bloom",
			Category = "graphics",
			Section = "Post Processing",
			Title = "Bloom",
			Description = "Light bleeding out of bright pixels. Costs very little.",
			Options = OffOn,
			AppliesRenderSettings = true,
			Read = () => Render.BloomEnabled,
			Write = value => Render.BloomEnabled = ToBool( value )
		} );

		Items.Add( new SettingItem
		{
			Id = "gfx.motionblur_quality",
			Category = "graphics",
			Section = "Post Processing",
			Title = "Motion Blur Quality",
			Description = "Levels take 4, 12 or 16 samples along movement. Cheap either way; strength decides whether it blurs at all.",
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
			Section = "Post Processing",
			Title = "Motion Blur Strength",
			Description = "How far the blur smears. 0 stops motion blur even in games that ask for it.",
			Kind = SettingKind.Slider,
			Min = 0,
			Max = 1,
			Step = 0.01f,
			NumberFormat = "0.##",
			AppliesRenderSettings = true,
			Read = () => Render.MotionBlurScale,
			Write = value => Render.MotionBlurScale = ToFloat( value )
		} );

		Items.Add( new SettingItem
		{
			Id = "gfx.upscaler",
			Category = "graphics",
			Section = "Upscaling",
			Title = "Upscaler",
			Description = "Renders below display resolution and scales up. The cheapest frame rate you can buy, at a visible cost to sharpness, worst on text and fine detail.",
			Options = BuildUpscalerOptions(),
			AppliesRenderSettings = true,
			Read = () => Render.UpscalerMode,
			Write = value => Render.UpscalerMode = ToEnum<UpscalerMode>( value ),
			Warning = () => StagedUpscaler switch
			{
				UpscalerMode.FSR3 => "Temporal upscaler. Can ghost on moving objects and in-world UI.",
				UpscalerMode.DLSS => "Temporal AI upscaler. Can ghost on moving objects and in-world UI.",
				_ => null
			}
		} );

		Items.Add( new SettingItem
		{
			Id = "gfx.upscaler_scale",
			Category = "graphics",
			Section = "Upscaling",
			Title = "Render Quality",
			Description = "Share of display resolution the scene is drawn at. Lower is faster and softer.",
			Kind = SettingKind.Slider,
			Min = 40,
			Max = 100,
			NumberFormat = "0'%'",
			IsVisible = () => StagedUpscaler is UpscalerMode.Stretch or UpscalerMode.FSR1,
			AppliesRenderSettings = true,
			Read = () => (int)MathF.Round( Render.UpscalerRenderScale * 100f ),
			Write = value => Render.UpscalerRenderScale = ToFloat( value ) / 100f
		} );

		Items.Add( new SettingItem
		{
			Id = "gfx.fsr1_sharpness",
			Category = "graphics",
			Section = "Upscaling",
			Title = "Sharpness",
			Description = "How hard FSR sharpens the upscaled image. Too much makes edges crunchy.",
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
			Section = "Upscaling",
			Title = "FSR3 Quality",
			Description = "Which render resolution FSR3 works from. Native still runs the temporal pass, so it's anti-aliasing rather than upscaling.",
			Kind = SettingKind.Dropdown,
			Options =
			[
				new( "Native", Fsr3UpscalerQuality.Native ),
				new( "Quality", Fsr3UpscalerQuality.Quality ),
				new( "Balanced", Fsr3UpscalerQuality.Balanced ),
				new( "Performance", Fsr3UpscalerQuality.Performance ),
				new( "Ultra Performance", Fsr3UpscalerQuality.UltraPerformance )
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
			Section = "Upscaling",
			Title = "Sharpness",
			Description = "How hard FSR3 sharpens the upscaled image.",
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
			Section = "Upscaling",
			Title = "DLSS Quality",
			Description = "Which render resolution DLSS works from. DLAA runs at native resolution and spends the whole budget on image quality instead.",
			Kind = SettingKind.Dropdown,
			Options =
			[
				new( "DLAA", DlssQuality.DLAA ),
				new( "Quality", DlssQuality.Quality ),
				new( "Balanced", DlssQuality.Balanced ),
				new( "Performance", DlssQuality.Performance ),
				new( "Ultra Performance", DlssQuality.UltraPerformance )
			],
			IsVisible = () => StagedUpscaler == UpscalerMode.DLSS,
			AppliesRenderSettings = true,
			Read = () => Render.DlssQuality,
			Write = value => Render.DlssQuality = ToEnum<DlssQuality>( value )
		} );

	}

	/// <summary>The staged motion blur level, which the strength row follows.</summary>
	EffectQuality StagedMotionBlur => Find( "gfx.motionblur_quality" )?.As<EffectQuality>() ?? EffectQuality.High;

	/// <summary>The staged upscaler, which the rows below it follow.</summary>
	UpscalerMode StagedUpscaler => Find( "gfx.upscaler" )?.As<UpscalerMode>() ?? UpscalerMode.Off;

	/// <summary>The staged window mode, which the resolution row follows.</summary>
	string StagedWindowMode => Find( "display.mode" )?.Value?.ToString() ?? "borderless";

	static List<Option> BuildUpscalerOptions()
	{
		var options = new List<Option>
		{
			new( "Off", UpscalerMode.Off ),
			new( "Stretch", UpscalerMode.Stretch ),
			new( "FSR", UpscalerMode.FSR1 ),
			new( "FSR3", UpscalerMode.FSR3 )
		};

		if ( RenderSettings.IsUpscalerModeSupported( UpscalerMode.DLSS ) )
			options.Add( new Option( "DLSS", UpscalerMode.DLSS ) );

		return options;
	}

	void AddAudio()
	{
		Items.Add( new SettingItem
		{
			Id = "audio.device",
			Category = "audio",
			Section = "Device",
			Title = "Output Device",
			Description = "Where sound is played. Follows Windows' default unless you pick something here.",
			Kind = SettingKind.Dropdown,
			OptionsBuilder = () => Sandbox.Internal.AudioSettings.GetAudioDevices().Select( x => new Option( x.Name, x.Id ) ).ToList(),
			Read = () => Sandbox.Internal.AudioSettings.GetActiveDevice().Id,
			Write = value => Sandbox.Internal.AudioSettings.SetActiveDevice( value?.ToString() )
		} );

		AddVolume( "audio.volume", "Master", "Everything, all at once.", "volume" );
		AddVolume( "audio.music", "Music", "Menu music and whatever a game plays as music.", "music_volume" );
		AddVolume( "audio.voice", "Voice Chat", "How loud other players are when they talk.", "voip_volume" );

		Items.Add( new SettingItem
		{
			Id = "audio.voicemode",
			Category = "audio",
			Section = "Voice",
			Title = "Voice Mode",
			Description = "Push to talk sends only while the key is held. Open microphone sends whenever there's sound.",
			Options =
			[
				new( "Push to Talk", VoiceMode.PushToTalk ),
				new( "Open Mic", VoiceMode.OpenMicrophone ),
				new( "Off", VoiceMode.Disabled )
			],
			Read = () => Enum.TryParse<VoiceMode>( ConsoleSystem.GetValue( "voip_mode" ), true, out var mode ) ? mode : VoiceMode.PushToTalk,
			Write = value => ConsoleSystem.SetValue( "voip_mode", (int)ToEnum<VoiceMode>( value ) )
		} );

		Items.Add( new SettingItem
		{
			Id = "audio.acoustics",
			Category = "audio",
			Section = "Simulation",
			Title = "Acoustic Simulation",
			Description = "Traces the room to work out reverb and occlusion rather than using a fixed effect. Costs a little CPU.",
			Options = OffOn,
			Read = () => ConsoleSystem.GetValue( "snd_simulation_enable" ).ToBool(),
			Write = value => ConsoleSystem.SetValue( "snd_simulation_enable", ToBool( value ) )
		} );

		Items.Add( new SettingItem
		{
			Id = "audio.background",
			Category = "audio",
			Section = "Misc",
			Title = "Background Audio",
			Description = "Keeps playing sound while the window isn't focused.",
			Options = OffOn,
			Read = () => !ConsoleSystem.GetValue( "snd_mute_losefocus" ).ToBool(),
			Write = value => ConsoleSystem.SetValue( "snd_mute_losefocus", !ToBool( value ) )
		} );

		Items.Add( new SettingItem
		{
			Id = "audio.subtitles",
			Category = "audio",
			Section = "Misc",
			Title = "Subtitles",
			Description = "Shows captions for sounds that carry them. Up to each game to provide them.",
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
			Section = "Volume",
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
			Section = "Mouse",
			Title = "Mouse Sensitivity",
			Description = "How far the view turns for a given amount of mouse movement.",
			Kind = SettingKind.Slider,
			Min = 0,
			Max = 20,
			Step = 0.1f,
			NumberFormat = "0.#",
			Read = () => ConsoleSystem.GetValue( "sensitivity" ).ToFloat(),
			Write = value => ConsoleSystem.SetValue( "sensitivity", ToFloat( value ) )
		} );

		AddConVarToggle( "input.invert_pitch", "controls", "Mouse", "Invert Vertical Look", "Pushing the mouse forward looks down instead of up.", "mouse_pitch_inverted" );
		AddConVarToggle( "input.invert_yaw", "controls", "Mouse", "Invert Horizontal Look", "Moving the mouse right looks left instead of right.", "mouse_yaw_inverted" );

		AddControllerSlider( "input.pad_yaw", "Look Speed (Horizontal)", "Degrees per second at full stick deflection.", "controller_look_speed_yaw", 360, 1 );
		AddControllerSlider( "input.pad_pitch", "Look Speed (Vertical)", "Degrees per second at full stick deflection.", "controller_look_speed_pitch", 360, 1 );
		AddControllerSlider( "input.pad_deadzone", "Stick Deadzone", "How far the stick has to move before it counts. Raise it if the view drifts on its own.", "controller_joystick_deadzone", 50, 0.5f );
	}

	void AddControllerSlider( string id, string title, string description, string convar, float max, float step )
	{
		Items.Add( new SettingItem
		{
			Id = id,
			Category = "controls",
			Section = "Controller",
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
			Section = "Localization",
			Title = "Language",
			Description = "The language the menu and any game that's been translated is shown in.",
			Kind = SettingKind.Dropdown,
			OptionsBuilder = () => Sandbox.Localization.Languages.List.Select( x => new Option( x.Title, x.Abbreviation ) ).ToList(),
			Read = () => Sandbox.Language.Current.Abbreviation,
			Write = value => ConsoleSystem.Run( "language", value?.ToString() )
		} );

		Items.Add( new SettingItem
		{
			Id = "game.chat",
			Category = "game",
			Section = "Platform",
			Title = "Chat",
			Description = "Turns the platform chat off everywhere, including in games that use it.",
			Options = OffOn,
			Read = () => Preferences.ChatEnabled,
			Write = value => Preferences.ChatEnabled = ToBool( value )
		} );

		Items.Add( new SettingItem
		{
			Id = "game.streamer",
			Category = "game",
			Section = "Platform",
			Title = "Streamer Mode",
			Description = "Hides names and anything else personal that would otherwise end up on a stream.",
			Options = OffOn,
			Read = () => Preferences.StreamerMode,
			Write = value => Preferences.StreamerMode = ToBool( value )
		} );
	}

	void AddDeveloper()
	{
		AddConVarToggle( "dev.consoleoverlay", "developer", "Console", "Console Overlay", "Draws console output over the game, so you can read it without opening the console.", "consoleoverlay" );

		Items.Add( new SettingItem
		{
			Id = "dev.benchmark",
			Category = "developer",
			Section = "Benchmark",
			Title = "Run Benchmarks",
			Description = "Closes the menu and runs the standard benchmark packages, then shows the results.",
			Kind = SettingKind.Action,
			ActionLabel = "Run",
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

	static void RestoreAudioDefaults()
	{
		var devices = Sandbox.Internal.AudioSettings.GetAudioDevices();
		if ( devices.Any() )
			Sandbox.Internal.AudioSettings.SetActiveDevice( devices.First().Id );

		ConsoleSystem.SetValue( "snd_simulation_enable", true );
		ConsoleSystem.SetValue( "volume", 1.0f );
		ConsoleSystem.SetValue( "music_volume", 1.0f );
		ConsoleSystem.SetValue( "voip_volume", 1.0f );
		ConsoleSystem.SetValue( "voip_mode", (int)VoiceMode.PushToTalk );
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

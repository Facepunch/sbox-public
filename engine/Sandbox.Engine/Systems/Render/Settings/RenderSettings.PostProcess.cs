namespace Sandbox.Engine.Settings;

public partial class RenderSettings
{
	/// <summary>A level that no longer exists reads as the default rather than as nothing.</summary>
	static EffectQuality Valid( EffectQuality value ) => Enum.IsDefined( value ) ? value : EffectQuality.High;

	/// <summary>
	/// Contact shading where surfaces meet. Its resolution and denoise passes ride with the level.
	/// </summary>
	public EffectQuality AmbientOcclusionQuality
	{
		get => Valid( VideoSettings.Get<EffectQuality>( "postprocess.ao", EffectQuality.High ) );
		set
		{
			VideoSettings.Set<EffectQuality>( "postprocess.ao", value );
			Config.SetGroupConVars( "AmbientOcclusionQuality", value.ToString() );
		}
	}

	public EffectQuality DepthOfFieldQuality
	{
		get => Valid( VideoSettings.Get<EffectQuality>( "postprocess.dof", EffectQuality.High ) );
		set
		{
			VideoSettings.Set<EffectQuality>( "postprocess.dof", value );
			Config.SetGroupConVars( "DepthOfFieldQuality", value.ToString() );
		}
	}

	public EffectQuality ScreenSpaceReflectionQuality
	{
		get => Valid( VideoSettings.Get<EffectQuality>( "postprocess.ssr", EffectQuality.High ) );
		set
		{
			VideoSettings.Set<EffectQuality>( "postprocess.ssr", value );
			Config.SetGroupConVars( "ScreenSpaceReflectionQuality", value.ToString() );
		}
	}

	/// <summary>
	/// How many samples motion blur takes, and whether it runs at all.
	/// </summary>
	public EffectQuality MotionBlurQuality
	{
		get => Valid( VideoSettings.Get<EffectQuality>( "postprocess.motionblur", EffectQuality.High ) );
		set
		{
			VideoSettings.Set<EffectQuality>( "postprocess.motionblur", value );
			ApplyMotionBlur();
		}
	}

	/// <summary>
	/// The only thing that stops motion blur is a zero scale, which is also what the strength
	/// setting drives. So Off zeroes the live scale and leaves the stored strength alone, and
	/// coming back off Off restores it.
	/// </summary>
	internal void ApplyMotionBlur()
	{
		var quality = MotionBlurQuality;

		if ( quality != EffectQuality.Off )
			Config.SetGroupConVars( "MotionBlurQuality", quality.ToString() );

		MotionBlur.UserScale = quality == EffectQuality.Off ? 0f : MotionBlurScale;
	}

	/// <summary>Bloom is on or off, it has no quality levels.</summary>
	public bool BloomEnabled
	{
		get => VideoSettings.Get<bool>( "postprocess.bloom", true );
		set
		{
			VideoSettings.Set<bool>( "postprocess.bloom", value );
			ApplyBloom();
		}
	}

	internal void ApplyBloom() => ConVarSystem.SetValue( "r_bloom", BloomEnabled ? "1" : "0", true );

	readonly record struct PostProcessValues(
		EffectQuality AmbientOcclusion,
		EffectQuality DepthOfField,
		EffectQuality ScreenSpaceReflections,
		EffectQuality MotionBlur,
		bool Bloom );

	/// <summary>The preset and the effects share one ladder, so a rung means the same on both.</summary>
	static EffectQuality EffectLevelFor( PostProcessQuality preset ) => preset switch
	{
		PostProcessQuality.Off => EffectQuality.Off,
		PostProcessQuality.Low => EffectQuality.Low,
		PostProcessQuality.Medium => EffectQuality.Medium,
		_ => EffectQuality.High
	};

	static PostProcessValues ValuesFor( PostProcessQuality preset )
	{
		var level = EffectLevelFor( preset );

		return new( level, level, level, level, preset != PostProcessQuality.Off );
	}

	/// <summary>
	/// What a post-processing preset writes, by setting name. The settings menu compares this
	/// against edits that haven't been saved yet, so it doesn't need its own copy of the table.
	/// </summary>
	internal static Dictionary<string, string> SettingsFor( PostProcessQuality preset )
	{
		var effects = ValuesFor( preset );

		return new Dictionary<string, string>
		{
			[nameof( AmbientOcclusionQuality )] = effects.AmbientOcclusion.ToString(),
			[nameof( DepthOfFieldQuality )] = effects.DepthOfField.ToString(),
			[nameof( ScreenSpaceReflectionQuality )] = effects.ScreenSpaceReflections.ToString(),
			[nameof( MotionBlurQuality )] = effects.MotionBlur.ToString(),
			[nameof( BloomEnabled )] = effects.Bloom.ToString()
		};
	}

	static readonly PostProcessQuality[] PostProcessPresets =
		[PostProcessQuality.Off, PostProcessQuality.Low, PostProcessQuality.Medium, PostProcessQuality.High];

	PostProcessValues CurrentPostProcessValues => new(
		AmbientOcclusionQuality, DepthOfFieldQuality, ScreenSpaceReflectionQuality, MotionBlurQuality, BloomEnabled );

	/// <summary>
	/// What the individual effects add up to, or <see cref="PostProcessQuality.Custom"/>. Derived
	/// rather than stored, so it can't drift from the effects it describes. Setting it writes them all.
	/// </summary>
	public PostProcessQuality PostProcessQuality
	{
		get
		{
			var current = CurrentPostProcessValues;

			foreach ( var preset in PostProcessPresets )
			{
				if ( ValuesFor( preset ) == current )
					return preset;
			}

			return PostProcessQuality.Custom;
		}

		set
		{
			if ( value == PostProcessQuality.Custom )
				return;

			var values = ValuesFor( value );

			AmbientOcclusionQuality = values.AmbientOcclusion;
			DepthOfFieldQuality = values.DepthOfField;
			ScreenSpaceReflectionQuality = values.ScreenSpaceReflections;
			MotionBlurQuality = values.MotionBlur;
			BloomEnabled = values.Bloom;
		}
	}

	const string PostProcessSplitKey = "postprocess.split";

	/// <summary>
	/// Post processing used to be one stored level. Seed the per-effect settings from it once, so
	/// somebody who had it on Low doesn't silently come back on High.
	/// </summary>
	void MigratePostProcessSettings()
	{
		if ( VideoSettings.Get<bool>( PostProcessSplitKey, false ) )
			return;

		VideoSettings.Set<bool>( PostProcessSplitKey, true );

		var legacy = VideoSettings.Get<PostProcessQuality>( "postprocess.quality", PostProcessQuality.High );
		if ( !Enum.IsDefined( legacy ) || legacy == PostProcessQuality.Custom )
			legacy = PostProcessQuality.High;

		PostProcessQuality = legacy;
	}
}

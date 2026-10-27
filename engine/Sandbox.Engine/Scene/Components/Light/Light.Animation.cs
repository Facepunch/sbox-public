namespace Sandbox;

public abstract partial class Light
{
	/// <summary>
	/// Animate the emitted light without changing its authored tint, intensity or temperature.
	/// Disabled by default so existing lights retain their appearance.
	/// </summary>
	[Property, ToggleGroup( nameof( AnimationEnabled ), Label = "Animation", Order = 4 ), Order( 0 )]
	public bool AnimationEnabled
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			ApplyLightColor();
		}
	}

	/// <summary>
	/// Built-in light patterns, or a custom color and intensity loop.
	/// Built-in patterns advance in discrete steps, with normal brightness normalized to one.
	/// </summary>
	[Property, Group( nameof( AnimationEnabled ), Order = 4 ), Title( "Pattern" ), Order( 1 )]
	public LightAnimationPattern AnimationPattern
	{
		get;
		set
		{
			field = value;
			ApplyLightColor();
		}
	} = LightAnimationPattern.Flicker;

	/// <summary>
	/// Playback speed multiplier. One plays classic patterns at ten steps per second.
	/// Zero holds the sample selected by the time offset.
	/// </summary>
	[Property, Group( nameof( AnimationEnabled ), Order = 4 ), Title( "Speed" ), Range( 0, 10 ), Order( 2 )]
	public float AnimationSpeed
	{
		get;
		set
		{
			field = float.IsFinite( value ) ? MathF.Max( 0, value ) : 1;
			ApplyLightColor();
		}
	} = 1;

	/// <summary>
	/// Offset in animation seconds, applied after speed. Lights with the same settings share
	/// the scene clock; change this to put otherwise identical lights out of phase.
	/// </summary>
	[Property, Group( nameof( AnimationEnabled ), Order = 4 ), Title( "Time Offset" ), Order( 3 )]
	public float AnimationTimeOffset
	{
		get;
		set
		{
			field = float.IsFinite( value ) ? value : 0;
			ApplyLightColor();
		}
	}

	/// <summary>
	/// Duration of one custom loop in seconds at a playback speed of one.
	/// </summary>
	[Property, Group( nameof( AnimationEnabled ), Order = 4 ), Title( "Duration" ), Order( 4 )]
	[ShowIf( nameof( AnimationPattern ), LightAnimationPattern.Custom )]
	public float AnimationDuration
	{
		get;
		set
		{
			field = float.IsFinite( value ) ? MathF.Max( 0.01f, value ) : 2;
			ApplyLightColor();
		}
	} = 2;

	/// <summary>
	/// Brightness multiplier over one custom loop. The full curve time range is mapped to
	/// Duration. Negative values emit no light; values above one can brighten it.
	/// </summary>
	[Property, Group( nameof( AnimationEnabled ), Order = 4 ), Title( "Intensity Curve" ), Order( 5 )]
	[ShowIf( nameof( AnimationPattern ), LightAnimationPattern.Custom )]
	public Curve AnimationCurve
	{
		get;
		set
		{
			field = value;
			ApplyLightColor();
		}
	} = new( new( 0, 0 ), new( 0.5f, 1 ), new( 1, 0 ) );

	/// <summary>
	/// Color multiplier over one custom loop. White preserves the authored tint.
	/// Gradient alpha scales brightness, allowing fades without changing the light's tint alpha.
	/// </summary>
	[Property, Group( nameof( AnimationEnabled ), Order = 4 ), Title( "Color Gradient" ), Order( 6 )]
	[ShowIf( nameof( AnimationPattern ), LightAnimationPattern.Custom )]
	public Gradient AnimationGradient
	{
		get;
		set
		{
			field = value;
			ApplyLightColor();
		}
	} = Color.White;

	/// <summary>
	/// Sample the animation using scene time. This also makes editor previews and paused or
	/// time-scaled scenes follow their own clock instead of wall time.
	/// </summary>
	internal Color EvaluateAnimation( double time )
	{
		if ( !AnimationEnabled ) return Color.White;

		time = time * AnimationSpeed + AnimationTimeOffset;
		if ( !double.IsFinite( time ) ) time = 0;

		if ( AnimationPattern != LightAnimationPattern.Custom )
		{
			var pattern = GetAnimationPattern( AnimationPattern );
			var step = Math.Floor( (time % (pattern.Length / 10.0)) * 10 );
			if ( step < 0 ) step += pattern.Length;

			var frame = Math.Min( (int)step, pattern.Length - 1 );
			var brightness = (pattern[frame] - 'a') / 12.0f;
			return new Color( brightness, brightness, brightness );
		}

		var phase = (float)((time % AnimationDuration) / AnimationDuration);
		if ( phase < 0 ) phase += 1;

		var curveTime = AnimationCurve.TimeRange.x.LerpTo( AnimationCurve.TimeRange.y, phase );
		var intensity = AnimationCurve.Length == 0 ? 1 : AnimationCurve.Evaluate( curveTime );
		intensity = float.IsFinite( intensity ) ? MathF.Max( 0, intensity ) : 0;
		var tint = (AnimationGradient.Colors?.Count ?? 0) == 0 && (AnimationGradient.Alphas?.Count ?? 0) == 0
			? Color.White
			: AnimationGradient.Evaluate( phase );
		var alpha = float.IsFinite( tint.a ) ? tint.a.Clamp( 0, 1 ) : 0;

		return new Color( Channel( tint.r ), Channel( tint.g ), Channel( tint.b ) );

		float Channel( float value )
		{
			var result = value * alpha * intensity;
			return float.IsFinite( result ) ? MathF.Max( 0, result ) : 0;
		}
	}

	/// <summary>
	/// Brightness sequences for the built-in patterns. Each character represents one step,
	/// with 'a' emitting no light and 'm' preserving the authored brightness.
	/// </summary>
	internal static string GetAnimationPattern( LightAnimationPattern pattern ) => pattern switch
	{
		LightAnimationPattern.Flicker => "mmnmmommommnonmmonqnmmo",
		LightAnimationPattern.SlowStrongPulse => "abcdefghijklmnopqrstuvwxyzyxwvutsrqponmlkjihgfedcba",
		LightAnimationPattern.Candle => "mmmmmaaaaammmmmaaaaaabcdefgabcdefg",
		LightAnimationPattern.FastStrobe => "mamamamamama",
		LightAnimationPattern.GentlePulse => "jklmnopqrstuvwxyzyxwvutsrqponmlkj",
		LightAnimationPattern.Flicker2 => "nmonqnmomnmomomno",
		LightAnimationPattern.Candle2 => "mmmaaaabcdefgmmmmaaaammmaamm",
		LightAnimationPattern.Candle3 => "mmmaaammmaaammmabcdefaaaammmmabcdefmmmaaaa",
		LightAnimationPattern.SlowStrobe => "aaaaaaaazzzzzzzz",
		LightAnimationPattern.FluorescentFlicker => "mmamammmmammamamaaamammma",
		LightAnimationPattern.SlowPulse => "abcdefghijklmnopqrrqponmlkjihgfedcba",
		LightAnimationPattern.Underwater => "mmnnmmnnnmmnn",
		_ => "m"
	};

	/// <summary>
	/// Refresh animated emission while the component is active, including editor previews.
	/// </summary>
	protected override void OnUpdate()
	{
		if ( AnimationEnabled ) ApplyLightColor();
	}
}

/// <summary>
/// Built-in light patterns and a custom color and intensity loop.
/// </summary>
[Expose]
public enum LightAnimationPattern
{
	/// <summary>
	/// Loop a user-authored intensity curve and color gradient together.
	/// </summary>
	Custom = 0,
	/// <summary>
	/// The first irregular flicker pattern.
	/// </summary>
	Flicker = 1,
	/// <summary>
	/// A slow, strong pulse through darkness and maximum brightness.
	/// </summary>
	SlowStrongPulse = 2,
	/// <summary>
	/// The first candle pattern.
	/// </summary>
	Candle = 3,
	/// <summary>
	/// Rapidly alternating light and darkness.
	/// </summary>
	FastStrobe = 4,
	/// <summary>
	/// A gentle pulse that never reaches darkness.
	/// </summary>
	GentlePulse = 5,
	/// <summary>
	/// The second irregular flicker pattern.
	/// </summary>
	[Title( "Flicker 2" )]
	Flicker2 = 6,
	/// <summary>
	/// The second candle pattern.
	/// </summary>
	[Title( "Candle 2" )]
	Candle2 = 7,
	/// <summary>
	/// The third candle pattern.
	/// </summary>
	[Title( "Candle 3" )]
	Candle3 = 8,
	/// <summary>
	/// Slowly alternating darkness and maximum brightness.
	/// </summary>
	SlowStrobe = 9,
	/// <summary>
	/// Irregular flickering from a fluorescent light.
	/// </summary>
	FluorescentFlicker = 10,
	/// <summary>
	/// The second slow pulse pattern.
	/// </summary>
	SlowPulse = 11,
	/// <summary>
	/// Subtle brightness variation resembling light moving through water.
	/// </summary>
	Underwater = 12
}

using Sandbox.UI;

namespace UITests.PropertyCoverage;

[TestClass]
public class TransitionAnimationPropertiesTest
{
	// ---------------------------------------------------------------------------------------------
	// transition (shorthand) -> Transitions.List ( Property, Duration ms, TimingFunction, Delay ms )
	// ---------------------------------------------------------------------------------------------

	[TestMethod]
	public void Transition_Full_AllFour()
	{
		var s = new Styles();
		bool ok = s.Set( "transition", "width 2s ease-in-out .5s" );

		Assert.IsTrue( ok );
		Assert.IsNotNull( s.Transitions );
		Assert.AreEqual( 1, s.Transitions.List.Count );
		Assert.AreEqual( "width", s.Transitions.List[0].Property );
		Assert.AreEqual( 2000, s.Transitions.List[0].Duration );           // 2s -> 2000ms
		Assert.AreEqual( "ease-in-out", s.Transitions.List[0].TimingFunction );
		Assert.AreEqual( 500, s.Transitions.List[0].Delay );               // .5s -> 500ms
	}

	[TestMethod]
	public void Transition_DurationMilliseconds_Defaults()
	{
		var s = new Styles();
		bool ok = s.Set( "transition", "width 10ms" );

		Assert.IsTrue( ok );
		Assert.IsNotNull( s.Transitions );
		Assert.AreEqual( 1, s.Transitions.List.Count );
		Assert.AreEqual( "width", s.Transitions.List[0].Property );
		Assert.AreEqual( 10, s.Transitions.List[0].Duration );             // 10ms stays 10
																		   // Defaults when timing/delay omitted.
		Assert.AreEqual( "ease", s.Transitions.List[0].TimingFunction );
		Assert.IsTrue( s.Transitions.List[0].Delay.HasValue );
		Assert.AreEqual( 0, s.Transitions.List[0].Delay.Value );
	}

	[TestMethod]
	public void Transition_DurationSeconds()
	{
		var s = new Styles();
		bool ok = s.Set( "transition", "opacity 1s" );

		Assert.IsTrue( ok );
		Assert.AreEqual( 1, s.Transitions.List.Count );
		Assert.AreEqual( "opacity", s.Transitions.List[0].Property );
		Assert.AreEqual( 1000, s.Transitions.List[0].Duration );           // 1s -> 1000ms
	}

	[TestMethod]
	public void Transition_DurationDelayBeforeTiming()
	{
		// "<duration> <delay> <timing>" form: second time value is the delay.
		var s = new Styles();
		bool ok = s.Set( "transition", "width 10ms 2s ease-out" );

		Assert.IsTrue( ok );
		Assert.AreEqual( 1, s.Transitions.List.Count );
		Assert.AreEqual( "width", s.Transitions.List[0].Property );
		Assert.AreEqual( 10, s.Transitions.List[0].Duration );
		Assert.AreEqual( "ease-out", s.Transitions.List[0].TimingFunction );
		Assert.AreEqual( 2000, s.Transitions.List[0].Delay );             // 2s -> 2000ms
	}

	[TestMethod]
	public void Transition_Multiple()
	{
		var s = new Styles();
		bool ok = s.Set( "transition", "a 1s, b 2s" );

		Assert.IsTrue( ok );
		Assert.AreEqual( 2, s.Transitions.List.Count );

		Assert.AreEqual( "a", s.Transitions.List[0].Property );
		Assert.AreEqual( 1000, s.Transitions.List[0].Duration );

		Assert.AreEqual( "b", s.Transitions.List[1].Property );
		Assert.AreEqual( 2000, s.Transitions.List[1].Duration );
	}

	[TestMethod]
	public void Transition_MultipleWithTimingAndDelay()
	{
		var s = new Styles();
		bool ok = s.Set( "transition", "width 10ms 2s ease-out, height 2s ease-in" );

		Assert.IsTrue( ok );
		Assert.AreEqual( 2, s.Transitions.List.Count );

		Assert.AreEqual( "width", s.Transitions.List[0].Property );
		Assert.AreEqual( 10, s.Transitions.List[0].Duration );
		Assert.AreEqual( "ease-out", s.Transitions.List[0].TimingFunction );
		Assert.AreEqual( 2000, s.Transitions.List[0].Delay );

		Assert.AreEqual( "height", s.Transitions.List[1].Property );
		Assert.AreEqual( 2000, s.Transitions.List[1].Duration );
		Assert.AreEqual( "ease-in", s.Transitions.List[1].TimingFunction );
		Assert.AreEqual( 0, s.Transitions.List[1].Delay.Value );
	}

	[TestMethod]
	public void Transition_CubicBezier()
	{
		var s = new Styles();
		bool ok = s.Set( "transition", "transform 350ms cubic-bezier(0.16, 1, 0.3, 1)" );

		Assert.IsTrue( ok );
		Assert.AreEqual( 1, s.Transitions.List.Count );
		Assert.AreEqual( "transform", s.Transitions.List[0].Property );
		Assert.AreEqual( 350, s.Transitions.List[0].Duration );
		Assert.AreEqual( "cubic-bezier(0.16,1,0.3,1)", s.Transitions.List[0].TimingFunction.Replace( " ", "" ) );
	}

	[TestMethod]
	public void Transition_CubicBezierWithSpaces()
	{
		var s = new Styles();
		bool ok = s.Set( "transition", "transform 350ms cubic-bezier( 0.16, 1, 0.3, 1 )" );

		Assert.IsTrue( ok );
		Assert.AreEqual( 1, s.Transitions.List.Count );
		Assert.IsTrue( s.Transitions.List[0].TimingFunction.StartsWith( "cubic-bezier" ) );
		Assert.IsTrue( s.Transitions.List[0].TimingFunction.EndsWith( ")" ) );
	}

	[TestMethod]
	public void Transition_Steps()
	{
		var s = new Styles();
		bool ok = s.Set( "transition", "width 1s steps(4, end), height 500ms ease" );

		Assert.IsTrue( ok );
		Assert.AreEqual( 2, s.Transitions.List.Count );
		Assert.AreEqual( "width", s.Transitions.List[0].Property );
		Assert.AreEqual( 1000, s.Transitions.List[0].Duration );
		Assert.IsTrue( s.Transitions.List[0].TimingFunction.StartsWith( "steps" ) );
		Assert.AreEqual( "height", s.Transitions.List[1].Property );
		Assert.AreEqual( 500, s.Transitions.List[1].Duration );
		Assert.AreEqual( "ease", s.Transitions.List[1].TimingFunction );
	}

	[TestMethod]
	public void Transition_TrailingComma()
	{
		var s = new Styles();
		bool ok = s.Set( "transition", "width 1s ease," );

		Assert.IsTrue( ok );
		Assert.AreEqual( 1, s.Transitions.List.Count );
		Assert.AreEqual( "width", s.Transitions.List[0].Property );
		Assert.AreEqual( 1000, s.Transitions.List[0].Duration );
		Assert.AreEqual( "ease", s.Transitions.List[0].TimingFunction );
	}

	/// <summary>
	/// The transition property is optional and defaults to "all" (eg "transition: 0.3s ease").
	/// </summary>
	[TestMethod]
	public void Transition_OmittedPropertyDefaultsToAll()
	{
		var s = new Styles();
		bool ok = s.Set( "transition", "0.3s ease" );

		Assert.IsTrue( ok );
		Assert.IsNotNull( s.Transitions );
		Assert.AreEqual( 1, s.Transitions.List.Count );
		Assert.AreEqual( "all", s.Transitions.List[0].Property );
		Assert.AreEqual( 300, s.Transitions.List[0].Duration );
	}

	// ---------------------------------------------------------------------------------------------
	// transition longhands (transition-duration / -delay / -property / -timing-function)
	// Each populates the matching field of a Transitions.List entry, creating one with defaults.
	// ---------------------------------------------------------------------------------------------

	[TestMethod]
	public void TransitionDuration_Longhand()
	{
		var s = new Styles();
		bool ok = s.Set( "transition-duration", "0.5s" );

		Assert.IsTrue( ok );
		Assert.IsNotNull( s.Transitions );
		Assert.AreEqual( 1, s.Transitions.List.Count );
		Assert.AreEqual( 500, s.Transitions.List[0].Duration );
	}

	[TestMethod]
	public void TransitionDelay_Longhand()
	{
		var s = new Styles();
		bool ok = s.Set( "transition-delay", "0.25s" );

		Assert.IsTrue( ok );
		Assert.IsNotNull( s.Transitions );
		Assert.AreEqual( 1, s.Transitions.List.Count );
		Assert.AreEqual( 250, s.Transitions.List[0].Delay );
	}

	[TestMethod]
	public void TransitionProperty_Longhand()
	{
		var s = new Styles();
		bool ok = s.Set( "transition-property", "width" );

		Assert.IsTrue( ok );
		Assert.IsNotNull( s.Transitions );
		Assert.AreEqual( 1, s.Transitions.List.Count );
		Assert.AreEqual( "width", s.Transitions.List[0].Property );
	}

	[TestMethod]
	public void TransitionTimingFunction_Longhand()
	{
		var s = new Styles();
		bool ok = s.Set( "transition-timing-function", "ease-in" );

		Assert.IsTrue( ok );
		Assert.IsNotNull( s.Transitions );
		Assert.AreEqual( 1, s.Transitions.List.Count );
		Assert.AreEqual( "ease-in", s.Transitions.List[0].TimingFunction );
	}

	// ---------------------------------------------------------------------------------------------
	// animation (shorthand) -> AnimationName / AnimationDuration (s) / AnimationDelay (s) /
	// AnimationTimingFunction / AnimationIterationCount / AnimationDirection / AnimationFillMode /
	// AnimationPlayState
	// ---------------------------------------------------------------------------------------------

	[TestMethod]
	public void Animation_DurationTimingName()
	{
		var s = new Styles();
		bool ok = s.Set( "animation", "2s ease-in slidein" );

		Assert.IsTrue( ok );
		Assert.AreEqual( 2.0f, s.AnimationDuration );          // 2s stored as 2.0 seconds
		Assert.AreEqual( "ease-in", s.AnimationTimingFunction );
		Assert.AreEqual( "slidein", s.AnimationName );
	}

	[TestMethod]
	public void Animation_DurationMillisecondsStoredAsSeconds()
	{
		var s = new Styles();
		bool ok = s.Set( "animation", "200ms linear myanim" );

		Assert.IsTrue( ok );
		Assert.AreEqual( 0.2f, s.AnimationDuration );          // 200ms -> 0.2s
		Assert.AreEqual( "linear", s.AnimationTimingFunction );
		Assert.AreEqual( "myanim", s.AnimationName );
	}

	[TestMethod]
	public void Animation_AllProperties()
	{
		var s = new Styles();
		bool ok = s.Set( "animation", "200ms linear 1s 3 alternate forwards paused name" );

		Assert.IsTrue( ok );
		Assert.AreEqual( 0.2f, s.AnimationDuration );          // first time -> duration 200ms -> 0.2s
		Assert.AreEqual( 1.0f, s.AnimationDelay );             // second time -> delay 1s -> 1.0s
		Assert.AreEqual( "linear", s.AnimationTimingFunction );
		Assert.AreEqual( 3.0f, s.AnimationIterationCount );
		Assert.AreEqual( "alternate", s.AnimationDirection );
		Assert.AreEqual( "forwards", s.AnimationFillMode );
		Assert.AreEqual( "paused", s.AnimationPlayState );
		Assert.AreEqual( "name", s.AnimationName );
	}

	[TestMethod]
	public void Animation_None()
	{
		var s = new Styles();
		bool ok = s.Set( "animation", "none" );

		Assert.IsTrue( ok );
		Assert.AreEqual( "none", s.AnimationName );
		Assert.IsFalse( s.HasAnimation );
	}

	[TestMethod]
	public void Animation_Infinite()
	{
		var s = new Styles();
		bool ok = s.Set( "animation", "1s infinite spin" );

		Assert.IsTrue( ok );
		Assert.AreEqual( 1.0f, s.AnimationDuration );
		Assert.AreEqual( float.PositiveInfinity, s.AnimationIterationCount );
		Assert.AreEqual( "spin", s.AnimationName );
	}

	/// <summary>
	/// Easing functions with internal spaces (cubic-bezier/steps) must be read as a single token.
	/// </summary>
	[TestMethod]
	public void Animation_CubicBezier()
	{
		var s = new Styles();
		bool ok = s.Set( "animation", "350ms cubic-bezier(0.16, 1, 0.3, 1) fade" );

		Assert.IsTrue( ok );
		Assert.AreEqual( 0.35f, s.AnimationDuration );
		Assert.IsTrue( s.AnimationTimingFunction.StartsWith( "cubic-bezier" ) );
		Assert.AreEqual( "fade", s.AnimationName );
	}

	[TestMethod]
	public void Animation_Steps()
	{
		var s = new Styles();
		bool ok = s.Set( "animation", "1s steps(4, end) walk" );

		Assert.IsTrue( ok );
		Assert.AreEqual( 1.0f, s.AnimationDuration );
		Assert.IsTrue( s.AnimationTimingFunction.StartsWith( "steps" ) );
		Assert.AreEqual( "walk", s.AnimationName );
	}

	[TestMethod]
	public void Animation_Reverse()
	{
		var s = new Styles();
		bool ok = s.Set( "animation", "2s reverse pulse" );

		Assert.IsTrue( ok );
		Assert.AreEqual( "reverse", s.AnimationDirection );
		Assert.AreEqual( "pulse", s.AnimationName );
	}

	// ---------------------------------------------------------------------------------------------
	// animation longhands
	// ---------------------------------------------------------------------------------------------

	[TestMethod]
	public void AnimationName_Longhand()
	{
		var s = new Styles();
		bool ok = s.Set( "animation-name", "Beetroot" );

		Assert.IsTrue( ok );
		Assert.AreEqual( "Beetroot", s.AnimationName );
	}

	[TestMethod]
	public void AnimationName_Quoted()
	{
		var s = new Styles();
		bool ok = s.Set( "animation-name", "\"Beetroot\"" );

		Assert.IsTrue( ok );
		Assert.AreEqual( "Beetroot", s.AnimationName );   // surrounding quotes stripped
	}

	[TestMethod]
	public void AnimationName_None()
	{
		var s = new Styles();
		bool ok = s.Set( "animation-name", "none" );

		Assert.IsTrue( ok );
		Assert.AreEqual( "none", s.AnimationName );
		Assert.IsFalse( s.HasAnimation );
	}

	[TestMethod]
	public void AnimationDuration_Longhand_Seconds()
	{
		var s = new Styles();
		bool ok = s.Set( "animation-duration", "3s" );

		Assert.IsTrue( ok );
		Assert.AreEqual( 3.0f, s.AnimationDuration );     // 3s -> 3.0
	}

	[TestMethod]
	public void AnimationDuration_Longhand_Fractional()
	{
		var s = new Styles();
		bool ok = s.Set( "animation-duration", "0.5s" );

		Assert.IsTrue( ok );
		Assert.AreEqual( 0.5f, s.AnimationDuration );
	}

	/// <summary>
	/// 200ms -&gt; 0.2s (durations are stored in seconds).
	/// </summary>
	[TestMethod]
	public void AnimationDuration_Longhand_Milliseconds()
	{
		var s = new Styles();
		bool ok = s.Set( "animation-duration", "200ms" );

		Assert.IsTrue( ok );
		Assert.AreEqual( 0.2f, s.AnimationDuration );
	}

	[TestMethod]
	public void AnimationDelay_Longhand_Seconds()
	{
		var s = new Styles();
		bool ok = s.Set( "animation-delay", "4s" );

		Assert.IsTrue( ok );
		Assert.AreEqual( 4.0f, s.AnimationDelay );
	}

	[TestMethod]
	public void AnimationDelay_Longhand_Negative()
	{
		var s = new Styles();
		bool ok = s.Set( "animation-delay", "-2s" );

		Assert.IsTrue( ok );
		Assert.AreEqual( -2.0f, s.AnimationDelay );
	}

	/// <summary>
	/// 200ms -&gt; 0.2s
	/// </summary>
	[TestMethod]
	public void AnimationDelay_Longhand_Milliseconds()
	{
		var s = new Styles();
		bool ok = s.Set( "animation-delay", "200ms" );

		Assert.IsTrue( ok );
		Assert.AreEqual( 0.2f, s.AnimationDelay );
	}

	[TestMethod]
	public void AnimationTimingFunction_Longhand()
	{
		var s = new Styles();
		bool ok = s.Set( "animation-timing-function", "ease-out" );

		Assert.IsTrue( ok );
		Assert.AreEqual( "ease-out", s.AnimationTimingFunction );
	}

	[TestMethod]
	public void AnimationIterationCount_Longhand_Number()
	{
		var s = new Styles();
		bool ok = s.Set( "animation-iteration-count", "16" );

		Assert.IsTrue( ok );
		Assert.AreEqual( 16.0f, s.AnimationIterationCount );
	}

	[TestMethod]
	public void AnimationIterationCount_Longhand_Fractional()
	{
		var s = new Styles();
		bool ok = s.Set( "animation-iteration-count", "2.5" );

		Assert.IsTrue( ok );
		Assert.AreEqual( 2.5f, s.AnimationIterationCount );
	}

	[TestMethod]
	public void AnimationIterationCount_Longhand_Infinite()
	{
		var s = new Styles();
		bool ok = s.Set( "animation-iteration-count", "infinite" );

		Assert.IsTrue( ok );
		Assert.AreEqual( float.PositiveInfinity, s.AnimationIterationCount );
	}

	[TestMethod]
	public void AnimationIterationCount_Longhand_Invalid()
	{
		var s = new Styles();
		bool ok = s.Set( "animation-iteration-count", "notanumber" );

		Assert.IsFalse( ok );
	}

	[TestMethod]
	public void AnimationDirection_Longhand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "animation-direction", "normal" ) );
		Assert.AreEqual( "normal", s.AnimationDirection );

		s = new Styles();
		Assert.IsTrue( s.Set( "animation-direction", "alternate-reverse" ) );
		Assert.AreEqual( "alternate-reverse", s.AnimationDirection );
	}

	[TestMethod]
	public void AnimationFillMode_Longhand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "animation-fill-mode", "forwards" ) );
		Assert.AreEqual( "forwards", s.AnimationFillMode );

		s = new Styles();
		Assert.IsTrue( s.Set( "animation-fill-mode", "both" ) );
		Assert.AreEqual( "both", s.AnimationFillMode );
	}

	[TestMethod]
	public void AnimationPlayState_Longhand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "animation-play-state", "running" ) );
		Assert.AreEqual( "running", s.AnimationPlayState );

		s = new Styles();
		Assert.IsTrue( s.Set( "animation-play-state", "paused" ) );
		Assert.AreEqual( "paused", s.AnimationPlayState );
	}

	// ---------------------------------------------------------------------------------------------
	// sound-in / sound-out
	// ---------------------------------------------------------------------------------------------

	[TestMethod]
	public void SoundIn()
	{
		var s = new Styles();
		bool ok = s.Set( "sound-in", "ui/hover.sound" );

		Assert.IsTrue( ok );
		Assert.AreEqual( "ui/hover.sound", s.SoundIn );
	}

	[TestMethod]
	public void SoundIn_Quoted()
	{
		var s = new Styles();
		bool ok = s.Set( "sound-in", "\"ui/hover.sound\"" );

		Assert.IsTrue( ok );
		Assert.AreEqual( "ui/hover.sound", s.SoundIn );   // surrounding quotes stripped
	}

	[TestMethod]
	public void SoundOut()
	{
		var s = new Styles();
		bool ok = s.Set( "sound-out", "ui/click.sound" );

		Assert.IsTrue( ok );
		Assert.AreEqual( "ui/click.sound", s.SoundOut );
	}

	[TestMethod]
	public void SoundOut_Quoted()
	{
		var s = new Styles();
		bool ok = s.Set( "sound-out", "'ui/click.sound'" );

		Assert.IsTrue( ok );
		Assert.AreEqual( "ui/click.sound", s.SoundOut );   // surrounding single-quotes stripped
	}
}

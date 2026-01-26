namespace Sandbox.UI;

public partial class Styles
{
	KeyFrames currentFrames;
	double animationStart;
	Styles animStyle;

	/// <summary>
	/// Stops the animation. If we have animation vars we'll start again.
	/// </summary>
	public void ResetAnimation()
	{
		currentFrames = default;
		animationStart = default;
		animStyle = default;
	}

	/// <summary>
	/// Stop any previous animations and start this one. Make it last this long.
	/// </summary>
	public void StartAnimation( string name, float duration, int iterations = 1, float delay = 0.0f, string timing = "linear", string direction = "normal", string fillmode = "none" )
	{
		ResetAnimation();

		AnimationName = name;
		AnimationDuration = duration;
		AnimationDelay = delay;
		AnimationIterationCount = iterations;
		AnimationDirection = direction;
		AnimationFillMode = fillmode;
		AnimationTimingFunction = timing;

		Dirty();
	}

	public bool ApplyAnimation( Panel panel )
	{
		if ( !HasAnimation )
		{
			currentFrames = default;
			return false;
		}

		if ( !panel.TryFindKeyframe( AnimationName, out var keyframes ) )
		{
			currentFrames = default;
			return false;
		}

		// Start Animation
		if ( currentFrames != keyframes )
		{
			currentFrames = keyframes;
			animationStart = panel.TimeNow;
			animStyle = new Styles();
		}

		if ( AnimationPlayState == "paused" )
		{
			animationStart += panel.TimeDelta;
		}

		var duration = AnimationDuration ?? 1.0f;
		var direction = AnimationDirection ?? "normal";
		var fillMode = AnimationFillMode ?? "none";

		if ( duration <= 0 )
			return false;

		var iterations = AnimationIterationCount ?? float.PositiveInfinity;
		var delay = AnimationDelay ?? 0;
		var totalDuration = iterations * duration;

		var playLength = (panel.TimeNow - animationStart) - delay;

		if ( playLength < 0 )
		{
			if ( fillMode == "backwards" || fillMode == "both" )
			{
				keyframes.FillStyle( 0f, animStyle );
				Add( animStyle );
			}

			if ( fillMode == "none" )
				return false;

			return false;
		}

		if ( !float.IsInfinity( iterations ) )
		{
			playLength = playLength.Clamp( 0, totalDuration );
		}

		var delta = (playLength % duration) / duration;

		if ( direction == "reverse" )
		{
			delta = 1 - delta;
		}
		else if ( direction == "alternate" || direction == "alternate-reverse" )
		{
			delta = (playLength % (duration * 2)) / duration;
			if ( delta > 1 ) delta = 1.0f - (delta - 1);

			if ( direction == "alternate-reverse" )
				delta = 1 - delta;
		}

		var f = Utility.Easing.GetFunction( AnimationTimingFunction ?? "linear" );
		delta = f( (float)delta );

		if ( playLength >= totalDuration )
		{
			if ( fillMode == "forwards" || fillMode == "both" )
				delta = 1.0f;

			if ( fillMode == "none" )
				return false;
		}

		keyframes.FillStyle( (float)delta, animStyle );

		Add( animStyle );

		return true;
	}
}

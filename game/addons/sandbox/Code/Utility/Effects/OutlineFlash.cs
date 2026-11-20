using Sandbox.Utility;

/// <summary>
/// A short-lived outline that fades in, peaks at boosted brightness and fades back out. Start one with <see cref="ObjectHighlight.Flash"/>.
/// </summary>
public sealed class OutlineFlash : Component
{
	public HighlightStyle Style { get; set; }
	public float Duration { get; set; } = 0.25f;

	/// <summary>
	/// Color multiplier at the peak of the flash effect
	/// </summary>
	public float Brightness { get; set; } = 3.0f;

	HighlightOutline _outline;
	TimeSince _timeSinceStart;

	internal void Play( HighlightOutline outline )
	{
		_outline = outline;
		_timeSinceStart = 0;
		Apply( 0 );
	}

	protected override void OnPreRender()
	{
		var t = Duration > 0 ? _timeSinceStart / Duration : 1;

		if ( t >= 1 )
		{
			GameObject.Destroy();
			return;
		}

		Apply( Easing.EaseInOut( 1 - MathF.Abs( t * 2 - 1 ) ) );
	}

	void Apply( float weight )
	{
		if ( !_outline.IsValid() ) return;

		var boost = MathX.Lerp( 1, Brightness, weight );

		_outline.Width = Style.Width;
		_outline.Color = (Style.Color * boost).WithAlpha( Style.Color.a * weight );
		_outline.ObscuredColor = (Style.ObscuredColor * boost).WithAlpha( Style.ObscuredColor.a * weight );
	}
}

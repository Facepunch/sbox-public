/// <summary>One distance model for speaker playback and the tool's volume preview.</summary>
public sealed class SpeakerAttenuation
{
	public const float DefaultRange = 1024f;
	public const float MinRange = 64f;
	public const float MaxRange = 4096f;
	public const float DefaultFalloff = 1f;
	public const float MinFalloff = 0.25f;
	public const float MaxFalloff = 4f;

	float _falloff = float.NaN;
	Curve _curve;

	public static float ClampRange( float range ) => float.IsFinite( range ) ? range.Clamp( MinRange, MaxRange ) : DefaultRange;
	public static float ClampFalloff( float falloff ) => float.IsFinite( falloff ) ? falloff.Clamp( MinFalloff, MaxFalloff ) : DefaultFalloff;

	public Curve GetCurve( float falloff )
	{
		falloff = ClampFalloff( falloff );
		if ( _falloff == falloff ) return _curve;

		// Linear segments prevent overshoot/negative gain at the range boundary.
		// Rebuild only when the control changes, never once per playing sound/frame.
		const int segments = 64;
		var frames = new Curve.Frame[segments + 1];
		for ( int i = 0; i <= segments; i++ )
		{
			float distance = (float)i / segments;
			frames[i] = new Curve.Frame( distance, MathF.Pow( 1f - distance, falloff ) ) { Mode = Curve.HandleMode.Linear };
		}

		_curve = new Curve( frames );
		_falloff = falloff;
		return _curve;
	}

	/// <summary>Volume multiplier, not perceived loudness or an occlusion simulation.</summary>
	public float GainAt( float distance, float range, float falloff, float volume = 1f )
	{
		var fraction = (distance / ClampRange( range )).Clamp( 0f, 1f );
		return GetCurve( falloff ).Evaluate( fraction ).Clamp( 0f, 1f ) * volume.Clamp( 0f, 1f );
	}
}

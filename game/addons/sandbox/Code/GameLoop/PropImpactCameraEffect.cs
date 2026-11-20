/// <summary>
/// A sharp impact jolt followed by a fast, fading rumble.
/// Angular motion makes the hit visible against distant scenery as well as nearby props.
/// </summary>
public sealed class PropImpactCameraEffect : CameraEffectSystem.BaseEffect
{
	public float Amplitude { get; set; }
	public float Side { get; set; }
	public float Frequency { get; set; } = 12f;

	public override void Evaluate( float scale, ref Vector3 position, ref Angles angles, ref float fieldOfView )
	{
		var progress = (TimeAlive / MathF.Max( Duration, 0.001f )).Clamp( 0f, 1f );
		var remaining = 1f - progress;
		// Oscillate at a fixed rate while the envelope returns exactly to rest.
		var pulse = MathF.Sin( TimeAlive * MathF.Tau * Frequency ) * remaining * remaining * 1.1f;
		var strength = Amplitude * scale * pulse * 0.25f;

		position += new Vector3( -0.15f, -Side * 0.2f, -0.55f ) * strength;
		angles += new Angles( -3.5f, -Side * 0.35f, Side * 1.8f ) * strength;
	}
}

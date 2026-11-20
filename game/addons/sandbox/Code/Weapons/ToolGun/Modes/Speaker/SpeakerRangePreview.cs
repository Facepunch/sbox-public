/// <summary>Local, one-frame world guides at the tool's placement point; nothing is spawned or networked.</summary>
public sealed class SpeakerRangePreview
{
	readonly SpeakerAttenuation _attenuation = new();
	readonly Vector3[] _circle = new Vector3[65];
	static readonly Color Loud = new( 0.25f, 0.95f, 0.65f );
	static readonly Color Quiet = new( 1f, 0.72f, 0.2f );
	static readonly Color Edge = new( 1f, 0.32f, 0.25f );

	public void Draw( Scene scene, Transform placement, float range, float falloff, float volume )
	{
		range = SpeakerAttenuation.ClampRange( range );
		// SelectionPoint uses Rotation.LookAt(hit.Normal), so Forward is the surface normal.
		// Lift the surface guides slightly to avoid z-fighting with the placement surface.
		var guideCenter = placement.Position + placement.Forward * 2f;

		// Concentric bands: actual volume after distance attenuation at each radius.
		for ( int i = 1; i <= 4; i++ )
		{
			float radius = range * i / 4f;
			float gain = _attenuation.GainAt( radius, range, falloff, volume );
			var color = (i == 4 ? Edge : Color.Lerp( Quiet, Loud, gain )).WithAlpha( BandOpacity( gain ) );
			Circle( scene, guideCenter, placement.Left, placement.Up, radius, color );
		}
	}

	// Leave a faint outline at zero volume so the range limit remains visible.
	static float BandOpacity( float gain ) => 0.12f + 0.73f * gain.Clamp( 0f, 1f );

	void Circle( Scene scene, Vector3 center, Vector3 axisX, Vector3 axisY, float radius, Color color )
	{
		for ( int i = 0; i < _circle.Length; i++ )
		{
			float angle = i * MathF.Tau / (_circle.Length - 1);
			_circle[i] = center + (axisX * MathF.Cos( angle ) + axisY * MathF.Sin( angle )) * radius;
		}
		scene.DebugOverlay.Line( _circle, color );
	}
}

namespace Sandbox.Audio;

internal readonly record struct FrequencyBands
{
	private readonly Vector3 _v;

	public float Low => _v.x;
	public float Mid => _v.y;
	public float High => _v.z;

	public FrequencyBands( float low, float mid, float high ) => _v = new Vector3( low, mid, high );
	private FrequencyBands( Vector3 v ) => _v = v;

	public static FrequencyBands Zero => new( 0, 0, 0 );
	public static FrequencyBands One => new( 1, 1, 1 );

	public static FrequencyBands operator +( FrequencyBands a, FrequencyBands b ) => new( a._v + b._v );
	public static FrequencyBands operator *( FrequencyBands a, FrequencyBands b ) => new( a._v * b._v );
	public static FrequencyBands operator *( FrequencyBands a, float s ) => new( a._v * s );
	public static FrequencyBands operator /( FrequencyBands a, float s ) => new( a._v * (1f / s) );

	public static FrequencyBands Min( FrequencyBands a, FrequencyBands b ) =>
		new( MathF.Min( a.Low, b.Low ), MathF.Min( a.Mid, b.Mid ), MathF.Min( a.High, b.High ) );

	public static FrequencyBands Max( FrequencyBands a, FrequencyBands b ) =>
		new( MathF.Max( a.Low, b.Low ), MathF.Max( a.Mid, b.Mid ), MathF.Max( a.High, b.High ) );

	public static FrequencyBands Lerp( FrequencyBands a, FrequencyBands b, float t ) =>
		new( MathX.Lerp( a.Low, b.Low, t ), MathX.Lerp( a.Mid, b.Mid, t ), MathX.Lerp( a.High, b.High, t ) );

	public FrequencyBands Log() =>
		new( MathF.Log( MathF.Max( Low, 1e-6f ) ), MathF.Log( MathF.Max( Mid, 1e-6f ) ), MathF.Log( MathF.Max( High, 1e-6f ) ) );

	public FrequencyBands Exp() => new( MathF.Exp( Low ), MathF.Exp( Mid ), MathF.Exp( High ) );

	/// <summary>Per-band exponential decay toward <paramref name="target"/> with given half-life (seconds).</summary>
	public FrequencyBands Decay( FrequencyBands target, float halfLife, float dt ) =>
		new(
			MathX.ExponentialDecay( Low, target.Low, halfLife, dt ),
			MathX.ExponentialDecay( Mid, target.Mid, halfLife, dt ),
			MathX.ExponentialDecay( High, target.High, halfLife, dt )
		);
}

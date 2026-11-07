namespace Sandbox.Audio;

/// <summary>
/// Which escape sets a trace skips. Sources and listeners are often placed inside the body they belong to
/// (a prop, the player's capsule), so traces leaving them ignore those bodies.
/// </summary>
[Flags]
internal enum AcousticIgnore
{
	None = 0,
	Source = 1,
	Listener = 2,
	Both = Source | Listener,
}

internal struct AcousticHit
{
	public Vector3 Position;
	public Vector3 Normal;
	public AudioSurface Surface;
}

/// <summary>
/// The geometry the acoustic simulation traces against: the physics world in game, anything else in tests.
/// </summary>
internal interface IAcousticTracer
{
	bool Trace( Vector3 from, Vector3 to, AcousticIgnore ignore, out AcousticHit hit );
}

namespace Sandbox.Audio;

struct ReverbSnapshot
{
	public float DecayTime;
	public float MfpMeters;
	public float Openness;
	public float Mix;

	// Where the room's reverb is as loud as a source's direct sound.
	public float CriticalDistanceMeters;
	public FrequencyBands MaterialTone = FrequencyBands.One;

	// Cached per-band reverb decay times (computed on main thread, read by mix thread).
	public float DecayTimeLow;
	public float DecayTimeMid;
	public float DecayTimeHigh;

	public ReverbSnapshot() { }
}

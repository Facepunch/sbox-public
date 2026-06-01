namespace Sandbox.Audio;

struct ReverbSnapshot
{
	public float DecayTime;
	public float MfpMeters;
	public float Openness;
	public float Mix;
	public FrequencyBands MaterialTone = FrequencyBands.One;

	// Cached per-band reverb decay times (computed on main thread, read by mix thread).
	public float DecayTimeLow;
	public float DecayTimeMid;
	public float DecayTimeHigh;

	public ReverbSnapshot() { }
}

namespace Sandbox.Audio;

/// <summary>
/// A high-pass filter for individual sound events.
/// Allows frequencies above the cutoff to pass through while attenuating lower frequencies.
/// </summary>
[Expose]
public sealed class HighPassEventProcessor : AudioEventProcessor
{
	/// <summary>
	/// Cutoff frequency of the high-pass filter (0 to 1, where 1 is Nyquist frequency).
	/// Higher values = less bass, more treble.
	/// </summary>
	[Range( 0, 1 )]
	public float Cutoff { get; set; } = 0.5f;

	private PerChannel<float> _previousInput;
	private PerChannel<float> _previousOutput;

	protected override unsafe void ProcessSingleChannel( AudioChannel channel, Span<float> samples )
	{
		if ( samples.Length == 0 )
			return;

		float alpha = Cutoff;
		float prevIn = _previousInput.Get( channel );
		float prevOut = _previousOutput.Get( channel );

		for ( int i = 0; i < samples.Length; i++ )
		{
			float current = samples[i];
			samples[i] = prevOut + alpha * (current - prevIn);
			prevIn = current;
			prevOut = samples[i];
		}

		_previousInput.Set( channel, prevIn );
		_previousOutput.Set( channel, prevOut );
	}
}

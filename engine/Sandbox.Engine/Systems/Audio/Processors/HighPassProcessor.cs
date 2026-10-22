namespace Sandbox.Audio;

[Expose]
public sealed class HighPassProcessor : AudioProcessor<HighPassProcessor.State>
{
	/// <summary>
	/// Cutoff frequency of the high-pass filter.
	/// </summary>
	[Range( 0, 12000 )]
	public float Cutoff { get; set; } = 300f;

	public class State : ListenerState
	{
		internal PerChannel<float> PreviousInput;
		internal PerChannel<float> PreviousOutput;
	}

	/// <summary>
	/// Processes each channel individually using a simple one-pole high-pass filter.
	/// </summary>
	protected override unsafe void ProcessSingleChannel( AudioChannel channel, Span<float> input )
	{
		float rc = 1f / (2f * MathF.PI * Cutoff);
		float dt = 1f / AudioEngine.SamplingRate;
		float alpha = rc / (rc + dt);

		float prevInput = CurrentState.PreviousInput.Get( channel );
		float prevOutput = CurrentState.PreviousOutput.Get( channel );

		for ( int i = 0; i < input.Length; i++ )
		{
			float sample = input[i];
			float output = alpha * (prevOutput + sample - prevInput);
			input[i] = output;
			prevInput = sample;
			prevOutput = output;
		}

		CurrentState.PreviousInput.Set( channel, prevInput );
		CurrentState.PreviousOutput.Set( channel, prevOutput );
	}
}

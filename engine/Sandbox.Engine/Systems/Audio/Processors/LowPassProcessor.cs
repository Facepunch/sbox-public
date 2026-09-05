namespace Sandbox.Audio;

[Expose]
public sealed class LowPassProcessor : AudioProcessor<LowPassProcessor.State>
{
	/// <summary>
	/// Cutoff frequency for the low-pass filter.
	/// </summary>
	[Range( 0, 12000 )]
	public float Cutoff { get; set; } = 3400f;


	public class State : ListenerState
	{
		internal PerChannel<float> PreviousSample;
	}

	/// <summary>
	/// Processes a single audio channel with a low-pass filter.
	/// </summary>
	protected override void ProcessSingleChannel( AudioChannel channel, Span<float> input )
	{
		float rc = 1f / (2f * MathF.PI * Cutoff);
		float dt = 1f / AudioEngine.SamplingRate;
		float alpha = dt / (rc + dt);

		float prevOutput = CurrentState.PreviousSample.Get( channel );

		for ( int i = 0; i < input.Length; i++ )
		{
			float output = prevOutput + (alpha * (input[i] - prevOutput));
			input[i] = output;
			prevOutput = output;
		}

		CurrentState.PreviousSample.Set( channel, prevOutput );
	}
}

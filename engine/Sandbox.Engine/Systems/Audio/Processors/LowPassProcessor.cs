using System.Numerics;

namespace Sandbox.Audio;

/// <summary>
/// Just a test - don't count on this sticking around
/// </summary>
[Expose]
public sealed class LowPassProcessor : AudioProcessor<LowPassProcessor.State>
{
	/// <summary>
	/// Cutoff frequency for the low-pass filter (normalized 0 to 1).
	/// </summary>
	[Range( 0, 1 )]
	public float Cutoff { get; set; } = 0.5f;

	public class State : ListenerState
	{
		internal PerChannel<float> PreviousSample;
	}

	/// <summary>
	/// Processes a single audio channel with a low-pass filter.
	/// </summary>
	protected override unsafe void ProcessSingleChannel( AudioChannel channel, Span<float> input )
	{
		float alpha = Cutoff; // Simple smoothing factor
		float previous = CurrentState.PreviousSample.Get( channel );

		int vectorSize = Vector<float>.Count;
		int i = 0;

		if ( input.Length >= vectorSize )
		{
			var alphaVec = new Vector<float>( alpha );
			var prevVec = new Vector<float>( previous );

			for ( ; i <= input.Length - vectorSize; i += vectorSize )
			{
				var inputVec = new Vector<float>( input.Slice( i, vectorSize ) );
				prevVec = prevVec + alphaVec * (inputVec - prevVec);
				prevVec.CopyTo( input.Slice( i, vectorSize ) );
			}
			previous = prevVec[vectorSize - 1]; // Store last processed value
		}

		// Process remaining elements
		for ( ; i < input.Length; i++ )
		{
			previous = previous + alpha * (input[i] - previous);
			input[i] = previous;
		}

		CurrentState.PreviousSample.Set( channel, previous );
	}
}
